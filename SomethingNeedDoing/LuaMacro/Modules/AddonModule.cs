using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using NLua;
using SomethingNeedDoing.LuaMacro.Wrappers;

namespace SomethingNeedDoing.LuaMacro.Modules;
public unsafe class AddonModule : LuaModuleBase
{
    public override string ModuleName => "Addons";

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
}
