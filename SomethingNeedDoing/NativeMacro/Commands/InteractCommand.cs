using System.Threading;
using System.Threading.Tasks;

namespace SomethingNeedDoing.NativeMacro.Commands;
/// <summary>
/// Interacts with the current target.
/// </summary>
[GenericDoc(
    "Interact with the current target",
    [],
    ["/interact", "/interact <errorif.targetnotfound>"]
)]
public class InteractCommand(string text) : MacroCommandBase(text)
{
    /// <inheritdoc/>
    public override bool RequiresFrameworkThread => true;

    /// <inheritdoc/>
    public override async Task Execute(MacroContext context, CancellationToken token)
    {
        await context.RunOnFramework(() =>
        {
            var target = Svc.Targets.Target;
            if (target == null && ErrorIfModifier?.Condition == Modifiers.ErrorCondition.TargetNotFound)
                throw new MacroException("Could not find target");

            if (Game.Interact(target))
                FrameworkLogger.Debug($"Interacting with [{target?.Address:X}] {target?.Name}");
            else
                FrameworkLogger.Warning("No target to interact with.");
        });

        await PerformWait(token);
    }
}
