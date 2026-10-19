using NLua;
using SomethingNeedDoing.Core.Interfaces;
using System.Linq.Expressions;
using System.Reflection;

namespace SomethingNeedDoing.LuaMacro;
/// <summary>
/// Base class for Lua API modules
/// </summary>
public abstract class LuaModuleBase : ILuaModule
{
    public abstract string ModuleName { get; }
    public ILuaModule? ParentModule { get; set; }
    public virtual Type? ParentType => null;

    private LuaModuleManager? _moduleManager;
    internal void SetModuleManager(LuaModuleManager manager) => _moduleManager = manager;
    protected T? GetModule<T>() where T : class, ILuaModule => _moduleManager?.GetModule<T>();

    private readonly Dictionary<string, Delegate> _propertyDelegateMap = [];

    public virtual void Register(Lua lua)
    {
        RegisterEnumsFromReturnTypes(lua);

        // Create module table
        var modulePath = GetModulePath();
        lua.DoString($"{modulePath} = {{}}");
        RegisterMetaTable(lua, modulePath);

        // Register all methods marked with LuaFunction attribute
        var registeredNames = new HashSet<string>();
        foreach (var method in GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            if (method.GetCustomAttribute<LuaFunctionAttribute>() is not { } attr) continue;
            var name = attr.Name ?? method.Name;

            // Lua 註冊表不支援同名 overload,後註冊者會蓋掉先註冊者;
            // 撞名時記警告,提醒用 [LuaFunction(name: ...)] 給其中一個獨立名稱
            if (!registeredNames.Add(name))
                FrameworkLogger.Warning($"Duplicate Lua function name {modulePath}.{name}: a previously registered overload is being overwritten. Give one of the overloads a distinct [LuaFunction] name.");

            lua[$"{modulePath}.{name}"] = CreateDelegate(method);
        }

        // Register all properties marked with LuaFunction attribute as getter functions for the metatable
        foreach (var property in GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetCustomAttribute<LuaFunctionAttribute>() is not { } attr) continue;
            var name = attr.Name ?? property.Name;
            _propertyDelegateMap[name] = CreateDelegate(property.GetMethod!);
        }
    }

    /// <summary>
    /// Automatically registers enums that are used as return types in LuaFunction and LuaDocs attributes
    /// </summary>
    /// <param name="lua">The Lua state</param>
    private void RegisterEnumsFromReturnTypes(Lua lua)
    {
        var enumTypes = new HashSet<Type>();
        ScanTypeForEnums(GetType(), enumTypes);
        ScanWrapperTypesForEnums(enumTypes);

        foreach (var enumType in enumTypes)
        {
            try
            {
                if (typeof(LuaExtensions).GetMethod("RegisterEnum", BindingFlags.Public | BindingFlags.Static) is { } method)
                    method.MakeGenericMethod(enumType).Invoke(null, [lua]);
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"Failed to register enum {enumType.Name}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Recursively scans a type and its nested types for enum return types in LuaFunction and LuaDocs attributes
    /// </summary>
    /// <param name="type">The type to scan</param>
    /// <param name="enumTypes">Set to collect found enum types</param>
    private void ScanTypeForEnums(Type type, HashSet<Type> enumTypes)
    {
        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            if (method.GetCustomAttribute<LuaFunctionAttribute>() != null)
            {
                if (method.ReturnType.IsEnum)
                    enumTypes.Add(method.ReturnType);

                foreach (var parameter in method.GetParameters())
                    if (parameter.ParameterType.IsEnum)
                        enumTypes.Add(parameter.ParameterType);
            }
        }

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            if (property.GetCustomAttribute<LuaFunctionAttribute>() != null && property.PropertyType.IsEnum)
                enumTypes.Add(property.PropertyType);

        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            if (method.GetCustomAttribute<LuaDocsAttribute>() != null)
            {
                if (method.ReturnType.IsEnum)
                    enumTypes.Add(method.ReturnType);

                foreach (var parameter in method.GetParameters())
                    if (parameter.ParameterType.IsEnum)
                        enumTypes.Add(parameter.ParameterType);
            }
        }

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            if (property.GetCustomAttribute<LuaDocsAttribute>() != null && property.PropertyType.IsEnum)
                enumTypes.Add(property.PropertyType);

        foreach (var nestedType in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
            ScanTypeForEnums(nestedType, enumTypes);
    }

    /// <summary>
    /// Scans wrapper types returned by LuaFunction methods/properties for enum return types in LuaDocs attributes
    /// </summary>
    /// <param name="enumTypes">Set to collect found enum types</param>
    private void ScanWrapperTypesForEnums(HashSet<Type> enumTypes)
    {
        foreach (var method in GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance))
            if (method.GetCustomAttribute<LuaFunctionAttribute>() != null && IsWrapperType(method.ReturnType))
                ScanTypeForEnums(method.ReturnType, enumTypes);

        foreach (var property in GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            if (property.GetCustomAttribute<LuaFunctionAttribute>() != null && IsWrapperType(property.PropertyType))
                ScanTypeForEnums(property.PropertyType, enumTypes);
    }

    private bool IsWrapperType(Type type)
    {
        if (type == null) return false;
        if (typeof(IWrapper).IsAssignableFrom(type))
            return true;
        if (type.IsGenericType)
            foreach (var arg in type.GetGenericArguments())
                if (IsWrapperType(arg))
                    return true;

        return false;
    }

    protected virtual object? MetaIndex(LuaTable table, object key) => null;

    private object? MetatableOnIndex(LuaTable table, object key) => key switch
    {
        string stringKey when _propertyDelegateMap.TryGetValue(stringKey, out var propertyFunc) => propertyFunc.DynamicInvoke(),
        _ => MetaIndex(table, key)
    };

    private void RegisterMetaTable(Lua lua, string modulePath)
    {
        var metaPath = $"{modulePath}.__mt";
        var metaTable = lua.GetTable(metaPath);
        if (metaTable == null)
        {
            lua.NewTable(metaPath);
            lua.DoString($"setmetatable({modulePath}, {metaPath})");
            metaTable = lua.GetTable(metaPath);
        }

        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        metaTable[nameof(MetatableOnIndex)] = CreateDelegate(typeof(LuaModuleBase).GetMethod(nameof(MetatableOnIndex), flags)!);
        lua.DoString($"{metaPath}.__index = function(...) return {metaPath}.{nameof(MetatableOnIndex)}(...) end");
    }

    public string GetModulePath()
    {
        if (ParentModule == null)
            return ModuleName;

        if (ParentModule is LuaModuleBase parent)
            return $"{parent.GetModulePath()}.{ModuleName}";

        return $"{ParentModule.ModuleName}.{ModuleName}";
    }

    private Delegate CreateDelegate(MethodInfo method)
    {
        var delegateType = GetDelegateType(method);
        return Delegate.CreateDelegate(delegateType, this, method);
    }

    private Type GetDelegateType(MethodInfo method)
    {
        var parameters = method.GetParameters().Select(p => p.ParameterType).ToArray();
        return method.ReturnType == typeof(void) ? Expression.GetActionType(parameters) : Expression.GetFuncType([.. parameters, method.ReturnType]);
    }
}
