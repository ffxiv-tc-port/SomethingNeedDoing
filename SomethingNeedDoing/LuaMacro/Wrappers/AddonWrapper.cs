using ECommons.Automation.UIInput;
using ECommons.UIHelpers;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FFXIVClientStructs.Interop;
using SomethingNeedDoing.Core.Interfaces;

namespace SomethingNeedDoing.LuaMacro.Wrappers;
public unsafe class AddonWrapper(string name) : IWrapper
{
    private AtkUnitBase* Addon => (AtkUnitBase*)Svc.GameGui.GetAddonByName(name);
    private Pointer<AtkResNode>[] NodeList => Addon->UldManager.Nodes.ToArray();
    private AtkValue[] AtkValuesList => Addon->AtkValuesSpan.ToArray();

    [LuaDocs(description: "Check if the Addon Exists, regardless of visibility.")] public bool Exists => Addon != null;
    [LuaDocs(description: "Check if the Addon is Visible and Ready.")]
    public bool Ready
    {
        get
        {
            var addon = Addon;
            return addon != null && IsAddonReady(addon);
        }
    }

    [LuaDocs] public AtkValueWrapper GetAtkValue(int index) => new(Addon->AtkValues[index]);

    [LuaDocs]
    public unsafe IEnumerable<AtkValueWrapper> AtkValues
    {
        get
        {
            foreach (var v in AtkValuesList)
                yield return new AtkValueWrapper(v);
        }
    }

    [LuaDocs(description: "Gets all non-empty string values from the addon's AtkValues, in order.")]
    public List<string> GetValueTexts() => [.. AtkValuesList.Select(v => v.GetValueAsString()).Where(s => !string.IsNullOrEmpty(s))];

    [LuaDocs(description: "Dumps every node's id/type/visibility/text, for diagnosing an addon's node layout.")]
    public List<string> DumpNodes()
    {
        var list = new List<string>();
        foreach (var node in NodeList)
        {
            var n = node.Value;
            var text = string.Empty;
            try
            {
                if (n->Type == NodeType.Text)
                    text = n->GetAsAtkTextNode()->NodeText.ToString();
            }
            catch { /* not a text node or unreadable, ignore */ }
            list.Add($"id={n->NodeId} type={n->Type} visible={n->IsVisible()} text={text}");
        }
        return list;
    }

    [LuaDocs(description: "Fires a DragDropClick event with the right-click button flag on the given 'which' slot index (as seen via a ReceiveEvent hook), simulating a right-click on a drag-drop slot (e.g. the soil/seed slots in the housing gardening addon) without needing real mouse input.")]
    public void RightClickDragDropSlot(int which)
    {
        var addon = Addon;
        var evt = new AtkEvent
        {
            Listener = (AtkEventListener*)addon,
            Target = &AtkStage.Instance()->AtkEventTarget,
            Param = (uint)which,
        };
        var data = new AtkEventDataBuilder().Write<byte>(6, 1).Build(); // offset 6 = AtkMouseData.ButtonId, 1 = right click
        addon->ReceiveEvent(AtkEventType.DragDropClick, which, &evt, &data);
    }

    [LuaDocs(description: "Clicks a component button by node id, if enabled and visible. Returns true if clicked.")]
    public bool ClickButton(uint nodeId)
    {
        var addon = Addon;
        var button = addon->GetComponentButtonById(nodeId);
        if (button == null || !button->IsEnabled || !button->AtkResNode->IsVisible())
            return false;

        button->ClickAddonButton(addon);
        return true;
    }

    [LuaDocs] public NodeWrapper GetNode(params int[] nodeIds) => new(Addon, nodeIds);

    [LuaDocs]
    public unsafe IEnumerable<NodeWrapper> Nodes
    {
        get
        {
            foreach (var node in NodeList)
                yield return new NodeWrapper(node);
        }
    }
}

public unsafe class NodeWrapper : IWrapper
{
    public NodeWrapper(AtkUnitBase* addon, params int[] nodeIds) => Node = GetNodeByIDChain(addon->RootNode, nodeIds);
    public NodeWrapper(Pointer<AtkResNode> node) => Node = node.Value;
    private AtkResNode* Node { get; set; }

    [LuaDocs] public uint Id => Node->NodeId;
    [LuaDocs] public bool IsVisible => Node->IsVisible();
    [LuaDocs] public string Text { get => Node->GetAsAtkTextNode()->NodeText.ToString(); set => Node->GetAsAtkTextNode()->NodeText.SetString(value); }
    [LuaDocs] public NodeType NodeType => Node->Type;
}

public class AtkValueWrapper(AtkValue value) : IWrapper
{
    private AtkValue Value = value;

    [LuaDocs] public string ValueString => Value.GetValueAsString();

}
