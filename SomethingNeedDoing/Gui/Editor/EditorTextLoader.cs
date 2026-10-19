using System.Buffers;
using System.Text;
using DalamudCodeEditor;
using TextEditor = DalamudCodeEditor.TextEditor.Editor;

namespace SomethingNeedDoing.Gui.Editor;

/// <summary>
/// 把巨集文字載入 DalamudCodeEditor。套件的 <c>TextBuffer.SetText</c> 逐個 char 建 <c>Rune</c>，
/// 遇到代理字元（emoji 等 BMP 以外的字）擲 ArgumentOutOfRangeException（參數 ch），緩衝區留在只載一半的狀態。
/// 含代理字元時改成逐個 Rune 建字形；其餘轉換與 SetText 相同（去掉 \r、tab 換成 TabSize 個 TabCharacter）。
/// </summary>
internal static class EditorTextLoader
{
    /// <returns>false＝含不成對的代理字元、已顯示成 U+FFFD：存回會改到內容，呼叫端要設唯讀。</returns>
    public static bool Load(TextEditor editor, string text)
    {
        if (!ContainsSurrogate(text))
        {
            editor.Buffer.SetText(text);
            return true;
        }

        var buffer = editor.Buffer;
        buffer.MarkDirty();
        var lines = buffer.GetLines();
        lines.Clear();
        buffer.AddLine();
        var lossless = true;
        var rest = text.AsSpan();
        while (!rest.IsEmpty)
        {
            if (Rune.DecodeFromUtf16(rest, out var rune, out var consumed) != OperationStatus.Done)
                lossless = false;
            rest = rest[consumed..];
            switch (rune.Value)
            {
                case '\n':
                    buffer.AddLine();
                    break;
                case '\t':
                    for (var i = 0; i < editor.Style.TabSize; i++)
                        lines[^1].Add(new Glyph(editor.Style.TabCharacter));
                    break;
                case '\r':
                    break;
                default:
                    lines[^1].Add(new Glyph(rune));
                    break;
            }
        }

        editor.Colorizer.Colorize(0, buffer.LineCount);
        editor.Scroll.RequestScrollToTop();
        return lossless;
    }

    private static bool ContainsSurrogate(string text)
    {
        foreach (var c in text)
            if (char.IsSurrogate(c))
                return true;
        return false;
    }
}
