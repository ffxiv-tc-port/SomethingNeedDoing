using NLua;
using SomethingNeedDoing.Documentation;
using SomethingNeedDoing.LuaMacro.Modules.Engines;
using System.Threading.Tasks;

namespace SomethingNeedDoing.LuaMacro.Modules;

/// <summary>
/// Lua module that provides access to various execution engines.
/// </summary>
public class EnginesModule : LuaModuleBase
{
    private readonly Dictionary<string, IEngine> _engines = [];

    public override string ModuleName => "Engines";

    public EnginesModule(IEnumerable<IEngine> engines)
    {
        foreach (var engine in engines)
            _engines[engine.Name] = engine;
    }

    public override void Register(Lua lua)
    {
        base.Register(lua);
        RegisterEngineFunctions(lua);
    }

    public void RegisterDocumentation(LuaDocumentation docs)
    {
        var moduleDocs = new List<LuaFunctionDoc>
        {
            // Register global helper functions
            new(
            ModuleName,
            "Run",
            "Executes content using the best available engine (fire and forget)",
            LuaTypeConverter.GetLuaType(typeof(void)),
            [("content", LuaTypeConverter.GetLuaType(typeof(string)), "The content to execute", null)],
            null,
            true
        )
        };

        // Register each engine's functions
        foreach (var (name, engine) in _engines)
        {
            moduleDocs.Add(new LuaFunctionDoc(
                $"{ModuleName}.{name}",
                "Run",
                $"Executes content using the {engine.Name} engine (fire and forget)",
                LuaTypeConverter.GetLuaType(typeof(void)),
                [("content", LuaTypeConverter.GetLuaType(typeof(string)), "The content to execute", null)],
                null,
                true
            ));
        }

        docs.RegisterModule(this, moduleDocs);
    }

    /// <summary>
    /// Registers documentation for the Engines module at startup (without requiring actual engine instances).
    /// </summary>
    /// <param name="docs">The documentation system to register with.</param>
    public static void RegisterDocumentationStatic(LuaDocumentation docs)
    {
        var moduleDocs = new List<LuaFunctionDoc>
        {
            new(
            "Engines",
            "Run",
            "Executes content using the best available engine (fire and forget)",
            LuaTypeConverter.GetLuaType(typeof(void)),
            [("content", LuaTypeConverter.GetLuaType(typeof(string)), "The content to execute", null)],
            null,
            true
        )
        };

        var knownEngines = new[] { "Native", "NLua" };
        foreach (var engineName in knownEngines)
        {
            moduleDocs.Add(new LuaFunctionDoc(
                $"Engines.{engineName}",
                "Run",
                $"Executes content using the {engineName} engine (fire and forget)",
                LuaTypeConverter.GetLuaType(typeof(void)),
                [("content", LuaTypeConverter.GetLuaType(typeof(string)), "The content to execute", null)],
                null,
                true
            ));
        }

        docs.RegisterModule("Engines", moduleDocs);
    }

    private void RegisterEngineFunctions(Lua lua)
    {
        // Register each engine
        foreach (var (name, engine) in _engines)
        {
            var captured = engine;
            lua[$"{ModuleName}.{name}"] = new
            {
                Run = new Action<string>(content => ExecuteEngine(captured, content)),
                // 相容層:上游已移除 RunAsync。它回傳的 Task 在 Lua 端本來就 await 不了,
                // 行為上等同 fire-and-forget;這裡保留名稱並轉接到 Run,免得舊巨集直接壞掉。
                RunAsync = new Action<string>(content => { WarnRunAsyncDeprecated(); ExecuteEngine(captured, content); }),
            };
        }

        // Register helper functions for auto-detection
        lua[$"{ModuleName}.Run"] = new Action<string>(ExecuteBestEngine);
        lua[$"{ModuleName}.RunAsync"] = new Action<string>(content => { WarnRunAsyncDeprecated(); ExecuteBestEngine(content); });
    }

    private static bool _warnedRunAsync;

    private static void WarnRunAsyncDeprecated()
    {
        if (_warnedRunAsync) return;
        _warnedRunAsync = true;
        FrameworkLogger.Warning("Engines.RunAsync is deprecated and now behaves exactly like Engines.Run; please switch to Run.");
    }

    /// <summary>
    /// Executes content using the specified engine synchronously (fire and forget).
    /// </summary>
    /// <param name="engine">The engine to use.</param>
    /// <param name="content">The content to execute.</param>
    private void ExecuteEngine(IEngine engine, string content)
    {
        // Execute synchronously (fire and forget)
        _ = Task.Run(async () =>
        {
            try
            {
                await engine.ExecuteAsync(content);
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"Error executing {engine.Name} content: {ex}");
            }
        });
    }

    /// <summary>
    /// Executes content using the best available engine.
    /// </summary>
    /// <param name="content">The content to execute.</param>
    private void ExecuteBestEngine(string content)
    {
        if (FindBestEngine(content) is { } engine)
            ExecuteEngine(engine, content);
        else
            FrameworkLogger.Warning($"No suitable engine found for content: {content}");
    }

    /// <summary>
    /// Finds the best engine for executing the given content.
    /// </summary>
    /// <param name="content">The content to find an engine for.</param>
    /// <returns>The best engine, or null if none found.</returns>
    private IEngine? FindBestEngine(string content) => _engines.Values.FirstOrDefault(engine => engine.CanExecute(content));
}
