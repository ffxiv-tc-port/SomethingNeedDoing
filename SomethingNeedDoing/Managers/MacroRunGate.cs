using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace SomethingNeedDoing.Managers;

/// <summary>
/// 同一支巨集（同一個 Id）同一時間只允許一輪存在：上一輪被停下或跑完之後、到它真正收完之前，新的一輪排隊等它。
/// </summary>
/// <remarks>
/// 契約：<see cref="EnterAsync"/> 回傳 <see cref="Outcome.Entered"/> 的那一輪，呼叫端一定要在收尾最後呼叫 <see cref="Exit"/>；
/// <see cref="Exit"/> 只移除自己那一輪，不會動到之後排進來的。
/// 巨集物件、清理函式、Lua 環境、引擎表都以 Id 為鍵且兩輪共用，所以要擋的是「重疊」本身，不是逐一改成分輪記帳。
/// 本類別不碰 Dalamud 服務，可以單獨編進探針。
/// </remarks>
public sealed class MacroRunGate
{
    public enum Outcome { Entered, AlreadyRunning, AlreadyQueued, TimedOut, Cancelled }

    public readonly record struct EnterResult(Outcome Outcome, Run? Run, bool Waited);

    public sealed class Run
    {
        private volatile bool _ending;

        internal Run(string id) => Id = id;

        public string Id { get; }

        /// <summary>這一輪已被停下、跑完或出錯，只差收尾；排隊的新一輪會等它，而不是當成「已經在跑」。</summary>
        public bool Ending => _ending;

        internal TaskCompletionSource Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal void MarkEnding() => _ending = true;
    }

    private readonly ConcurrentDictionary<string, Run> _runs = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _pending = new();

    /// <summary>
    /// 沒有上一輪就同步進場（不經過任何 await）；上一輪還在收尾就排隊等，最多 <paramref name="maxWait"/>。
    /// 同一個 Id 只排一個；等超過 <paramref name="noticeAfter"/> 還沒收完會呼叫一次 <paramref name="onNotice"/>（可能在執行緒池上）。
    /// </summary>
    public async Task<EnterResult> EnterAsync(string id, TimeSpan maxWait, TimeSpan noticeAfter, Action? onNotice = null)
    {
        var run = new Run(id);
        if (_runs.TryAdd(id, run))
            return new(Outcome.Entered, run, false);
        if (_runs.TryGetValue(id, out var current) && !current.Ending)
            return new(Outcome.AlreadyRunning, null, false);

        // 沒有計時器的取消源不持有原生資源，刻意不釋放：省掉與 CancelPending 搶著 Cancel／Dispose 的競態。
        var cts = new CancellationTokenSource();
        if (!_pending.TryAdd(id, cts))
            return new(Outcome.AlreadyQueued, null, false);

        try
        {
            var clock = Stopwatch.StartNew();
            var noticed = false;
            while (true)
            {
                if (cts.IsCancellationRequested)
                    return new(Outcome.Cancelled, null, true);
                if (_runs.TryAdd(id, run))
                    return new(Outcome.Entered, run, true);
                if (!_runs.TryGetValue(id, out var previous))
                    continue;
                if (!previous.Ending)
                    return new(Outcome.AlreadyRunning, null, true);

                var remaining = maxWait - clock.Elapsed;
                if (remaining <= TimeSpan.Zero)
                    return new(Outcome.TimedOut, null, true);

                if (!noticed && clock.Elapsed >= noticeAfter)
                {
                    noticed = true;
                    onNotice?.Invoke();
                }

                var slice = noticed ? remaining : Min(remaining, noticeAfter - clock.Elapsed);
                if (slice < TimeSpan.FromMilliseconds(1))
                    slice = TimeSpan.FromMilliseconds(1);
                await Task.WhenAny(previous.Finished.Task, Task.Delay(slice, cts.Token)).ConfigureAwait(false);
            }
        }
        finally
        {
            _pending.TryRemove(new KeyValuePair<string, CancellationTokenSource>(id, cts));
        }
    }

    /// <summary>標成「正在收尾」。沒有這個 Id 的一輪時什麼都不做。</summary>
    public void MarkEnding(string id)
    {
        if (_runs.TryGetValue(id, out var run))
            run.MarkEnding();
    }

    /// <summary>這一輪收完：先讓出位置、再叫醒排隊的。</summary>
    public void Exit(Run run)
    {
        run.MarkEnding();
        _runs.TryRemove(new KeyValuePair<string, Run>(run.Id, run));
        run.Finished.TrySetResult();
    }

    /// <summary>取消這個 Id 排隊中的啟動；有排隊的才回 <see langword="true"/>。</summary>
    public bool CancelPending(string id)
    {
        if (!_pending.TryGetValue(id, out var cts))
            return false;
        cts.Cancel();
        return true;
    }

    public void CancelAllPending()
    {
        foreach (var cts in _pending.Values)
            cts.Cancel();
    }

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;
}
