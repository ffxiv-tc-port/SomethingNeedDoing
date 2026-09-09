using Dalamud.Plugin.Ipc.Exceptions;
using System.Reflection;

namespace SomethingNeedDoing.Utils;

/// <summary>
/// 單向橋接到「塔塔露誇獎」(TataruPraise)：巨集自己跑到結束、或是出錯停掉時請它念一句。
/// </summary>
/// <remarks>
/// 🔴 <b>巨集「跑完了」在本外掛是完全沒有輸出的</b>——不印聊天、不跳 toast、不出聲，
/// <c>MacroScheduler</c> 的 <c>MacroStateChanged</c> 事件在整個外掛裡<b>一個訂閱端都沒有</b>
/// （2026-09-09 逐檔查過）。掛著巨集去做別的事的人因此完全不知道它結束了，這個橋接就是補這個洞。
/// <para>
/// 🔴 <b>只用 Dalamud 原生 CallGate 的字串契約</b>，不透過 <c>ECommons.EzIPC</c>：
/// 這條路徑要在對方沒安裝時安靜地什麼都不做，而 <c>EzIPC</c> 的包裝層對「端點不存在」的處置
/// 是這個檔管不到的。契約名逐字取自 TataruPraise 的 <c>IpcContract.cs</c> 與 <c>PraiseCategory.cs</c>；
/// CallGate 是純字串比對，<b>名字打錯不會有任何錯誤訊息</b>，只會永遠拿到「這個頻道沒有人註冊」。
/// </para>
/// <para>
/// 🔴 <b>一律排到 framework 執行緒才呼叫。</b>IPC 的實作跑在<b>呼叫端的執行緒</b>上，
/// 而本外掛的完成點在執行緒池上（<c>MacroScheduler.RunMacroAsync</c> 的
/// <c>await state.ExecutionTask</c> 之後、Lua 引擎跑過 <c>DelayTicks</c> 之後都是）。
/// 直接打過去等於把 TataruPraise 的碼拉到執行緒池上跑。
/// 📌 <c>IFramework.RunOnFrameworkThread(Action)</c> 在<b>已經是</b> framework 執行緒時就地執行，
/// 所以從 UI／指令停止巨集那條路徑不會多花任何一幀。
/// </para>
/// <para>
/// ⚠️ 這是<b>單向通知</b>：不重試、不看回傳值改流程，也不會因此做任何遊戲操作。
/// </para>
/// </remarks>
public static class TataruPraiseIPC
{
    /// <summary>
    /// <c>Func&lt;string, bool&gt;</c>：<b>指定的那個情境</b>現在出得了聲嗎
    /// （總開關開著＋這個情境沒被關掉＋這個情境至少有一句已合成的語音）。
    /// </summary>
    /// <remarks>
    /// 🔴 閘門要問的是這一個，<b>不是</b> <c>TataruPraise.IsAvailable</c>：後者問的是
    /// 「整池<b>有某個情境</b>播得出來」，於是「別的情境有語音、巨集這個一句都沒有」時照樣通過，
    /// 接著 <c>Praise</c> 回 <c>false</c>——呼叫端就分不出「不能出聲」與「這次剛好沒出聲」。
    /// <para>
    /// 🔴 舊版 TataruPraise 沒有註冊這個端點，<c>InvokeFunc</c> 會擲 <c>IpcNotReadyError</c>，
    /// 剛好落進既有的 catch＝安靜不出聲，這是正確的 fail-safe。
    /// <b>失敗時絕不可以退回去叫 <c>IsAvailable</c></b>——那樣就把這個端點的意義整個抵銷掉了。
    /// </para>
    /// </remarks>
    private const string TagIsAvailableFor = "TataruPraise.IsAvailableFor";

    /// <summary><c>Func&lt;string, bool&gt;</c>：從指定情境的誇獎池挑一句念。</summary>
    private const string TagPraise = "TataruPraise.Praise";

    /// <summary>
    /// 「巨集自己跑到結束」的情境鍵。對方端的常數是 <c>PraiseCategory.MacroDone</c>，逐字相同。
    /// </summary>
    /// <remarks>
    /// ⚠️ TataruPraise 拿這個字串當 <c>pool.json</c> 的鍵，<b>對不上就靜默不出聲</b>
    /// （它會在記錄檔印一次「未知情境」）。
    /// </remarks>
    private const string CategoryMacroDone = "巨集完成";

    /// <summary>
    /// 「巨集出錯停掉」用的情境鍵。對方端的常數是 <c>PraiseCategory.NeedHelp</c>，逐字相同。
    /// </summary>
    /// <remarks>
    /// 🔴 刻意<b>不</b>跟 <see cref="CategoryMacroDone"/> 共用：出錯跟跑完念同一句話等於沒講。
    /// 這個鍵在 TataruPraise 的語意就是「自動化卡住了，需要前輩過來看一下」，AutoDuty 也用它。
    /// </remarks>
    private const string CategoryNeedHelp = "需要幫忙";

    /// <summary>巨集<b>自己跑到結束</b>時叫這個（閘門是 <see cref="Config.TataruPraiseOnMacroComplete"/>）。</summary>
    /// <param name="reason">寫進記錄用的來源描述，讓記錄檔分得出是哪一支巨集觸發的。</param>
    public static void TryPraiseMacroDone(string reason)
    {
        if (!C.TataruPraiseOnMacroComplete) return;
        Send(CategoryMacroDone, reason);
    }

    /// <summary>巨集<b>因為錯誤而停下來</b>時叫這個（閘門是 <see cref="Config.TataruPraiseOnMacroError"/>）。</summary>
    /// <param name="reason">寫進記錄用的來源描述。</param>
    public static void TryPraiseMacroError(string reason)
    {
        if (!C.TataruPraiseOnMacroError) return;
        Send(CategoryNeedHelp, reason);
    }

    /// <summary>把整段 IPC 交易排到 framework 執行緒上跑；使用者的開關已經在呼叫端判過了。</summary>
    /// <remarks>
    /// 🔴 <b>刻意不等它跑完</b>：呼叫點都在「巨集已經結束、正在收尾」的路徑上，
    /// 沒有任何一處需要等念完才能往下走；同步等待只會在 framework 執行緒以外多一個阻塞點。
    /// </remarks>
    private static void Send(string category, string reason)
    {
        try
        {
            _ = Svc.Framework.RunOnFrameworkThread(() => SendOnFramework(category, reason));
        }
        catch (Exception ex)
        {
            // 外掛正在卸載時排程器可能已經收掉了。這不是使用者要處理的事，只留一行線索。
            FrameworkLogger.Info($"[TataruPraise] 排程失敗（{reason}）：{ex.Message}");
        }
    }

    /// <summary>真正打 IPC 的那一段，保證在 framework 執行緒上。</summary>
    private static void SendOnFramework(string category, string reason)
    {
        try
        {
            // 先問 IsAvailableFor：對方的總開關關著、這個情境被使用者關掉、或這個情境一句已合成的
            // 都沒有，就不要浪費它的冷卻。
            if (!Svc.PluginInterface.GetIpcSubscriber<string, bool>(TagIsAvailableFor).InvokeFunc(category))
                return;

            var accepted = Svc.PluginInterface.GetIpcSubscriber<string, bool>(TagPraise).InvokeFunc(category);
            // Information 級：這是「使用者說沒出聲」時唯一問得出真相的一行。
            FrameworkLogger.Info($"[TataruPraise] {reason}：Praise(「{category}」) 回傳 {accepted}。");
        }
        catch (IpcNotReadyError)
        {
            // 對方沒安裝／沒載入。完全正常的狀態，刻意不寫記錄——沒裝的人每支巨集跑完都會走到這裡。
        }
        catch (TargetInvocationException ex)
        {
            // 🔴 CallGate 走 Func.DynamicInvoke，**提供端自己擲的例外**一律被包成這個型別，
            //    IpcError 那一族一個都攔不到。不攔它的話這裡會把例外丟回 framework 執行緒上。
            FrameworkLogger.Info($"[TataruPraise] 對方端擲出例外（{reason}）：{ex.InnerException?.Message ?? ex.Message}");
        }
        catch (Exception ex)
        {
            FrameworkLogger.Info($"[TataruPraise] 呼叫失敗（{reason}）：{ex.Message}");
        }
    }
}
