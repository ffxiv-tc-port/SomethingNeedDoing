using Dalamud.Game.ClientState.Aetherytes;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using NLua;
using SomethingNeedDoing.Core.Interfaces;
using SomethingNeedDoing.LuaMacro.Wrappers;
using static FFXIVClientStructs.FFXIV.Client.UI.Info.InfoProxyCommonList.CharacterData;

namespace SomethingNeedDoing.LuaMacro.Modules;
public unsafe class InstancesModule : LuaModuleBase
{
    public override string ModuleName => "Instances";

    [LuaFunction] public DutyFinderWrapper DutyFinder => new();
    public unsafe class DutyFinderWrapper : IWrapper
    {
        // ── Instance() 的四種形態,決定要不要判空(依 CS 那一側的宣告;適用整個檔案與任何抄這裡寫法的地方)──
        //  A. [StaticAddress(sig, off)] 沒有 isPointer:回靜態位址本身;特徵碼失配時 Instance() 擲例外 ⇒ 一律走 GameStatics.TryX() 判空,不直接呼叫 Instance()。
        //  B. [StaticAddress(sig, off, isPointer: true)]:回「存放指標的位址」解出來的值,物件還沒建立時是 null ⇒ 必須判;特徵碼失配同 A,也走 GameStatics.TryX()。
        //  C. [Agent(...)] 產生的、以及 CS 裡手寫的 Instance():實作帶 `== null ? null :`,未登入時是 null ⇒ 必須判;底下那條鏈的位址失配時同樣是擲 ⇒ 前面先過 GameStatics.AgentChainResolved()。
        //  D. Instance() 之後再往下走的成員(GetQueueInfo() / CurrentFate / InfoProxy …)各自有 null 路徑,要分開判。

        // 本檔的 ContentsFinder / UIState / Telepo 是 A 類;Framework / EnvManager 是 B 類;AgentContentsFinder / AgentMap 是 C 類。
        // 失敗語意的分界:**使用者明確呼叫的動作型方法**取不到就記一行錯誤(安靜失敗會讓人以為指令送出去了);
        // **巨集會放在等待迴圈裡輪詢的存取子**安靜回預設值(每幀記一行會把整份 log 洗掉)。

        // C 類 + 動作型 ⇒ 記錯誤。
        [LuaDocs]
        public void OpenRouletteDuty(byte contentRouletteID)
        {
            var agent = GameStatics.AgentChainResolved() ? AgentContentsFinder.Instance() : null;
            if (agent == null || !GameStatics.Resolved(AgentContentsFinder.MemberFunctionPointers.OpenRouletteDuty, "AgentContentsFinder.OpenRouletteDuty"))
            {
                FrameworkLogger.Error("Duty finder agent is unavailable (not logged in?)");
                return;
            }
            agent->OpenRouletteDuty(contentRouletteID);
        }

        [LuaDocs]
        public void OpenRegularDuty(uint contentsFinderCondition)
        {
            var agent = GameStatics.AgentChainResolved() ? AgentContentsFinder.Instance() : null;
            if (agent == null || !GameStatics.Resolved(AgentContentsFinder.MemberFunctionPointers.OpenRegularDuty, "AgentContentsFinder.OpenRegularDuty"))
            {
                FrameworkLogger.Error("Duty finder agent is unavailable (not logged in?)");
                return;
            }
            agent->OpenRegularDuty(contentsFinderCondition);
        }

        [LuaDocs]
        [Changelog("12.69")]
        public void QueueDuty(uint contentsFinderCondition)
        {
            if (!FindRows<Sheets.ContentFinderCondition>(x => x.Unknown47 && x.Unknown48).Select(x => x.RowId).Contains(contentsFinderCondition)) // 47 = IsInUse, 48 = ShownInDf (I think)
            {
                FrameworkLogger.Error($"Invalid cfcID: {contentsFinderCondition}");
                return;
            }
            var QueueInfo = TryGetQueueInfo();
            if (QueueInfo == null || !CanCancelQueue() || !GameStatics.Resolved(ContentsFinderQueueInfo.MemberFunctionPointers.QueueDuties, "ContentsFinderQueueInfo.QueueDuties"))
            {
                FrameworkLogger.Error("Duty finder queue info is unavailable");
                return;
            }
            if (QueueInfo->QueueState is ContentsFinderQueueInfo.QueueStates.Pending or ContentsFinderQueueInfo.QueueStates.Queued) QueueInfo->CancelQueue();
            QueueInfo->QueueDuties(&contentsFinderCondition, 1);
        }

        [LuaDocs]
        [Changelog("12.69")]
        public void QueueRoulette(byte contentRouletteId)
        {
            if (!FindRows<Sheets.ContentRoulette>(x => !x.Description.IsEmpty).Select(x => x.RowId).Contains(contentRouletteId))
            {
                FrameworkLogger.Error($"Invalid content roulette ID: {contentRouletteId}");
                return;
            }
            var QueueInfo = TryGetQueueInfo();
            if (QueueInfo == null || !CanCancelQueue() || !GameStatics.Resolved(ContentsFinderQueueInfo.MemberFunctionPointers.QueueRoulette, "ContentsFinderQueueInfo.QueueRoulette"))
            {
                FrameworkLogger.Error("Duty finder queue info is unavailable");
                return;
            }
            if (QueueInfo->QueueState is ContentsFinderQueueInfo.QueueStates.Pending or ContentsFinderQueueInfo.QueueStates.Queued) QueueInfo->CancelQueue();
            QueueInfo->QueueRoulette(contentRouletteId);
        }

        // 契約:取不到回 null、不擲例外,呼叫端必須判空。
        // 現行台服執行檔的 GetQueueInfo 只回內嵌欄位的位址(lea rax,[rcx+0x20]),不會是 null;
        // 判空是為了改版後函式形狀變了也不會對 null 解參(那是攔不到的 AVE)。
        private static ContentsFinderQueueInfo* TryGetQueueInfo()
        {
            var finder = ContentsFinder.StaticAddressPointers.pInstance;
            if (finder == null || ContentsFinder.MemberFunctionPointers.GetQueueInfo == null) return null;
            return finder->GetQueueInfo();
        }

        private static bool CanCancelQueue() => GameStatics.Resolved(ContentsFinderQueueInfo.MemberFunctionPointers.CancelQueue, "ContentsFinderQueueInfo.CancelQueue");

        [LuaDocs]
        [Changelog("12.69")]
        public void CancelQueue()
        {
            var queueInfo = TryGetQueueInfo();
            if (queueInfo == null || !CanCancelQueue())
            {
                FrameworkLogger.Error("Duty finder queue info is unavailable");
                return;
            }
            queueInfo->CancelQueue();
        }

        // 以下的 ContentsFinder 與 UIState 是 A 類,走 GameStatics 判空。位址沒解出時:
        // 查詢型回「沒有資料」值(數值用該型別的最大值,不與任何合法值相等;bool 只能回 false),寫入型記一行錯誤後不寫。
        // 0 是合法的「沒有懲罰」,所以取不到時回 uint.MaxValue。
        [LuaDocs]
        [Changelog("12.73")]
        public uint GetPenaltyTimeRemainingInMinutes()
        {
            var ui = GameStatics.TryUIState();
            if (ui == null || !GameStatics.Resolved(InstanceContent.MemberFunctionPointers.GetPenaltyRemainingInMinutes, "InstanceContent.GetPenaltyRemainingInMinutes")) return uint.MaxValue;
            return ui->InstanceContent.GetPenaltyRemainingInMinutes(0);
        }

        [LuaDocs]
        [Changelog("12.73")]
        public bool IsRouletteIncomplete(byte rouletteId)
        {
            var ui = GameStatics.TryUIState();
            if (ui == null || !GameStatics.Resolved(InstanceContent.MemberFunctionPointers.IsRouletteIncomplete, "InstanceContent.IsRouletteIncomplete")) return false;
            return ui->InstanceContent.IsRouletteIncomplete(rouletteId);
        }

        private static ContentsFinder* FinderForWrite()
        {
            var finder = GameStatics.TryContentsFinder();
            if (finder == null) FrameworkLogger.Error("Duty finder settings are unavailable; nothing was changed");
            return finder;
        }

        [LuaDocs] public bool IsUnrestrictedParty { get { var f = GameStatics.TryContentsFinder(); return f != null && f->IsUnrestrictedParty; } set { var f = FinderForWrite(); if (f != null) f->IsUnrestrictedParty = value; } }
        [LuaDocs] public bool IsLevelSync { get { var f = GameStatics.TryContentsFinder(); return f != null && f->IsLevelSync; } set { var f = FinderForWrite(); if (f != null) f->IsLevelSync = value; } }
        [LuaDocs] public bool IsMinIL { get { var f = GameStatics.TryContentsFinder(); return f != null && f->IsMinimalIL; } set { var f = FinderForWrite(); if (f != null) f->IsMinimalIL = value; } }
        [LuaDocs] public bool IsSilenceEcho { get { var f = GameStatics.TryContentsFinder(); return f != null && f->IsSilenceEcho; } set { var f = FinderForWrite(); if (f != null) f->IsSilenceEcho = value; } }
        [LuaDocs] public bool IsExplorerMode { get { var f = GameStatics.TryContentsFinder(); return f != null && f->IsExplorerMode; } set { var f = FinderForWrite(); if (f != null) f->IsExplorerMode = value; } }
        [LuaDocs] public bool IsLimitedLevelingRoulette { get { var f = GameStatics.TryContentsFinder(); return f != null && f->IsLimitedLevelingRoulette; } set { var f = FinderForWrite(); if (f != null) f->IsLimitedLevelingRoulette = value; } }

        // 輪詢型 ⇒ 取不到時安靜回一個不對應任何排隊狀態的值:0 是合法的 None(沒在排),
        // 回 0 會讓「等到沒在排」的迴圈誤以為排完了而往下走。
        [LuaDocs]
        public ContentsFinderQueueInfo.QueueStates QueueState
        {
            get
            {
                var queueInfo = TryGetQueueInfo();
                if (queueInfo == null) return (ContentsFinderQueueInfo.QueueStates)byte.MaxValue;
                return queueInfo->QueueState;
            }
        }
    }

    [LuaFunction] public FriendsListWrapper FriendsList => new();
    public class FriendsListWrapper : IWrapper
    {
        [LuaDocs]
        public List<FriendWrapper> Friends
        {
            get
            {
                var friends = new List<FriendWrapper>();

                // 這是 [LuaFunction] 底下的存取子,使用者巨集可以在任意時機呼叫(未登入、換區途中都算),這條鏈三層各自都有 null 路徑:
                //  - AgentFriendlist.Instance() 在 UIModule 尚未建立時回 null;InfoProxy 在好友名單資訊代理人還沒掛上時是 null;
                //  - CharDataSpan 在 CharData 為 null 而 EntryCount 非 0 時是一個指向位址 0 的 span,建構不報錯、取用才 AVE。
                // 前兩層取不到就記一行錯誤後回空清單。
                var agentFriendlist = GameStatics.AgentChainResolved() ? AgentFriendlist.Instance() : null;
                if (agentFriendlist == null || agentFriendlist->InfoProxy == null)
                {
                    FrameworkLogger.Error("Friend list is unavailable (agent or info proxy not ready)");
                    return friends;
                }

                var infoProxy = agentFriendlist->InfoProxy;

                // 名單一次都沒載入過時 CharData 是 null。這不是錯誤,安靜回空清單即可。
                if (infoProxy->CharData == null)
                    return friends;

                // 只解析一次 span,不要每次迭代都重跑整條鏈。
                var charDataSpan = infoProxy->CharDataSpan;
                for (var i = 0; i < charDataSpan.Length; i++)
                    friends.Add(new(charDataSpan[i]));
                return friends;
            }
        }

        [LuaDocs] public FriendWrapper? GetFriendByName(string name) => Friends.FirstOrDefault(f => f.Name == name);
    }

    public class FriendWrapper(InfoProxyCommonList.CharacterData data) : IWrapper
    {
        [LuaDocs] public string Name => data.NameString;
        [LuaDocs] public ulong ContentId => data.ContentId;
        [LuaDocs] public OnlineStatus State => data.State;
        [LuaDocs] public bool IsOtherServer => data.IsOtherServer;
        [LuaDocs] public ushort CurrentWorld => data.CurrentWorld;
        [LuaDocs] public ushort HomeWorld => data.HomeWorld;
        [LuaDocs] public ushort Location => data.Location;
        [LuaDocs] public GrandCompany GrandCompany => data.GrandCompany;
        [LuaDocs] public Language ClientLanguage => data.ClientLanguage;
        [LuaDocs] public byte Sex => data.Sex;
        [LuaDocs] public JobWrapper Job => new(data.Job);
    }

    [LuaFunction]
    [Changelog("12.8")]
    public MapWrapper Map => new();
    public class MapWrapper : IWrapper
    {
        // AgentMap 是 C 類:Instance() 走 AgentModule.Instance() → UIModule,未登入或換區
        // 途中回 null。兩個都是巨集會輪詢的存取子 ⇒ 安靜回預設值,不記 log。
        [LuaDocs]
        [Changelog("12.8")]
        public bool IsFlagMarkerSet
        {
            get
            {
                var agent = GameStatics.AgentChainResolved() ? AgentMap.Instance() : null;
                if (agent == null) return false;
                return agent->FlagMarkerCount > 0;
            }
        }

        // 取不到代理人時回一個全零的 FlagWrapper,不是 null:回 null 到 Lua 端就是 nil,
        // 巨集寫 Instances.Map.Flag.TerritoryId 會直接以 "attempt to index a nil value" 中斷,
        // 那比拿到 0 更難處理。全零與「旗標從來沒設過」是同一個結果(本來就沒有先看
        // FlagMarkerCount 才讀 [0]),要分辨「有沒有旗標」請用 IsFlagMarkerSet。
        [LuaDocs]
        [Changelog("12.8")]
        public FlagWrapper Flag
        {
            get
            {
                var agent = GameStatics.AgentChainResolved() ? AgentMap.Instance() : null;
                if (agent == null) return new(default);
                return new(agent->FlagMapMarkers[0]);
            }
        }
    }

    public class FlagWrapper(FlagMapMarker data) : IWrapper
    {
        [LuaDocs][Changelog("12.8")] public uint TerritoryId => data.TerritoryId;
        [LuaDocs][Changelog("12.8")] public uint MapId => data.MapId;
        [LuaDocs][Changelog("12.8")] public float XFloat => data.XFloat;
        [LuaDocs][Changelog("12.8")] public float YFloat => data.YFloat;
        [LuaDocs][Changelog("12.8")] public Vector2 Vector2 => new(XFloat, YFloat);
        [LuaDocs][Changelog("12.8")] public Vector3 Vector3 => new(XFloat, 0, YFloat); // TODO use navmesh PointOnFloor

        // C 類 + 動作型(在地圖上插旗)⇒ 取不到代理人記一行錯誤再返回,
        // 與下面那個多載的 territoryId 檢查同一個慣例。
        [LuaDocs]
        [Changelog("12.22")]
        public void SetFlagMapMarker(uint territoryId, uint mapId, float x, float y)
        {
            var agent = GameStatics.AgentChainResolved() ? AgentMap.Instance() : null;
            if (agent == null || !GameStatics.Resolved(AgentMap.MemberFunctionPointers.SetFlagMapMarker, "AgentMap.SetFlagMapMarker"))
            {
                FrameworkLogger.Error("Map agent is unavailable (not logged in?)");
                return;
            }
            agent->SetFlagMapMarker(territoryId, mapId, new Vector3(x, 0, y));
        }

        [LuaDocs]
        [Changelog("12.22")]
        public void SetFlagMapMarker(uint territoryId, float x, float y)
        {
            // territoryId 是 Lua 巨集的參數,也就是使用者輸入;TerritoryType 的 id 中間有空洞,打錯 id 時 GetRow 回 null。
            // 照本檔慣例記一行錯誤後安全返回,不要用 !.Value 把整個巨集打斷。
            var territory = GetRow<Sheets.TerritoryType>(territoryId);
            if (territory == null)
            {
                FrameworkLogger.Error($"Invalid territory ID: {territoryId}");
                return;
            }
            SetFlagMapMarker(territoryId, territory.Value.Map.RowId, x, y);
        }
    }

    public class MapMarkerDataWrapper(MapMarkerData data) : IWrapper
    {
        [LuaDocs][Changelog("12.8")] public uint LevelId => data.LevelId;
        [LuaDocs][Changelog("12.8")] public uint ObjectiveId => data.ObjectiveId;
        // TooltipString 是指標欄位:null 時回空字串,不解參。
        [LuaDocs][Changelog("12.8")] public string TooltipString => data.TooltipString == null ? string.Empty : data.TooltipString->ToString();
        [LuaDocs][Changelog("12.8")] public uint IconId => data.IconId;
        [LuaDocs][Changelog("12.8")] public Vector3 Position => data.Position;
        [LuaDocs][Changelog("12.8")] public float Radius => data.Radius;
        [LuaDocs][Changelog("12.8")] public uint MapId => data.MapId;
        [LuaDocs][Changelog("12.8")] public uint PlaceNameZoneId => data.PlaceNameZoneId;
        [LuaDocs][Changelog("12.8")] public uint PlaceNameId => data.PlaceNameId;
        [LuaDocs][Changelog("12.8")] public int EndTimestamp => data.EndTimestamp;
        [LuaDocs][Changelog("12.8")] public ushort RecommendedLevel => data.RecommendedLevel;
        [LuaDocs][Changelog("12.8")] public ushort TerritoryTypeId => data.TerritoryTypeId;
        [LuaDocs][Changelog("12.8")] public ushort DataId => data.DataId;
        [LuaDocs][Changelog("12.8")] public byte MarkerType => data.MarkerType;
        [LuaDocs][Changelog("12.8")] public sbyte EventState => data.EventState;
        [LuaDocs][Changelog("12.8")] public byte Flags => data.Flags;
    }

    [LuaFunction] public FrameworkWrapper Framework => new();
    public class FrameworkWrapper : IWrapper
    {
        // B 類:Framework 是 [StaticAddress(..., isPointer: true)]。外掛載入時 Framework 幾乎一定已經在了
        // (Dalamud 自己的 Framework 服務就靠它),但「很難走到」不是「不會走到」,而走到的代價是 AVE(攔不到、直接關遊戲)。
        // 三個都是輪詢型存取子 ⇒ 安靜回預設值。
        private static FFXIVClientStructs.FFXIV.Client.System.Framework.Framework* GetFramework()
        {
            var slot = FFXIVClientStructs.FFXIV.Client.System.Framework.Framework.StaticAddressPointers.ppInstance;
            return GameStatics.Resolved(slot, "Framework.Instance") ? *slot : null;
        }

        [LuaDocs][Changelog("12.9")] public long EorzeaTime { get { var f = GetFramework(); if (f == null) return 0L; return f->ClientTime.EorzeaTime; } }

        // ⚠️ 這兩個刻意不回 0:0 是合法值(語言 0 = 日文,區域 0 也真的存在),回 0 等於
        //    謊報一個具體答案。byte.MaxValue 不對應任何合法的語言/區域,巨集拿它去比對
        //    (台服的 ClientLanguage 回報值是 7)一定是 false,不會被誤導。
        [LuaDocs][Changelog("12.9")] public byte ClientLanguage { get { var f = GetFramework(); if (f == null) return byte.MaxValue; return f->ClientLanguage; } }
        [LuaDocs][Changelog("12.9")] public byte Region { get { var f = GetFramework(); if (f == null) return byte.MaxValue; return f->Region; } }
    }

    [LuaFunction] public TelepoWrapper Telepo => new();
    public class TelepoWrapper : IWrapper
    {
        // Telepo 與 UIState 是 A 類,走 GameStatics 判空:傳送(動作型)取不到記一行錯誤後不執行,IsAetheryteUnlocked 回 false。
        // ⚠️ 「傳送清單是空的」是另一回事(那要先 UpdateAetheryteList),不是 null 問題。
        private static FFXIVClientStructs.FFXIV.Client.Game.UI.Telepo* TelepoForTeleport()
        {
            var telepo = GameStatics.TryTelepo();
            if (telepo != null && GameStatics.Resolved(FFXIVClientStructs.FFXIV.Client.Game.UI.Telepo.MemberFunctionPointers.Teleport, "Telepo.Teleport")) return telepo;
            FrameworkLogger.Error("Teleport is unavailable; nothing was done");
            return null;
        }

        [LuaDocs][Changelog("12.18")] public void Teleport(IAetheryteEntry aetheryte) { var telepo = TelepoForTeleport(); if (telepo != null) telepo->Teleport(aetheryte.AetheryteId, aetheryte.SubIndex); }
        [LuaDocs][Changelog("12.18")] public void Teleport(uint aetheryteId, byte subIndex) { var telepo = TelepoForTeleport(); if (telepo != null) telepo->Teleport(aetheryteId, subIndex); }
        [LuaDocs][Changelog("12.18")] public Vector3 GetAetherytePosition(uint aetheryteId) => ECommons.GameHelpers.Map.AetherytePosition(aetheryteId);
        [LuaDocs][Changelog("12.18")] public bool IsAetheryteUnlocked(uint aetheryteId) { var ui = GameStatics.TryUIState(); return ui != null && ui->IsAetheryteUnlocked(aetheryteId); }
    }

    [LuaFunction] public EnvManagerWrapper EnvManager => new();
    public class EnvManagerWrapper : IWrapper
    {
        // B 類:EnvManager 是 [StaticAddress(..., isPointer: true)] —— 圖形環境管理器在標題畫面/讀取畫面尚未建立時
        // Instance() 回 null。這五個正是巨集最常拿去寫「等某個天氣」迴圈的欄位,每幀輪詢,
        // ⇒ 一律安靜回預設值,不記 log。
        private static FFXIVClientStructs.FFXIV.Client.Graphics.Environment.EnvManager* GetEnvManager()
        {
            var slot = FFXIVClientStructs.FFXIV.Client.Graphics.Environment.EnvManager.StaticAddressPointers.ppInstance;
            return GameStatics.Resolved(slot, "EnvManager.Instance") ? *slot : null;
        }

        [LuaDocs][Changelog("12.20")] public float DayTimeSeconds { get { var e = GetEnvManager(); if (e == null) return 0f; return e->DayTimeSeconds; } }
        [LuaDocs][Changelog("12.20")] public float ActiveTransitionTime { get { var e = GetEnvManager(); if (e == null) return 0f; return e->ActiveTransitionTime; } }
        [LuaDocs][Changelog("12.20")] public float CurrentTransitionTime { get { var e = GetEnvManager(); if (e == null) return 0f; return e->CurrentTransitionTime; } }

        // Weather 表第 0 列是空白列(Name / Description 皆空、Icon = 0),
        // 所以 0 本來就是「沒有天氣」,拿它當取不到時的預設值不會和任何真實天氣撞號。
        [LuaDocs][Changelog("12.20")] public byte ActiveWeather { get { var e = GetEnvManager(); if (e == null) return 0; return e->ActiveWeather; } }
        [LuaDocs][Changelog("12.20")] public float TransitionTime { get { var e = GetEnvManager(); if (e == null) return 0f; return e->TransitionTime; } }
    }

    [LuaFunction][Changelog("12.22")] public BuddyWrapper Buddy => new();
}
