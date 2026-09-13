using System.Threading;

namespace SomethingNeedDoing.Utils;

/// <summary>
/// 卸載窗內「轉派到 framework 執行緒」的短路判斷：卸載時 <c>RunOnFrameworkThread</c>
/// 與無延遲的 <c>RunOnTick</c> <b>就地在呼叫端執行緒執行</b>，轉派因此失去保護作用
/// ——背景執行緒會與 framework 執行緒並行改同一個裸佇列／字典／ImGui 緩衝。
/// </summary>
internal static class FrameworkUnloadGuard
{
    private static int _reported;

    /// <summary>
    /// 卸載中且<b>不在</b> framework 執行緒上 ⇒ <see langword="true"/>，呼叫端直接放棄這次工作。
    /// 已經在 framework 執行緒上時恆為 <see langword="false"/>：那時就地執行是對的。
    /// </summary>
    internal static bool ShouldSkip(string scope)
    {
        var framework = Svc.Framework;
        if (!framework.IsFrameworkUnloading || framework.IsInFrameworkUpdateThread)
            return false;

        if (Interlocked.Exchange(ref _reported, 1) == 0)
            FrameworkLogger.Info($"外掛正在卸載，略過背景執行緒排進來的工作（{scope}）。");

        return true;
    }

    /// <summary>
    /// 給<b>被 <c>await</c> 的</b>轉派用：卸載窗內以取消收場，而不是靜靜放棄。
    /// </summary>
    /// <remarks>
    /// 契約：呼叫端會看到 <see cref="OperationCanceledException"/>，走它原本處理「使用者按停止」的路徑。
    /// <see cref="ShouldSkip"/> 那種「回 true 就 return」只能用在沒人 await 的地方——那等於餵一個假的預設值。
    /// 判斷條件與 <see cref="ShouldSkip"/> 相同：已在 framework 執行緒上時不擲。
    /// </remarks>
    internal static void ThrowIfUnloading(string scope, CancellationToken cancellationToken = default)
    {
        var framework = Svc.Framework;
        if (!framework.IsFrameworkUnloading || framework.IsInFrameworkUpdateThread)
            return;

        var message = $"插件卸載，巨集 {scope} 中止。";
        FrameworkLogger.Info(message);
        throw new OperationCanceledException(message, cancellationToken);
    }
}
