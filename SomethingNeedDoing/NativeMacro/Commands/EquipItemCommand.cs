using ECommons.Automation;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System.Threading;
using System.Threading.Tasks;

namespace SomethingNeedDoing.NativeMacro.Commands;
/// <summary>
/// Equips an item from inventory or armory chest.
/// </summary>
[GenericDoc(
    "Equip an item from inventory or armory chest",
    ["itemId"],
    ["/equip 12345", "/equip 12345 <errorif.itemnotfound>"]
)]
public class EquipItemCommand(string text, uint itemId) : MacroCommandBase(text)
{
    private static int EquipAttemptLoops = 0;

    /// <inheritdoc/>
    public override bool RequiresFrameworkThread => true;

    /// <inheritdoc/>
    public override async Task Execute(MacroContext context, CancellationToken token)
    {
        await context.RunOnFramework(() => EquipItem(itemId));
        await Task.Delay(10, token); // Small delay to allow equip to process
        await PerformWait(token);
    }

    private unsafe void EquipItem(uint itemId)
    {
        var pos = FindItemInInventory(itemId, [
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
            InventoryType.ArmoryRings,
            InventoryType.ArmorySoulCrystal
        ]);

        if (pos == null)
        {
            // 這行本身就在「找不到道具」的錯誤回報路徑上。itemId 來自巨集參數,超出 Item 表範圍時 GetRow 回 null;
            // 查不到名字就退回 #<id> 這個字面替代名,不要讓錯誤訊息自己先擲例外、把真正要回報的錯誤蓋掉。
            var itemName = GetRow<Sheets.Item>(itemId)?.Name.ToString() ?? $"#{itemId}";
            FrameworkLogger.Error($"Failed to find item {itemName} (ID: {itemId}) in inventory");
            return;
        }

        var agentId = IsArmoryInventory(pos.Value.inv) ?
            AgentId.ArmouryBoard : AgentId.Inventory;

        // 三層都合法會回 null,而且每一層都要分開判:AgentModule.Instance()、GetAgentByInternalId()(代理人尚未建立)、
        // AgentInventoryContext.Instance()。任一層解參考 null 都是 AccessViolation,try/catch(包含巨集引擎自己的例外處理)
        // 完全攔不到。照同檔上方「找不到道具」的既有慣例:記一行錯誤後放棄這次 /equip,不擲例外。
        // 位址沒解出(改版後特徵碼對不上)時走同一條路:AgentChainResolved 涵蓋 GetAgentByInternalId。
        var agentsResolved = GameStatics.AgentChainResolved();
        var agentModule = agentsResolved ? AgentModule.Instance() : null;
        var inventoryAgent = agentModule is null ? null : agentModule->GetAgentByInternalId(agentId);
        var ctx = agentsResolved ? AgentInventoryContext.Instance() : null;
        if (inventoryAgent is null || ctx is null || !GameStatics.Resolved(AgentInventoryContext.MemberFunctionPointers.OpenForItemSlot, "AgentInventoryContext.OpenForItemSlot"))
        {
            FrameworkLogger.Error($"Cannot equip item #{itemId}: inventory agents are unavailable (not logged in?)");
            return;
        }

        var addonId = inventoryAgent->GetAddonId();
        ctx->OpenForItemSlot(pos.Value.inv, pos.Value.slot, 0, addonId);

        var contextMenu = (AtkUnitBase*)Svc.GameGui.GetAddonByName("ContextMenu").Address;
        if (contextMenu != null)
        {
            // AtkValuesCount 是 ContextMenu 這個 addon 的 AtkValues 陣列長度,跟 agent 的
            // _eventIds(FixedSizeArray84<byte>,84 格)是兩個不相干的長度。拿前者當後者的
            // 迴圈上界,只要選單的 AtkValuesCount 超過 84 就會丟 IndexOutOfRangeException
            // (產生器產出的是 Span<byte>,有邊界檢查,所以不會 AVE,但 /equip 會靜默失效)。
            // 取兩者較小值,索引的上下界都要驗。
            var entryCount = System.Math.Min(contextMenu->AtkValuesCount, ctx->EventIds.Length);

            // 第 n 個選單項在 EventIds 的第 firstEntry+n 格、callback 編號是 n；firstEntry 由遊戲每次開選單時寫入
            // (7.25 是 7、7.4 世代是 8),不寫死。讀到界外值就什麼都不選、關掉選單並中止。
            var firstEntry = ctx->ContexItemStartIndex;
            if (firstEntry < 1 || firstEntry >= ctx->EventIds.Length)
            {
                FrameworkLogger.Error($"/equip: AgentInventoryContext.ContexItemStartIndex={firstEntry} is out of range; not selecting any entry");
                Callback.Fire(contextMenu, true, 0, -1, 0, 0, 0);
                throw new MacroException($"Cannot equip item #{itemId}: unexpected context menu layout (first entry index {firstEntry})");
            }

            // 🔴🔴 一次 /equip 對同一扇選單最多只能送「一發」close:true 的 callback:Callback.Fire 的 updateState 就是原生
            //    FireCallback 的 close 參數,close 為真時原生會在**回到這段 C# 之前**跑完 Hide + Close ⇒ 送出之後
            //    contextMenu 這個指標隨時可能已經失效,而對失效的原生指標再開火是攔不到的存取違規。

            // ⚠️ 不能靠「再 GetAddonByName 解一次位址」當守衛:Close 只把窗從顯示清單移除、不動 GetAddonByName 查的那張表,
            //    關掉之後照樣解得到同一個位址;再加 IsVisible/IsReady 也不夠。
            // ⇒ 唯一能離線證明安全的做法就是「送過就再也不碰它」。
            var equipCallbackSent = false;
            for (var i = firstEntry; i < entryCount; i++)
            {
                var firstEntryIsEquip = ctx->EventIds[i] == 25;
                if (firstEntryIsEquip)
                {
                    FrameworkLogger.Debug($"/equip: sending equip callback for item #{itemId} from {pos.Value.inv} @ {pos.Value.slot}, index {i}");
                    Callback.Fire(contextMenu, true, 0, i - firstEntry, 0, 0, 0);
                    equipCallbackSent = true;
                    // 送出的那一刻 contextMenu 就可能已經失效,迴圈不可以再跑下一輪。
                    break;
                }
            }

            if (equipCallbackSent)
            {
                FrameworkLogger.Debug("/equip: the equip callback already closed the context menu; skipping the redundant close callback");
            }
            else
            {
                // 走到這裡代表上面一發都沒送出去 ⇒ 這扇選單還是幾行前才剛解出來的那一扇,
                // 中間沒有任何原生程式碼碰過它,送 p2=-1(關閉)是安全的。
                Callback.Fire(contextMenu, true, 0, -1, 0, 0, 0);
            }
            EquipAttemptLoops++;

            if (EquipAttemptLoops >= 5)
                throw new MacroException("Failed to find equip option after 5 attempts");
        }
    }

    private static unsafe (InventoryType inv, int slot)? FindItemInInventory(uint itemId, IEnumerable<InventoryType> inventories)
    {
        foreach (var inv in inventories)
        {
            // 位址沒解出或容器不存在時回 null:跳過這個容器,最後落到「找不到道具」那條錯誤路徑。
            var cont = GameStatics.TryInventoryContainer(inv);
            if (cont == null) continue;
            for (var i = 0; i < cont->Size; ++i)
            {
                if (cont->GetInventorySlot(i)->ItemId == itemId)
                    return (inv, i);
            }
        }
        return null;
    }

    private static bool IsArmoryInventory(InventoryType type) => type switch
    {
        InventoryType.ArmoryMainHand or
        InventoryType.ArmoryOffHand or
        InventoryType.ArmoryHead or
        InventoryType.ArmoryBody or
        InventoryType.ArmoryHands or
        InventoryType.ArmoryLegs or
        InventoryType.ArmoryFeets or
        InventoryType.ArmoryEar or
        InventoryType.ArmoryNeck or
        InventoryType.ArmoryWrist or
        InventoryType.ArmoryRings or
        InventoryType.ArmorySoulCrystal => true,
        _ => false
    };
}
