using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using NLua;
using SomethingNeedDoing.Core.Interfaces;
using SomethingNeedDoing.LuaMacro.Wrappers;
using ContentType = FFXIVClientStructs.FFXIV.Client.Game.Event.ContentType;

namespace SomethingNeedDoing.LuaMacro.Modules;
/// <summary>
/// Module for deep dungeons and forays
/// </summary>
public unsafe class InstancedContentModule : LuaModuleBase
{
    public override string ModuleName => "InstancedContent";

    [LuaFunction]
    public float ContentTimeLeft
    {
        get
        {
            // EventFramework 是 [StaticAddress(..., isPointer: true)],產生器出來的實作是
            // `return *ppInstance;` —— 解出來的是「存放指標的位址」,副本框架尚未建立時
            // 解參考結果就是 null(遊戲自己的取用點也帶 test rax,rax / jz,CS 給的特徵碼
            // 裡逐字看得到)。原本只擋了 director 這一半,Instance() 那一半是裸讀。
            // 這是巨集會拿去輪詢的存取子,取不到安靜回 0f(與原本 director == null 同一個結果)。
            var framework = EventFramework.Instance();
            if (framework == null) return 0f;
            var director = framework->GetContentDirector();
            if (director == null) return 0f;
            return director->ContentTimeLeft;
        }
    }

    [LuaFunction]
    [Changelog("12.47")]
    [Changelog("12.55", ChangelogType.Changed, "Changed name")]
    public uint GetCurrentContentId() => EventFramework.GetCurrentContentId();

    [LuaFunction]
    [Changelog("12.47")]
    [Changelog("12.55", ChangelogType.Changed, "Changed name")]
    public ContentType GetCurrentContentType() => EventFramework.GetCurrentContentType();

    [LuaFunction]
    [Changelog("12.47")]
    [Changelog("12.55", ChangelogType.Changed, "Changed name")]
    public bool CanLeaveCurrentContent() => EventFramework.CanLeaveCurrentContent();

    [LuaFunction][Changelog("12.47")] public void LeaveCurrentContent() => EventFramework.LeaveCurrentContent(true);

    [LuaFunction] public OceanFishingWrapper OceanFishing => new();
    [LuaFunction] public OccultCrescentWrapper OccultCrescent => new(this);

    /// <summary>南方博德/新月島(Occult Crescent)的包裝層。</summary>
    /// <remarks>
    /// 🔴 <b>第五種形態</b>:<c>PublicContentOccultCrescent</c> 這幾個成員既不是 A~C 類的
    /// <c>Instance()</c>,也不是欄位,而是 <c>[MemberFunction]</c> 的<b>靜態遊戲函式</b>。
    /// 判別依據是 CS 給的呼叫點特徵碼 —— 遊戲自己在 <c>call</c> 回來之後就立刻測回傳值:
    /// <list type="bullet">
    ///   <item><c>GetInstance</c> "E8 ?? ?? ?? ?? <b>48 85 C0 74 08</b> 0F B6 CB" → test rax,rax / jz</item>
    ///   <item><c>GetMKDData</c>  "E8 ?? ?? ?? ?? <b>48 85 C0 0F 84</b> ..."      → test rax,rax / jz(遠跳)</item>
    ///   <item><c>GetState</c>    "E8 ?? ?? ?? ?? 48 8B E8 <b>48 85 C0 75 12</b>" → mov rbp,rax / test / jnz</item>
    /// </list>
    /// <b>遊戲自己都要判,就代表這三個真的會回 null</b>(人不在該副本裡的時候)。
    /// 對 null 解參考是 AccessViolationException,在 .NET Core 屬於 corrupted-state exception,
    /// <c>try/catch</c> 與 <c>HookSafety.ExecuteSafe</c> 都攔不到 —— 唯一有效的作法是解參考前判空。
    ///
    /// 失敗語意沿用 d605137／df5608a／2e68bfd 的分界:這四個都是巨集會放進等待迴圈輪詢的
    /// 存取子(不是使用者明確觸發的動作),所以<b>一律安靜回預設值,不記 log</b> ——
    /// 每幀記一行會把整份 log 洗掉。
    ///
    /// ⚠️ <b>本輪未處理</b>:<c>MKDDataWrapper</c>／<c>OccultCrescentStateWrapper</c> 把原生指標
    /// 存進欄位,等於跨幀保存指標。屬性每次取用都會重新解析,所以「取到就馬上讀」是安全的;
    /// 但 Lua 端把它存成區域變數、隔幾幀再讀,拿到的是舊指標(非 null 但可能已失效),
    /// 判空擋不住那一種。要根治得讓包裝層每次存取自己重解 —— 那會動到公開的建構子簽章,
    /// 不在本次範圍。
    /// </remarks>
    public class OccultCrescentWrapper(InstancedContentModule parentModule) : IWrapper
    {
        [LuaDocs] public List<DynamicEventWrapper> Events
        {
            get
            {
                var instance = PublicContentOccultCrescent.GetInstance();
                if (instance == null) return [];

                return [.. instance->DynamicEventContainer.Events.ToArray().Select(e => new DynamicEventWrapper(e, parentModule))];
            }
        }

        [LuaDocs] public MKDDataWrapper MKDData => new(PublicContentOccultCrescent.GetMKDData());
        [LuaDocs] public OccultCrescentStateWrapper OccultCrescentState => new(PublicContentOccultCrescent.GetState());

        /// <remarks>
        /// <c>o.Character()</c> 逐字是 <c>(Character*)o.Address</c> —— 不做任何檢查,而
        /// <c>IsChainTarget</c> 的特徵碼 "E8 ?? ?? ?? ?? 84 C0 74 ..." 顯示遊戲測的是<b>回傳值</b>,
        /// 沒有證據說它會判輸入。所以位址是 0 的物件先濾掉,不要送進遊戲函式。
        /// </remarks>
        [LuaDocs] public List<EntityWrapper>? ChainTargets => [.. Svc.Objects.OfType<IBattleChara>().Where(o => o.Address != nint.Zero && PublicContentOccultCrescent.IsChainTarget(o.Character())).Select(o => new EntityWrapper(o))];
    }

    public class DynamicEventWrapper(DynamicEvent evt, InstancedContentModule parentModule) : IWrapper
    {
        [LuaDocs] public uint Quest => evt.Quest;
        [LuaDocs] public object? QuestRow => parentModule.GetModule<ExcelModule>()?.GetRow("Quest", evt.Quest);
        [LuaDocs] public uint Announce => evt.Announce;
        [LuaDocs] public byte EventType => evt.EventType;
        [LuaDocs] public object? EventTypeRow => parentModule.GetModule<ExcelModule>()?.GetRow("EventType", evt.EventType);
        [LuaDocs] public byte EnemyType => evt.EnemyType;
        [LuaDocs] public object? EnemyTypeRow => parentModule.GetModule<ExcelModule>()?.GetRow("EnemyType", evt.EnemyType);
        [LuaDocs] public byte MaxParticipants => evt.MaxParticipants;
        [LuaDocs] public byte SingleBattle => evt.SingleBattle;
        [LuaDocs] public object? SingleBattleRow => parentModule.GetModule<ExcelModule>()?.GetRow("DynamicEventSingleBattle", evt.SingleBattle);
        [LuaDocs] public int StartTimestamp => evt.StartTimestamp;
        [LuaDocs] public uint SecondsLeft => evt.SecondsLeft;
        [LuaDocs] public uint SecondsDuration => evt.SecondsDuration;
        [LuaDocs] public byte Participants => evt.Participants;
        [LuaDocs] public string Name => evt.Name.ToString();
        [LuaDocs] public string Description => evt.Description.ToString();
        [LuaDocs] public byte Progress => evt.Progress;
        [LuaDocs] public DynamicEventState State => evt.State;
        [LuaDocs] public bool IsActive => evt.IsActive();
    }

    /// <summary><c>GetMKDData()</c> 回 null(不在該副本裡)時,四個成員一律回 0。</summary>
    /// <remarks>
    /// 四個都是 RowId 型欄位,而 0 在這四張表都是「沒有」的慣例值:<c>QuestId</c>=沒有任務、
    /// <c>ZoneNameId</c>／<c>CipherNameId</c> 指 Addon 表、<c>CipherItemId</c> 指 Item 表,
    /// 道具 0 就是「沒有道具」。所以回 0 不會和任何真實資料撞號。
    /// </remarks>
    public class MKDDataWrapper(OccultCrescentMKDData* data) : IWrapper
    {
        [LuaDocs] public uint QuestId
        {
            get
            {
                if (data == null) return 0;
                return data->QuestId;
            }
        }

        [LuaDocs] public uint ZoneNameId
        {
            get
            {
                if (data == null) return 0;
                return data->ZoneNameId;
            }
        }

        [LuaDocs] public uint CipherItemId
        {
            get
            {
                if (data == null) return 0;
                return data->CurrencyItemIds[2];
            }
        }

        [LuaDocs] public uint CipherNameId
        {
            get
            {
                if (data == null) return 0;
                return data->CurrencyNameIds[2];
            }
        }
    }

    /// <summary><c>GetState()</c> 回 null(不在該副本裡)時的預設值,逐成員裁定。</summary>
    /// <remarks>
    /// 數量型的(知識/經驗/銀幣/金幣/等級同步)回 0 —— 0 本來就是它們的合法下限,不會誤導。
    /// 兩個陣列回空陣列而不是 null:回 null 到 Lua 端是 nil,巨集寫 <c>#state.SupportJobLevels</c>
    /// 會直接以 attempt to get length of a nil value 中斷,比拿到空陣列更難處理。
    ///
    /// 🔴 <c>CurrentSupportJob</c> 刻意回 <c>byte.MaxValue</c> 而不是 0。它是 MKDSupportJob 的
    /// RowId,而<b>我無法離線證明 0 是「沒有支援職業」的空列</b>:台服 7.20 的 MKDSupportJob.csv
    /// 有 13 列(0~12,剛好對上 CS 的 FixedSizeArray13),但<b>13 列的 Name 全是空字串、
    /// Action 與 LevelMax 全為 0</b> —— 整張表在台服還沒填內容,所以「第 0 列是不是佔位列」
    /// 在台服資料裡分辨不出來。0 有可能是一個真的職業,回 0 就是謊報一個具體答案;
    /// 255 不在 0~12 裡,巨集拿它去比對任何真實職業一定是 false,不會被誤導。
    /// (同一個理由,2e68bfd 讓 ClientLanguage／Region 回 byte.MaxValue 而不是 0。)
    /// </remarks>
    public class OccultCrescentStateWrapper(OccultCrescentState* state) : IWrapper
    {
        [LuaDocs] public uint CurrentKnowledge
        {
            get
            {
                if (state == null) return 0;
                return state->CurrentKnowledge;
            }
        }

        [LuaDocs] public uint NeededKnowledge
        {
            get
            {
                if (state == null) return 0;
                return state->NeededKnowledge;
            }
        }

        [LuaDocs] public uint NeededJobExperience
        {
            get
            {
                if (state == null) return 0;
                return state->NeededJobExperience;
            }
        }

        [LuaDocs] public ushort Silver
        {
            get
            {
                if (state == null) return 0;
                return state->Silver;
            }
        }

        [LuaDocs] public ushort Gold
        {
            get
            {
                if (state == null) return 0;
                return state->Gold;
            }
        }

        /// <summary>取不到回 <c>byte.MaxValue</c>(不是 0)——理由見類別上的說明。</summary>
        [LuaDocs] public byte CurrentSupportJob
        {
            get
            {
                if (state == null) return byte.MaxValue;
                return state->CurrentSupportJob;
            }
        }

        [LuaDocs] public byte KnowledgeLevelSync
        {
            get
            {
                if (state == null) return 0;
                return state->KnowledgeLevelSync;
            }
        }

        [LuaDocs][Changelog("12.47")] public uint[] SupportJobExperience
        {
            get
            {
                if (state == null) return [];
                return state->SupportJobExperience.ToArray();
            }
        }

        [LuaDocs][Changelog("12.47")] public byte[] SupportJobLevels
        {
            get
            {
                if (state == null) return [];
                return state->SupportJobLevels.ToArray();
            }
        }
    }

    [LuaFunction][Changelog("12.22")] public PublicInstanceWrapper PublicInstance => new();
    public class PublicInstanceWrapper : IWrapper
    {
        [LuaDocs][Changelog("12.22")] public uint TerritoryTypeId => UIState.Instance()->PublicInstance.TerritoryTypeId;
        [LuaDocs][Changelog("12.22")] public uint InstanceId => UIState.Instance()->PublicInstance.InstanceId;
        [LuaDocs][Changelog("12.22")] public bool IsInstancedArea => UIState.Instance()->PublicInstance.IsInstancedArea();
    }
}
