using ECommons.EzIpcManager;
using SomethingNeedDoing.Core.Interfaces;

namespace SomethingNeedDoing.External;

public class Artisan : IPC
{
    public override string Name => "Artisan";
    public override string Repo => Repos.TcPort;

    [EzIPC]
    [LuaFunction(description: "Gets the endurance status")]
    public Func<bool> GetEnduranceStatus = null!;

    [EzIPC]
    [LuaFunction(
        description: "Sets the endurance status",
        parameterDescriptions: ["enabled"])]
    public Action<bool> SetEnduranceStatus = null!;

    [EzIPC]
    [LuaFunction(description: "Checks if a list is currently running")]
    public Func<bool> IsListRunning = null!;

    [EzIPC]
    [LuaFunction(description: "Checks if a list is currently paused")]
    public Func<bool> IsListPaused = null!;

    [EzIPC]
    [LuaFunction(
        description: "Sets the pause state of a list",
        parameterDescriptions: ["paused"])]
    public Action<bool> SetListPause = null!;

    [EzIPC]
    [LuaFunction(description: "Gets the stop request status")]
    public Func<bool> GetStopRequest = null!;

    [EzIPC]
    [LuaFunction(
        description: "Sets the stop request status",
        parameterDescriptions: ["stop"])]
    public Action<bool> SetStopRequest = null!;

    [EzIPC]
    [LuaFunction(
        description: "Crafts a specific item",
        parameterDescriptions: ["itemId", "quantity"])]
    public Action<ushort, int> CraftItem = null!;
}
