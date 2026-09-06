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
/// <para>
/// 🔴🔴 <b>鎖內三不</b>：持有 <see cref="_gate"/> 的期間<b>不呼叫 IPC、不寫 log、不做檔案 I/O</b>。
/// 在鎖內打跨外掛 IPC ＝ <b>跨外掛鎖序</b>（我的鎖 → 對方的鎖）：IPC 是在<b>呼叫端的執行緒</b>上
/// 執行對方的程式碼，等於把 <see cref="_gate"/> 交給 YesAlready 持有；對方日後只要長出一條回頭
/// 呼叫這裡的路徑就是死鎖，而那條路徑是在<b>別人的 repo</b> 裡長出來的，這邊看不到。
/// </para>
/// <para>
/// 🔑 <b>作法</b>：<c>lock{ 讀寫本地狀態、決定這次要做什麼、拍下版本號 }</c> → <b>鎖外</b>做 IPC →
/// <c>lock{ 版本號沒變就照原本的結論寫回，變了就依當下的狀態決定丟棄或補做 }</c>。
/// 丟棄時若那一步<b>剛取得了一把租約</b>，會在鎖外主動交回，所以「重複 Acquire」不會漏租約。
/// 診斷訊息一律等到出了鎖才寫。
/// </para>
/// </remarks>
public class YesAlready : IPC, IDisableable, IDisposable
{
    /// <summary>租約登記的名字，會出現在 YesAlready 的 log 與設定視窗。</summary>
    private const string LeaseOwner = "SomethingNeedDoing";

    /// <summary>每次取得／續約要求的租期（5 分鐘）＝提供端的硬性上限。</summary>
    /// <remarks>
    /// 🔑 全艦隊的壓制租約時間政策統一成「租 5 分鐘、每 30 秒續約」（AutoRetainer 那套
    /// 本來就是這個值）。取捨是：租期短 ⇒ 我們當掉或被卸載時，使用者最多等 5 分鐘
    /// YesAlready 就自己恢復；心跳間隔留 10 倍餘裕 ⇒ 要連續漏掉 9 次心跳才會真的過期。
    /// <para>
    /// 🔴 這個值<b>不可以</b>大於提供端的上限：提供端是<b>夾值不是拒絕</b>，要多了只會
    /// 被靜默砍短，心跳反而會來不及。
    /// </para>
    /// </remarks>
    private const int LeaseMilliseconds = 300_000;

    /// <summary>心跳間隔（30 秒），是 <see cref="LeaseMilliseconds"/> 的十分之一。</summary>
    /// <remarks>
    /// 📌 <see cref="Timer"/> 的 dueTime 與 period <b>都</b>用這個常數
    /// （<see cref="CommitAcquiredLocked"/> 裡唯一一處 <c>new Timer(...)</c>），沒有另外寫死的值。
    /// </remarks>
    private const int RenewIntervalMilliseconds = 30_000;

    /// <summary>
    /// 保護底下那組租約狀態。
    /// </summary>
    /// <remarks>
    /// 🔴 <b>持有它的期間不呼叫 IPC、不寫 log、不做檔案 I/O</b>。
    /// 名字帶 <c>Locked</c> 的私有方法是「呼叫端必須先持有它」的意思，
    /// 名字帶 <c>OutsideGate</c> 的是「絕不可在持有它時呼叫」的意思。
    /// </remarks>
    private readonly object _gate = new();

    /// <summary>目前有幾支巨集要求壓制。<b>0 才真的放開。</b></summary>
    private int _suppressionDepth;

    /// <summary>目前持有的租約；<see cref="Guid.Empty"/>＝沒有（含「正在走 fail-safe 舊路徑」）。</summary>
    private Guid _lease;

    /// <summary>續約心跳；只在真的握著租約的期間存在。</summary>
    private Timer? _heartbeat;

    /// <summary>舊路徑寫入的收斂上限，防止在病態的競爭下無限重寫。</summary>
    private const int LegacyWriteRounds = 4;

    /// <summary>
    /// <see cref="_suppressionDepth"/> 與 <see cref="_lease"/> 的版本號。
    /// <b>那兩個欄位的每一次變動都必須把它往前推。</b>
    /// </summary>
    /// <remarks>
    /// 🔑 這是「鎖外做 IPC」的正確性依據：規劃階段在鎖內記下當下的序號，提交階段拿它比對 ——
    /// 序號沒變就代表這段期間沒有人動過狀態，鎖外拿回來的結果一定是新鮮的，照規劃時的結論寫回；
    /// 序號變了就必須改用<b>當下的狀態</b>重新判斷（丟棄或補做）。
    /// <para>
    /// ⚠️ 漏掉任何一處 <c>++</c> 都會讓過期的結果被當成新鮮的寫回去，所以那兩個欄位的寫入
    /// <b>全部</b>收斂到 <see cref="MutateLocked"/> 這一個地方，沒有第二個寫入點。
    /// </para>
    /// </remarks>
    private long _epoch;

    /// <summary>現在有幾個「取得租約」的 IPC 正在鎖外進行。</summary>
    /// <remarks>
    /// 🔴 <see cref="EnableAsync"/> 靠它分辨「真的沒有租約」與「租約還在路上」：後者不可以走
    /// fail-safe 的裸寫，否則會在改動前根本不會寫的情況下把 YesAlready 的全域開關扳成
    /// <see langword="true"/> —— 那正是這整份檔要避免的舊行為。
    /// </remarks>
    private int _acquireInFlight;

    /// <summary>fail-safe 舊路徑目前「想要」的 YesAlready 全域開關值。</summary>
    /// <remarks>
    /// 📌 只有改動前本來就會裸寫的那兩條路徑會動它：壓制取不到租約時寫 <see langword="false"/>、
    /// 巨集結束而手上沒有租約時寫 <see langword="true"/>。走租約軌時完全不碰它。
    /// </remarks>
    private bool _legacyDesired = true;

    /// <summary>舊路徑有一次寫入還沒送出去。</summary>
    private bool _legacyPending;

    /// <summary>現在有人正在鎖外送出舊路徑的寫入。</summary>
    /// <remarks>
    /// 🔑 <b>這是旗標不是鎖</b>：撞上的人只更新 <see cref="_legacyDesired"/> 就走，現任寫入者會在
    /// 自己的迴圈裡收斂到最新的值，<b>沒有任何執行緒會阻塞等待 IPC</b>。
    /// </remarks>
    private bool _legacyWriting;

    public override string Name => "YesAlready";
    public override string Repo => Repos.TcPort;
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
    /// <remarks>🔴 <see cref="_gate"/> 之內只改本地狀態；交回租約與 fail-safe 的裸寫都在鎖外。</remarks>
    public Task<bool> EnableAsync()
    {
        var lease = Guid.Empty;
        bool drive;

        lock (_gate)
        {
            if (_suppressionDepth > 0)
                MutateLocked(_suppressionDepth - 1, _lease);

            // 🔑 還有別的巨集在跑 ⇒ 不要放開。舊的布林做法就是在這裡把別人的壓制掀掉的。
            if (_suppressionDepth > 0)
                return Task.FromResult(true);

            if (_lease != Guid.Empty)
            {
                // 🔴 先清欄位再送出：送出途中擲例外的話手上這把也已經是廢的，
                // 留著只會讓心跳繼續對一把不存在的租約續約。
                lease = _lease;
                MutateLocked(_suppressionDepth, Guid.Empty);
                StopHeartbeat();
                drive = false;
            }
            else
            {
                // ── fail-safe：當初就是走舊路徑壓下去的，還原也走舊路徑（與改動前逐字相同）──
                _legacyDesired = true;
                _legacyPending = true;
                drive = TakeLegacyWriteLocked();
            }
        }

        // ── 以下全部在鎖外 ──
        if (lease != Guid.Empty)
        {
            ReleaseOutsideGate(lease);
            FrameworkLogger.Info($"已交回 YesAlready 壓制租約 {lease}");
            return Task.FromResult(true);
        }

        // drive 為 false ＝ 有 Acquire 在途（交給它的提交階段送出），或已經有別人在鎖外寫。
        if (!drive || DriveLegacy())
            return Task.FromResult(true);

        FrameworkLogger.Error("Failed to enable plugin");
        return Task.FromResult(false);
    }

    /// <summary>巨集開始：請 YesAlready 在這支巨集跑完之前讓開。</summary>
    /// <remarks>
    /// 🔴 取租約的 IPC 在 <see cref="_gate"/> 之外呼叫；結果先與 <see cref="_epoch"/> 比對過才寫回，
    /// 這段期間被別人搶先裝好一把、或計數已經歸零時，剛拿到的那把會在鎖外主動交回。
    /// </remarks>
    public Task<bool> DisableAsync()
    {
        long seq;
        lock (_gate)
        {
            MutateLocked(_suppressionDepth + 1, _lease);

            // 已經壓著了（巢狀或並行的巨集）：只加計數，不重複取租約。
            if (_suppressionDepth > 1)
                return Task.FromResult(true);

            _acquireInFlight++;
            seq = _epoch;
        }

        // 🔴 鎖外：這一支會在呼叫端的執行緒上執行 YesAlready 的程式碼。
        Guid lease;
        try
        {
            lease = AcquireSuppressionFor(LeaseOwner, LeaseMilliseconds);
        }
        catch
        {
            lease = Guid.Empty;
        }

        Guid orphan;
        bool installed;
        bool legacySuppress;
        bool drive;

        lock (_gate)
        {
            _acquireInFlight--;
            installed = CommitAcquiredLocked(lease, seq, out orphan);

            // ── fail-safe：提供端沒裝、或版本太舊沒有租約端點 ⇒ 退回改動前的裸寫 ──
            legacySuppress = !installed && lease == Guid.Empty && _suppressionDepth > 0 && _lease == Guid.Empty;
            if (legacySuppress)
            {
                _legacyDesired = false;
                _legacyPending = true;
            }

            drive = TakeLegacyWriteLocked();
        }

        // ── 以下全部在鎖外 ──
        if (lease == Guid.Empty)
            FrameworkLogger.Info("YesAlready 沒有壓制租約端點（沒安裝或版本太舊），退回舊的開關寫入");

        if (orphan != Guid.Empty)
            ReleaseOrphanOutsideGate(orphan);

        if (installed)
        {
            FrameworkLogger.Info($"已向 YesAlready 取得壓制租約 {lease}（{LeaseMilliseconds} 毫秒）");
            return Task.FromResult(true);
        }

        if (drive && !DriveLegacy() && legacySuppress)
        {
            lock (_gate)
            {
                // 壓制根本沒成立，就不要留下計數（否則之後永遠回不到 0）。
                if (_suppressionDepth > 0)
                    MutateLocked(_suppressionDepth - 1, _lease);
            }

            FrameworkLogger.Error("Failed to disable plugin");
            return Task.FromResult(false);
        }

        return Task.FromResult(true);
    }

    /// <remarks>🔴 交回租約的 IPC 與那一行診斷都在 <see cref="_gate"/> 之外。</remarks>
    public void Dispose()
    {
        Guid lease;
        lock (_gate)
        {
            lease = _lease;
            MutateLocked(0, Guid.Empty);
            if (lease != Guid.Empty)
                StopHeartbeat();
        }

        // ── 以下全部在鎖外 ──
        if (lease != Guid.Empty)
        {
            ReleaseOutsideGate(lease);
            FrameworkLogger.Info($"已交回 YesAlready 壓制租約 {lease}");
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// 心跳：巨集可以跑好幾個小時，而租約上限只有 5 分鐘。
    /// </summary>
    /// <remarks>
    /// 🔴 <b>續約回 <see langword="false"/> 代表那把已經不在了</b>（逾時、YesAlready 被重載、
    /// 或使用者按了「強制解除鎖定」）—— 必須<b>重新取得</b>，不能繼續假設自己還壓著。
    /// 🔴 續約與重新取得<b>都在 <see cref="_gate"/> 之外呼叫</b>；回到鎖裡時先比對版本號與手上的憑證，
    /// 確認這段期間沒有人把租約放掉或換成別把，結果才算數。
    /// </remarks>
    private void OnHeartbeat(object? state)
    {
        Guid lease;
        long seq;

        lock (_gate)
        {
            if (_lease == Guid.Empty)
                return;

            lease = _lease;
            seq = _epoch;
        }

        bool renewed;
        try
        {
            renewed = RenewSuppressionFor(lease, LeaseMilliseconds);
        }
        catch
        {
            renewed = false;
        }

        long reacquireSeq;
        lock (_gate)
        {
            // 版本號沒變 ⇒ 結果是新鮮的。變了就看手上這把還是不是當前那把：
            // 已經被放掉、或換成別把了的話，這個結果已經過期，直接丟棄。
            if (_epoch != seq && _lease != lease)
                return;

            if (renewed)
                return;

            MutateLocked(_suppressionDepth, Guid.Empty);
            reacquireSeq = _epoch;
        }

        FrameworkLogger.Info($"YesAlready 壓制租約 {lease} 已經不在了，重新取得一把");

        Guid acquired;
        try
        {
            acquired = AcquireSuppressionFor(LeaseOwner, LeaseMilliseconds);
        }
        catch
        {
            acquired = Guid.Empty;
        }

        Guid orphan;
        bool stop;
        lock (_gate)
        {
            CommitAcquiredLocked(acquired, reacquireSeq, out orphan);

            // 重新取得也失敗（YesAlready 被卸載了？）：停掉心跳，別讓它每 30 秒空轉一次。
            // 計數仍然留著，下一支巨集的 DisableAsync 會再試一次。
            stop = acquired == Guid.Empty && _lease == Guid.Empty;
            if (stop)
                StopHeartbeat();
        }

        // ── 以下全部在鎖外 ──
        if (orphan != Guid.Empty)
            ReleaseOrphanOutsideGate(orphan);

        if (stop)
            FrameworkLogger.Info("重新取得 YesAlready 壓制租約失敗，停止續約心跳");
    }

    // ── 鎖內：只讀寫本地狀態，一行 IPC／log 都沒有 ────────────────────────────

    /// <summary>
    /// <see cref="_suppressionDepth"/> 與 <see cref="_lease"/> 的<b>唯一</b>寫入點：改欄位並把
    /// <see cref="_epoch"/> 往前推。<b>呼叫端必須持有 <see cref="_gate"/>。</b>
    /// </summary>
    private void MutateLocked(int depth, Guid lease)
    {
        _suppressionDepth = depth;
        _lease = lease;
        _epoch++;
    }

    /// <summary>
    /// 把鎖外剛取得的租約寫回狀態。回 <see langword="true"/>＝真的裝上去了。
    /// <b>呼叫端必須持有 <see cref="_gate"/>。</b>
    /// </summary>
    /// <param name="lease">鎖外拿到的憑證；<see cref="Guid.Empty"/>＝沒拿到。</param>
    /// <param name="seq">規劃階段拍下的 <see cref="_epoch"/>。</param>
    /// <param name="orphan">用不到的憑證，呼叫端必須在<b>鎖外</b>交回；<see cref="Guid.Empty"/>＝沒有。</param>
    private bool CommitAcquiredLocked(Guid lease, long seq, out Guid orphan)
    {
        orphan = Guid.Empty;

        if (lease == Guid.Empty)
            return false;

        // 版本號沒變 ⇒ 這段期間沒有人動過狀態，規劃時的前提（計數大於 0 而且手上沒有租約）依然成立。
        // 變了就改用當下的狀態重新判斷，條件與規劃時逐字相同。
        if (_epoch == seq || (_suppressionDepth > 0 && _lease == Guid.Empty))
        {
            MutateLocked(_suppressionDepth, lease);
            _heartbeat ??= new Timer(OnHeartbeat, null, RenewIntervalMilliseconds, RenewIntervalMilliseconds);
            return true;
        }

        // 已經不需要壓制（計數歸零），或別人已經裝好一把 ⇒ 這把是多的。
        orphan = lease;
        return false;
    }

    /// <summary>
    /// 決定「舊路徑的那一次寫入現在該不該由我送出」。<b>呼叫端必須持有 <see cref="_gate"/>。</b>
    /// </summary>
    private bool TakeLegacyWriteLocked()
    {
        if (!_legacyPending)
            return false;

        // 有 Acquire 在途：等它的提交階段決定是走租約軌還是舊路徑，別提前寫。
        if (_acquireInFlight > 0)
            return false;

        // 已經改走租約軌 ⇒ 舊路徑的寫入作廢，不要再去動使用者的全域開關。
        if (_lease != Guid.Empty)
        {
            _legacyPending = false;
            return false;
        }

        // 已經有人在鎖外寫了：他會在自己的迴圈裡收斂到最新的 _legacyDesired。
        if (_legacyWriting)
        {
            _legacyPending = false;
            return false;
        }

        _legacyPending = false;
        _legacyWriting = true;
        return true;
    }

    /// <summary>停掉心跳。<b>呼叫端必須持有 <see cref="_gate"/>。</b></summary>
    /// <remarks>
    /// 📌 用無參數的 <see cref="Timer.Dispose()"/>：它<b>不等</b>回呼跑完，所以持鎖時呼叫不會死鎖。
    /// </remarks>
    private void StopHeartbeat()
    {
        _heartbeat?.Dispose();
        _heartbeat = null;
    }

    // ── 鎖外：所有的 IPC 與診斷 ──────────────────────────────────────────────

    /// <summary>交回租約。<b>絕不可在持有 <see cref="_gate"/> 時呼叫。</b></summary>
    private void ReleaseOutsideGate(Guid lease)
    {
        try
        {
            ReleaseSuppression(lease);
        }
        catch
        {
            // 交不回去也不要緊：提供端會讓它自行逾時。
        }
    }

    /// <summary>
    /// 交回一把「拿到之後才發現用不著」的租約。<b>絕不可在持有 <see cref="_gate"/> 時呼叫。</b>
    /// </summary>
    /// <remarks>
    /// 🔴 不交回的話它會壓著提供端的租約上限直到逾時（<see cref="LeaseMilliseconds"/>）。
    /// 這條路徑只有在「取租約的期間有別人動過狀態」時才走得到，寫一行 Information 好讓它看得見。
    /// </remarks>
    private void ReleaseOrphanOutsideGate(Guid lease)
    {
        ReleaseOutsideGate(lease);
        FrameworkLogger.Info($"取得 YesAlready 壓制租約 {lease} 之後才發現用不著（計數已歸零，或已經有別的一把），直接交回");
    }

    /// <summary>
    /// 把 YesAlready 的全域開關推到 <see cref="_legacyDesired"/>。
    /// 回 <see langword="false"/>＝寫入擲了例外（與改動前的 <c>catch</c> 同義）。
    /// </summary>
    /// <remarks>
    /// 🔴 <b>絕不可在持有 <see cref="_gate"/> 時呼叫</b>：<see cref="SetPluginEnabled"/> 本身就是 IPC。
    /// <para>
    /// 🔑 進到這裡的人是<b>唯一</b>的寫入者（<see cref="_legacyWriting"/>）：期間有別人改了意圖就再寫一次，
    /// 所以<b>最後一次寫下去的一定是最新的意圖</b>，而且沒有任何執行緒需要阻塞等待 IPC。
    /// </para>
    /// </remarks>
    private bool DriveLegacy()
    {
        for (var round = 0; round < LegacyWriteRounds; round++)
        {
            bool want;
            lock (_gate)
            {
                // 期間改走租約軌了 ⇒ 這次寫入作廢（租約已經在壓了，別再動使用者的全域開關）。
                if (_lease != Guid.Empty)
                {
                    _legacyWriting = false;
                    return true;
                }

                want = _legacyDesired;
            }

            try
            {
                SetPluginEnabled(want);
            }
            catch
            {
                lock (_gate)
                    _legacyWriting = false;

                return false;
            }

            lock (_gate)
            {
                // 寫下去的就是最新的意圖 ⇒ 收工。期間又被改過就再寫一次。
                if (_legacyDesired == want)
                {
                    _legacyWriting = false;
                    return true;
                }
            }
        }

        lock (_gate)
            _legacyWriting = false;

        FrameworkLogger.Info($"YesAlready 的開關在 {LegacyWriteRounds} 次寫入之內都沒有穩定下來，這一輪就寫到這裡");
        return true;
    }
}
