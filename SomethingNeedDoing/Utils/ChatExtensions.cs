using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin.Services;
using ECommons.ChatMethods;
using System.Collections.Concurrent;

namespace SomethingNeedDoing.Utils;

/// <summary>
/// 這個外掛的聊天輸出都走這裡。唯一的例外是 <c>MigrationModal</c> 匯入成功那一行，
/// 它直接呼叫 <c>Svc.Chat.Print</c>，而那條路徑只在 ImGui 繪製回呼裡走得到（＝framework 執行緒）。
/// </summary>
/// <remarks>
/// 🔴🔴 <b>為什麼要排到 framework 執行緒才送出。</b>
/// 本 pin 的 Dalamud <c>ChatGui.Print(XivChatEntry)</c> 只是把項目 <c>Enqueue</c> 進一個
/// <b>沒有任何同步</b>的 <c>Queue&lt;XivChatEntry&gt;</c>（<c>Dalamud/Game/Gui/ChatGui.cs:43</c>、
/// <c>:119-121</c>），而 <c>UpdateQueue</c> 在 framework 執行緒上 <c>TryDequeue</c>（<c>:207-214</c>）。
/// 從別的執行緒呼叫 ⇒ 與 framework 執行緒並行改同一個 <c>Queue</c>，
/// <b>失敗形式不是「訊息晚一點出現」而是那個佇列本身壞掉</b>。
/// <para>
/// 🔴 這個外掛有一大半的呼叫點在執行緒池上：<c>RequiresFrameworkThread</c> 為
/// <see langword="false"/> 的巨集指令（<c>/loop</c>、<c>/gate</c>、<c>/runmacro</c> 在
/// <c>&lt;wait&gt;</c> 之後都在執行緒池）、兩個巨集引擎的 <c>OnMacroError</c>、
/// 以及排程器 <c>Task.Run</c> 裡的相依檢查。Lua 迴圈更是只要跑過一次
/// <c>await Svc.Framework.DelayTicks(1)</c> 就<b>整段都在執行緒池</b>
/// —— <c>Framework.DelayTicks</c> 建的 TCS 帶 <c>RunContinuationsAsynchronously</c>。
/// </para>
/// <para>
/// 📌 <see cref="IFramework.RunOnFrameworkThread(Action)"/> 在<b>已經是</b> framework 執行緒時
/// 就地同步執行（<c>Framework.cs:171-186</c>），所以 UI 與聊天指令那些路徑的行為一個位元都沒變。
/// </para>
/// <para>
/// 🔑 <b>為什麼還要自己排一個佇列</b>：Dalamud 的 <c>ThreadBoundTaskScheduler</c> 用
/// <c>ConcurrentDictionary</c> 存待跑的工作，<c>Run()</c> 走訪它的 <c>Keys</c>
/// ⇒ <b>不保證先進先出</b>。單純把每一次 <c>Print</c> 各自包成一個排程工作的話，
/// 同一格內送出的兩則訊息順序會變成隨機的。改成自己用 <see cref="ConcurrentQueue{T}"/> 排隊、
/// 到了 framework 執行緒再一次排乾，順序就與呼叫順序<b>逐字相同</b>。
/// </para>
/// </remarks>
public static class ChatExtensions
{
    /// <summary>還沒送出的訊息。<b>順序就是呼叫順序。</b></summary>
    private static readonly ConcurrentQueue<(IChatGui Chat, XivChatEntry Entry)> Pending = new();

    public static void PrintMessage(this IChatGui chat, string message)
        => QueueForFramework(chat, new XivChatEntry
        {
            Type = C.ChatType,
            Message = $"[{P.Prefix}] {message}"
        });

    public static void PrintErrorMsg(this IChatGui chat, string message)
        => QueueForFramework(chat, new XivChatEntry
        {
            Type = C.ErrorChatType,
            Message = $"[{P.Prefix}] {message}"
        });

    public static void PrintColor(this IChatGui chat, string message, UIColor color)
        => QueueForFramework(chat, new XivChatEntry
        {
            Type = C.ChatType,
            Message = new SeString(
                new UIForegroundPayload((ushort)color),
                new TextPayload($"[{P.Prefix}] {message}"),
                UIForegroundPayload.UIForegroundOff)
        });

    /// <summary>把一則已經組好的訊息排進佇列，並要求在 framework 執行緒上排乾。</summary>
    /// <remarks>
    /// 📌 訊息內容<b>在呼叫端的執行緒上就組好了</b>（<c>C.ChatType</c>、<c>P.Prefix</c> 與內插
    /// 都在進到這裡之前完成），排隊的只是「送出」這個動作，所以看到的字一個都沒變。
    /// <para>
    /// 🔴 <b>刻意不等它跑完</b>：全部呼叫點都是「印一行就繼續做事」，沒有任何一處需要印完
    /// 才能往下走。同步等待只會在 framework 執行緒以外的地方多一個阻塞點。
    /// </para>
    /// </remarks>
    private static void QueueForFramework(IChatGui chat, XivChatEntry entry)
    {
        // 卸載窗內轉派會就地在呼叫端執行緒跑，那等於直接動 Dalamud 的裸聊天佇列。
        if (FrameworkUnloadGuard.ShouldSkip("聊天輸出"))
            return;

        Pending.Enqueue((chat, entry));
        _ = Svc.Framework.RunOnFrameworkThread(static () =>
        {
            while (Pending.TryDequeue(out var p))
                p.Chat.Print(p.Entry);
        });
    }
}
