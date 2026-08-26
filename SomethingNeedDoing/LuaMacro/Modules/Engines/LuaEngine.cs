using SomethingNeedDoing.Core.Events;
using System.Threading;
using System.Threading.Tasks;

namespace SomethingNeedDoing.LuaMacro.Modules.Engines;

/// <summary>
/// Engine for executing Lua code.
/// </summary>
public class LuaEngine : IEngine
{
    private readonly NLuaMacroEngine _luaEngine;

    public string Name => "NLua";

    /// <inheritdoc/>
    public event EventHandler<MacroExecutionRequestedEventArgs>? MacroExecutionRequested;

    public LuaEngine(NLuaMacroEngine luaEngine)
    {
        _luaEngine = luaEngine;

        // Forward macro execution requests from the Lua engine
        luaEngine.MacroExecutionRequested += (sender, e) =>
            MacroExecutionRequested?.Invoke(this, e);
    }

    public async Task ExecuteAsync(string content, CancellationToken cancellationToken = default)
    {
        try
        {
            var tempMacro = new TemporaryMacro(content) { Type = MacroType.Lua };
            await _luaEngine.StartMacro(tempMacro, cancellationToken);
        }
        catch (Exception ex)
        {
            FrameworkLogger.Error($"Error executing Lua code '{content}': {ex}");
            throw;
        }
    }

    public bool CanExecute(string content) => !string.IsNullOrWhiteSpace(content) && !content.StartsWith('/');
}
