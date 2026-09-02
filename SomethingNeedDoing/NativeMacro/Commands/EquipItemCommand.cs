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
            // 這行本身就在「找不到道具」的錯誤回報路徑上。itemId 來自巨集參數,超出 Item 表範圍時
            // GetRow 回 null,原本的 !.Value 會讓錯誤訊息自己先擲 InvalidOperationException——
            // 真正要回報的錯誤(道具不在背包裡)反而被蓋掉。查不到名字就退回 #<id> 這個字面替代名。
            var itemName = GetRow<Sheets.Item>(itemId)?.Name.ToString() ?? $"#{itemId}";
            FrameworkLogger.Error($"Failed to find item {itemName} (ID: {itemId}) in inventory");
            return;
        }

        var agentId = IsArmoryInventory(pos.Value.inv) ?
            AgentId.ArmouryBoard : AgentId.Inventory;

        // 三層都合法會回 null,而且每一層都要分開判:
        //  - AgentModule.Instance() 手寫成 `uiModule == null ? null : uiModule->GetAgentModule()`
        //  - GetAgentByInternalId() 對尚未建立的代理人回 null
        //  - AgentInventoryContext.Instance() 是 [Agent] 產生器版(同樣帶 `== null ? null :`)
        // 任一層解參考 null 都是 AccessViolation,而 AVE 在 .NET Core 是 corrupted-state
        // exception,try/catch(包含巨集引擎自己的例外處理)完全攔不到。
        // 照同檔上方「找不到道具」的既有慣例:記一行錯誤後放棄這次 /equip,不擲例外。
        var agentModule = AgentModule.Instance();
        var inventoryAgent = agentModule is null ? null : agentModule->GetAgentByInternalId(agentId);
        var ctx = AgentInventoryContext.Instance();
        if (inventoryAgent is null || ctx is null)
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

            // 第 n 個選單項對應的事件編號是 7+n,所以 i<7 沒有對應的選單列;
            // 那種情況下 i-7 會是負數,而 p2=-1 對選單的語意是「關閉」——
            // 送出去等於在還沒找到裝備選項時就把選單關掉。
            // 🔴🔴 一次 /equip 對同一扇選單最多只能送「一發」close:true 的 callback。
            // ECommons `Callback.Fire(base, updateState, …)` 的 updateState 就是原生
            // AtkUnitBase::FireCallback 的 close 參數。台服 7.20 反組譯(FireCallback = 0x1406422B0):
            // close 為真且處理常式回非零時,原生會在**回到這段 C# 之前**於同一個呼叫堆疊內
            // 跑完 vf6 Hide + vf4 Close。也就是說送出裝備那一發之後,contextMenu 這個指標
            // 隨時可能已經失效,而對失效的原生指標再開火是攔不到的存取違規
            // (AVE 在 .NET Core 是 corrupted-state exception,try/catch 完全無效)。
            // 原本的碼迴圈裡沒有 break、迴圈後又無條件再送一發關閉,兩處都踩在這條線上。
            //
            // ⚠️ 不能靠「再 GetAddonByName 解一次位址」當守衛:台服
            //    AtkUnitManager::GetAddonByName(0x14064B960)查的是 AllLoadedUnitsList(管理器 +0x6900),
            //    而 AtkUnitBase::Close(0x14063CFE0)只把窗從 UnitList16(+0x7920)移除、完全不動那張表
            //    ⇒ 關掉之後照樣解得到同一個位址,守衛等於不存在。再加 IsVisible/IsReady 也不夠
            //    (艦隊已有「三關全過但 FireCallback 仍 AVE」的實例)。
            // ⇒ 唯一能離線證明安全的做法就是「送過就再也不碰它」。
            var equipCallbackSent = false;
            for (var i = 7; i < entryCount; i++)
            {
                var firstEntryIsEquip = ctx->EventIds[i] == 25;
                if (firstEntryIsEquip)
                {
                    FrameworkLogger.Debug($"/equip: sending equip callback for item #{itemId} from {pos.Value.inv} @ {pos.Value.slot}, index {i}");
                    Callback.Fire(contextMenu, true, 0, i - 7, 0, 0, 0);
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
            var cont = InventoryManager.Instance()->GetInventoryContainer(inv);
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
