using FFXIVClientStructs.FFXIV.Component.GUI;
using System.Runtime.InteropServices;
using System.Threading;
using Layout = SomethingNeedDoing.Utils.ContextIconMenuLayoutDerivation.Layout;

namespace SomethingNeedDoing.Utils;

/// <summary>帶圖示右鍵選單（"ContextIconMenu" addon）AtkValues 的版面（7.25：項目數在 [4]、項目 i 的文字在 [11 + 8i + 2]）。</summary>
/// <remarks>
/// 從遊戲碼推導、不寫死；第一次查詢時才掃描，結果整個插件生命期共用。
/// 推不出來時 <see cref="TryGet"/> 回 false：呼叫端必須停用依賴項目位置的功能（讀錯一格＝點到別的項目），
/// 第一次查詢（已登入時）在聊天視窗提示一次。
/// </remarks>
internal static unsafe class ContextIconMenuLayout
{
    private static readonly Lazy<Layout?> Derived = new(Derive, LazyThreadSafetyMode.ExecutionAndPublication);
    private static int echoed;

    public static bool TryGet(out Layout layout)
    {
        if (Derived.Value is { } derived)
        {
            layout = derived;
            return true;
        }
        layout = default;
        if (Svc.ClientState.IsLoggedIn && Interlocked.Exchange(ref echoed, 1) == 0)
            Svc.Chat.PrintErrorMsg("推不出帶圖示右鍵選單（ContextIconMenu）的版面（遊戲改版？），巨集函式 SelectContextIconMenuEntryByText 先停用（不點、回 false），等插件更新。");
        return false;
    }

    private static Layout? Derive()
    {
        Layout? layout = null;
        string evidence;
        try
        {
            if (sizeof(AtkValue) != ContextIconMenuLayoutDerivation.AtkValueSize)
                evidence = $"sizeof(AtkValue)={sizeof(AtkValue)}";
            else
                layout = ContextIconMenuLayoutDerivation.Derive(ReadMatch, out evidence);
        }
        catch (Exception ex)
        {
            evidence = ex.ToString();
        }
        if (layout is { } l)
            FrameworkLogger.Info($"ContextIconMenu 版面：項目數在 AtkValues[{l.CountIndex}]、項目 i 的文字在 [{l.FirstEntryIndex} + {l.EntryStride}i + {l.TextOffset}]（{evidence}）");
        else
            FrameworkLogger.Error($"推不出 ContextIconMenu 的版面，SelectContextIconMenuEntryByText 停用（{evidence}）");
        return layout;
    }

    // 讀 SigScanner 的模組副本，命中點與長度都要落在 .text 內。
    private static ContextIconMenuLayoutDerivation.Hit? ReadMatch(string signature, int length)
    {
        var scanner = Svc.SigScanner;
        if (!scanner.TryScanText(signature, out var address)) return null;
        var rva = (long)address - (long)scanner.Module.BaseAddress;
        if (rva < scanner.TextSectionOffset || rva + length > scanner.TextSectionOffset + scanner.TextSectionSize) return null;
        var bytes = new byte[length];
        Marshal.Copy(scanner.SearchBase + (nint)rva, bytes, 0, length);
        return new ContextIconMenuLayoutDerivation.Hit(rva, bytes);
    }
}
