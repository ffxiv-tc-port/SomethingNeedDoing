using SomethingNeedDoing.NativeMacro.Modifiers;
using System.Threading;
using System.Threading.Tasks;

namespace SomethingNeedDoing.Core.Interfaces;
/// <summary>
/// Represents a command that can be executed as part of a macro.
/// </summary>
public interface IMacroCommand
{
    /// <summary>
    /// Gets whether this command must run on the framework thread.
    /// </summary>
    public bool RequiresFrameworkThread { get; }

    /// <summary>
    /// Gets the original text of the command.
    /// </summary>
    public string CommandText { get; }

    /// <summary>
    /// Gets or sets the wait modifier for the command.
    /// </summary>
    WaitModifier? WaitModifier { get; set; }

    /// <summary>
    /// Gets or sets the echo modifier for the command.
    /// </summary>
    EchoModifier? EchoModifier { get; set; }

    /// <summary>
    /// Gets or sets the unsafe modifier for the command.
    /// </summary>
    UnsafeModifier? UnsafeModifier { get; set; }

    /// <summary>
    /// Gets or sets the condition modifier for the command.
    /// </summary>
    ConditionModifier? ConditionModifier { get; set; }

    /// <summary>
    /// Gets or sets the max wait modifier for the command.
    /// </summary>
    MaxWaitModifier? MaxWaitModifier { get; set; }

    /// <summary>
    /// Gets or sets the index modifier for the command.
    /// </summary>
    IndexModifier? IndexModifier { get; set; }

    /// <summary>
    /// Gets or sets the list index modifier for the command.
    /// </summary>
    ListIndexModifier? ListIndexModifier { get; set; }

    /// <summary>
    /// Gets or sets the party index modifier for the command.
    /// </summary>
    PartyIndexModifier? PartyIndexModifier { get; set; }

    /// <summary>
    /// Gets or sets the distance modifier for the command.
    /// </summary>
    DistanceModifier? DistanceModifier { get; set; }

    /// <summary>
    /// Gets or sets the item quality modifier for the command.
    /// </summary>
    HqModifier? ItemQualityModifier { get; set; }

    /// <summary>
    /// Gets or sets the error if modifier for the command.
    /// </summary>
    ErrorIfModifier? ErrorIfModifier { get; set; }

    /// <summary>
    /// Executes the command with the given context and cancellation token.
    /// </summary>
    /// <param name="context">The context in which the command is executing.</param>
    /// <param name="token">A token to cancel execution.</param>
    public Task Execute(MacroContext context, CancellationToken token);

    /// <summary>
    /// Parses a command from text.
    /// </summary>
    /// <param name="text">The text to parse.</param>
    /// <returns>The parsed command.</returns>
    /// <exception cref="MacroSyntaxError">Thrown when the text cannot be parsed as a valid command.</exception>
    //public IMacroCommand Parse(string text);
}
