using AutoRetainerAPI.Configuration;
using ECommons.EzIpcManager;
using SomethingNeedDoing.Core.Interfaces;
using SomethingNeedDoing.Services;
using GCInfo = (uint ShopDataID, uint ExchangeDataID, System.Numerics.Vector3 Position);

namespace SomethingNeedDoing.External;

public class AutoRetainer : IPC
{
    public override string Name => "AutoRetainer";
    public override string Repo => Repos.TcPort;

    [EzIPC]
    [LuaFunction(description: "Gets whether multi-mode is enabled")]
    public readonly Func<bool> GetMultiModeEnabled = null!;

    [EzIPC]
    [LuaFunction(
        description: "Sets whether multi-mode is enabled",
        parameterDescriptions: ["enabled"])]
    public readonly Action<bool> SetMultiModeEnabled = null!;

    [EzIPC("PluginState.%m")]
    [LuaFunction(description: "Checks if the plugin is busy")]
    public readonly Func<bool> IsBusy = null!;

    [EzIPC("PluginState.%m")]
    [LuaFunction(description: "Gets the number of free inventory slots")]
    public readonly Func<int> GetInventoryFreeSlotCount = null!;

    [EzIPC("PluginState.%m")]
    [LuaFunction(description: "Gets all enabled retainers")]
    public readonly Func<Dictionary<ulong, HashSet<string>>> GetEnabledRetainers = null!;

    [EzIPC("PluginState.%m")]
    [LuaFunction(description: "Checks if any retainers are available for the current character")]
    public readonly Func<bool> AreAnyRetainersAvailableForCurrentChara = null!;

    [EzIPC("PluginState.%m")]
    [LuaFunction(description: "Aborts all tasks")]
    public readonly Action AbortAllTasks = null!;

    [EzIPC("PluginState.%m")]
    [LuaFunction(description: "Disables all functions")]
    public readonly Action DisableAllFunctions = null!;

    [EzIPC("PluginState.%m")]
    [LuaFunction(description: "Enables multi-mode")]
    public readonly Action EnableMultiMode = null!;

    /// <summary>
    /// Action onFailure
    /// </summary>
    [EzIPC("PluginState.%m")]
    [LuaFunction(
        description: "Enqueues a high-end task",
        parameterDescriptions: ["onFailure"])]
    public readonly Action<Action> EnqueueHET = null!;

    [EzIPC("PluginState.%m")]
    [LuaFunction(description: "Checks if auto-login is possible")]
    public readonly Func<bool> CanAutoLogin = null!;

    /// <summary>
    /// string charaNameWithWorld
    /// </summary>
    [EzIPC("PluginState.%m")]
    [LuaFunction(
        description: "Relogs to a specific character",
        parameterDescriptions: ["charaNameWithWorld"])]
    public readonly Func<string, bool> Relog = null!;

    [EzIPC("PluginState.%m")]
    [LuaFunction(description: "Gets whether retainer sense is enabled")]
    public readonly Func<bool> GetOptionRetainerSense = null!;

    [EzIPC("PluginState.%m")]
    [LuaFunction(
        description: "Sets whether retainer sense is enabled",
        parameterDescriptions: ["enabled"])]
    public readonly Action<bool> SetOptionRetainerSense = null!;

    [EzIPC("PluginState.%m")]
    [LuaFunction(description: "Gets the retainer sense threshold")]
    public readonly Func<int> GetOptionRetainerSenseThreshold = null!;

    [EzIPC("PluginState.%m")]
    [LuaFunction(
        description: "Sets the retainer sense threshold",
        parameterDescriptions: ["threshold"])]
    public readonly Action<int> SetOptionRetainerSenseThreshold = null!;

    /// <summary>
    /// ulong CID
    /// </summary>
    [EzIPC("PluginState.%m")]
    [LuaFunction(
        description: "Gets the closest retainer venture seconds remaining",
        parameterDescriptions: ["cid"])]
    public readonly Func<ulong, long?> GetClosestRetainerVentureSecondsRemaining = null!;

    [EzIPC("PluginState.%m")]
    [LuaFunction(description: "Retrieves one item slot from the currently open retainer's inventory into the player's own bags, without waiting to confirm it landed. Slots that already have a retrieve command in flight are skipped, so looping this fires roughly one command per slot. Returns false once nothing is left, the player's inventory is nearly full, or every remaining slot is already in flight; loop this with a short yield between calls, then let the inventory settle and start a new round.")]
    public readonly Func<bool> RetrieveNextRetainerItemSlot = null!;

    [EzIPC("PluginState.%m")]
    [LuaFunction(description: "Forgets which retainer slots RetrieveNextRetainerItemSlot already fired at, so the next call considers every occupied slot again. Call at the start of each retrieval sweep. Older AutoRetainer builds do not provide this method, so wrap the call in pcall.")]
    public readonly Action ResetRetainerRetrieveTracking = null!;

    // ── 指定道具的取回介面 ────────────────────────────────────────────────
    // RetrieveNextRetainerItemSlot 取的是「第一個有東西的格子」,想只取特定道具(例如只把裝備
    // 拿回來交稀有品)的巨集用不上。AutoRetainer 那端另外提供了一組以道具 ID 為單位的介面,
    // 這裡把它接出來。⚠️ 舊版 AutoRetainer 沒有這三個方法,呼叫端要先問 API 版本(用 pcall 包住)。

    [EzIPC("PluginState.%m")]
    [LuaFunction(description: "Version of AutoRetainer's specific-item retainer retrieve API (RetrieveRetainerItemSlotById / GetOpenRetainerItemQuantity). Older builds do not provide this method at all, so wrap the call in pcall and treat a failure as 'not supported'.")]
    public readonly Func<int> GetRetainerItemRetrieveApiVersion = null!;

    /// <summary>
    /// uint itemId, bool hqOnly, bool includeCrystals
    /// </summary>
    [EzIPC("PluginState.%m")]
    [LuaFunction(
        description: "Fires one retrieve command at the first slot of the currently open retainer holding the given item, into the player's own bags. Always takes the WHOLE slot. Returns the quantity aimed at (>= 1) on success, or: 0 = proved absent, -1 = retainer storage could not be read (NOT 'absent'), -2 = every matching slot already has a command in flight (let it settle and retry), -3 = player bags at or below AutoRetainer's reserve, -4 = unique item the player already owns (will never succeed, skip it), -5 = only present in the crystal container and includeCrystals was false.",
        parameterDescriptions: ["itemId", "hqOnly", "includeCrystals"])]
    public readonly Func<uint, bool, bool, int> RetrieveRetainerItemSlotById = null!;

    /// <summary>
    /// uint itemId, bool hqOnly, bool includeCrystals
    /// </summary>
    [EzIPC("PluginState.%m")]
    [LuaFunction(
        description: "How many of the given item the currently open retainer holds. Returns the total (0 meaning proved absent), or -1 when the retainer's storage could not be read - -1 is 'unknown', not 'none'.",
        parameterDescriptions: ["itemId", "hqOnly", "includeCrystals"])]
    public readonly Func<uint, bool, bool, int> GetOpenRetainerItemQuantity = null!;

    // ── 用 AutoRetainer 自己的任務鏈,不要在巨集裡重造 addon 操作 ──────────
    // 下面四個門後面是 AutoRetainer 正式流程每天在跑的那條鏈(走到鈴前、開雇員清單、選雇員、
    // 開道具管理、去大國防聯軍繳交)。巨集自己拼這些需要一串寫死的 callback 參數與選單索引 ——
    // 離線驗不了、改版靜默失效、選單文字還隨語系不同。
    // ⚠️ 這些都是 Enqueue:呼叫後任務進佇列就回來了,要自己輪詢 IsBusy() 等它做完。

    [EzIPC("PluginState.%m")]
    [LuaFunction(
        description: "Enqueues AutoRetainer's own chain to walk to the nearest summoning bell, open it, select the named retainer and open their item storage. Returns false without enqueuing anything when a precondition fails (no such retainer on this character, player unavailable, AutoRetainer already busy) - stop rather than waiting out a timeout in that case. This is asynchronous: poll IsBusy until it goes false.",
        parameterDescriptions: ["retainerName"])]
    public readonly Func<string, bool> EnqueueOpenRetainerItemStorage = null!;

    [EzIPC("PluginState.%m")]
    [LuaFunction(description: "Enqueues closing whatever retainer UI is open, back out to the world. Safe to call when nothing is open. Asynchronous: poll IsBusy until it goes false.")]
    public readonly Action EnqueueCloseRetainer = null!;

    [EzIPC("PluginState.%m")]
    [LuaFunction(description: "Enqueues the same flow as AutoRetainer's Deliver Items button: Lifestream navigates to the Grand Company if needed, then AutoRetainer interacts with the NPC, opens the supply list on the expert delivery tab and turns automatic handin on. ⚠️ This is the full flow, so it also runs the seal-spending purchase step. Returns false without enqueuing when the character has no Grand Company or something is already busy.")]
    public readonly Func<bool> EnqueueGCDeliverItems = null!;

    [EzIPC("PluginState.%m")]
    [LuaFunction(description: "Retainer names of the current character that have an entrust plan assigned in AutoRetainer, in AutoRetainer's own order. Lets a macro act on 'the retainers the user configured' instead of a hardcoded list.")]
    public readonly Func<List<string>> GetRetainersWithEntrustPlan = null!;

    [EzIPC("GC.%m")]
    [LuaFunction(description: "Enqueues initiation")]
    public readonly Action EnqueueInitiation = null!;

    [EzIPC("GC.%m")]
    [LuaFunction(description: "Gets GC information")]
    public readonly Func<GCInfo?> GetGCInfo = null!;

    [LuaFunction(description: "Gets all registered characters")]
    [Changelog("12.19")]
    public List<ulong> GetRegisteredCharacters() => StaticsService.AutoRetainerApi.GetRegisteredCharacters();

    [LuaFunction(
        description: "Gets offline character data for a specific character ID",
        parameterDescriptions: ["cid"])]
    [Changelog("12.19")]
    public OfflineCharacterDataWrapper GetOfflineCharacterData(ulong cid) => new(StaticsService.AutoRetainerApi.GetOfflineCharacterData(cid));

    // 這是「哪些雇員被使用者掛了設定」的唯一可讀來源。OfflineCharacterData 只有雇員的名字與探險
    // 狀態,使用者在 AutoRetainer 裡對個別雇員做的設定(存放計畫、存入重複品、提金幣…)在另一份
    // AdditionalRetainerData 裡,以 (角色 CID, 雇員名) 為鍵。巨集想「只處理掛了某個存放計畫的雇員」
    // 就得靠它。
    [LuaFunction(
        description: "Gets AutoRetainer's per-retainer settings (entrust plan, entrust duplicates, gil handling) for one retainer of one character, keyed by character CID and retainer name.",
        parameterDescriptions: ["cid", "retainerName"])]
    public AdditionalRetainerDataWrapper GetAdditionalRetainerData(ulong cid, string retainerName) => new(StaticsService.AutoRetainerApi.GetAdditionalRetainerData(cid, retainerName));

    public class AdditionalRetainerDataWrapper(AdditionalRetainerData data) : IWrapper
    {
        /// <summary>Guid 對 Lua 沒有意義,一律以字串呈現。空計畫是 Guid.Empty,不是 null。</summary>
        [LuaDocs(description: "The entrust plan's id as a string. All-zero (\"00000000-0000-0000-0000-000000000000\") means no plan is assigned - test HasEntrustPlan instead of comparing strings.")]
        public string EntrustPlanId => data.EntrustPlan.ToString();

        [LuaDocs(description: "Whether this retainer has an entrust plan assigned in AutoRetainer.")]
        public bool HasEntrustPlan => data.EntrustPlan != Guid.Empty;

        [LuaDocs] public bool EntrustDuplicates => data.EntrustDuplicates;
        [LuaDocs] public bool WithdrawGil => data.WithdrawGil;
        [LuaDocs] public bool Deposit => data.Deposit;
        [LuaDocs] public bool EnablePlanner => data.EnablePlanner;
        [LuaDocs] public int Ilvl => data.Ilvl;
    }

    public class OfflineCharacterDataWrapper(OfflineCharacterData data) : IWrapper
    {
        [LuaDocs][Changelog("12.19")] public ulong CID => data.CID;
        [LuaDocs][Changelog("12.19")] public string Name => data.Name;
        [LuaDocs][Changelog("12.19")] public string World => data.World;
        [LuaDocs][Changelog("12.19")] public bool Enabled => data.Enabled;
        [LuaDocs][Changelog("12.19")] public List<OfflineRetainerDataWrapper> RetainerData => [.. data.RetainerData.Select(x => new OfflineRetainerDataWrapper(x))];
        [LuaDocs][Changelog("12.19")] public uint InventorySpace => data.InventorySpace;
        [LuaDocs][Changelog("12.19")] public uint VentureCoffers => data.VentureCoffers;
        [LuaDocs][Changelog("12.19")] public uint Gil => data.Gil;
        [LuaDocs][Changelog("12.19")] public List<OfflineVesselDataWrapper> OfflineAirshipData => [.. data.OfflineAirshipData.Select(x => new OfflineVesselDataWrapper(x))];
        [LuaDocs][Changelog("12.19")] public List<OfflineVesselDataWrapper> OfflineSubmarineData => [.. data.OfflineSubmarineData.Select(x => new OfflineVesselDataWrapper(x))];
        [LuaDocs][Changelog("12.19")] public int Ceruleum => data.Ceruleum;
        [LuaDocs][Changelog("12.19")] public int RepairKits => data.RepairKits;
        [LuaDocs][Changelog("12.19")] public bool RetainersAwaitingProcessing => RetainerData.Any(x => x.HasVenture && x.VentureEndsAt <= TimeProvider.System.GetUtcNow().ToUnixTimeSeconds());
        [LuaDocs][Changelog("12.19")] public bool SubsAwaitingProcessing => OfflineSubmarineData.Any(x => x.ReturnTime <= TimeProvider.System.GetUtcNow().ToUnixTimeSeconds());
        [LuaDocs][Changelog("12.19")] public bool AnyAwaitingProcessing => RetainersAwaitingProcessing || SubsAwaitingProcessing;
    }

    public class OfflineRetainerDataWrapper(OfflineRetainerData data) : IWrapper
    {
        [LuaDocs][Changelog("12.19")] public string Name => data.Name;
        [LuaDocs][Changelog("12.19")] public long VentureEndsAt => data.VentureEndsAt;
        [LuaDocs][Changelog("12.19")] public bool HasVenture => data.HasVenture;
        [LuaDocs][Changelog("12.19")] public int Level => data.Level;
        [LuaDocs][Changelog("12.19")] public long VentureBeginsAt => data.VentureBeginsAt;
        [LuaDocs][Changelog("12.19")] public uint Job => data.Job;
        [LuaDocs][Changelog("12.19")] public uint VentureID => data.VentureID;
        [LuaDocs][Changelog("12.19")] public uint Gil => data.Gil;
        [LuaDocs][Changelog("12.19")] public ulong RetainerID => data.RetainerID;
        [LuaDocs][Changelog("12.19")] public int MBItems => data.MBItems;
    }

    public class OfflineVesselDataWrapper(OfflineVesselData data) : IWrapper
    {
        [LuaDocs][Changelog("12.19")] public string Name => data.Name;
        [LuaDocs][Changelog("12.19")] public uint ReturnTime => data.ReturnTime;
    }
}
