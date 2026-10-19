using System.Threading;
using System.Threading.Tasks;

namespace SomethingNeedDoing.Core;

/// <summary>
/// 引擎在每一步之前等暫停解除用的閘門。
/// </summary>
/// <remarks>
/// 契約：沒暫停時同步完成、不換執行緒（與直接 <c>Wait</c> 行為相同）；暫停中才轉到執行緒池去等，
/// 因為引擎這時可能正跑在框架執行緒上，在那裡阻塞＝遊戲凍住、繼續鈕與 <c>/snd resume</c> 都處理不到。
/// <para>取消一律擲 <see cref="OperationCanceledException"/>（含暫停中按停止），與原本 <c>Wait(token)</c> 相同。</para>
/// </remarks>
public static class MacroPause
{
    public static Task WaitWhilePausedAsync(ManualResetEventSlim pauseEvent, CancellationToken token)
    {
        if (token.IsCancellationRequested)
            return Task.FromCanceled(token);
        return pauseEvent.IsSet ? Task.CompletedTask : Task.Run(() => pauseEvent.Wait(token), token);
    }
}
