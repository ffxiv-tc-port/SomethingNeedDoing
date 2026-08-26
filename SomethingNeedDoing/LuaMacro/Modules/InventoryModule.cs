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
            if (container == null || container->Items == null) continue;
            for (var i = 0; i < container->Size; i++)
                if (container->Items[i].ItemId == itemId)
                    // 用 (容器型別, 格號) 建構,不要把 container 這個原生指標傳下去 ——
                    // 包裝物件會被巨集跨幀留著,存進去的鍵必須是「身分」而不是「位址」。
                    return new(type, i);
        }
        return null;
    }

    [LuaFunction]
    [Changelog("12.8")]
    public List<InventoryItemWrapper> GetItemsInNeedOfRepairs(int durability = 0)
    {
        List<InventoryItemWrapper> list = [];
        // GetInventoryContainer() 未登入時回 null;原本直接讀 container->Size 是攔不到的 AccessViolation。
        var container = InventoryManager.Instance()->GetInventoryContainer(InventoryType.EquippedItems);
        if (container == null) return list;
        for (var i = 0; i < container->Size; i++)
        {
            var item = container->GetInventorySlot(i);
            if (item is null) continue;
            if (Convert.ToInt32(Convert.ToDouble(item->Condition) / 30000.0 * 100.0) <= durability)
                list.Add(new(InventoryType.EquippedItems, i));
        }
        return list;
    }

    [LuaFunction]
    [Changelog("12.8")]
    public List<InventoryItemWrapper> GetSpiritbondedItems()
    {
        List<InventoryItemWrapper> list = [];
        var container = InventoryManager.Instance()->GetInventoryContainer(InventoryType.EquippedItems);
        if (container == null) return list;
        for (var i = 0; i < container->Size; i++)
        {
            var item = container->GetInventorySlot(i);
            if (item is null) continue;
            if (item->SpiritbondOrCollectability / 100 == 100)
                list.Add(new(InventoryType.EquippedItems, i));
        }
        return list;
    }

    public unsafe class InventoryContainerWrapper(InventoryType container) : IWrapper
    {
        // 🔴 容器可能拿不到（管理器尚未初始化、或該容器當下沒載入，例如雇員/部隊置物櫃
        // 沒開的時候）。解參考 null 產生的是 AccessViolationException——在 .NET Core 屬
        // corrupted-state exception，Lua 的 pcall 與 C# 的 try/catch 都攔不到，會直接把
        // 遊戲帶走。而這個包裝類別是任何一支 Lua 巨集寫 `Inventory.某容器.Count` 就到得了的。
        //
        // 🔴 原本這裡在建構當下就把 GetInventoryContainer() 的結果凍結成 readonly 欄位。判空是有的,
        //    但「凍結」本身是另一個問題:巨集的典型寫法是 `local bag = Inventory.RetainerPage1`
        //    然後跨 yield/Sleep 反覆讀它。①建構時容器還沒載入 ⇒ 凍結了一個 null,之後就算雇員視窗
        //    開了也永遠回 0(一個不會自己好起來的靜默失敗);②建構時載入了、之後卸載 ⇒ 手上那個指標
        //    指向已經還給遊戲的記憶體,再讀就是攔不到的 AccessViolation。
        // ⇒ 改成只存 InventoryType,每次存取重新查。查一次是一個 GetInventoryContainer 呼叫,
        //   便宜到不需要快取。同一個存取子「內部」把結果放進區域變數(單幀之內安全),但不跨呼叫保存。
        // ⚠️ InventoryManager.Instance() 的 [StaticAddress] 沒有 isPointer(產生器實作是
        //    `return pInstance;`,靜態位址本身),特徵碼失配時擲例外、永遠不會回 null ——
        //    對它判空是死碼,所以這裡刻意不判;GetInventoryContainer() 的回值則一定要判。
        //    分類的完整說明見 InstancesModule.cs 檔頭。
        private readonly InventoryType _type = container;

        /// <summary>每次存取都重新解析出來的容器指標。呼叫端一律先存進區域變數,不要在同一個成員裡讀兩次。</summary>
        private InventoryContainer* Container =>
            _type == InventoryType.Invalid ? null : InventoryManager.Instance()->GetInventoryContainer(_type);

        /// <summary>這個包裝物件現在指得到真的容器嗎（未登入／該容器尚未載入時為 false）。</summary>
        [LuaDocs(description: "Whether this container can be resolved right now. False while it is not loaded, e.g. a retainer inventory before the retainer window has been opened.")]
        [Changelog(ChangelogAttribute.Unreleased)]
        public bool Exists => Container != null;

        /// <summary>容器拿不到時回 0（＝視為空容器），呼叫端的迴圈自然不會執行。</summary>
        [LuaDocs] public int Count { get { var c = Container; return c == null ? 0 : c->Size; } }

        [LuaDocs]
        public int FreeSlots
        {
            get
            {
                var c = Container;
                if (c == null || c->Items == null)
                    return 0;

                var count = 0;
                var size = c->Size;
                for (var i = 0; i < size; i++)
                    if (c->Items[i].ItemId == 0)
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
                var c = Container;
                if (c == null || c->Items == null)
                    return list;

                var size = c->Size;
                for (var i = 0; i < size; i++)
                    if (c->Items[i].ItemId != 0)
                        list.Add(new(_type, i));
                return list;
            }
        }

        // 索引子照舊不丟例外。容器拿不到／索引越界時回一個「解析不到」的包裝物件,它的每個成員都回
        // 中性值(見 InventoryItemWrapper 檔頭),不會解參考 null。
        [LuaDocs] public InventoryItemWrapper this[int index] => new(_type, index);
    }

    // 🔴 這個類別原本是 `private InventoryItem* Item { get; set; }` —— 建構當下把原生指標凍結下來,
    //    然後底下 20 幾個 [LuaDocs] 成員全部裸寫 `Item->`。兩種失敗方式,都是紅線形狀:
    //    ① 建構時掃不到道具(背包裡根本沒有、或未登入),Item 停在 null,第一次讀屬性就解參考 null;
    //       (uint itemId) 建構子連「找不到」都沒有回報管道,呼叫端拿到的是一個看起來正常的物件。
    //    ② 掃得到,但巨集的典型寫法就是把包裝物件存進區域變數跨 yield/Sleep 反覆讀:
    //
    //           local item = Inventory.Inventory1[0]
    //           while item.Count > 0 do        -- 這裡每一次讀取都落在不同的幀
    //               yield("/wait 1")
    //           end
    //
    //       道具被用掉/賣掉/搬走、容器卸載(雇員視窗關掉)之後,那塊記憶體已經還給遊戲或改配給別人。
    //    兩者的結果都是 AccessViolationException:在 .NET Core 屬 corrupted-state exception,
    //    C# 的 try/catch 與 Lua 的 pcall 都攔不到,遊戲當場關閉。
    //
    // ⇒ 改成存「身分」不存「位址」,每次成員存取重新解析(樣板＝同 repo 的 EntityWrapper):
    //      · 槽位鍵 (container, slot):使用者要的就是「這一格裡的東西」,重查就是重讀那一格。
    //      · 道具鍵 (itemId):使用者要的是「那件道具」,而道具會換格,所以槽位只當提示;
    //        提示對不上就重掃一次玩家自己的容器(清單與原本的建構子逐字相同)。
    //    同一次成員存取「內部」一律先把解析結果放進區域變數(單幀之內安全),但不跨呼叫保存 ——
    //    跨呼叫的快取就是這次要根治的那個 bug。
    //
    // 失敗語意(照 InstancesModule.cs 檔頭的成文分類):
    //    · 巨集會放進等待迴圈輪詢的存取子 ⇒ 安靜回中性值,不記 log(每幀一行會把整份 log 洗掉)。
    //      中性值一律取「空格子」的值(0 / false / null):遊戲對一格空的位置回的本來就是一個整塊
    //      歸零的 InventoryItem,所以「解析不到」與「這一格是空的」在 Lua 面看起來一致,既有腳本
    //      的判斷式不必多一種分支。要分辨兩者請用新增的 Exists。
    //    · 使用者明確呼叫的動作型方法(Use / Desynth / OpenContextMenu / MoveItemSlot)⇒ 記一行
    //      錯誤再返回;安靜失敗會讓巨集作者以為指令已經送出去了。
    //
    // ⚠️ Lua 面的成員名稱、參數與回傳型別一個都沒有改,既有腳本一行都不用動。
    // ⚠️ 唯一的行為差異:道具鍵在「提示那一格已經不是它」時重掃,取的是**第一個**命中的格子,
    //    而原建構子沒有 break、取的是**最後一個**。道具沒有移動時走的是提示快路徑、結果與原本
    //    完全相同,只有道具真的被搬走之後、而且同一件道具同時存在多格時才可能挑到不同的一格。
    public unsafe class InventoryItemWrapper : IWrapper
    {
        // 原本是每個包裝物件各配一份的實體欄位;內容是常數,改成靜態的省掉每次建構的陣列配置。
        private static readonly InventoryType[] PlayerInventories = [
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

        /// <summary>槽位鍵的容器。<see cref="InventoryType.Invalid"/>＝這個包裝物件沒有可用的鍵。</summary>
        private readonly InventoryType _container;

        /// <summary>槽位鍵的格號;道具鍵時只是「上次看到它在這一格」的提示。-1＝不知道。</summary>
        private readonly int _slot;

        /// <summary>道具鍵。0＝這個包裝物件認的是槽位而不是道具。</summary>
        private readonly uint _itemId;

        public InventoryItemWrapper(InventoryType container, int slot)
        {
            _container = container;
            _slot = slot;
        }

        public InventoryItemWrapper(InventoryContainer* container, int slot)
        {
            // 建構當下讀一次容器型別、把它換成鍵,之後再也不碰這個指標。
            (_container, _slot) = container == null ? (InventoryType.Invalid, -1) : (container->Type, slot);
        }

        public InventoryItemWrapper(InventoryItem* item) => (_container, _slot) = LocateByAddress(item);

        public InventoryItemWrapper(uint itemId)
        {
            _itemId = itemId;
            (_container, _slot) = FindByItemId(itemId);
        }

        /// <summary>
        /// 從裸指標把道具定位成 (容器, 格號)。
        /// 🔴 刻意不讀 item->Container / item->Slot,也不呼叫 GetInventoryType() / GetSlot():
        /// 前者是「這個結構自己說它在哪」（空格子的欄位有沒有維護沒有被驗證過），後者是虛擬函式 ——
        /// 位址若不是真的 InventoryItem 就等於透過假 vtable 跳轉,必定崩潰而且攔不到。
        /// ⇒ 只做指標範圍比較（純比較、完全不解參考,對任何輸入都安全）,確認它真的落在某個容器的
        /// Items 陣列裡,再把座標算出來。定不出來就是一個解析不到的包裝物件（所有成員回中性值）。
        /// </summary>
        private static (InventoryType Container, int Slot) LocateByAddress(InventoryItem* item)
        {
            if (item == null) return (InventoryType.Invalid, -1);

            var manager = InventoryManager.Instance();
            foreach (var type in Enum.GetValues<InventoryType>())
            {
                if (type == InventoryType.Invalid) continue;

                var container = manager->GetInventoryContainer(type);
                if (container == null) continue;

                var items = container->Items;
                var size = container->Size;
                if (items == null || size <= 0) continue;
                if (item < items || item >= items + size) continue;

                return (type, (int)(item - items));
            }

            return (InventoryType.Invalid, -1);
        }

        /// <summary>在玩家自己的容器裡找這件道具,回它現在的 (容器, 格號);找不到回 (Invalid, -1)。</summary>
        private static (InventoryType Container, int Slot) FindByItemId(uint itemId)
        {
            // itemId 0 沒有意義（那是「空格子」的值,會命中任何一個空位）。原本的建構子會把最後一個
            // 空格當成結果,而空格的每個欄位都是 0 —— 與這裡回「解析不到」時的中性值完全一樣。
            if (itemId == 0) return (InventoryType.Invalid, -1);

            var manager = InventoryManager.Instance();
            foreach (var inv in PlayerInventories)
            {
                var container = manager->GetInventoryContainer(inv);
                if (container == null) continue;

                var size = container->Size;
                for (var i = 0; i < size; i++)
                {
                    var slot = container->GetInventorySlot(i);
                    if (slot != null && slot->ItemId == itemId)
                        return (inv, i);
                }
            }

            return (InventoryType.Invalid, -1);
        }

        /// <summary>
        /// 解析出這一格的原生指標,並回報它現在真正的位置。
        /// 呼叫端一律先把回值存進區域變數,不要在同一個成員裡解析兩次（兩次之間狀態可能已經不同）。
        /// </summary>
        private InventoryItem* Resolve(out InventoryType container, out int slot)
        {
            container = _container;
            slot = _slot;

            var hinted = GetSlot(_container, _slot);

            // 槽位鍵:那一格是什麼就是什麼（空的也照回,與遊戲對空格回歸零結構的行為一致）。
            if (_itemId == 0) return hinted;

            // 道具鍵:先驗提示的那一格還是不是同一件道具（絕大多數情況道具根本沒動,這是 O(1) 快路徑）。
            if (hinted != null && hinted->ItemId == _itemId) return hinted;

            // 提示對不上 ⇒ 道具被搬走了,重掃一次。
            (container, slot) = FindByItemId(_itemId);
            return GetSlot(container, slot);
        }

        /// <summary>每次讀取都重新解析的原生指標。⚠️ 不要連續寫兩個 `Item->`,先存區域變數。</summary>
        private InventoryItem* Item => Resolve(out _, out _);

        private static InventoryItem* GetSlot(InventoryType container, int slot)
        {
            if (container == InventoryType.Invalid || slot < 0) return null;

            var c = InventoryManager.Instance()->GetInventoryContainer(container);
            if (c == null) return null;

            // 🔴 越界的格號不要送進 GetInventorySlot():那是虛擬函式,它做不做邊界檢查沒有被驗證過,
            // 而 Lua 面的索引子（container[index]）讓使用者可以傳任何數字進來。
            if (slot >= c->Size) return null;

            return c->GetInventorySlot(slot);
        }

        /// <summary>這個包裝物件現在解析得到真的道具格嗎。</summary>
        [LuaDocs(description: "Whether this wrapper still resolves to a real inventory slot right now. When it is false every other member returns the same neutral value an empty slot would (0 / false / nil).")]
        [Changelog(ChangelogAttribute.Unreleased)]
        public bool Exists => Item != null;

        [LuaDocs] public uint ItemId { get { var item = Item; return item == null ? 0u : item->ItemId; } }
        [LuaDocs] public uint BaseItemId { get { var item = Item; return item == null ? 0u : item->GetBaseItemId(); } }
        [LuaDocs] public int Count { get { var item = Item; return item == null ? 0 : item->Quantity; } }
        [LuaDocs] public ushort SpiritbondOrCollectability { get { var item = Item; return item == null ? (ushort)0 : item->SpiritbondOrCollectability; } }
        [LuaDocs] public ushort Condition { get { var item = Item; return item == null ? (ushort)0 : item->Condition; } }
        [LuaDocs] public uint GlamourId { get { var item = Item; return item == null ? 0u : item->GlamourId; } }
        [LuaDocs] public bool IsHighQuality { get { var item = Item; return item != null && item->IsHighQuality(); } }

        // 原本寫成 `Item->GetLinkedItem() is not null ? new(Item->GetLinkedItem()) : null`：解析三次
        // （其中兩次是 Item 這個當時會凍結的指標）,而且把回來的裸指標直接包起來。行為維持不變:
        //   · GetLinkedItem() 回 null ⇒ 回 null；
        //   · 非符號連結 ⇒ 它回的是這件道具自己,所以回這個包裝物件本身（鍵完全不會遺失）；
        //   · 符號連結 ⇒ 目標的容器與格號本來就寫在結構裡,直接拿來當鍵。
        // ⚠️ CS 對 GetLinkedItem() 的說明是散文註解、沒有在台服驗證過,所以用結構欄位組出來的鍵
        //    要先驗它解析回同一個位址;對不上就退回純指標範圍定位。
        [LuaDocs]
        public InventoryItemWrapper? LinkedItem
        {
            get
            {
                var item = Item;
                if (item == null) return null;

                var linked = item->GetLinkedItem();
                if (linked == null) return null;
                if (!item->IsSymbolic) return this;

                var keyed = new InventoryItemWrapper((InventoryType)item->LinkedInventoryType, item->LinkedItemSlot);
                return keyed.Item == linked ? keyed : new InventoryItemWrapper(linked);
            }
        }

        // 解析得到就回它現在真正的位置（道具鍵的包裝物件在道具被搬走之後會回新位置）;
        // 解析不到就回這個包裝物件當初認的那一格,道具鍵完全找不到時是 Invalid / -1。
        [LuaDocs] public InventoryType Container { get { Resolve(out var container, out _); return container; } }
        [LuaDocs] public int Slot { get { Resolve(out _, out var slot); return slot; } }

        [LuaDocs]
        public void Use()
        {
            // 動作型 ⇒ 記一行錯誤。原本解析不到時會拿 ItemId 的中性值 0 去呼叫 UseItem。
            var item = Item;
            if (item == null)
            {
                FrameworkLogger.Error("Cannot use item: this wrapper no longer resolves to an inventory slot.");
                return;
            }
            Game.UseItem(item->ItemId, item->IsHighQuality());
        }

        [LuaDocs(description: "Opens the right-click context menu for this inventory slot, as if the item was right-clicked.")]
        public void OpenContextMenu()
        {
            // 動作型 ⇒ 解析不到就記一行錯誤。原本 Container / Slot 各自解參考一次 Item。
            if (Resolve(out var container, out var slot) == null)
            {
                FrameworkLogger.Error("Cannot open the context menu: this wrapper no longer resolves to an inventory slot.");
                return;
            }

            var addonName = $"InventoryGrid{(int)container}E";
            var addon = (AtkUnitBase*)Svc.GameGui.GetAddonByName(addonName).Address;
            var addonId = addon != null ? addon->Id : (uint)0;

            // AgentInventoryContext.Instance() 是 C 類([Agent] 產生器,實作逐字帶
            // `agentModule == null ? null : ...`),未登入／UIModule 尚未建立時合法回 null。
            // 解參考它是 AccessViolation,corrupted-state exception,try/catch 攔不到。
            // 動作型方法 ⇒ 照 InstancesModule 的既有慣例記一行錯誤再返回,不要安靜失敗
            // (安靜失敗會讓巨集作者以為選單已經開了)。
            var agent = AgentInventoryContext.Instance();
            if (agent == null)
            {
                FrameworkLogger.Error("Inventory context agent is unavailable (not logged in?)");
                return;
            }
            agent->OpenForItemSlot(container, slot, 0, addonId);
        }

        [LuaDocs]
        [Changelog("12.8")]
        public void Desynth()
        {
            // 動作型 ⇒ 解析不到就記一行錯誤。⚠️ 順序必須是「先解析道具、再查表」:原本先讀 ItemId
            // 就已經解參考過 Item 了,而 SalvageItem 收的又是同一個指標,兩者必須是同一次解析的結果。
            var item = Item;
            if (item == null)
            {
                FrameworkLogger.Error("Cannot desynthesise: this wrapper no longer resolves to an inventory slot.");
                return;
            }

            if (GetRow<Sheets.Item>(item->ItemId)?.Desynth == 0)
                return;

            // 同上:AgentSalvage.Instance() 是 C 類,合法回 null。原本在同一支方法裡裸呼叫兩次,
            // 改成取一次本地指標、判空後重用(不跨幀保存)。
            var agent = AgentSalvage.Instance();
            if (agent == null)
            {
                FrameworkLogger.Error("Desynthesis agent is unavailable (not logged in?)");
                return;
            }

            agent->SalvageItem(item);
            var retval = new AtkValue();
            Span<AtkValue> param = [
                new AtkValue { Type = ValueType.Int, Int = 0 },
                new AtkValue { Type = ValueType.Bool, Byte = 1 }
            ];
            agent->AgentInterface.ReceiveEvent(&retval, param.GetPointer(0), 2, 1);
        }

        // 🔴 第 5 個引數 a6 是「這次搬移要不要送給伺服器」的總開關,不是無關緊要的 unknown。
        // a6=false(預設值)時遊戲只更新本機容器並刷新 UI,一個封包都不送 —— 對**任何**容器都成立,
        // 包含背包→背包。畫面上道具會動,但伺服器根本不知道,下一次同步就彈回原處。
        // 原本這裡省略了 a6,所以任何用這個 API 的 Lua 巨集都只改本機、靜默失敗。
        // 遊戲自己的拖放處理常式走的就是 a6=true,這裡照做。
        // ⚠️ 不做成可選參數:a6=false 對巨集作者沒有任何正當用途(只會製造本機與伺服器不一致),
        //    而且維持原本的呼叫形狀就不會動到 NLua 的參數繫結,既有腳本一行都不用改。
        // ⚠️ 目的地滿的時候**不搬**。GetFirstEmptySlot 找不到空格時原本回傳 0,
        // 在 a6 被省略的年代那只是本機亂改、沒有後果;現在封包會真的送出去,
        // 那就變成「與目的地 0 號格對調」——使用者沒要求的行為,而且只在容器剛好滿的時候
        // 才發生,很難重現。改成明確不動作並留下記錄。
        [LuaDocs(description: "Moves this item to the first empty slot of the given container. Does nothing if the destination has no empty slot. The move is sent to the server, i.e. it is a real move and not a local-only change.")]
        [Changelog("12.51")]
        public void MoveItemSlot(InventoryType destinationContainer)
        {
            // 動作型 ⇒ 解析不到就記一行錯誤。⚠️ 來源的容器與格號一定要取自同一次解析:分兩次讀
            // Container 與 Slot,道具剛好在兩次之間被搬走就會送出一組不存在的座標。
            var item = Resolve(out var container, out var slot);
            if (item == null)
            {
                FrameworkLogger.Error("MoveItemSlot: this wrapper no longer resolves to an inventory slot, nothing was moved.");
                return;
            }

            if (GetFirstEmptySlot(destinationContainer) is not { } destinationSlot)
            {
                FrameworkLogger.Warning(
                    $"MoveItemSlot: {destinationContainer} has no empty slot, item {item->ItemId} in {container}#{slot} was not moved.");
                return;
            }

            InventoryManager.Instance()->MoveItemSlot(container, (ushort)slot, destinationContainer, destinationSlot, a6: true);
        }
    }

    /// <summary>
    /// 目的地容器的第一個空格,找不到就回 <c>null</c>。
    /// 🔴 不要改回「找不到就回 0」:呼叫端會拿它當落點,而 0 號格通常是有東西的,
    /// 搬過去就是把兩件道具對調。容器讀不到(未載入／無效的 InventoryType)同樣回 null,
    /// 免得對空指標解參考——那是攔不到的 AccessViolation。
    /// </summary>
    private static unsafe ushort? GetFirstEmptySlot(InventoryType container)
    {
        var cont = InventoryManager.Instance()->GetInventoryContainer(container);
        if (cont == null) return null;

        for (ushort i = 0; i < cont->Size; i++)
            if (cont->Items[i].ItemId == 0)
                return i;
        return null;
    }
}
