using System;
using System.Collections.Generic;

namespace SomethingNeedDoing.Utils;

/// <summary>
/// 從遊戲碼推導帶圖示右鍵選單（"ContextIconMenu" addon）AtkValues 的版面：項目數在哪格、第一個項目從哪格開始、
/// 每個項目佔幾格、項目文字在項目內第幾格。跟一般右鍵選單（"ContextMenu"）不同源、版面也不同。
/// </summary>
/// <remarks>
/// 契約：純函式、不碰遊戲記憶體——命中點 RVA 與位元組由呼叫端提供（實機＝SigScanner 的模組副本；離線＝執行檔）。
/// 三個互相獨立的來源，至少兩個解得出、每個欄位至少一個來源給得出、而且給得出的全部一致才採用；否則回 null。
/// 本檔只依賴 BCL，可整檔複製到離線探針（推導見 commit 訊息）。
/// </remarks>
internal static class ContextIconMenuLayoutDerivation
{
    internal readonly record struct Layout(int CountIndex, int FirstEntryIndex, int EntryStride, int TextOffset);
    internal readonly record struct Hit(long Rva, byte[] Bytes);
    internal readonly record struct Partial(int? CountIndex, int? FirstEntryIndex, int? EntryStride, int? TextOffset);

    /// <param name="ExtraBytes">特徵碼之後多讀、由 Decode 自己驗的位元組數（避免特徵碼以萬用字元結尾）。</param>
    internal sealed record Part(string Signature, int ExtraBytes);
    internal sealed record Source(string Name, Part[] Parts, Func<Hit[], Partial?> Decode);

    internal const int AtkValueSize = 16;
    private const int MaxFunctionSpan = 0x400;
    private const int MaxAlignPadding = 15;

    internal static readonly Source[] Sources =
    [
        // 選單自己讀值（AddonContextIconMenu.OnRefresh）：項目數、項目迴圈開頭、迴圈尾的步長與回跳。
        new("AddonContextIconMenu.OnRefresh",
        [
            new("49 8D 4C 24 ?? E8 ?? ?? ?? ?? B9 ?? 00 00 00 89 86", 4),
            new("45 8B EE 48 89 86 ?? ?? ?? ?? 44 39 B6 ?? ?? ?? ?? 0F 86 ?? ?? ?? ?? BB ?? 00 00 00", 0),
            new("4C 8B BE ?? ?? ?? ?? 8D 4B ?? 41 8B C5 48 C1 E1 04 49 03 CC 48 8D 3C 80 E8 ?? ?? ?? ?? 8D 4B ?? 41 89 04 FF 48 C1 E1 04 49 03 CC E8 ?? ?? ?? ?? 8B CB 48 C1 E1 04 49 03 CC 41 89 44 FF ?? E8 ?? ?? ?? ?? 8D 4B 01 49 89 44 FF", 0),
            new("83 C3 ?? 41 89 44 FF ?? 41 FF C5 44 3B AE ?? ?? ?? ?? 0F 82", 4),
        ], DecodeOnRefresh),
        // 開選單（寫項目數、valueCount＝項目數×步長＋開頭格數、addon 編號 0x85）。
        new("開選單.項目數與總格數",
        [
            new("48 8B 5F 28 BA 05 00 00 00 48 8D 8B ?? ?? ?? ?? E8 ?? ?? ?? ?? 89 B3 ?? ?? ?? ?? BA 05 00 00 00 48 8B 5F 28 48 8D 8B ?? ?? ?? ?? E8 ?? ?? ?? ?? C7 83 ?? ?? ?? ?? ?? ?? ?? ?? 48 8B 4F 10 48 8B 01 FF 50 40 0F B7 4F 20 44 8D 04 F5 ?? 00 00 00 4C 8B 4F 28 BA 85 00 00 00 C7 44 24 38 04 00 00 00 49 81 C1", 4),
        ], DecodeOpenCount),
        // 開選單（逐項寫入：項目數、第一個項目的文字格、第一個項目的起點、迴圈尾的步長）。
        new("開選單.逐項寫入",
        [
            new("49 8D 4F ?? 0F B6 BB ?? ?? ?? ?? BA 05 00 00 00 E8 ?? ?? ?? ?? BA 05 00 00 00 41 89 7F ?? 49 8D 4F ?? E8 ?? ?? ?? ?? 41 C7 47 ?? ?? ?? ?? ?? 80 BB ?? ?? ?? ?? 00 0F 86 ?? ?? ?? ?? 41 BD ?? 00 00 00 4D 8D A7", 4),
            new("4D 8D A4 24 ?? ?? ?? ?? 0F B6 83 ?? ?? ?? ?? 41 FF C6 41 83 C5 ?? 4C 89 74 24 ?? 44 3B F0 0F 82", 4),
        ], DecodeOpenFill),
    ];

    /// <param name="readMatch">給特徵碼與要讀的長度，回第一個命中點的 RVA 與位元組；沒命中回 null。</param>
    /// <param name="evidence">每個來源的結果，寫進 log 用。</param>
    internal static Layout? Derive(Func<string, int, Hit?> readMatch, out string evidence)
    {
        var solved = new List<Partial>();
        var notes = new List<string>();
        foreach (var source in Sources)
        {
            var hits = new Hit[source.Parts.Length];
            string? note = null;
            for (var i = 0; i < source.Parts.Length && note == null; i++)
            {
                var part = source.Parts[i];
                var length = SignatureLength(part.Signature) + part.ExtraBytes;
                if (readMatch(part.Signature, length) is not { } hit || hit.Bytes.Length != length)
                    note = $"第{i + 1}段沒命中";
                else if (!MatchesFixedBytes(part.Signature, hit.Bytes))
                    note = $"第{i + 1}段命中處位元組與特徵碼不符";
                else
                    hits[i] = hit;
            }
            if (note == null)
            {
                var value = source.Decode(hits);
                if (value is { } v)
                {
                    solved.Add(v);
                    note = $"項目數[{Show(v.CountIndex)}] 起點{Show(v.FirstEntryIndex)} 步長{Show(v.EntryStride)} 文字+{Show(v.TextOffset)}";
                }
                else
                {
                    note = "命中但內部不一致";
                }
            }
            notes.Add($"{source.Name}={note}");
        }
        evidence = string.Join("；", notes);
        if (solved.Count < 2) return null;
        if (!Agree(solved, p => p.CountIndex, out var count)
            || !Agree(solved, p => p.FirstEntryIndex, out var first)
            || !Agree(solved, p => p.EntryStride, out var stride)
            || !Agree(solved, p => p.TextOffset, out var text))
            return null;
        var layout = new Layout(count, first, stride, text);
        return InRange(layout) ? layout : null;
    }

    internal static bool InRange(Layout l)
        => l.CountIndex is >= 0 and <= 15 && l.CountIndex < l.FirstEntryIndex && l.FirstEntryIndex <= 64
           && l.EntryStride is >= 1 and <= 32 && l.TextOffset >= 0 && l.TextOffset < l.EntryStride;

    // 給得出這個欄位的來源全部一致，且至少一個給得出。
    private static bool Agree(List<Partial> solved, Func<Partial, int?> pick, out int value)
    {
        int? agreed = null;
        foreach (var p in solved)
        {
            if (pick(p) is not { } v) continue;
            if (agreed is { } a && a != v)
            {
                value = 0;
                return false;
            }
            agreed = v;
        }
        value = agreed ?? 0;
        return agreed is not null;
    }

    private static string Show(int? v) => v?.ToString() ?? "-";

    // 項目數格（disp8）、迴圈開頭（mov ebx,第一個文字格）、迴圈內 lea ecx,[rbx-k] 讀第 0、1 格、
    // 文字格＝ebx 本身（唯一 64 位元寫入）、迴圈尾 add ebx,步長＋回跳到迴圈開頭；同一個項目數欄位出現三次。
    private static Partial? DecodeOnRefresh(Hit[] h)
    {
        byte[] b0 = h[0].Bytes, b1 = h[1].Bytes, b2 = h[2].Bytes, b3 = h[3].Bytes;
        var countDisp = (sbyte)b0[4];
        int countField = I32(b0, 17), rel0 = (sbyte)b2[9], rel1 = (sbyte)b2[31], stride = (sbyte)b3[2];
        var loopHead = h[3].Rva + 24 + I32(b3, 20);
        var padding = h[2].Rva - (h[1].Rva + b1.Length);
        var ok = countDisp > 0 && countDisp % AtkValueSize == 0
                 && I32(b1, 13) == countField && I32(b3, 14) == countField
                 && I32(b1, 6) == I32(b2, 3)
                 && rel0 < 0 && rel1 == rel0 + 1
                 && loopHead == h[2].Rva && padding is >= 0 and <= MaxAlignPadding
                 && Within(h[0].Rva, h[1].Rva) && Within(h[2].Rva, h[3].Rva);
        return ok ? new Partial(countDisp / AtkValueSize, b1[24] + rel0, stride, -rel0) : null;
    }

    // valueCount＝lea r8d,[rsi*步長+開頭格數]，項目數 esi 同時寫進項目數格；values＝[rdi+28h]+base。
    private static Partial? DecodeOpenCount(Hit[] h)
    {
        var b = h[0].Bytes;
        int countDisp = I32(b, 12), flagsDisp = I32(b, 39), valuesBase = I32(b, 100);
        var ok = I32(b, 23) == countDisp + 8 && I32(b, 50) == flagsDisp + 8
                 && countDisp > valuesBase && (countDisp - valuesBase) % AtkValueSize == 0
                 && flagsDisp > valuesBase && (flagsDisp - valuesBase) % AtkValueSize == 0;
        return ok ? new Partial((countDisp - valuesBase) / AtkValueSize, b[76], 1 << (b[75] >> 6), null) : null;
    }

    // 項目數格（disp8，寫入在 +8）、mov r13d,第一個文字格、lea r12,[r15+第一個項目*16+8]；
    // 迴圈尾 lea r12,[r12+步長*16] 與 add r13d,步長 一致、回跳落在開頭之後的對齊空白內；項目數欄位出現三次。
    private static Partial? DecodeOpenFill(Hit[] h)
    {
        byte[] b1 = h[0].Bytes, b2 = h[1].Bytes;
        int countDisp = b1[3], firstDisp = I32(b1, 69), strideBytes = I32(b2, 4), stride = (sbyte)b2[21];
        var loopHead = h[1].Rva + b2.Length + I32(b2, 32);
        var padding = loopHead - (h[0].Rva + b1.Length);
        var ok = countDisp > 0 && countDisp % AtkValueSize == 0 && b1[29] == countDisp + 8 && b1[42] == b1[33] + 8
                 && I32(b1, 7) == I32(b1, 49) && I32(b2, 11) == I32(b1, 7)
                 && firstDisp > 8 && (firstDisp - 8) % AtkValueSize == 0
                 && strideBytes == stride * AtkValueSize
                 && padding is >= 0 and <= MaxAlignPadding && Within(h[0].Rva, h[1].Rva);
        if (!ok) return null;
        var first = (firstDisp - 8) / AtkValueSize;
        return new Partial(countDisp / AtkValueSize, first, stride, b1[62] - first);
    }

    private static bool Within(long earlier, long later) => later > earlier && later - earlier < MaxFunctionSpan;

    private static int I32(byte[] b, int offset) => BitConverter.ToInt32(b, offset);

    internal static int SignatureLength(string signature)
        => signature.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

    /// <summary>只比特徵碼那一段；之後的 ExtraBytes 由各 Decode 自己驗。</summary>
    internal static bool MatchesFixedBytes(string signature, byte[] bytes)
    {
        var tokens = signature.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length > bytes.Length) return false;
        for (var i = 0; i < tokens.Length; i++)
        {
            if (tokens[i] == "??") continue;
            if (Convert.ToByte(tokens[i], 16) != bytes[i]) return false;
        }
        return true;
    }
}
