using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using NLua;
using SomethingNeedDoing.Core.Interfaces;
using SomethingNeedDoing.LuaMacro.Wrappers;
using System.Runtime.CompilerServices;
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
    /// 📌 <b>MKDDataWrapper／OccultCrescentStateWrapper／DynamicEventWrapper 已不再跨幀保存原生指標</b>:
    /// 前兩個原本把 GetMKDData()／GetState() 的回傳指標存進欄位,第三個是主建構子直接捕獲整個
    /// DynamicEvent 結構(那個結構內嵌兩個 Utf8String,而 Utf8String.StringPtr 指向原生容器裡的
    /// 緩衝區)。三個都是「建構當下取一次、之後每次屬性存取都拿舊指標去解參考」——
    /// Lua 端把包裝物件存成區域變數、跨 yield 反覆讀它時,指標可能已經失效,判空擋不住那一種。
    /// 現在一律改成存身分、每次讀時自己重解,細節見各自的說明。

    /// </remarks>
    public class OccultCrescentWrapper(InstancedContentModule parentModule) : IWrapper
    {
        [LuaDocs] public List<DynamicEventWrapper> Events
        {
            get
            {
                var instance = PublicContentOccultCrescent.GetInstance();
                if (instance == null) return [];

                // 槽位號一起帶進去當重查的提示 —— 見 DynamicEventWrapper 的說明。
                return [.. instance->DynamicEventContainer.Events.ToArray()
                    .Select((e, i) => new DynamicEventWrapper(e, i, parentModule))];
            }
        }

        [LuaDocs] public MKDDataWrapper MKDData => new();
        [LuaDocs] public OccultCrescentStateWrapper OccultCrescentState => new();

        /// <remarks>
        /// <c>o.Character()</c> 逐字是 <c>(Character*)o.Address</c> —— 不做任何檢查,而
        /// <c>IsChainTarget</c> 的特徵碼 "E8 ?? ?? ?? ?? 84 C0 74 ..." 顯示遊戲測的是<b>回傳值</b>,
        /// 沒有證據說它會判輸入。所以位址是 0 的物件先濾掉,不要送進遊戲函式。
        /// </remarks>
        [LuaDocs] public List<EntityWrapper>? ChainTargets => [.. Svc.Objects.OfType<IBattleChara>().Where(o => o.Address != nint.Zero && PublicContentOccultCrescent.IsChainTarget(o.Character())).Select(o => new EntityWrapper(o))];
    }

    /// <summary>單一動態事件(南方博德的緊急遭遇戰之類)的包裝層。</summary>
    /// <remarks>
    /// 🔴 <b>原本是主建構子直接捕獲整個 <c>DynamicEvent</c> 結構</b>。那個結構在 0x80／0xE8
    /// 各內嵌一個 <c>Utf8String</c>,而 <c>Utf8String.StringPtr</c> 指向的是<b>原生容器裡的</b>
    /// 緩衝區 —— 把結構整份複製進受管理陣列之後,那兩個指標仍然指著原生記憶體,
    /// 等於跨幀保存原生指標。而巨集的典型寫法正是把包裝物件存進區域變數再跨 yield 反覆讀:
    /// <code>
    ///     local ev = InstancedContent.OccultCrescent.Events[1]
    ///     while ev.SecondsLeft > 0 do yield("/wait 1") end
    /// </code>
    /// 副本結束、換區或登出之後那塊記憶體會被遊戲回收或改配給別的事件,再解參考輕則讀到
    /// 別人的文字,重則 AccessViolationException —— 那在 .NET Core 屬於 corrupted-state
    /// exception,C# 的 try/catch 與 Lua 的 pcall 都攔不到。
    ///
    /// ⇒ 改成兩段:
    /// <list type="number">
    ///   <item><b>建構當下抄成受管理值</b>:名稱、說明(字串),以及那幾個來自 DynamicEvent 表、
    ///   事件存在期間不會變的 RowId。這些的值與改動前完全相同(本來就是快照)。</item>
    ///   <item><b>會變的欄位改成每次讀時重查</b>:剩餘秒數、參加人數、進度、狀態、IsActive。
    ///   身分是 <c>DynamicEventId</c>,建構當下的槽位號只當提示 —— 提示那一格的 id 對得上就
    ///   直接用,對不上才掃完整個容器。查不到就安靜回中性值,與本檔其他存取子的慣例一致。</item>
    /// </list>
    /// ⚠️ <b>行為差異只有一個方向</b>:會變的那幾個欄位原本永遠停在建構當下的值(讀的是受管理
    /// 副本),現在會跟著遊戲更新 —— 那正是巨集拿它們做等待迴圈時期待的結果。其餘成員逐字不變。
    /// </remarks>
    public class DynamicEventWrapper : IWrapper
    {
        private readonly InstancedContentModule _parentModule;

        /// <summary>身分。重查時拿它比對。</summary>
        private readonly ushort _dynamicEventId;

        /// <summary>建構當下的槽位號;只是「上次看到它在這一格」的提示。</summary>
        private readonly int _index;

        // 建構當下抄走的不變欄位 —— 全是純量或受管理字串,不含任何原生指標。
        private readonly uint _quest;
        private readonly uint _announce;
        private readonly byte _eventType;
        private readonly byte _enemyType;
        private readonly byte _maxParticipants;
        private readonly byte _singleBattle;
        private readonly string _name;
        private readonly string _description;

        public DynamicEventWrapper(DynamicEvent evt, int index, InstancedContentModule parentModule)
        {
            _parentModule = parentModule;
            _index = index;
            _dynamicEventId = evt.DynamicEventId;
            _quest = evt.Quest;
            _announce = evt.Announce;
            _eventType = evt.EventType;
            _enemyType = evt.EnemyType;
            _maxParticipants = evt.MaxParticipants;
            _singleBattle = evt.SingleBattle;
            // 🔴 Utf8String.ToString() 是 Encoding.UTF8.GetString(AsSpan()),不剝 SeString payload。
            //    動態事件的名稱／說明是遊戲自己組的文字,裡面會帶圖示與連結 payload,
            //    直接解碼會混進 U+FFFD 與雜字元,而巨集拿它去比對的是純文字。
            //    GetText() 是 ECommons 既有的讀法(同 NodeWrapper.Text)。
            // ⚠️ 這兩行讀的是 evt 這份副本裡的 StringPtr:呼叫端才剛從原生容器複製出來,
            //    還在同一幀內,指標必定有效。抄成受管理 string 之後就不再碰它。
            _name = evt.Name.GetText();
            _description = evt.Description.GetText();
        }

        /// <summary>重新解析出這個事件在原生容器裡的那一格;找不到回 <c>null</c>。</summary>
        /// <remarks>
        /// 回傳的指標只在呼叫端當場用一次,不往外存 —— 那正是「存 id、每次讀時重查」的形狀。
        /// <c>GetInstance()</c> 人不在該副本裡時回 null(特徵碼後面就是 test rax,rax / jz),先判空。
        /// </remarks>
        private DynamicEvent* Live()
        {
            var instance = PublicContentOccultCrescent.GetInstance();
            if (instance == null) return null;

            var events = instance->DynamicEventContainer.Events;
            if ((uint)_index < (uint)events.Length)
            {
                ref var hint = ref events[_index];
                if (hint.DynamicEventId == _dynamicEventId)
                    return (DynamicEvent*)Unsafe.AsPointer(ref hint);
            }

            for (var i = 0; i < events.Length; i++)
            {
                ref var e = ref events[i];
                if (e.DynamicEventId == _dynamicEventId)
                    return (DynamicEvent*)Unsafe.AsPointer(ref e);
            }
            return null;
        }

        [LuaDocs] public uint Quest => _quest;
        [LuaDocs] public object? QuestRow => _parentModule.GetModule<ExcelModule>()?.GetRow("Quest", _quest);
        [LuaDocs] public uint Announce => _announce;
        [LuaDocs] public byte EventType => _eventType;
        [LuaDocs] public object? EventTypeRow => _parentModule.GetModule<ExcelModule>()?.GetRow("EventType", _eventType);
        [LuaDocs] public byte EnemyType => _enemyType;
        [LuaDocs] public object? EnemyTypeRow => _parentModule.GetModule<ExcelModule>()?.GetRow("EnemyType", _enemyType);
        [LuaDocs] public byte MaxParticipants => _maxParticipants;
        [LuaDocs] public byte SingleBattle => _singleBattle;
        [LuaDocs] public object? SingleBattleRow => _parentModule.GetModule<ExcelModule>()?.GetRow("DynamicEventSingleBattle", _singleBattle);
        [LuaDocs] public int StartTimestamp { get { var e = Live(); return e == null ? 0 : e->StartTimestamp; } }
        [LuaDocs] public uint SecondsLeft { get { var e = Live(); return e == null ? 0u : e->SecondsLeft; } }
        [LuaDocs] public uint SecondsDuration { get { var e = Live(); return e == null ? 0u : e->SecondsDuration; } }
        [LuaDocs] public byte Participants { get { var e = Live(); return e == null ? (byte)0 : e->Participants; } }
        [LuaDocs] public string Name => _name;
        [LuaDocs] public string Description => _description;
        [LuaDocs] public byte Progress { get { var e = Live(); return e == null ? (byte)0 : e->Progress; } }
        /// <summary>查不到時回 <c>Inactive</c>(=0)—— 那本來就是「沒在進行」的合法值。</summary>
        [LuaDocs] public DynamicEventState State { get { var e = Live(); return e == null ? DynamicEventState.Inactive : e->State; } }
        [LuaDocs] public bool IsActive { get { var e = Live(); return e != null && e->IsActive(); } }
    }

    /// <summary><c>GetMKDData()</c> 回 null(不在該副本裡)時,四個成員一律回 0。</summary>
    /// <remarks>
    /// 四個都是 RowId 型欄位,而 0 在這四張表都是「沒有」的慣例值:<c>QuestId</c>=沒有任務、
    /// <c>ZoneNameId</c>／<c>CipherNameId</c> 指 Addon 表、<c>CipherItemId</c> 指 Item 表,
    /// 道具 0 就是「沒有道具」。所以回 0 不會和任何真實資料撞號。
    ///
    /// 🔴 原本是主建構子捕獲 <c>OccultCrescentMKDData*</c>,等於跨幀保存原生指標:Lua 端把包裝
    /// 物件存成區域變數、隔幾幀再讀,拿到的是舊指標(非 null 但可能已失效),判空擋不住那一種。
    /// 改成<b>每個成員自己重新呼叫 <c>GetMKDData()</c></b>,不再持有任何指標。
    /// ⚠️ 附帶的行為差異:原本是建構當下取一次,建構時人不在副本裡就永遠回 0;
    /// 現在會跟著當下的狀態走 —— 那才是巨集輪詢它時期待的結果。
    /// </remarks>
    public class MKDDataWrapper : IWrapper
    {
        [LuaDocs] public uint QuestId
        {
            get
            {
                var data = PublicContentOccultCrescent.GetMKDData();
                if (data == null) return 0;
                return data->QuestId;
            }
        }

        [LuaDocs] public uint ZoneNameId
        {
            get
            {
                var data = PublicContentOccultCrescent.GetMKDData();
                if (data == null) return 0;
                return data->ZoneNameId;
            }
        }

        [LuaDocs] public uint CipherItemId
        {
            get
            {
                var data = PublicContentOccultCrescent.GetMKDData();
                if (data == null) return 0;
                return data->CurrencyItemIds[2];
            }
        }

        [LuaDocs] public uint CipherNameId
        {
            get
            {
                var data = PublicContentOccultCrescent.GetMKDData();
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
    ///
    /// 🔴 原本是主建構子捕獲 <c>OccultCrescentState*</c>,等於跨幀保存原生指標,成因與失敗形式
    /// 同 <c>MKDDataWrapper</c>。改成<b>每個成員自己重新呼叫 <c>GetState()</c></b>,不再持有指標。
    /// </remarks>
    public class OccultCrescentStateWrapper : IWrapper
    {
        [LuaDocs] public uint CurrentKnowledge
        {
            get
            {
                var state = PublicContentOccultCrescent.GetState();
                if (state == null) return 0;
                return state->CurrentKnowledge;
            }
        }

        [LuaDocs] public uint NeededKnowledge
        {
            get
            {
                var state = PublicContentOccultCrescent.GetState();
                if (state == null) return 0;
                return state->NeededKnowledge;
            }
        }

        [LuaDocs] public uint NeededJobExperience
        {
            get
            {
                var state = PublicContentOccultCrescent.GetState();
                if (state == null) return 0;
                return state->NeededJobExperience;
            }
        }

        [LuaDocs] public ushort Silver
        {
            get
            {
                var state = PublicContentOccultCrescent.GetState();
                if (state == null) return 0;
                return state->Silver;
            }
        }

        [LuaDocs] public ushort Gold
        {
            get
            {
                var state = PublicContentOccultCrescent.GetState();
                if (state == null) return 0;
                return state->Gold;
            }
        }

        /// <summary>取不到回 <c>byte.MaxValue</c>(不是 0)——理由見類別上的說明。</summary>
        [LuaDocs] public byte CurrentSupportJob
        {
            get
            {
                var state = PublicContentOccultCrescent.GetState();
                if (state == null) return byte.MaxValue;
                return state->CurrentSupportJob;
            }
        }

        [LuaDocs] public byte KnowledgeLevelSync
        {
            get
            {
                var state = PublicContentOccultCrescent.GetState();
                if (state == null) return 0;
                return state->KnowledgeLevelSync;
            }
        }

        [LuaDocs][Changelog("12.47")] public uint[] SupportJobExperience
        {
            get
            {
                var state = PublicContentOccultCrescent.GetState();
                if (state == null) return [];
                return state->SupportJobExperience.ToArray();
            }
        }

        [LuaDocs][Changelog("12.47")] public byte[] SupportJobLevels
        {
            get
            {
                var state = PublicContentOccultCrescent.GetState();
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
