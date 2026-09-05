using ECommons.EzIpcManager;
using SomethingNeedDoing.Core.Interfaces;

namespace SomethingNeedDoing.External;

#nullable disable
public class BossMod : IPC
{
    public override string Name => "BossMod";
    public override string Repo => Repos.TcPort;

    [EzIPC("Presets.%m", true)]
    [LuaFunction(
        description: "Gets a preset",
        parameterDescriptions: ["name"])]
    public readonly Func<string, string> Get;

    [EzIPC("Presets.%m", true)]
    [LuaFunction(
        description: "Creates a preset",
        parameterDescriptions: ["name", "overwrite"])]
    public readonly Func<string, bool, bool> Create;

    [EzIPC("Presets.%m", true)]
    [LuaFunction(
        description: "Deletes a preset",
        parameterDescriptions: ["name"])]
    public readonly Func<string, bool> Delete;

    [EzIPC("Presets.%m", true)]
    [LuaFunction(description: "Gets the active preset")]
    public readonly Func<string> GetActive;

    [EzIPC("Presets.%m", true)]
    [LuaFunction(
        description: "Sets the active preset",
        parameterDescriptions: ["name"])]
    public readonly Func<string, bool> SetActive;

    [EzIPC("Presets.%m", true)]
    [LuaFunction(description: "Clears the active preset")]
    public readonly Func<bool> ClearActive;

    [EzIPC("Presets.%m", true)]
    [LuaFunction(description: "Gets whether force disabled is enabled")]
    public readonly Func<bool> GetForceDisabled;

    [EzIPC("Presets.%m", true)]
    [LuaFunction(description: "Sets whether force disabled is enabled")]
    public readonly Func<bool> SetForceDisabled;

    [EzIPC("Presets.%m", true)]
    [LuaFunction(
        description: "Adds a transient strategy",
        parameterDescriptions: ["name", "strategyName", "description", "code"])]
    public readonly Func<string, string, string, string, bool> AddTransientStrategy;

    [EzIPC("Presets.%m", true)]
    [LuaFunction(
        description: "Adds a transient strategy with target enemy OID",
        parameterDescriptions: ["name", "strategyName", "description", "code", "targetEnemyOID"])]
    public readonly Func<string, string, string, string, int, bool> AddTransientStrategyTargetEnemyOID;
}
