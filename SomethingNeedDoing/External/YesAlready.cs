using ECommons.EzIpcManager;
using SomethingNeedDoing.Core.Interfaces;
using System.Threading;
using System.Threading.Tasks;

namespace SomethingNeedDoing.External;

/// <summary>
/// YesAlready 的 IPC 門面：<b>對 Lua 的部分逐字不變</b>，而巨集自動化用的
/// <see cref="EnableAsync"/>／<see cref="DisableAsync"/> 改走<b>具名壓制租約</b>。
/// </summary>
/// <remarks>
/// 🔴🔴 <b>為什麼要分成兩套。</b>
/// <list type="bullet">
/// <item><b>Lua 的 <c>SetPluginEnabled</c> 不能動</b>：使用者寫
/// <c>IPC.YesAlready.SetPluginEnabled(true)</c> 期待的就是「不管原本開沒開都給我打開」。
/// 把它改成「放開租約」會讓既有巨集靜默失效，所以那條路原封不動。</item>
/// <item><b>巨集中繼資料 <c>PluginsToDisable</c> 走的自動化路徑要改</b>：舊做法是裸寫
/// 同一格全域布林 <c>C.Enabled</c>，而 Questionable／AutoDuty 也寫同一格 ⇒ 巨集跑完
/// 一律寫回 <see langword="true"/>，會把 Questionable 正在跑的任務壓制整個掀掉；
/// 反過來 Questionable 關掉它，巨集這邊的還原也蓋不回去。<b>全程零訊息。</b></item>
/// </list>
/// <para>
/// 🔑 <b>refcount</b>：兩支巨集同時列了 YesAlready 時，先跑完的那支
/// <b>不會</b>把還在跑的那支的壓制放掉（<see cref="_suppressionDepth"/>）。
/// 這正是舊的布林做不到的事。
/// </para>
/// <para>
/// 🔴 <b>fail-safe</b>：取租約拿到 <see cref="Guid.Empty"/>（提供端沒裝、或版本太舊沒有
/// 租約端點）就<b>退回改動前的裸寫</b>，絕不卡住巨集。
/// </para>
/// <para>
/// ⚠️ <b>執行緒</b>：巨集排程器不在主執行緒上呼叫這裡，心跳又在 <see cref="Timer"/> 的
/// 執行緒集區上跑 ⇒ 全部狀態都由 <see cref="_gate"/> 保護。提供端本身也是全程上鎖的。
/// </para>
/// </remarks>
public class YesAlready : IPC, IDisableable, IDisposable
{
    /// <summary>租約登記的名字，會出現在 YesAlready 的 log 與設定視窗。</summary>
    private const string LeaseOwner = "SomethingNeedDoing";

    /// <summary>每次取得／續約要求的租期；提供端硬性上限就是 60 分鐘，直接要滿。</summary>
    /// <remarks>🔑 續約只當保險：續約整條路壞掉時仍有一小時緩衝，而不是 10 分鐘就醒過來。</remarks>
    private const int LeaseMilliseconds = 3_600_000;

    /// <summary>心跳間隔（5 分鐘），遠小於 <see cref="LeaseMilliseconds"/>。</summary>
    private const int RenewIntervalMilliseconds = 300_000;

    private readonly object _gate = new();

    /// <summary>目前有幾支巨集要求壓制。<b>0 才真的放開。</b></summary>
    private int _suppressionDepth;

    /// <summary>目前持有的租約；<see cref="Guid.Empty"/>＝沒有（含「正在走 fail-safe 舊路徑」）。</summary>
    private Guid _lease;

    /// <summary>續約心跳；只在真的握著租約的期間存在。</summary>
    private Timer? _heartbeat;

    public override string Name => "YesAlready";
    public override string Repo => Repos.Punish;
    public string InternalName => Name;

    [EzIPC]
    [LuaFunction(description: "Gets whether the plugin is active")]
    public Func<bool> IsPluginEnabled = null!;

    [EzIPC]
    [LuaFunction(description: "Sets whether the plugin is active", parameterDescriptions: ["state"])]
    public Action<bool> SetPluginEnabled = null!;

    [EzIPC]
    [LuaFunction(description: "Gets whether the bother is active", parameterDescriptions: ["name"])]
    public Func<string, bool> IsBotherEnabled = null!;

    [EzIPC]
    [LuaFunction(description: "Sets whether the bother is active", parameterDescriptions: ["name", "state"])]
    public Action<string, bool> SetBotherEnabled = null!;

    [EzIPC]
    [LuaFunction(description: "Pauses the plugin for the given amount of milliseconds", parameterDescriptions: ["milliseconds"])]
    public Action<int> PausePlugin = null!;

    [EzIPC]
    [LuaFunction(description: "Pauses the bother for the given amount of milliseconds", parameterDescriptions: ["name", "milliseconds"])]
    public Func<string, int, bool> PauseBother = null!;

    // ── 壓制租約端點 ──────────────────────────────────────────────────────────
    // 🔴 刻意**不加** [LuaFunction]：租約是外掛內部自動化用的，不是給使用者的巨集 API。
    //    IPCModule 只反射 public + [LuaFunction] 的成員，這裡宣告成 private 是雙重保險。
    //    (EzIPC.Init 用 Public | NonPublic，所以 private 欄位照樣綁得上。)
    // 📌 這個 class 的 EzIPC.Init 走 SafeWrapper.None（IPC 基底類別），所以提供端缺席時
    //    會**擲** IpcNotReadyError 而不是靜默回 default ⇒ 底下每一處都自己 try/catch。

    [EzIPC] private Func<string, int, Guid> AcquireSuppressionFor = null!;
    [EzIPC] private Func<Guid, int, bool> RenewSuppressionFor = null!;
    [EzIPC] private Func<Guid, bool> ReleaseSuppression = null!;

    /// <summary>
    /// 巨集結束：放掉這一支巨集的壓制。<b>還有別的巨集壓著就不會真的放開。</b>
    /// </summary>
    public Task<bool> EnableAsync()
    {
        lock (_gate)
        {
            if (_suppressionDepth > 0)
                _suppressionDepth--;

            // 🔑 還有別的巨集在跑 ⇒ 不要放開。舊的布林做法就是在這裡把別人的壓制掀掉的。
            if (_suppressionDepth > 0)
                return Task.FromResult(true);

            if (ReleaseLease())
                return Task.FromResult(true);

            // ── fail-safe：當初就是走舊路徑壓下去的，還原也走舊路徑（與改動前逐字相同）──
            try
            {
                SetPluginEnabled(true);
                return Task.FromResult(true);
            }
            catch
            {
                FrameworkLogger.Error("Failed to enable plugin");
                return Task.FromResult(false);
            }
        }
    }

    /// <summary>巨集開始：請 YesAlready 在這支巨集跑完之前讓開。</summary>
    public Task<bool> DisableAsync()
    {
        lock (_gate)
        {
            _suppressionDepth++;

            // 已經壓著了（巢狀或並行的巨集）：只加計數，不重複取租約。
            if (_suppressionDepth > 1)
                return Task.FromResult(true);

            if (TryAcquireLease())
                return Task.FromResult(true);

            // ── fail-safe：提供端沒裝、或版本太舊沒有租約端點 ⇒ 退回改動前的裸寫 ──
            try
            {
                SetPluginEnabled(false);
                return Task.FromResult(true);
            }
            catch
            {
                // 壓制根本沒成立，就不要留下計數（否則之後永遠回不到 0）。
                _suppressionDepth--;
                FrameworkLogger.Error("Failed to disable plugin");
                return Task.FromResult(false);
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _suppressionDepth = 0;
            ReleaseLease();
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>取一把租約。回 <see langword="false"/>＝提供端給不了，呼叫端請走舊路徑。</summary>
    /// <remarks>
    /// 📌 刻意<b>每次都重試</b>而不記住「上次失敗過」：這支最多每支巨集開頭跑一次（不是每幀），
    /// 而使用者可能在遊戲跑到一半才裝好／更新 YesAlready。
    /// </remarks>
    private bool TryAcquireLease()
    {
        Guid lease;
        try
        {
            lease = AcquireSuppressionFor(LeaseOwner, LeaseMilliseconds);
        }
        catch
        {
            lease = Guid.Empty;
        }

        if (lease == Guid.Empty)
        {
            FrameworkLogger.Info("YesAlready 沒有壓制租約端點（沒安裝或版本太舊），退回舊的開關寫入");
            return false;
        }

        _lease = lease;
        _heartbeat ??= new Timer(OnHeartbeat, null, RenewIntervalMilliseconds, RenewIntervalMilliseconds);
        FrameworkLogger.Info($"已向 YesAlready 取得壓制租約 {lease}（{LeaseMilliseconds} 毫秒）");
        return true;
    }

    /// <summary>交回租約。回 <see langword="false"/>＝本來就沒有（呼叫端請走舊路徑還原）。</summary>
    private bool ReleaseLease()
    {
        if (_lease == Guid.Empty)
            return false;

        var lease = _lease;

        // 🔴 先清欄位再送出：送出途中擲例外的話手上這把也已經是廢的，
        // 留著只會讓心跳繼續對一把不存在的租約續約。
        _lease = Guid.Empty;
        StopHeartbeat();

        try
        {
            ReleaseSuppression(lease);
        }
        catch
        {
            // 交不回去也不要緊：提供端會讓它自行逾時。
        }

        FrameworkLogger.Info($"已交回 YesAlready 壓制租約 {lease}");
        return true;
    }

    /// <summary>
    /// 心跳：巨集可以跑好幾個小時，而租約上限只有 60 分鐘。
    /// </summary>
    /// <remarks>
    /// 🔴 <b>續約回 <see langword="false"/> 代表那把已經不在了</b>（逾時、YesAlready 被重載、
    /// 或使用者按了「強制解除鎖定」）—— 必須<b>重新取得</b>，不能繼續假設自己還壓著。
    /// </remarks>
    private void OnHeartbeat(object? state)
    {
        lock (_gate)
        {
            if (_lease == Guid.Empty)
                return;

            bool renewed;
            try
            {
                renewed = RenewSuppressionFor(_lease, LeaseMilliseconds);
            }
            catch
            {
                renewed = false;
            }

            if (renewed)
                return;

            FrameworkLogger.Info($"YesAlready 壓制租約 {_lease} 已經不在了，重新取得一把");
            _lease = Guid.Empty;

            try
            {
                var lease = AcquireSuppressionFor(LeaseOwner, LeaseMilliseconds);
                if (lease != Guid.Empty)
                {
                    _lease = lease;
                    return;
                }
            }
            catch
            {
                // 落到下面停掉心跳。
            }

            // 重新取得也失敗（YesAlready 被卸載了？）：停掉心跳，別讓它每 5 分鐘空轉一次。
            // 計數仍然留著，下一支巨集的 DisableAsync 會再試一次。
            FrameworkLogger.Info("重新取得 YesAlready 壓制租約失敗，停止續約心跳");
            StopHeartbeat();
        }
    }

    /// <summary>停掉心跳。<b>呼叫端必須持有 <see cref="_gate"/>。</b></summary>
    /// <remarks>
    /// 📌 用無參數的 <see cref="Timer.Dispose()"/>：它<b>不等</b>回呼跑完，
    /// 所以從 <see cref="OnHeartbeat"/> 自己裡面呼叫（此時鎖在手上）不會死鎖。
    /// </remarks>
    private void StopHeartbeat()
    {
        _heartbeat?.Dispose();
        _heartbeat = null;
    }
}
