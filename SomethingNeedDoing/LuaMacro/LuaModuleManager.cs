using NLua;
using SomethingNeedDoing.Core.Interfaces;
using SomethingNeedDoing.Documentation;
using SomethingNeedDoing.LuaMacro.Modules;

namespace SomethingNeedDoing.LuaMacro;
/// <summary>
/// Manages Lua modules and their registration.
/// </summary>
public class LuaModuleManager
{
    private readonly List<ILuaModule> _modules = [];
    private readonly LuaDocumentation _documentation;

    public LuaModuleManager(LuaDocumentation documentation)
    {
        _documentation = documentation;
        RegisterModule(new ActionsModule());
        RegisterModule(new AddonModule());
        RegisterModule(new ChatModule());
        RegisterModule(new DalamudModule());
        RegisterModule(new EntityModule());
        RegisterModule(new ExcelModule());
        RegisterModule(new FateModule());
        RegisterModule(new InstancedContentModule());
        RegisterModule(new InstancesModule());
        RegisterModule(new InventoryModule());
        RegisterModule(new IPCModule());
        RegisterModule(new PlayerModule());
        RegisterModule(new QuestsModule());
        RegisterModule(new SystemModule());

        // Register Engines module documentation statically
        EnginesModule.RegisterDocumentationStatic(_documentation);
    }

    public void RegisterAll(Lua lua) => _modules.ForEach(m => m.Register(lua));
    public T? GetModule<T>() where T : class, ILuaModule => _modules.FirstOrDefault(m => m is T, null) as T;

    public void RegisterModule(ILuaModule module)
    {
        if (module is LuaModuleBase baseModule)
            baseModule.SetModuleManager(this);
        if (module is LuaModuleBase baseModule2 && baseModule2.ParentType != null)
        {
            if (_modules.FirstOrDefault(m => m.GetType() == baseModule2.ParentType) is { } parent)
                baseModule2.ParentModule = parent;
            else
                throw new InvalidOperationException($"Parent module of type {baseModule2.ParentType.Name} not found for {module.GetType().Name}");
        }

        _modules.Add(module);

        if (module is IPCModule ipcModule)
            ipcModule.RegisterDocumentation(_documentation);
        else if (module is EnginesModule enginesModule)
            enginesModule.RegisterDocumentation(_documentation);
        else
            _documentation.RegisterModule(module);
    }
}
