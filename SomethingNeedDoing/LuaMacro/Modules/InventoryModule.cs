using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FFXIVClientStructs.Interop;
using NLua;
using SomethingNeedDoing.Core.Interfaces;
using ValueType = FFXIVClientStructs.FFXIV.Component.GUI.ValueType;

namespace SomethingNeedDoing.LuaMacro.Modules;
public unsafe class InventoryModule : LuaModuleBase
{
    public override string ModuleName => "Inventory";
    protected override object? MetaIndex(LuaTable table, object key) => GetInventoryContainer(Enum.Parse<InventoryType>(key.ToString() ?? string.Empty));

    [LuaFunction] public InventoryContainerWrapper GetInventoryContainer(InventoryType container) => new(container);

    // Lua 端不支援同名 overload:LuaModuleBase.Register 逐一以「模組.函式名」為 key 註冊,
    // 後註冊者會蓋掉先註冊者。這個 (container, slot) 版本過去被下面的 (itemId) 版本遮蔽,
    // 從 Lua 實際上呼叫不到;改用獨立的 Lua 名稱 GetInventoryItemInSlot 讓兩個都可用。
    // 沿用 GetInventoryItem 名稱的是 (itemId) 版本,與既有腳本的實際行為一致。
    [LuaFunction(name: "GetInventoryItemInSlot", description: "Gets the item in the given slot of the given container.")]
    public InventoryItemWrapper GetInventoryItem(InventoryType container, int slot) => new(container, slot);

    [LuaFunction]
    [Changelog("12.9")]
    [Changelog("12.10", ChangelogType.Fixed, "Support for Key Items")]
    public int GetItemCount(uint itemId)
    {
        var isHq = itemId < 2_000_000 && itemId % 500_000 != itemId;
        if (itemId < 2_000_000)
            itemId %= 500_000;
        return InventoryManager.Instance()->GetInventoryItemCount(itemId, isHq);
    }

    [LuaFunction]
    [Changelog("12.9")]
    public int GetHqItemCount(uint itemId)
    {
        return InventoryManager.Instance()->GetInventoryItemCount(itemId % 500_000, true);
    }

    [LuaFunction]
    [Changelog("12.17")]
    public int GetCollectableItemCount(uint itemId, int minimumCollectability)
    {
        minimumCollectability = Math.Clamp(minimumCollectability, 1, 1000);
        return InventoryManager.Instance()->GetInventoryItemCount(itemId, false, false, false, (short)minimumCollectability);
    }

    [LuaFunction]
    [Changelog("12.17")]
    public uint GetFreeInventorySlots()
    {
        return InventoryManager.Instance()->GetEmptySlotsInBag();
    }

    [LuaFunction]
    public unsafe InventoryItemWrapper? GetInventoryItem(uint itemId)
    {
        foreach (var type in Enum.GetValues<InventoryType>())
        {
            var container = InventoryManager.Instance()->GetInventoryContainer(type);
            if (container == null) continue;
            for (var i = 0; i < container->Size; i++)
                if (container->Items[i].ItemId == itemId)
                    return new(container, i);
        }
        return null;
    }

    [LuaFunction]
    [Changelog("12.8")]
    public List<InventoryItemWrapper> GetItemsInNeedOfRepairs(int durability = 0)
    {
        List<InventoryItemWrapper> list = [];
        var container = InventoryManager.Instance()->GetInventoryContainer(InventoryType.EquippedItems);
        for (var i = 0; i < container->Size; i++)
        {
            var item = container->GetInventorySlot(i);
            if (item is null) continue;
            if (Convert.ToInt32(Convert.ToDouble(item->Condition) / 30000.0 * 100.0) <= durability)
                list.Add(new(item));
        }
        return list;
    }

    [LuaFunction]
    [Changelog("12.8")]
    public List<InventoryItemWrapper> GetSpiritbondedItems()
    {
        List<InventoryItemWrapper> list = [];
        var container = InventoryManager.Instance()->GetInventoryContainer(InventoryType.EquippedItems);
        for (var i = 0; i < container->Size; i++)
        {
            var item = container->GetInventorySlot(i);
            if (item is null) continue;
            if (item->SpiritbondOrCollectability / 100 == 100)
                list.Add(new(item));
        }
        return list;
    }

    public unsafe class InventoryContainerWrapper(InventoryType container) : IWrapper
    {
        // 🔴 容器可能拿不到（管理器尚未初始化、或該容器當下沒載入，例如雇員/部隊置物櫃
        // 沒開的時候）。解參考 null 產生的是 AccessViolationException——在 .NET Core 屬
        // corrupted-state exception，Lua 的 pcall 與 C# 的 try/catch 都攔不到，會直接把
        // 遊戲帶走。而這個包裝類別是任何一支 Lua 巨集寫 `Inventory.某容器.Count` 就到得了的。
        // AutoRetainer 對同一個呼叫本來就有 null 檢查，這裡補齊。
        private readonly InventoryContainer* _container =
            InventoryManager.Instance() is var mgr && mgr != null ? mgr->GetInventoryContainer(container) : null;

        /// <summary>容器拿不到時回 0（＝視為空容器），呼叫端的迴圈自然不會執行。</summary>
        [LuaDocs] public int Count => _container == null ? 0 : _container->Size;

        [LuaDocs]
        public int FreeSlots
        {
            get
            {
                if (_container == null)
                    return 0;

                var count = 0;
                var size = _container->Size;
                for (var i = 0; i < size; i++)
                    if (_container->Items[i].ItemId == 0)
                        count++;
                return count;
            }
        }

        [LuaDocs]
        public List<InventoryItemWrapper> Items
        {
            get
            {
                List<InventoryItemWrapper> list = [];
                if (_container == null)
                    return list;

                var size = _container->Size;
                for (var i = 0; i < size; i++)
                    if (_container->Items[i].ItemId != 0)
                        list.Add(new(_container, i));
                return list;
            }
        }

        // ⚠️ 索引子在容器拿不到時仍會回一個包著 null 的 wrapper——維持既有行為（不丟例外），
        // 但呼叫端在 Count == 0 時本來就不該走到這裡。
        [LuaDocs] public InventoryItemWrapper this[int index] => new(_container, index);
    }

    public unsafe class InventoryItemWrapper : IWrapper
    {
        private readonly InventoryType[] playerInv = [
            InventoryType.Inventory1,
            InventoryType.Inventory2,
            InventoryType.Inventory3,
            InventoryType.Inventory4,
            InventoryType.ArmoryMainHand,
            InventoryType.ArmoryOffHand,
            InventoryType.ArmoryHead,
            InventoryType.ArmoryBody,
            InventoryType.ArmoryHands,
            InventoryType.ArmoryLegs,
            InventoryType.ArmoryFeets,
            InventoryType.ArmoryEar,
            InventoryType.ArmoryNeck,
            InventoryType.ArmoryWrist,
            InventoryType.ArmoryRings
            ];

        private InventoryItem* Item { get; set; }
        public InventoryItemWrapper(InventoryType container, int slot) => Item = InventoryManager.Instance()->GetInventoryContainer(container)->GetInventorySlot(slot);
        public InventoryItemWrapper(InventoryContainer* container, int slot) => Item = container->GetInventorySlot(slot);
        public InventoryItemWrapper(InventoryItem* item) => Item = item;
        public InventoryItemWrapper(uint itemId)
        {
            foreach (var inv in playerInv)
            {
                var cont = InventoryManager.Instance()->GetInventoryContainer(inv);
                for (var i = 0; i < cont->Size; ++i)
                    if (cont->GetInventorySlot(i)->ItemId == itemId)
                        Item = cont->GetInventorySlot(i);
            }
        }

        [LuaDocs] public uint ItemId => Item->ItemId;
        [LuaDocs] public uint BaseItemId => Item->GetBaseItemId();
        [LuaDocs] public int Count => Item->Quantity;
        [LuaDocs] public ushort SpiritbondOrCollectability => Item->SpiritbondOrCollectability;
        [LuaDocs] public ushort Condition => Item->Condition;
        [LuaDocs] public uint GlamourId => Item->GlamourId;
        [LuaDocs] public bool IsHighQuality => Item->IsHighQuality();
        [LuaDocs] public InventoryItemWrapper? LinkedItem => Item->GetLinkedItem() is not null ? new(Item->GetLinkedItem()) : null;

        [LuaDocs] public InventoryType Container => Item->Container;
        [LuaDocs] public int Slot => Item->Slot;

        [LuaDocs] public void Use() => Game.UseItem(ItemId, IsHighQuality);

        [LuaDocs(description: "Opens the right-click context menu for this inventory slot, as if the item was right-clicked.")]
        public void OpenContextMenu()
        {
            var addonName = $"InventoryGrid{(int)Container}E";
            var addon = (AtkUnitBase*)Svc.GameGui.GetAddonByName(addonName).Address;
            var addonId = addon != null ? addon->Id : (uint)0;
            AgentInventoryContext.Instance()->OpenForItemSlot(Container, Slot, 0, addonId);
        }

        [LuaDocs]
        [Changelog("12.8")]
        public void Desynth()
        {
            if (GetRow<Sheets.Item>(ItemId)?.Desynth == 0)
                return;

            AgentSalvage.Instance()->SalvageItem(Item);
            var retval = new AtkValue();
            Span<AtkValue> param = [
                new AtkValue { Type = ValueType.Int, Int = 0 },
                new AtkValue { Type = ValueType.Bool, Byte = 1 }
            ];
            AgentSalvage.Instance()->AgentInterface.ReceiveEvent(&retval, param.GetPointer(0), 2, 1);
        }

        // 🔴 第 5 個引數 a6 是「這次搬移要不要送給伺服器」的總開關,不是無關緊要的 unknown。
        // a6=false(預設值)時遊戲只更新本機容器並刷新 UI,一個封包都不送 —— 對**任何**容器都成立,
        // 包含背包→背包。畫面上道具會動,但伺服器根本不知道,下一次同步就彈回原處。
        // 原本這裡省略了 a6,所以任何用這個 API 的 Lua 巨集都只改本機、靜默失敗。
        // 遊戲自己的拖放處理常式走的就是 a6=true,這裡照做。
        // ⚠️ 不做成可選參數:a6=false 對巨集作者沒有任何正當用途(只會製造本機與伺服器不一致),
        //    而且維持原本的呼叫形狀就不會動到 NLua 的參數繫結,既有腳本一行都不用改。
        [LuaDocs(description: "Moves this item to the first empty slot of the given container. The move is sent to the server, i.e. it is a real move and not a local-only change.")]
        [Changelog("12.51")]
        public void MoveItemSlot(InventoryType destinationContainer)
            => InventoryManager.Instance()->MoveItemSlot(Container, (ushort)Slot, destinationContainer, GetFirstEmptySlot(destinationContainer), a6: true);
    }

    private static unsafe ushort GetFirstEmptySlot(InventoryType container)
    {
        var cont = InventoryManager.Instance()->GetInventoryContainer(container);
        for (ushort i = 0; i < cont->Size; i++)
            if (cont->Items[i].ItemId == 0)
                return i;
        return 0;
    }
}
