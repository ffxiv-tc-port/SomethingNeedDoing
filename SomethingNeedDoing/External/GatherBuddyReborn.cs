using ECommons.EzIpcManager;
using SomethingNeedDoing.Core.Interfaces;

namespace SomethingNeedDoing.External;

/// <summary>
/// GatherBuddyReborn 的自動採集開關。
/// ⚠️ 加這個包裝的原因：GBR 沒有任何「停止自動採集」的聊天指令
/// （<c>/gbr</c> 的子指令只有 window / alarm / spear / fish / edit / unlock），
/// 唯一的關法就是這條 IPC。少了它，用巨集寫的全艦隊急停會靜默漏掉 GBR ——
/// 一個「按了但還有東西在跑」的急停比沒有更危險。
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
