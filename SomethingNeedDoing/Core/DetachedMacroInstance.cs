using SomethingNeedDoing.Core.Interfaces;
using System.Threading;
using System.Threading.Tasks;

namespace SomethingNeedDoing.Core;

/// <summary>
/// 不經排程器、直接交給引擎跑的巨集用的執行個體：沒有暫停鈕碰得到它，所以永遠不會暫停。
/// </summary>
public sealed class DetachedMacroInstance(IMacro macro) : IMacroInstance
{
    public IMacro Macro { get; } = macro;
    public CancellationTokenSource CancellationSource { get; } = new();
    public ManualResetEventSlim PauseEvent { get; } = new(true);
    public Task? ExecutionTask { get; set; }
    public bool PauseAtLoop { get; set; }
    public bool StopAtLoop { get; set; }

    public void Dispose()
    {
        CancellationSource.Dispose();
        PauseEvent.Dispose();
    }
}
