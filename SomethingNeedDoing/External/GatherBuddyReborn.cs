using ECommons.EzIpcManager;
using SomethingNeedDoing.Core.Interfaces;

namespace SomethingNeedDoing.External;

/// <summary>
/// GatherBuddyReborn 的自動採集開關。
/// ⚠️ 急停要用這條 IPC（或完整指令 <c>/gatherbuddy auto off</c>）：短指令 <c>/gbr</c> 的子指令只有
/// window / alarm / spear / fish / edit / unlock，沒有任何「停止自動採集」。
/// 用巨集寫的全艦隊急停漏掉 GBR 的話，就是一個「按了但還有東西在跑」的急停，比沒有更危險。
/// </summary>
public class GatherBuddyReborn : IPC
{
    public override string Name => "GatherBuddyReborn";
    public override string Repo => Repos.TcPort;

    [EzIPC]
    [LuaFunction(description: "Checks whether GatherBuddyReborn auto-gather is currently enabled")]
    public readonly Func<bool> IsAutoGatherEnabled = null!;

    [EzIPC]
    [LuaFunction(
        description: "Turns GatherBuddyReborn auto-gather on or off",
        parameterDescriptions: ["enabled"])]
    public readonly Action<bool> SetAutoGatherEnabled = null!;

    [EzIPC]
    [LuaFunction(description: "Gets the current auto-gather status text")]
    public readonly Func<string> GetAutoGatherStatusText = null!;
}
