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
            // EventFramework 是 [StaticAddress(..., isPointer: true)]:副本框架尚未建立時 Instance() 回 null
            // (遊戲自己的取用點也帶 test rax,rax / jz),所以 Instance() 與 director 兩半都要判。
            // 這是巨集會拿去輪詢的存取子,取不到安靜回 0f。
            // 位址沒解出(改版後特徵碼對不上)時與「副本框架尚未建立」走同一條路。
            var framework = GameStatics.TryEventFramework();
            if (framework == null || !GameStatics.Resolved(EventFramework.MemberFunctionPointers.GetContentDirector, "EventFramework.GetContentDirector")) return 0f;
            var director = framework->GetContentDirector();
            if (director == null) return 0f;
            return director->ContentTimeLeft;
        }
    }

    // 以下四個是遊戲的靜態函式,位址沒解出時:Id 回 uint.MaxValue、Type 回 255 —— 0 是合法的「不在任何內容裡」/None,
    // 最大值不等於任何合法值;CanLeave 回 false;LeaveCurrentContent 記一行錯誤後不執行。
    [LuaFunction]
    [Changelog("12.47")]
    [Changelog("12.55", ChangelogType.Changed, "Changed name")]
    public uint GetCurrentContentId()
        => GameStatics.Resolved(EventFramework.MemberFunctionPointers.GetCurrentContentId, "EventFramework.GetCurrentContentId") ? EventFramework.GetCurrentContentId() : uint.MaxValue;

    [LuaFunction]
    [Changelog("12.47")]
    [Changelog("12.55", ChangelogType.Changed, "Changed name")]
    public ContentType GetCurrentContentType()
        => GameStatics.Resolved(EventFramework.MemberFunctionPointers.GetCurrentContentType, "EventFramework.GetCurrentContentType") ? EventFramework.GetCurrentContentType() : (ContentType)byte.MaxValue;

    [LuaFunction]
    [Changelog("12.47")]
    [Changelog("12.55", ChangelogType.Changed, "Changed name")]
    public bool CanLeaveCurrentContent()
        => GameStatics.Resolved(EventFramework.MemberFunctionPointers.CanLeaveCurrentContent, "EventFramework.CanLeaveCurrentContent") && EventFramework.CanLeaveCurrentContent();

    [LuaFunction]
    [Changelog("12.47")]
    public void LeaveCurrentContent()
    {
        if (!GameStatics.Resolved(EventFramework.MemberFunctionPointers.LeaveCurrentContent, "EventFramework.LeaveCurrentContent"))
        {
            FrameworkLogger.Error("LeaveCurrentContent is unavailable; nothing was done");
            return;
        }
        EventFramework.LeaveCurrentContent(true);
    }

    [LuaFunction] public OceanFishingWrapper OceanFishing => new();
    [LuaFunction] public OccultCrescentWrapper OccultCrescent => new(this);

    /// <summary>南方博德/新月島(Occult Crescent)的包裝層。</summary>
    /// <remarks>
    /// 🔴 <c>PublicContentOccultCrescent</c> 的 <c>GetInstance</c>／<c>GetMKDData</c>／<c>GetState</c> 是
    /// <c>[MemberFunction]</c> 的<b>靜態遊戲函式</b>(不是 A~C 類的 <c>Instance()</c>),人不在該副本裡時回 null
    /// (遊戲自己在 <c>call</c> 回來之後就立刻測回傳值)⇒ 解參考前一律判空,AccessViolationException 攔不到。
    /// <para>
    /// 失敗語意:這一層的四個成員都是巨集會放進等待迴圈輪詢的存取子(不是使用者明確觸發的動作),
    /// 所以<b>一律安靜回預設值,不記 log</b> —— 每幀記一行會把整份 log 洗掉。
    /// </para>
    /// <para>
    /// 📌 <b>MKDDataWrapper／OccultCrescentStateWrapper／DynamicEventWrapper 都不跨幀保存原生指標</b>:
    /// Lua 端會把包裝物件存成區域變數、跨 yield 反覆讀,所以只存身分、每次讀時自己重解,細節見各自的說明。
    /// </para>
    /// </remarks>
    public class OccultCrescentWrapper(InstancedContentModule parentModule) : IWrapper
    {
        [LuaDocs] public List<DynamicEventWrapper> Events
        {
            get
            {
                // 函式位址沒解出(改版後特徵碼對不上)時與「人不在該副本裡」走同一條路,本檔其餘三個靜態函式亦同。
                if (!GameStatics.Resolved(PublicContentOccultCrescent.MemberFunctionPointers.GetInstance, "PublicContentOccultCrescent.GetInstance")) return [];
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
        [LuaDocs]
        public List<EntityWrapper>? ChainTargets
        {
            get
            {
                if (!GameStatics.Resolved(PublicContentOccultCrescent.MemberFunctionPointers.IsChainTarget, "PublicContentOccultCrescent.IsChainTarget")) return [];
                return [.. Svc.Objects.OfType<IBattleChara>().Where(o => o.Address != nint.Zero && PublicContentOccultCrescent.IsChainTarget(o.Character())).Select(o => new EntityWrapper(o))];
            }
        }
    }

    /// <summary>單一動態事件(南方博德的緊急遭遇戰之類)的包裝層。</summary>
    /// <remarks>
    /// 🔴 不可保存 <c>DynamicEvent</c> 結構本身:它內嵌兩個 <c>Utf8String</c>,而 <c>Utf8String.StringPtr</c> 指向的是
    /// <b>原生容器裡的</b>緩衝區,複製進受管理物件之後仍然指著原生記憶體;巨集又慣於把包裝物件存進區域變數跨 yield 反覆讀,
    /// 副本結束、換區或登出之後再解參考就是 AccessViolationException(C# 的 try/catch 與 Lua 的 pcall 都攔不到)。
    /// <para>
    /// <b>建構當下抄成受管理值</b>:名稱、說明(字串),以及那幾個來自 DynamicEvent 表、事件存在期間不會變的 RowId。
    /// <b>會變的欄位每次讀時重查</b>(剩餘秒數、參加人數、進度、狀態、IsActive):身分是 <c>DynamicEventId</c>,建構當下的
    /// 槽位號只當提示 —— 提示那一格的 id 對得上就直接用,對不上才掃完整個容器;查不到就安靜回中性值。
    /// </para>
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
            // 🔴 Utf8String.ToString() 不剝 SeString payload;動態事件的名稱／說明會帶圖示與連結 payload,
            //    直接解碼會混進 U+FFFD 與雜字元,而巨集拿它去比對的是純文字 ⇒ 用 ECommons 的 GetText()(同 NodeWrapper.Text)。
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
            if (!GameStatics.Resolved(PublicContentOccultCrescent.MemberFunctionPointers.GetInstance, "PublicContentOccultCrescent.GetInstance")) return null;
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
        [LuaDocs] public bool IsActive { get { var e = Live(); return e != null && GameStatics.Resolved(DynamicEvent.MemberFunctionPointers.IsActive, "DynamicEvent.IsActive") && e->IsActive(); } }
    }

    /// <summary><c>GetMKDData()</c> 回 null(不在該副本裡)時,四個成員一律回 0。</summary>
    /// <remarks>
    /// 四個都是 RowId 型欄位,而 0 在這四張表都是「沒有」的慣例值:<c>QuestId</c>=沒有任務、
    /// <c>ZoneNameId</c>／<c>CipherNameId</c> 指 Addon 表、<c>CipherItemId</c> 指 Item 表,
    /// 道具 0 就是「沒有道具」。所以回 0 不會和任何真實資料撞號。
    /// <para>
    /// 🔴 不持有 <c>OccultCrescentMKDData*</c>:<b>每個成員自己重新呼叫 <c>GetMKDData()</c></b>,
    /// Lua 端把包裝物件存成區域變數、隔幾幀再讀,拿到的也是當下的狀態(人進出副本會跟著變)。
    /// </para>
    /// </remarks>
    public class MKDDataWrapper : IWrapper
    {
        private static OccultCrescentMKDData* Data()
            => GameStatics.Resolved(PublicContentOccultCrescent.MemberFunctionPointers.GetMKDData, "PublicContentOccultCrescent.GetMKDData") ? PublicContentOccultCrescent.GetMKDData() : null;

        [LuaDocs] public uint QuestId
        {
            get
            {
                var data = Data();
                if (data == null) return 0;
                return data->QuestId;
            }
        }

        [LuaDocs] public uint ZoneNameId
        {
            get
            {
                var data = Data();
                if (data == null) return 0;
                return data->ZoneNameId;
            }
        }

        [LuaDocs] public uint CipherItemId
        {
            get
            {
                var data = Data();
                if (data == null) return 0;
                return data->CurrencyItemIds[2];
            }
        }

        [LuaDocs] public uint CipherNameId
        {
            get
            {
                var data = Data();
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
    /// <para>
    /// 🔴 <c>CurrentSupportJob</c> 刻意回 <c>byte.MaxValue</c> 而不是 0。它是 MKDSupportJob 的 RowId,
    /// 而第 0 列是一個真的職業(輔助自由人),回 0 就是謊報一個具體答案;
    /// 255 不對應任何一列,巨集拿它去比對任何真實職業一定是 false,不會被誤導。
    /// </para>
    /// <para>
    /// 🔴 不持有 <c>OccultCrescentState*</c>:<b>每個成員自己重新呼叫 <c>GetState()</c></b>,理由同 <c>MKDDataWrapper</c>。
    /// </para>
    /// </remarks>
    public class OccultCrescentStateWrapper : IWrapper
    {
        private static OccultCrescentState* State()
            => GameStatics.Resolved(PublicContentOccultCrescent.MemberFunctionPointers.GetState, "PublicContentOccultCrescent.GetState") ? PublicContentOccultCrescent.GetState() : null;

        [LuaDocs] public uint CurrentKnowledge
        {
            get
            {
                var state = State();
                if (state == null) return 0;
                return state->CurrentKnowledge;
            }
        }

        [LuaDocs] public uint NeededKnowledge
        {
            get
            {
                var state = State();
                if (state == null) return 0;
                return state->NeededKnowledge;
            }
        }

        [LuaDocs] public uint NeededJobExperience
        {
            get
            {
                var state = State();
                if (state == null) return 0;
                return state->NeededJobExperience;
            }
        }

        [LuaDocs] public ushort Silver
        {
            get
            {
                var state = State();
                if (state == null) return 0;
                return state->Silver;
            }
        }

        [LuaDocs] public ushort Gold
        {
            get
            {
                var state = State();
                if (state == null) return 0;
                return state->Gold;
            }
        }

        /// <summary>取不到回 <c>byte.MaxValue</c>(不是 0)——理由見類別上的說明。</summary>
        [LuaDocs] public byte CurrentSupportJob
        {
            get
            {
                var state = State();
                if (state == null) return byte.MaxValue;
                return state->CurrentSupportJob;
            }
        }

        [LuaDocs] public byte KnowledgeLevelSync
        {
            get
            {
                var state = State();
                if (state == null) return 0;
                return state->KnowledgeLevelSync;
            }
        }

        [LuaDocs][Changelog("12.47")] public uint[] SupportJobExperience
        {
            get
            {
                var state = State();
                if (state == null) return [];
                return state->SupportJobExperience.ToArray();
            }
        }

        [LuaDocs][Changelog("12.47")] public byte[] SupportJobLevels
        {
            get
            {
                var state = State();
                if (state == null) return [];
                return state->SupportJobLevels.ToArray();
            }
        }
    }

    [LuaFunction][Changelog("12.22")] public PublicInstanceWrapper PublicInstance => new();
    public class PublicInstanceWrapper : IWrapper
    {
        // UIState 位址沒解出(改版後特徵碼對不上)時:兩個 id 回 uint.MaxValue(0 是合法的「不在分流裡」),IsInstancedArea 回 false。
        [LuaDocs][Changelog("12.22")] public uint TerritoryTypeId { get { var ui = GameStatics.TryUIState(); return ui == null ? uint.MaxValue : ui->PublicInstance.TerritoryTypeId; } }
        [LuaDocs][Changelog("12.22")] public uint InstanceId { get { var ui = GameStatics.TryUIState(); return ui == null ? uint.MaxValue : ui->PublicInstance.InstanceId; } }
        [LuaDocs][Changelog("12.22")] public bool IsInstancedArea { get { var ui = GameStatics.TryUIState(); return ui != null && ui->PublicInstance.IsInstancedArea(); } }
    }
}
