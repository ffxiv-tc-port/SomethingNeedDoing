using Lumina.Excel.Sheets;
using System.Threading;
using System.Threading.Tasks;

namespace SomethingNeedDoing.NativeMacro.Commands;
/// <summary>
/// Uses a key item from the inventory.
/// </summary>
[GenericDoc(
    "Use a key item from your inventory",
    ["itemName"],
    ["/keyitem \"Wondrous Tails\"", "/keyitem \"Wondrous Tails\" <errorif.itemnotfound>"]
)]
public class KeyItemCommand(string text, string itemName) : MacroCommandBase(text)
{
    /// <inheritdoc/>
    public override bool RequiresFrameworkThread => true;

    /// <inheritdoc/>
    public override async Task Execute(MacroContext context, CancellationToken token)
    {
        var itemId = SearchItemId(itemName);
        FrameworkLogger.Debug($"KeyItem found: {itemId}");

        var count = Game.GetInventoryItemCount(itemId, false);
        FrameworkLogger.Debug($"Item Count: {count}");

        if (count == 0)
        {
            if (C.StopOnError || ErrorIfModifier?.Condition == Modifiers.ErrorCondition.ItemNotFound)
                throw new MacroException("You do not have that item");
            return;
        }

        await context.RunOnFramework(() => Game.UseItem(itemId, false));
        await PerformWait(token);
    }

    // 台服 EventItem 的名稱放在 Singular（Name 幾乎全空）：先照舊比 Name，找不到再比 Singular。
    private uint SearchItemId(string itemName)
        => FindRow<EventItem>(x => x.Name.ExtractText().Equals(itemName, StringComparison.InvariantCultureIgnoreCase))?.RowId
        ?? FindRow<EventItem>(x => x.Singular.ExtractText().Equals(itemName, StringComparison.InvariantCultureIgnoreCase))?.RowId
        ?? throw new MacroException($"Key item not found: {itemName}");
}
