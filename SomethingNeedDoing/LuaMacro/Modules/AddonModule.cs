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

        var addon = (AtkUnitBase*)Svc.GameGui.GetAddonByName(addonName);
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
        Svc.Log.Info($"[DebugHook] addon={thisPtr->NameString} type={eventType} which={which}");
        _debugHook!.Original(thisPtr, eventType, which, atkEvent, data);
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

    [LuaFunction(description: "If the ContextMenu addon is open, selects the entry whose text matches the given label. Returns true if selected.")]
    public bool SelectContextMenuEntry(string label)
    {
        var addon = (AtkUnitBase*)Svc.GameGui.GetAddonByName("ContextMenu");
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
        var addon = (AtkUnitBase*)Svc.GameGui.GetAddonByName("ContextIconMenu");
        if (addon == null || !IsAddonReady(addon))
            return false;

        Callback.Fire(addon, true, 0, index, 0);
        return true;
    }

    [LuaFunction(description: "If the ContextIconMenu addon is open, finds the entry whose GetValueTexts() contains the given substring and selects it. Returns true if found and selected.")]
    public bool SelectContextIconMenuEntryByText(string containsText)
    {
        var addonPtr = (AtkUnitBase*)Svc.GameGui.GetAddonByName("ContextIconMenu");
        if (addonPtr == null || !IsAddonReady(addonPtr))
            return false;

        var wrapper = new AddonWrapper("ContextIconMenu");
        var texts = wrapper.GetValueTexts();
        for (var i = 0; i < texts.Count; i++)
        {
            if (texts[i].Contains(containsText))
                return SelectContextIconMenuEntry(0); // single-entry case confirmed; multi-entry index mapping not yet verified
        }
        return false;
    }
}
