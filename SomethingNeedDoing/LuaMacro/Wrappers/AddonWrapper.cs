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
                        text = textNode->NodeText.GetText();
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

    // 傳的是 addon 的「名字」不是「指標」:NodeWrapper 每次存取都要自己重新 GetAddonByName,
    // 把指標交出去等於又把生命週期凍結回來。
    [LuaDocs] public NodeWrapper GetNode(params int[] nodeIds) => new(name, nodeIds);

    // 這個 helper 刻意不是 iterator:iterator 的方法體裡不能出現指標,也不能有 ref struct(Span)
    // 的區域變數。先在這裡把每個節點的「身分」抄成純受控資料,再交給 Nodes 包成 wrapper。
    private List<(int Index, uint NodeId)> NodeIdentities()
    {
        var addon = Addon;
        if (addon == null || addon->UldManager.NodeList == null) return [];

        var nodes = addon->UldManager.Nodes;
        var list = new List<(int, uint)>(nodes.Length);
        for (var i = 0; i < nodes.Length; i++)
        {
            var node = nodes[i].Value;
            list.Add((i, node == null ? 0u : node->NodeId));
        }
        return list;
    }

    [LuaDocs]
    public IEnumerable<NodeWrapper> Nodes
    {
        get
        {
            var identities = NodeIdentities();
            var result = new List<NodeWrapper>(identities.Count);
            foreach (var (index, nodeId) in identities)
                result.Add(NodeWrapper.FromNodeList(name, index, nodeId));
            return result;
        }
    }
}

// 🔴 這一層原本在建構時就把 AtkResNode* 凍結下來,而 Lua 巨集的典型寫法是:
//
//        local node = Addons.GetAddon("Foo"):GetNode(1, 2, 3)
//        yield("/wait 1")        -- 視窗在這一秒裡被關掉、或整個重建
//        LogInfo(node.Text)      -- 指標指向已經被釋放的節點
//
//    節點記憶體由 addon 的 UldManager 擁有:addon 關閉會整批釋放,重開會重新配置(位址不會一樣)。
//    對釋放後的節點解參考是 AccessViolationException —— 在 .NET Core 屬於 corrupted-state
//    exception,C# 的 try/catch 與 Lua 的 pcall 都攔不到,遊戲當場關閉。
//    ⚠️ 判空對這件事沒有用:指標不是 null,只是不再指向那個節點。
//
// ⇒ 改成存「怎麼找到它」而不是「它在哪」,每次屬性存取重走一次解析。樣板就是同檔的
//   AddonWrapper.Addon:那裡只存 addon 名稱,每次都重新 GetAddonByName。
//
// 兩種解析模式:
//   1. ID 鏈(AddonWrapper.GetNode 建立的):存 addon 名 + node id 鏈,每次重走
//      GetNodeByIDChain(addon->RootNode, ids)。這是完整、可重現的路徑。
//   2. 節點清單(AddonWrapper.Nodes 列舉出來的):ULD 節點清單裡的節點不保證能用一條父子 id 鏈
//      從 RootNode 走到(GetNodeByIDChain 只沿 ChildNode / 元件的 NodeList[0] 下降),所以改存
//      addon 名 + 建構當下的槽位 + 節點自己的 NodeId:先看那一格、驗 NodeId 一致才採用,
//      不一致就把整份清單掃一次。
//      ⚠️ NodeId 為 0 的節點沒有可用的身分,那時只能相信槽位 —— 那仍然比凍結指標安全:
//         指標是這一幀重讀的,槽位也對照過當下的 NodeListCount。
//
// 解析不到時每個屬性都回佔位值;巨集要分辨「節點不存在」與「節點值就是這樣」請先看 Exists。
public unsafe class NodeWrapper : IWrapper
{
    private readonly string _addonName;
    private readonly int[] _idChain;
    private readonly int _listIndex;
    private readonly uint _nodeId;

    /// <summary>ID 鏈模式:addon 名 + 從 RootNode 走下去的 node id 鏈。</summary>
    public NodeWrapper(string addonName, params int[] nodeIds)
    {
        _addonName = addonName;
        _idChain = nodeIds ?? [];
        _listIndex = -1;
        _nodeId = 0;
    }

    private NodeWrapper(string addonName, int listIndex, uint nodeId)
    {
        _addonName = addonName;
        _idChain = [];
        _listIndex = listIndex;
        _nodeId = nodeId;
    }

    /// <summary>
    /// 節點清單模式。用工廠方法而不是建構子多載:(string, int, uint) 會跟 (string, params int[])
    /// 在呼叫端寫成整數字面值時撞號,靜默挑到錯的那個。
    /// </summary>
    internal static NodeWrapper FromNodeList(string addonName, int listIndex, uint nodeId) => new(addonName, listIndex, nodeId);

    /// <summary>每次存取都重新解析。呼叫端一律先存進區域變數(單幀內安全),不要在同一個屬性裡讀兩次。</summary>
    private AtkResNode* Node
    {
        get
        {
            var addon = (AtkUnitBase*)Svc.GameGui.GetAddonByName(_addonName).Address;
            if (addon == null) return null;

            // 模式 1:GetNodeByIDChain 自己會擋空節點,擋不到空的 addon —— 上面那行擋掉了。
            if (_idChain.Length > 0)
                return GetNodeByIDChain(addon->RootNode, _idChain);

            // 模式 2:Span 的建構子不驗指標,NodeList 為 null 時索引等同從位址 0 讀。
            if (addon->UldManager.NodeList == null) return null;
            var nodes = addon->UldManager.Nodes;

            // 快路徑:節點多半還在建構當下那一格。
            if (_listIndex >= 0 && _listIndex < nodes.Length)
            {
                var hinted = nodes[_listIndex].Value;
                if (hinted != null && (_nodeId == 0 || hinted->NodeId == _nodeId)) return hinted;
            }

            if (_nodeId == 0) return null;

            // 慢路徑:清單重排了,照 NodeId 找回來。
            for (var i = 0; i < nodes.Length; i++)
            {
                var candidate = nodes[i].Value;
                if (candidate != null && candidate->NodeId == _nodeId) return candidate;
            }

            return null;
        }
    }

    [LuaDocs(description: "Whether this wrapper still resolves to a node right now. Check this before trusting the other properties: when it is false they return placeholder values (Id 0, NodeType 0 - neither is a valid real value - empty text, not visible).")]
    public bool Exists => Node != null;

    [LuaDocs] public uint Id { get { var node = Node; return node == null ? 0u : node->NodeId; } }
    [LuaDocs] public bool IsVisible { get { var node = Node; return node != null && node->IsVisible(); } }

    [LuaDocs]
    public string Text
    {
        get
        {
            var node = Node;
            if (node == null) return string.Empty;
            var textNode = node->GetAsAtkTextNode();
            return textNode == null ? string.Empty : textNode->NodeText.GetText();
        }
        set
        {
            var node = Node;
            if (node == null) return;
            var textNode = node->GetAsAtkTextNode();
            if (textNode == null) return;
            textNode->NodeText.SetString(value);
        }
    }

    [LuaDocs] public NodeType NodeType { get { var node = Node; return node == null ? default : node->Type; } }
}

public class AtkValueWrapper(AtkValue value) : IWrapper
{
    private AtkValue Value = value;

    [LuaDocs] public string ValueString => Value.GetValueAsString();

}
