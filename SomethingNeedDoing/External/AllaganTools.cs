using ECommons.EzIpcManager;
using FFXIVClientStructs.FFXIV.Client.Game;
using SomethingNeedDoing.Core.Interfaces;
using System.Threading;

namespace SomethingNeedDoing.External;
public class AllaganTools : IPC
{
    public override string Name => "InventoryTools";
    public override string Repo => Repos.FirstParty;
    private const string _ipcName = "AllaganTools";

    public AllaganTools()
    {
        // 🔑 EzIPC.Init 對「事件訂閱失敗」只寫一行 Error 就繼續跑,失敗之後全程無聲。
        //    這裡把「實際訂到了哪些事件端點」用 Information 記下來(使用者的 LogLevel 收得到),
        //    要使用者回報時可以直接指這一行,不必請他去翻 Debug。
        var events = IpcTokens.Where(t => t.IsEvent).Select(t => t.IpcTag).ToArray();
        if (events.Length == ExpectedEventCount)
            FrameworkLogger.Info($"{_ipcName} 事件訂閱成功 ({events.Length}/{ExpectedEventCount}): {string.Join(", ", events)}");
        else
            FrameworkLogger.Info($"{_ipcName} 事件只訂到 {events.Length}/{ExpectedEventCount} 個: [{string.Join(", ", events)}] —— 缺少的那些事件不會作用。");
    }

    /// <summary>本 class 預期要訂閱的 AllaganTools 事件端點數量,只用在啟動時的自我檢查訊息。</summary>
    private const int ExpectedEventCount = 3;

    [EzIPC($"{_ipcName}.%m", false)]
    [LuaFunction(
        description: "Gets the count of items in a specific inventory type",
        parameterDescriptions: ["inventoryTypeId", "contentId"])]
    public readonly Func<uint, ulong?, uint> InventoryCountByType = null!;

    [EzIPC($"{_ipcName}.%m", false)]
    [LuaFunction(
        description: "Gets the count of items across multiple inventory types",
        parameterDescriptions: ["inventoryTypeIds", "contentId"])]
    public readonly Func<uint[], ulong?, uint> InventoryCountByTypes = null!;

    [EzIPC($"{_ipcName}.%m", false)]
    [LuaFunction(
        description: "Gets the count of a specific item in a specific inventory",
        parameterDescriptions: ["itemId", "contentId", "inventoryTypeId"])]
    public readonly Func<uint, ulong, int, uint> ItemCount = null!;

    [EzIPC($"{_ipcName}.%m", false)]
    [LuaFunction(
        description: "Gets the count of a specific high-quality item in a specific inventory",
        parameterDescriptions: ["itemId", "contentId", "inventoryTypeId"])]
    public readonly Func<uint, ulong, int, uint> ItemCountHQ = null!;

    [EzIPC($"{_ipcName}.%m", false)]
    [LuaFunction(
        description: "Gets the count of a specific item across multiple inventories",
        parameterDescriptions: ["itemId", "onlyCurrentCharacter", "inventoryTypeIds"])]
    public readonly Func<uint, bool, uint[], uint> ItemCountOwned = null!;

    [EzIPC($"{_ipcName}.%m", false)]
    [LuaFunction(
        description: "Enables a UI filter",
        parameterDescriptions: ["filterKey"])]
    public readonly Func<string, bool> EnableUiFilter = null!;

    [EzIPC($"{_ipcName}.%m", false)]
    [LuaFunction(description: "Disables all UI filters")]
    public readonly Func<bool> DisableUiFilter = null!;

    [EzIPC($"{_ipcName}.%m", false)]
    [LuaFunction(
        description: "Toggles a UI filter on/off",
        parameterDescriptions: ["filterKey"])]
    public readonly Func<string, bool> ToggleUiFilter = null!;

    [EzIPC($"{_ipcName}.%m", false)]
    [LuaFunction(
        description: "Enables a background filter",
        parameterDescriptions: ["filterKey"])]
    public readonly Func<string, bool> EnableBackgroundFilter = null!;

    [EzIPC($"{_ipcName}.%m", false)]
    [LuaFunction(description: "Disables all background filters")]
    public readonly Func<bool> DisableBackgroundFilter = null!;

    [EzIPC($"{_ipcName}.%m", false)]
    [LuaFunction(
        description: "Toggles a background filter on/off",
        parameterDescriptions: ["filterKey"])]
    public readonly Func<string, bool> ToggleBackgroundFilter = null!;

    [EzIPC($"{_ipcName}.%m", false)]
    [LuaFunction(
        description: "Enables a craft list",
        parameterDescriptions: ["filterKey"])]
    public readonly Func<string, bool> EnableCraftList = null!;

    [EzIPC($"{_ipcName}.%m", false)]
    [LuaFunction(description: "Disables all craft lists")]
    public readonly Func<bool> DisableCraftList = null!;

    [EzIPC($"{_ipcName}.%m", false)]
    [LuaFunction(
        description: "Toggles a craft list on/off",
        parameterDescriptions: ["filterKey"])]
    public readonly Func<string, bool> ToggleCraftList = null!;

    [EzIPC($"{_ipcName}.%m", false)]
    [LuaFunction(
        description: "Adds an item to a craft list",
        parameterDescriptions: ["filterKey", "itemId", "quantity"])]
    public readonly Func<string, uint, uint, bool> AddItemToCraftList = null!;

    [EzIPC($"{_ipcName}.%m", false)]
    [LuaFunction(
        description: "Removes an item from a craft list",
        parameterDescriptions: ["filterKey", "itemId", "quantity"])]
    public readonly Func<string, uint, uint, bool> RemoveItemFromCraftList = null!;

    [EzIPC($"{_ipcName}.%m", false)]
    [LuaFunction(
        description: "Gets all items in a filter",
        parameterDescriptions: ["filterKey"])]
    public readonly Func<string, Dictionary<uint, uint>> GetFilterItems = null!;

    [EzIPC($"{_ipcName}.%m", false)]
    [LuaFunction(
        description: "Gets all items in a craft list",
        parameterDescriptions: ["filterKey"])]
    public readonly Func<string, Dictionary<uint, uint>> GetCraftItems = null!;

    [EzIPC($"{_ipcName}.%m", false)]
    [LuaFunction(description: "Gets all items in the retrieval list")]
    public readonly Func<Dictionary<uint, uint>> GetRetrievalItems = null!;

    [EzIPC($"{_ipcName}.%m", false)]
    [LuaFunction(
        description: "Gets all items owned by a character",
        parameterDescriptions: ["contentId"])]
    public readonly Func<ulong, HashSet<ulong[]>> GetCharacterItems = null!;

    [EzIPC($"{_ipcName}.%m", false)]
    [LuaFunction(
        description: "Gets all characters owned by the active character",
        parameterDescriptions: ["includeOwner"])]
    public readonly Func<bool, HashSet<ulong>> GetCharactersOwnedByActive = null!;

    [EzIPC($"{_ipcName}.%m", false)]
    [LuaFunction(
        description: "Gets all items of a specific type owned by a character",
        parameterDescriptions: ["contentId", "inventoryTypeId"])]
    public readonly Func<ulong, uint, HashSet<ulong[]>> GetCharacterItemsByType = null!;

    [EzIPC($"{_ipcName}.%m", false)]
    [LuaFunction(description: "Gets all craft lists")]
    public readonly Func<Dictionary<string, string>> GetCraftLists = null!;

    [EzIPC($"{_ipcName}.%m", false)]
    [LuaFunction(description: "Gets all search filters")]
    public readonly Func<Dictionary<string, string>> GetSearchFilters = null!;

    [EzIPC($"{_ipcName}.%m", false)]
    [LuaFunction(
        description: "Adds a new craft list",
        parameterDescriptions: ["craftList", "itemsToAdd"])]
    public readonly Func<string, Dictionary<uint, uint>, string> AddNewCraftList = null!;

    [EzIPC($"{_ipcName}.%m", false)]
    [LuaFunction(description: "Gets the current character ID")]
    public readonly Func<ulong?> CurrentCharacter = null!;

    [EzIPC($"{_ipcName}.%m", false)]
    [LuaFunction(description: "Checks if the plugin is initialized")]
    public readonly Func<bool> IsInitialized = null!;

    // ── AllaganTools 事件端點 ────────────────────────────────────────────────
    // 🔴 這三個事件以前是標了 [EzIPCEvent] 的 Func<...,bool>「欄位」,從來沒有生效過。
    //    三個獨立的錯誤,任何一個單獨都足以讓它失效:
    //    ① 方向錯:ECommons 對「欄位 + [EzIPCEvent]」走的是「提供端」(把欄位綁成
    //       SendMessage 的發送器);我們要的是「訂閱端」——發送方是 AllaganTools 不是 SND。
    //       (ECommons/EzIpcManager/EzIPC.cs:210-245 是提供端,:165-207 才是訂閱端。)
    //    ② 型別錯:提供端要求委派回傳 void(EzIPC.cs:222 的 delegateReturn == typeof(void)),
    //       Func<...,bool> 不符合 ⇒ 每次載入只吐一行 "Event provider was not assigned"
    //       的 Error 就結束,之後全程無聲。
    //    ③ 名字錯:[EzIPCEvent] 的 applyPrefix 預設是 true,而這個 class 的前綴是 Name
    //       ("InventoryTools"),組出來會是 "InventoryTools.ItemAdded";AllaganTools 真正
    //       註冊的是 "AllaganTools.ItemAdded"。
    // 🔑 正解 = 標在「方法」上(訂閱端那條路徑才會呼叫 Subscribe),方法必須回傳 void,
    //    並且顯式指定 IPC 名稱與 applyPrefix: false。
    // 📌 對端定義(InventoryTools/InventoryTools/IPC/IPCService.cs:535-537):
    //      GetIpcProvider<(uint, InventoryItem.ItemFlags, ulong, uint), bool>("AllaganTools.ItemAdded")
    //      GetIpcProvider<(uint, InventoryItem.ItemFlags, ulong, uint), bool>("AllaganTools.ItemRemoved")
    //      GetIpcProvider<ulong?, bool>("AllaganTools.RetainerChanged")
    //    最後一個泛型參數是 Dalamud call gate 的回傳型別;事件只走 SendMessage 所以它是啞值,
    //    這裡用 actionLastGenericType: typeof(bool) 與對端逐字對齊。
    // 🔴 回呼是在「對方外掛的執行緒」上跑的(InventoryTools 的 InventoryMonitor /
    //    CharacterMonitor),而且 CallGateChannel.SendMessage 沒有替訂閱者攔例外 ⇒ 這裡擲出去
    //    的例外會傳回 InventoryTools 自己的事件迴圈。所以每個處理常式都自己 try/catch,
    //    而且只做「換掉一個不可變快照的參考」+ Interlocked 計數:不碰 ImGui、不做檔案 I/O、
    //    不用 EzThrottler(EzThrottler 不是執行緒安全的,不能用在 IPC 路徑上)。

    /// <summary>
    /// AllaganTools 物品事件的快照。<see cref="EventCount"/> 是這個遊戲工作階段收到的第幾筆,
    /// 巨集可以靠它判斷「有沒有新事件」,不必自己比對內容。
    /// </summary>
    public sealed record ItemEventInfo(uint ItemId, InventoryItem.ItemFlags ItemFlags, ulong ContentId, uint Quantity, int EventCount);

    /// <summary>AllaganTools 雇員切換事件的快照。</summary>
    public sealed record RetainerEventInfo(ulong? RetainerId, int EventCount);

    // volatile:回呼在對方的執行緒寫、Lua 在框架執行緒讀。參考指派本身就是不可分割的,
    // volatile 只負責可見性;快照物件不可變,所以讀到的一定是完整的一筆。
    private volatile ItemEventInfo? _lastItemAdded;
    private volatile ItemEventInfo? _lastItemRemoved;
    private volatile RetainerEventInfo? _lastRetainerChanged;
    private int _itemAddedCount;
    private int _itemRemovedCount;
    private int _retainerChangedCount;

    [EzIPCEvent($"{_ipcName}.%m", false, typeof(bool))]
    private void ItemAdded((uint ItemId, InventoryItem.ItemFlags ItemFlags, ulong ContentId, uint Quantity) data)
    {
        try
        {
            var n = Interlocked.Increment(ref _itemAddedCount);
            _lastItemAdded = new ItemEventInfo(data.ItemId, data.ItemFlags, data.ContentId, data.Quantity, n);
            if (n == 1)
                FrameworkLogger.Info($"已收到第一筆 {_ipcName}.{nameof(ItemAdded)} 事件 (itemId={data.ItemId}, qty={data.Quantity}),訂閱確實生效。");
        }
        catch (Exception ex)
        {
            FrameworkLogger.Error($"處理 {_ipcName}.{nameof(ItemAdded)} 事件時發生例外", ex);
        }
    }

    [EzIPCEvent($"{_ipcName}.%m", false, typeof(bool))]
    private void ItemRemoved((uint ItemId, InventoryItem.ItemFlags ItemFlags, ulong ContentId, uint Quantity) data)
    {
        try
        {
            var n = Interlocked.Increment(ref _itemRemovedCount);
            _lastItemRemoved = new ItemEventInfo(data.ItemId, data.ItemFlags, data.ContentId, data.Quantity, n);
            if (n == 1)
                FrameworkLogger.Info($"已收到第一筆 {_ipcName}.{nameof(ItemRemoved)} 事件 (itemId={data.ItemId}, qty={data.Quantity}),訂閱確實生效。");
        }
        catch (Exception ex)
        {
            FrameworkLogger.Error($"處理 {_ipcName}.{nameof(ItemRemoved)} 事件時發生例外", ex);
        }
    }

    // 🔴 參數宣告成 ulong? 而不是 ulong:對端的 call gate 就是宣告成 ulong?,
    //    而 Dalamud 的 CheckAndConvertArgs 對「null 傳進非可空值型別參數」會擲 IpcValueNullError,
    //    那個例外是在呼叫我的委派「之前」擲的,我這裡的 try/catch 攔不到,會直接傳回 InventoryTools。
    [EzIPCEvent($"{_ipcName}.%m", false, typeof(bool))]
    private void RetainerChanged(ulong? retainerId)
    {
        try
        {
            var n = Interlocked.Increment(ref _retainerChangedCount);
            _lastRetainerChanged = new RetainerEventInfo(retainerId, n);
            if (n == 1)
                FrameworkLogger.Info($"已收到第一筆 {_ipcName}.{nameof(RetainerChanged)} 事件 (retainerId={retainerId}),訂閱確實生效。");
        }
        catch (Exception ex)
        {
            FrameworkLogger.Error($"處理 {_ipcName}.{nameof(RetainerChanged)} 事件時發生例外", ex);
        }
    }

    [LuaFunction(description: "Gets the most recent ItemAdded event received from AllaganTools, or nil if none has arrived yet")]
    public ItemEventInfo? GetLastItemAdded() => _lastItemAdded;

    [LuaFunction(description: "Gets the most recent ItemRemoved event received from AllaganTools, or nil if none has arrived yet")]
    public ItemEventInfo? GetLastItemRemoved() => _lastItemRemoved;

    [LuaFunction(description: "Gets the most recent RetainerChanged event received from AllaganTools, or nil if none has arrived yet")]
    public RetainerEventInfo? GetLastRetainerChanged() => _lastRetainerChanged;

    // 📌 這個 helper 不只是方便:IPCModule.RegisterIpcEnums 只從「有 [LuaFunction] 的成員簽章」
    //    收集要註冊給 Lua 的列舉,而 InventoryItem.ItemFlags 以前唯一的來源就是上面被移除的
    //    那兩個欄位。少了這個參數,Lua 全域的 ItemFlags 會跟著消失(靜默的行為回退)。
    [LuaFunction(
        description: "Returns true if the given AllaganTools item flags mark the item as high quality",
        parameterDescriptions: ["itemFlags"])]
    public bool IsHighQuality(InventoryItem.ItemFlags itemFlags) => itemFlags.HasFlag(InventoryItem.ItemFlags.HighQuality);
}
