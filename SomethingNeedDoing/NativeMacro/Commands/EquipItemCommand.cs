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
            FrameworkLogger.Error($"Failed to find item {GetRow<Sheets.Item>(itemId)!.Value.Name} (ID: {itemId}) in inventory");
            return;
        }

        var agentId = IsArmoryInventory(pos.Value.inv) ?
            AgentId.ArmouryBoard : AgentId.Inventory;

        var addonId = AgentModule.Instance()->GetAgentByInternalId(agentId)->GetAddonId();
        var ctx = AgentInventoryContext.Instance();
        ctx->OpenForItemSlot(pos.Value.inv, pos.Value.slot, 0, addonId);

        var contextMenu = (AtkUnitBase*)Svc.GameGui.GetAddonByName("ContextMenu").Address;
        if (contextMenu != null)
        {
            for (var i = 0; i < contextMenu->AtkValuesCount; i++)
            {
                var firstEntryIsEquip = ctx->EventIds[i] == 25;
                if (firstEntryIsEquip)
                {
                    FrameworkLogger.Debug($"Equipping item #{itemId} from {pos.Value.inv} @ {pos.Value.slot}, index {i}");
                    Callback.Fire(contextMenu, true, 0, i - 7, 0, 0, 0);
                }
            }
            Callback.Fire(contextMenu, true, 0, -1, 0, 0, 0);
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
