using ECommons.Automation.UIInput;
using ECommons.UIHelpers;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FFXIVClientStructs.Interop;
using SomethingNeedDoing.Core.Interfaces;

namespace SomethingNeedDoing.LuaMacro.Wrappers;
public unsafe class AddonWrapper(string name) : IWrapper
{
    private AtkUnitBase* Addon => (AtkUnitBase*)Svc.GameGui.GetAddonByName(name).Address;

    // 🔴 這些包裝是 Lua 巨集直接叫得到的:視窗沒開、或在腳本兩行之間被關掉時,Addon 就是空指標,
    // 而 Span 的建構子不會驗指標,ToArray() 會直接從空位址複製 → AccessViolationException。
    // AVE 是 corrupted-state exception,try/catch 與任何例外隔離都攔不到,只能在讀取前擋。
    // 讀不到就回空集合(視窗不在 = 沒有節點),與 Exists / Ready 回報的狀態一致。
    private Pointer<AtkResNode>[] NodeList
    {
        get
        {
            var addon = Addon;
            if (addon == null || addon->UldManager.NodeList == null) return [];
            return addon->UldManager.Nodes.ToArray();
        }
    }

    private AtkValue[] AtkValuesList
    {
        get
        {
            var addon = Addon;
            if (addon == null || addon->AtkValues == null) return [];
            return addon->AtkValuesSpan.ToArray();
        }
    }

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

    [LuaDocs(description: "Reads one AtkValue by index. Returns an empty value if the addon is not open or the index is out of range.")]
    public AtkValueWrapper GetAtkValue(int index)
    {
        // 沒有邊界檢查時,index 超出 AtkValuesCount 讀的是配置外的記憶體。
        var addon = Addon;
        if (addon == null || addon->AtkValues == null || index < 0 || index >= addon->AtkValuesCount)
            return new AtkValueWrapper(default);
        return new(addon->AtkValues[index]);
    }

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
            // ⚠️ 下面那個 try/catch 對空指標解參考完全無效(AccessViolationException 是
            // corrupted-state exception),所以節點與文字節點都必須在解參考前先驗過。
            // 空的槽位照樣列出來,免得「這一格讀不到」被看成「這一格不存在」。
            if (n == null)
            {
                list.Add("id=? type=? visible=? text=? (空節點)");
                continue;
            }

            var text = string.Empty;
            try
            {
                if (n->Type == NodeType.Text)
                {
                    var textNode = n->GetAsAtkTextNode();
                    if (textNode != null)
                        text = textNode->NodeText.ToString();
                }
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
        if (addon == null) return; // 視窗不在就這次不做事;ReceiveEvent 對空指標是攔不到的 AVE
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
        if (addon == null) return false;

        // 🔴 AtkComponentButton.IsEnabled 解的是 OwnerNode(+0xA8),不是 AtkResNode(+0xA0)——
        // 兩個是不同欄位,原本只驗 button 本身擋不到 OwnerNode 為空的情況;而 AtkResNode 本身
        // 也是指標欄位,IsVisible() 一樣會裸解參考它。兩條路都換成 ECommons 的 null-safe 版本,
        // 任一層讀不出來就回 false(這次不按),不會丟出無法攔截的 AccessViolationException。
        var button = addon->GetComponentButtonById(nodeId);
        if (button == null || !IsComponentEnabled(button) || !IsComponentVisible(&button->AtkComponentBase))
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
    // addon 為空指標時連 RootNode 都不能讀(GetNodeByIDChain 自己會擋空節點,但擋不到空的 addon)。
    public NodeWrapper(AtkUnitBase* addon, params int[] nodeIds) => Node = addon == null ? null : GetNodeByIDChain(addon->RootNode, nodeIds);
    public NodeWrapper(Pointer<AtkResNode> node) => Node = node.Value;
    private AtkResNode* Node { get; set; }

    // 🔴 找不到節點時 Node 是空指標,底下每個屬性原本都會裸解參考它 → AccessViolationException,
    // 那是 corrupted-state exception,Lua 端 pcall 與 C# 的 try/catch 都攔不到。
    // 改成一律先驗指標;巨集要分辨「節點不存在」與「節點值就是這樣」請先看 Exists。
    [LuaDocs(description: "Whether this wrapper actually resolved to a node. Check this before trusting the other properties: when it is false they return placeholder values (Id 0, NodeType 0 - neither is a valid real value - empty text, not visible).")]
    public bool Exists => Node != null;

    [LuaDocs] public uint Id => Node == null ? 0 : Node->NodeId;
    [LuaDocs] public bool IsVisible => Node != null && Node->IsVisible();

    [LuaDocs]
    public string Text
    {
        get
        {
            if (Node == null) return string.Empty;
            var textNode = Node->GetAsAtkTextNode();
            return textNode == null ? string.Empty : textNode->NodeText.ToString();
        }
        set
        {
            if (Node == null) return;
            var textNode = Node->GetAsAtkTextNode();
            if (textNode == null) return;
            textNode->NodeText.SetString(value);
        }
    }

    [LuaDocs] public NodeType NodeType => Node == null ? default : Node->Type;
}

public class AtkValueWrapper(AtkValue value) : IWrapper
{
    private AtkValue Value = value;

    [LuaDocs] public string ValueString => Value.GetValueAsString();

}
