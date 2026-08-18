using Dalamud.Hooking;
using ECommons.Automation;
using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using NLua;
using SomethingNeedDoing.LuaMacro.Wrappers;

namespace SomethingNeedDoing.LuaMacro.Modules;
public unsafe class AddonModule : LuaModuleBase
{
    public override string ModuleName => "Addons";

    private unsafe delegate void ReceiveEventDelegate(AtkUnitBase* thisPtr, AtkEventType eventType, int which, AtkEvent* atkEvent, AtkEventData* data);
    private static Hook<ReceiveEventDelegate>? _debugHook;

    [LuaFunction(description: "TEMPORARY DIAGNOSTIC: hooks the given addon's ReceiveEvent and logs every call (type/which) to help reverse-engineer node click params. Call DebugUnhookReceiveEvent when done.")]
    public void DebugHookReceiveEvent(string addonName)
    {
        _debugHook?.Dispose();
        _debugHook = null;

        var addon = (AtkUnitBase*)Svc.GameGui.GetAddonByName(addonName).Address;
        if (addon == null)
        {
            Svc.Log.Warning($"[DebugHook] Addon {addonName} not found");
            return;
        }

        var vtbl = *(nint**)addon;
        var receiveEventAddr = vtbl[2]; // AtkUnitBase vtable slot 2 = ReceiveEvent (Dtor=0, ReceiveGlobalEvent=1, ReceiveEvent=2)
        _debugHook = Svc.Hook.HookFromAddress<ReceiveEventDelegate>(receiveEventAddr, DetourReceiveEvent);
        _debugHook.Enable();
        Svc.Log.Info($"[DebugHook] Hooked {addonName}.ReceiveEvent at 0x{(nint)receiveEventAddr:X}");
    }

    [LuaFunction(description: "Removes the temporary ReceiveEvent debug hook installed by DebugHookReceiveEvent.")]
    public void DebugUnhookReceiveEvent()
    {
        _debugHook?.Dispose();
        _debugHook = null;
        Svc.Log.Info("[DebugHook] Unhooked");
    }

    private static void DetourReceiveEvent(AtkUnitBase* thisPtr, AtkEventType eventType, int which, AtkEvent* atkEvent, AtkEventData* data)
    {
        // DebugUnhookReceiveEvent() 會把 _debugHook 設回 null,而它是 Lua 可呼叫的 ——
        // SND 的巨集跑在自己的工作執行緒,這個 detour 卻在 UI 執行緒,兩者是真的會撞上的跨執行緒窗口。
        // 所以欄位只讀一次、快照到區域變數,之後只用區域變數,不對欄位做第二次讀取。
        var hook = _debugHook;

        Svc.Log.Info($"[DebugHook] addon={thisPtr->NameString} type={eventType} which={which}");

        // 快照到手是 null ⇒ Dispose() 已經跑完、原始位元組已還原,這次呼叫沒有原始函式可以轉。
        // (欄位不是 null 但 hook 已 Dispose 的情況由 OriginalDisposeSafe 自己處理,不必另外接。)
        // ReceiveEvent 是事件監聽,不是寫 [this] 的建構子,略過只會漏掉這一次事件,
        // 不會留下沒有 vtable 的半初始化物件 ⇒ 這裡用「略過」收尾是安全的。
        if (hook == null)
        {
            Svc.Log.Information("[DebugHook] hook was removed mid-call; skipping the original call for this invocation.");
            return;
        }

        hook.OriginalDisposeSafe(thisPtr, eventType, which, atkEvent, data);
    }

    [LuaFunction] public AddonWrapper GetAddon(string name) => new(name);

    [LuaFunction(description: "Gets the names of all currently visible/loaded addons, for diagnostics.")]
    public List<string> GetVisibleAddonNames()
    {
        var names = new List<string>();
        var manager = RaptureAtkUnitManager.Instance();
        for (var i = 0; i < manager->AllLoadedUnitsList.Count; i++)
        {
            var unit = manager->AllLoadedUnitsList.Entries[i].Value;
            if (unit != null && unit->IsVisible)
                names.Add(unit->NameString);
        }
        return names;
    }

    // ── 雇員清單 ──────────────────────────────────────────────────────────
    // 🔴 RetainerList 的選取事件不是「送一個整數」那麼單純:遊戲要的是
    //    (int 2, uint index, 未定義值, 未定義值),其中後兩個是 Type = 0 的 AtkValue。
    //    巨集的 /callback 只能送出整數/字串,寫不出「未定義」這種型別,照著索引硬送會變成
    //    另一組參數 —— 而 addon 對參數型別不對的反應是靜默不動作,不是報錯。
    // 🔴 而且 Retainers 永遠是**固定 10 格**,沒用到的格子照樣有 Entry,名字讀到的是空字串或
    //    殘留值。所以「第 N 個雇員」必須以 IsActive 過濾之後再算,不能直接拿 addon 的槽位當序號。
    // 這兩件事都已經在 ECommons 的 AddonMaster.RetainerList 裡處理好(AutoRetainer 正式流程走的
    // 就是它),這裡直接用它,不要在 Lua 端重新發明。

    [LuaFunction(description: "Names of the retainers in the open RetainerList addon, in list order, with the addon's unused fixed slots filtered out. Returns an empty list if the addon is not open or ready.")]
    public List<string> GetRetainerEntryNames()
    {
        var addon = (AtkUnitBase*)Svc.GameGui.GetAddonByName("RetainerList").Address;
        if (addon == null || !IsAddonReady(addon))
            return [];

        var names = new List<string>();
        foreach (var entry in new AddonMaster.RetainerList((nint)addon).Retainers)
        {
            if (!entry.IsActive) continue;
            names.Add(entry.Name);
        }
        return names;
    }

    [LuaFunction(description: "Selects the named retainer in the open RetainerList addon, using the game's own entry activation. Returns false (and does nothing) if the addon is not open/ready or no active entry carries that name - match is exact.")]
    public bool SelectRetainerEntryByName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return false;

        var addon = (AtkUnitBase*)Svc.GameGui.GetAddonByName("RetainerList").Address;
        if (addon == null || !IsAddonReady(addon))
            return false;

        foreach (var entry in new AddonMaster.RetainerList((nint)addon).Retainers)
        {
            // IsActive 要先驗:Select() 對未使用的格子是 no-op 回 false,但名字比對本身會讀到殘留值。
            if (!entry.IsActive) continue;
            if (entry.Name != name) continue;
            return entry.Select();
        }
        return false;
    }

    [LuaFunction(description: "If the ContextMenu addon is open, selects the entry whose text matches the given label. Returns true if selected.")]
    public bool SelectContextMenuEntry(string label)
    {
        var addon = (AtkUnitBase*)Svc.GameGui.GetAddonByName("ContextMenu").Address;
        if (addon == null || !IsAddonReady(addon))
            return false;

        var contextMenu = new AddonMaster.ContextMenu((nint)addon);
        foreach (var entry in contextMenu.Entries)
        {
            if (entry.Text == label)
                return entry.Select();
        }
        return false;
    }

    [LuaFunction(description: "If the ContextIconMenu addon is open, selects entry 'index' via the same callback convention as ContextMenu (unverified for multi-entry cases; only tested with a single entry).")]
    public bool SelectContextIconMenuEntry(int index)
    {
        var addon = (AtkUnitBase*)Svc.GameGui.GetAddonByName("ContextIconMenu").Address;
        if (addon == null || !IsAddonReady(addon))
            return false;

        Callback.Fire(addon, true, 0, index, 0);
        return true;
    }

    [LuaFunction(description: "If the ContextIconMenu addon is open, finds the entry whose GetValueTexts() contains the given substring and selects it. Returns true if found and selected.")]
    public bool SelectContextIconMenuEntryByText(string containsText)
    {
        var addonPtr = (AtkUnitBase*)Svc.GameGui.GetAddonByName("ContextIconMenu").Address;
        if (addonPtr == null || !IsAddonReady(addonPtr))
            return false;

        // Entry item names start at raw AtkValues offset 7 (mirrors ContextMenu's confirmed
        // offset=7 convention), so entry index = raw text position - 7. Only verified against
        // a single-entry menu so far (text found at position 7 -> entry index 0); multi-entry
        // spacing (whether each entry occupies exactly one string slot, contiguous) is inferred
        // from that convention, not independently confirmed.
        const int EntryTextOffset = 7;
        var wrapper = new AddonWrapper("ContextIconMenu");
        var texts = wrapper.GetValueTexts();
        for (var i = EntryTextOffset; i < texts.Count; i++)
        {
            if (texts[i].Contains(containsText))
                return SelectContextIconMenuEntry(i - EntryTextOffset);
        }
        return false;
    }
}
