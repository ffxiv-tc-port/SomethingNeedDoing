using ECommons.EzIpcManager;
using SomethingNeedDoing.Core.Interfaces;

namespace SomethingNeedDoing.External;

/// <summary>
/// TC Toolbox（台服自製工具箱）的自動園圃作業 IPC。
///
/// 這是「一次一格」的細項操作層：腳本自己決定要處理哪一格、做什麼動作，
/// TC Toolbox 負責已驗證過的互動流程（選單文字比對、播種填格、施肥右鍵選單）。
/// TC Toolbox 端不提供「一鍵跑完整座庭院」的 IPC，整座庭院的批次只能由它自己的 UI 觸發。
///
/// 前置條件：使用者必須先在 TC Toolbox 設定視窗啟用「自動園圃作業」模組，
/// 否則動作類端點會回傳失敗原因（模組停用時它的佇列不會推進）。
///
/// 用法：動作類回傳空字串代表已排入佇列，非空代表失敗原因（繁體中文）。
/// 排入後輪詢 <c>IsBusy</c> 等它跑完，再讀 <c>GetLastSummary</c>。
/// 呼叫前務必先用 <c>IPC.IsInstalled("TCToolbox")</c> 確認外掛存在，沒裝就走腳本自己的原生實作。
/// </summary>
public class TCToolbox : IPC
{
    public override string Name => "TCToolbox";
    public override string Repo => Repos.TcPort;

    #region 動作類（回傳空字串＝已排入佇列，非空＝失敗原因）

    [EzIPC("Gardening.%m")]
    [LuaFunction(
        description: "Queues a harvest on the given garden patch (pass 0 to use the current target). Returns an empty string if queued, otherwise a zh-TW failure reason.",
        parameterDescriptions: ["gameObjectId"])]
    public readonly Func<ulong, string> Harvest = null!;

    [EzIPC("Gardening.%m")]
    [LuaFunction(
        description: "Queues a tend (護理) on the given garden patch (pass 0 to use the current target). Returns an empty string if queued, otherwise a zh-TW failure reason.",
        parameterDescriptions: ["gameObjectId"])]
    public readonly Func<ulong, string> Tend = null!;

    [EzIPC("Gardening.%m")]
    [LuaFunction(
        description: "Queues a fertilize on the given garden patch using the given fertilizer item id (pass 0 for the patch to use the current target). Returns an empty string if queued, otherwise a zh-TW failure reason.",
        parameterDescriptions: ["gameObjectId", "fertilizerItemId"])]
    public readonly Func<ulong, uint, string> Fertilize = null!;

    [EzIPC("Gardening.%m")]
    [LuaFunction(
        description: "Queues a planting on the given garden patch with the given seed and soil item ids (pass 0 for the patch to use the current target). Returns an empty string if queued, otherwise a zh-TW failure reason.",
        parameterDescriptions: ["gameObjectId", "seedItemId", "soilItemId"])]
    public readonly Func<ulong, uint, uint, string> Plant = null!;

    [EzIPC("Gardening.%m")]
    [LuaFunction(
        description: "Queues a state probe on the given garden patch: interacts, records which menu options are available, then cancels without changing anything. Required before GetPatchState/GetPatchActions return anything, because crop state cannot be read from memory. Returns an empty string if queued, otherwise a zh-TW failure reason.",
        parameterDescriptions: ["gameObjectId"])]
    public readonly Func<ulong, string> Scan = null!;

    #endregion

    #region 狀態類

    [EzIPC("Gardening.%m")]
    [LuaFunction(description: "Whether the gardening module is enabled and the current location allows garden operations.")]
    public readonly Func<bool> IsAvailable = null!;

    [EzIPC("Gardening.%m")]
    [LuaFunction(description: "Empty string when gardening operations are available, otherwise the zh-TW reason why not (module disabled, not in your own garden, no patches nearby...).")]
    public readonly Func<string> GetUnavailableReason = null!;

    [EzIPC("Gardening.%m")]
    [LuaFunction(description: "Whether a gardening action or batch is currently running.")]
    public readonly Func<bool> IsBusy = null!;

    [EzIPC("Gardening.%m")]
    [LuaFunction(description: "Name of the gardening step currently executing (empty when idle).")]
    public readonly Func<string> GetCurrentStep = null!;

    [EzIPC("Gardening.%m")]
    [LuaFunction(description: "Number of patches processed in the current/last run.")]
    public readonly Func<int> GetDoneCount = null!;

    [EzIPC("Gardening.%m")]
    [LuaFunction(description: "Number of patches skipped in the current/last run.")]
    public readonly Func<int> GetSkippedCount = null!;

    [EzIPC("Gardening.%m")]
    [LuaFunction(description: "zh-TW summary of the last completed gardening run (empty if none yet).")]
    public readonly Func<string> GetLastSummary = null!;

    [EzIPC("Gardening.%m")]
    [LuaFunction(description: "Game object ids of nearby garden patches, ordered by distance. Empty when not standing in a garden you own.")]
    public readonly Func<List<ulong>> GetNearbyPatches = null!;

    [EzIPC("Gardening.%m")]
    [LuaFunction(
        description: "Distance in yalms between the player and the given patch, or -1 if it cannot be found.",
        parameterDescriptions: ["gameObjectId"])]
    public readonly Func<ulong, float> GetPatchDistance = null!;

    [EzIPC("Gardening.%m")]
    [LuaFunction(
        description: "Menu options seen on the given patch during the last Scan (excluding cancel). Empty if it has not been scanned.",
        parameterDescriptions: ["gameObjectId"])]
    public readonly Func<ulong, List<string>> GetPatchActions = null!;

    [EzIPC("Gardening.%m")]
    [LuaFunction(
        description: "State of the given patch derived from the last Scan: 'unscanned', 'mature', 'empty', 'growing' or 'unknown'.",
        parameterDescriptions: ["gameObjectId"])]
    public readonly Func<ulong, string> GetPatchState = null!;

    #endregion

    #region 控制類

    [EzIPC("Gardening.%m")]
    [LuaFunction(description: "Stops whatever gardening action or batch is currently running.")]
    public readonly Action Stop = null!;

    #endregion
}
