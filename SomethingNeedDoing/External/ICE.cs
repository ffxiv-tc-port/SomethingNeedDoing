using ECommons.EzIpcManager;
using SomethingNeedDoing.Core.Interfaces;

namespace SomethingNeedDoing.External;

/// <summary>ICE（宇宙探索自動化）的 IPC。</summary>
/// <remarks>
/// 🔴 每個欄位的型別必須和 ICE 端 <c>ICE.IPC.IceCosmicExplorationIPC</c> 完全一致：EzIPC 只用名稱配對，型別不符要到呼叫時才擲例外。
/// <c>ChangeSetting</c> 只認 OnlyGrabMission、StopAfterCurrent、StopOnceHitCosmoCredits、StopOnceHitLunarCredits、XPRelicGrind，其他鍵 ICE 靜默忽略。
/// 呼叫前先用 <c>IPC.IsInstalled("ICE")</c> 確認 ICE 有載入。
/// </remarks>
public class ICE : IPC
{
    public override string Name => "ICE";
    public override string Repo => Repos.TcPort;

    [EzIPC]
    [LuaFunction(description: "Returns true while ICE is doing anything (its scheduler is not Idle).")]
    public readonly Func<bool> IsRunning = null!;

    [EzIPC]
    [LuaFunction(description: "Starts ICE, same as /ice start.")]
    public readonly Action Enable = null!;

    [EzIPC]
    [LuaFunction(description: "Stops ICE right away, same as /ice stop.")]
    public readonly Action Disable = null!;

    [EzIPC]
    [LuaFunction(
        description: "Turns an ICE option on or off. Keys: OnlyGrabMission, StopAfterCurrent (finish the current mission, then stop), StopOnceHitCosmoCredits, StopOnceHitLunarCredits, XPRelicGrind.",
        parameterDescriptions: ["config", "state"])]
    public readonly Action<string, bool> ChangeSetting = null!;

    [EzIPC]
    [LuaFunction(
        description: "Sets an ICE amount option. Keys: CosmoCreditsCap, LunarCreditsCap.",
        parameterDescriptions: ["config", "amount"])]
    public readonly Action<string, int> ChangeSettingAmount = null!;

    [EzIPC]
    [LuaFunction(description: "Returns ICE's current scheduler state as text (Idle when stopped).")]
    public readonly Func<string> CurrentState = null!;

    [EzIPC]
    [LuaFunction(description: "Returns the id of the mission ICE is currently on (0 when none).")]
    public readonly Func<uint> CurrentMission = null!;
}
