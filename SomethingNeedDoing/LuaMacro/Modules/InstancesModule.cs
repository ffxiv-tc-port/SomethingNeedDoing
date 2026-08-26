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
        // ── Instance() 的四種形態,決定要不要判空 ─────────────────────────────────
        // 這份分類適用整個檔案(以及任何抄這裡寫法的地方)。判別依據是 CS 那一側的宣告,
        // 不是型別名字看起來像什麼:
        //
        //  A. [StaticAddress(sig, off)] 沒有 isPointer → 產生器出來的是 `return pInstance;`,
        //     那是特徵碼解出來的靜態位址本身(組語是 lea rcx, [rip+x]),永遠不是 null;
        //     特徵碼失配時走的是 ThrowHelper.ThrowNullAddress(擲 InvalidOperationException),
        //     也不是回 null。⇒ 對 A 類判空是死碼,不要加。
        //     本檔的 ContentsFinder / UIState / Telepo 屬此類。
        //  B. [StaticAddress(sig, off, isPointer: true)] → 產生器出來的是 `return *ppInstance;`,
        //     解出來的是「存放指標的位址」,解參考結果會是 null(物件還沒建立)。⇒ 必須判。
        //     本檔的 Framework / EnvManager 屬此類。
        //  C. [Agent(...)] 產生的、以及 CS 裡手寫的 Instance() → 實作逐字帶 `== null ? null :`
        //     (先走 AgentModule.Instance() → UIModule,未登入時是 null)。⇒ 必須判。
        //     本檔的 AgentContentsFinder / AgentMap 屬此類。
        //  D. Instance() 之後再往下走的成員(GetQueueInfo() / CurrentFate / InfoProxy …)
        //     各自有各自的 null 路徑,要分開判,與 Instance() 屬哪一類無關。
        //
        // 失敗語意的分界(照 d605137 既有慣例):**使用者明確呼叫的動作型方法**取不到就記一行
        // 錯誤(安靜失敗會讓人以為指令送出去了);**巨集會放在等待迴圈裡輪詢的存取子**安靜回
        // 預設值(每幀記一行會把整份 log 洗掉)。

        // C 類 + 動作型 ⇒ 記錯誤。
        [LuaDocs]
        public void OpenRouletteDuty(byte contentRouletteID)
        {
            var agent = AgentContentsFinder.Instance();
            if (agent == null)
            {
                FrameworkLogger.Error("Duty finder agent is unavailable (not logged in?)");
                return;
            }
            agent->OpenRouletteDuty(contentRouletteID);
        }

        [LuaDocs]
        public void OpenRegularDuty(uint contentsFinderCondition)
        {
            var agent = AgentContentsFinder.Instance();
            if (agent == null)
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
            var QueueInfo = ContentsFinder.Instance()->GetQueueInfo();
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
            var QueueInfo = ContentsFinder.Instance()->GetQueueInfo();
            if (QueueInfo->QueueState is ContentsFinderQueueInfo.QueueStates.Pending or ContentsFinderQueueInfo.QueueStates.Queued) QueueInfo->CancelQueue();
            QueueInfo->QueueRoulette(contentRouletteId);
        }

        // 以下到本類別結尾的 ContentsFinder 與 UIState 全是 A 類,Instance() 永遠不是 null,
        // 所以刻意不加判空(加了是死碼)。
        // GetQueueInfo() 這一層(D 類)同樣不必判:2026-08-19 離線反組譯台服執行檔,
        // 該函式逐字只有兩條指令 —— lea rax, [rcx + 0x20] / ret,也就是 return &this->QueueInfo,
        // 對得上 CS 的 [FieldOffset(0x20)] ContentsFinderQueueInfo QueueInfo,不存在回 null 的路徑。
        // ⚠️ 台服改版後這個結論要重驗(偏移或函式形狀變了就不成立)。
        [LuaDocs][Changelog("12.69")] public void CancelQueue() => ContentsFinder.Instance()->GetQueueInfo()->CancelQueue();
        [LuaDocs][Changelog("12.73")] public uint GetPenaltyTimeRemainingInMinutes() => UIState.Instance()->InstanceContent.GetPenaltyRemainingInMinutes(0);
        [LuaDocs][Changelog("12.73")] public bool IsRouletteIncomplete(byte rouletteId) => UIState.Instance()->InstanceContent.IsRouletteIncomplete(rouletteId);

        [LuaDocs] public bool IsUnrestrictedParty { get => ContentsFinder.Instance()->IsUnrestrictedParty; set => ContentsFinder.Instance()->IsUnrestrictedParty = value; }
        [LuaDocs] public bool IsLevelSync { get => ContentsFinder.Instance()->IsLevelSync; set => ContentsFinder.Instance()->IsLevelSync = value; }
        [LuaDocs] public bool IsMinIL { get => ContentsFinder.Instance()->IsMinimalIL; set => ContentsFinder.Instance()->IsMinimalIL = value; }
        [LuaDocs] public bool IsSilenceEcho { get => ContentsFinder.Instance()->IsSilenceEcho; set => ContentsFinder.Instance()->IsSilenceEcho = value; }
        [LuaDocs] public bool IsExplorerMode { get => ContentsFinder.Instance()->IsExplorerMode; set => ContentsFinder.Instance()->IsExplorerMode = value; }
        [LuaDocs] public bool IsLimitedLevelingRoulette { get => ContentsFinder.Instance()->IsLimitedLevelingRoulette; set => ContentsFinder.Instance()->IsLimitedLevelingRoulette = value; }
        [LuaDocs] public ContentsFinderQueueInfo.QueueStates QueueState => ContentsFinder.Instance()->GetQueueInfo()->QueueState;
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

                // 這是 [LuaFunction] 底下的存取子,使用者巨集可以在任意時機呼叫(未登入、好友
                // 名單代理人還沒建立、換區途中都算),原本整條 AgentFriendlist.Instance()->
                // InfoProxy->CharDataSpan 是三層裸讀,三層各自都有真實的 null 路徑:
                //  - AgentFriendlist.Instance() 走 AgentModule.Instance(),UIModule 尚未建立時回 null
                //    (產生器出來的實作逐字是 agentModule == null ? null : ...);
                //  - InfoProxy 是欄位 +0x28,好友名單資訊代理人還沒掛上時是 null;
                //  - CharDataSpan = new ReadOnlySpan<>(CharData, EntryCount) —— CharData 為 null
                //    而 EntryCount 非 0 時會做出一個指向位址 0 的 span,建構不報錯、取用才 AVE。
                // 任一層取不到就回空清單(照本模組既有慣例:記一行錯誤後回預設值)。
                var agentFriendlist = AgentFriendlist.Instance();
                if (agentFriendlist == null || agentFriendlist->InfoProxy == null)
                {
                    FrameworkLogger.Error("Friend list is unavailable (agent or info proxy not ready)");
                    return friends;
                }

                var infoProxy = agentFriendlist->InfoProxy;

                // 名單一次都沒載入過時 CharData 是 null。這不是錯誤,安靜回空清單即可
                // (原本 EntryCount 剛好是 0 時也是走到同樣的結果)。
                if (infoProxy->CharData == null)
                    return friends;

                // 只解析一次 span;原本迴圈每次迭代都重跑整條鏈兩遍。
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
                var agent = AgentMap.Instance();
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
                var agent = AgentMap.Instance();
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
            var agent = AgentMap.Instance();
            if (agent == null)
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
            // territoryId 是 Lua 巨集的參數,也就是使用者輸入。台服 TerritoryType 的 id 是
            // 1..1333,但中間有 4 段空洞(23、32-127、165、173),1333 個 id 只有 1234 個真的存在。
            // 打錯 id 時 GetRow 會回 null,原本的 !.Value 會擲 InvalidOperationException 把整個
            // 巨集打斷。改成照本檔既有慣例記一行錯誤後安全返回。
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
        [LuaDocs][Changelog("12.8")] public string TooltipString => data.TooltipString->ToString();
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
        // B 類:Framework 是 [StaticAddress("48 8B 1D ?? ?? ?? ?? 8B 7C 24 64", 3, isPointer: true)],
        // 產生器出來的是 return *ppInstance;。外掛載入時 Framework 幾乎一定已經在了
        // (Dalamud 自己的 Framework 服務就靠它),所以這條路很難走到 —— 但「很難走到」
        // 不是「不會走到」,而走到的代價是 AVE(攔不到、直接關遊戲)。
        // 三個都是輪詢型存取子 ⇒ 安靜回預設值。
        private static FFXIVClientStructs.FFXIV.Client.System.Framework.Framework* GetFramework() => FFXIVClientStructs.FFXIV.Client.System.Framework.Framework.Instance();

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
        // Telepo 與 UIState 都是 A 類([StaticAddress] 沒有 isPointer),Instance() 永遠不是
        // null,所以這三個刻意不加判空 —— 加了是死碼。
        // ⚠️ 「傳送清單是空的」是另一回事(那要先 UpdateAetheryteList),不是 null 問題,本次不動。
        [LuaDocs][Changelog("12.18")] public void Teleport(IAetheryteEntry aetheryte) => FFXIVClientStructs.FFXIV.Client.Game.UI.Telepo.Instance()->Teleport(aetheryte.AetheryteId, aetheryte.SubIndex);
        [LuaDocs][Changelog("12.18")] public void Teleport(uint aetheryteId, byte subIndex) => FFXIVClientStructs.FFXIV.Client.Game.UI.Telepo.Instance()->Teleport(aetheryteId, subIndex);
        [LuaDocs][Changelog("12.18")] public Vector3 GetAetherytePosition(uint aetheryteId) => ECommons.GameHelpers.Map.AetherytePosition(aetheryteId);
        [LuaDocs][Changelog("12.18")] public bool IsAetheryteUnlocked(uint aetheryteId) => UIState.Instance()->IsAetheryteUnlocked(aetheryteId);
    }

    [LuaFunction] public EnvManagerWrapper EnvManager => new();
    public class EnvManagerWrapper : IWrapper
    {
        // B 類:EnvManager 是 [StaticAddress("0F 28 F2 48 8B 05", 6, isPointer: true)],
        // 產生器出來的是 return *ppInstance; —— 圖形環境管理器在標題畫面/讀取畫面尚未建立
        // 時解參考結果是 null。這五個正是巨集最常拿去寫「等某個天氣」迴圈的欄位,每幀輪詢,
        // ⇒ 一律安靜回預設值,不記 log。
        private static FFXIVClientStructs.FFXIV.Client.Graphics.Environment.EnvManager* GetEnvManager() => FFXIVClientStructs.FFXIV.Client.Graphics.Environment.EnvManager.Instance();

        [LuaDocs][Changelog("12.20")] public float DayTimeSeconds { get { var e = GetEnvManager(); if (e == null) return 0f; return e->DayTimeSeconds; } }
        [LuaDocs][Changelog("12.20")] public float ActiveTransitionTime { get { var e = GetEnvManager(); if (e == null) return 0f; return e->ActiveTransitionTime; } }
        [LuaDocs][Changelog("12.20")] public float CurrentTransitionTime { get { var e = GetEnvManager(); if (e == null) return 0f; return e->CurrentTransitionTime; } }

        // 台服 7.20 的 Weather 表第 0 列是空白列(Name / Description 皆空、Icon = 0),
        // 所以 0 本來就是「沒有天氣」,拿它當取不到時的預設值不會和任何真實天氣撞號。
        [LuaDocs][Changelog("12.20")] public byte ActiveWeather { get { var e = GetEnvManager(); if (e == null) return 0; return e->ActiveWeather; } }
        [LuaDocs][Changelog("12.20")] public float TransitionTime { get { var e = GetEnvManager(); if (e == null) return 0f; return e->TransitionTime; } }
    }

    [LuaFunction][Changelog("12.22")] public BuddyWrapper Buddy => new();
}
