using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace PVZRHTools.Utils;

/// <summary>
///     将 Unity TextMeshPro 富文本标签解析为 WPF Inlines，或剥离为纯文本。
///     支持 color / #RRGGBB / b / i / u / s / size / alpha / mark / nobr / br / noparse 以及 \n。
///     由参考版 PVZRHTools\Utils\TmpMarkup.cs（Avalonia）移植：Avalonia.Controls.Documents/Media
///     → System.Windows.Documents/Media；Color.FromArgb(a,r,g,b) 语义一致；
///     sprite/align/... 等不认识的标签一律按「容错跳过」处理（保留在忽略清单里）；
///     ToPlainText 必须保留（搜索过滤与 ID 解析依赖纯文本）。
///     与 REF 的唯一刻意差异：FontSize 仅在 &lt;size&gt; 显式指定时写入，
///     其余情况让 Run 继承宿主 TextBlock 字号（Avalonia 侧 REF 恒写死 baseFontSize，
///     WPF DataGrid 内继承更贴合既有排版）。
/// </summary>
public static class TmpMarkup
{
    private static readonly Dictionary<string, Color> NamedColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["aqua"] = Color.FromRgb(0x00, 0xFF, 0xFF),
        ["black"] = Color.FromRgb(0x00, 0x00, 0x00),
        ["blue"] = Color.FromRgb(0x00, 0x00, 0xFF),
        ["brown"] = Color.FromRgb(0xA5, 0x2A, 0x2A),
        ["cyan"] = Color.FromRgb(0x00, 0xFF, 0xFF),
        ["darkblue"] = Color.FromRgb(0x00, 0x00, 0xA0),
        ["fuchsia"] = Color.FromRgb(0xFF, 0x00, 0xFF),
        ["gray"] = Color.FromRgb(0x80, 0x80, 0x80),
        ["green"] = Color.FromRgb(0x00, 0x80, 0x00),
        ["grey"] = Color.FromRgb(0x80, 0x80, 0x80),
        ["lightblue"] = Color.FromRgb(0xAD, 0xD8, 0xE6),
        ["lime"] = Color.FromRgb(0x00, 0xFF, 0x00),
        ["magenta"] = Color.FromRgb(0xFF, 0x00, 0xFF),
        ["maroon"] = Color.FromRgb(0x80, 0x00, 0x00),
        ["navy"] = Color.FromRgb(0x00, 0x00, 0x80),
        ["olive"] = Color.FromRgb(0x80, 0x80, 0x00),
        ["orange"] = Color.FromRgb(0xFF, 0xA5, 0x00),
        ["purple"] = Color.FromRgb(0x80, 0x00, 0x80),
        ["red"] = Color.FromRgb(0xFF, 0x00, 0x00),
        ["silver"] = Color.FromRgb(0xC0, 0xC0, 0xC0),
        ["teal"] = Color.FromRgb(0x00, 0x80, 0x80),
        ["white"] = Color.FromRgb(0xFF, 0xFF, 0xFF),
        ["yellow"] = Color.FromRgb(0xFF, 0xFF, 0x00)
    };

    public static string ToPlainText(string? markup)
    {
        if (string.IsNullOrEmpty(markup))
            return string.Empty;

        var sb = new StringBuilder(markup.Length);
        Parse(markup, (text, _) => sb.Append(text), () => sb.Append('\n'), 14);
        return sb.ToString();
    }

    public static IEnumerable<Inline> ToInlines(string? markup, double baseFontSize = 14)
    {
        var result = new List<Inline>();
        if (string.IsNullOrEmpty(markup))
            return result;

        Parse(markup, (text, style) => result.Add(CreateRun(text, style, baseFontSize)),
            () => result.Add(new LineBreak()), baseFontSize);
        return result;
    }

    private static Run CreateRun(string text, StyleState style, double baseFontSize)
    {
        var run = new Run { Text = text };
        if (style.Color is { } color)
        {
            if (style.Alpha is { } alpha)
                color = Color.FromArgb(alpha, color.R, color.G, color.B);
            run.Foreground = new SolidColorBrush(color);
        }
        else if (style.Alpha is { } alpha)
        {
            run.Foreground = new SolidColorBrush(Color.FromArgb(alpha, 0xFF, 0xFF, 0xFF));
        }

        if (style.Bold)
            run.FontWeight = FontWeights.Bold;
        if (style.Italic)
            run.FontStyle = FontStyles.Italic;

        if (style.Underline || style.Strikethrough)
        {
            var decorations = new TextDecorationCollection();
            if (style.Underline)
                foreach (var decoration in TextDecorations.Underline)
                    decorations.Add(decoration);
            if (style.Strikethrough)
                foreach (var decoration in TextDecorations.Strikethrough)
                    decorations.Add(decoration);
            run.TextDecorations = decorations;
        }

        // 刻意差异（见类注释）：只有显式 <size> 才写死字号，否则继承宿主。
        if (style.FontSize is { } fontSize)
            run.FontSize = fontSize;

        if (style.Mark is { } mark)
            run.Background = new SolidColorBrush(mark);
        if (style.Baseline != BaselineAlignment.Baseline)
        {
            run.BaselineAlignment = style.Baseline;
            run.FontSize = (style.FontSize ?? baseFontSize) * 0.75;
        }

        return run;
    }

    private static void Parse(string markup, Action<string, StyleState> emitText, Action emitBreak, double baseFontSize)
    {
        var style = new StyleState();
        var colorStack = new Stack<Color?>();
        var alphaStack = new Stack<byte?>();
        var sizeStack = new Stack<double?>();
        var markStack = new Stack<Color?>();
        var sb = new StringBuilder();
        var i = 0;
        var noparse = false;

        void Flush()
        {
            if (sb.Length == 0)
                return;
            emitText(sb.ToString(), style);
            sb.Clear();
        }

        while (i < markup.Length)
        {
            var c = markup[i];

            if (!noparse && c == '\\' && i + 1 < markup.Length)
            {
                var next = markup[i + 1];
                if (next is 'n' or 'N')
                {
                    Flush();
                    emitBreak();
                    i += 2;
                    continue;
                }

                if (next is 't' or 'T')
                {
                    sb.Append('\t');
                    i += 2;
                    continue;
                }

                if (next == '\\')
                {
                    sb.Append('\\');
                    i += 2;
                    continue;
                }
            }

            if (c == '\r')
            {
                Flush();
                emitBreak();
                i += i + 1 < markup.Length && markup[i + 1] == '\n' ? 2 : 1;
                continue;
            }

            if (c == '\n')
            {
                Flush();
                emitBreak();
                i++;
                continue;
            }

            if (c == '<' && i + 1 < markup.Length)
            {
                var close = markup.IndexOf('>', i + 1);
                if (close < 0)
                {
                    sb.Append(c);
                    i++;
                    continue;
                }

                var raw = markup.AsSpan(i + 1, close - i - 1).Trim();
                if (raw.Length == 0)
                {
                    i = close + 1;
                    continue;
                }

                var closing = raw[0] == '/';
                if (closing)
                    raw = raw[1..].Trim();

                var selfClosing = raw.Length > 0 && raw[^1] == '/';
                if (selfClosing)
                    raw = raw[..^1].Trim();

                var (name, value) = SplitTag(raw);

                if (noparse)
                {
                    if (closing && name.Equals("noparse", StringComparison.OrdinalIgnoreCase))
                    {
                        noparse = false;
                        i = close + 1;
                        continue;
                    }

                    sb.Append(markup, i, close - i + 1);
                    i = close + 1;
                    continue;
                }

                if (!closing && name.Equals("noparse", StringComparison.OrdinalIgnoreCase))
                {
                    noparse = true;
                    i = close + 1;
                    continue;
                }

                if (name.Length > 0 && name[0] == '#')
                {
                    Flush();
                    if (!closing && TryParseTmpColor(name, out var hexColor))
                    {
                        colorStack.Push(style.Color);
                        style = style with { Color = hexColor };
                    }
                    else if (closing && colorStack.Count > 0)
                    {
                        style = style with { Color = colorStack.Pop() };
                    }

                    i = close + 1;
                    continue;
                }

                if (HandleTag(name, value, closing || (selfClosing && IsVoid(name)), ref style,
                        colorStack, alphaStack, sizeStack, markStack, baseFontSize, emitBreak, Flush))
                {
                    i = close + 1;
                    continue;
                }

                i = close + 1;
                continue;
            }

            if (c == '&' && TryAppendEntity(markup, ref i, sb))
                continue;

            sb.Append(c);
            i++;
        }

        Flush();
    }

    private static bool HandleTag(string name, string? value, bool closing, ref StyleState style,
        Stack<Color?> colorStack, Stack<byte?> alphaStack, Stack<double?> sizeStack, Stack<Color?> markStack,
        double baseFontSize, Action emitBreak, Action flush)
    {
        if (name.Equals("br", StringComparison.OrdinalIgnoreCase))
        {
            flush();
            emitBreak();
            return true;
        }

        if (name.Equals("nobr", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("align", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("cspace", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("mspace", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("pos", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("voffset", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("space", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("margin", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("indent", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("line-height", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("line-indent", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("sprite", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("font", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("material", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("gradient", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("rotate", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("style", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("link", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("page", StringComparison.OrdinalIgnoreCase))
            return true;

        flush();

        if (name.Equals("color", StringComparison.OrdinalIgnoreCase))
        {
            if (!closing && value is not null && TryParseTmpColor(value, out var color))
            {
                colorStack.Push(style.Color);
                style = style with { Color = color };
            }
            else if (closing && colorStack.Count > 0)
            {
                style = style with { Color = colorStack.Pop() };
            }

            return true;
        }

        if (name.Equals("alpha", StringComparison.OrdinalIgnoreCase))
        {
            if (!closing && value is not null && TryParseAlpha(value, out var alpha))
            {
                alphaStack.Push(style.Alpha);
                style = style with { Alpha = alpha };
            }
            else if (closing && alphaStack.Count > 0)
            {
                style = style with { Alpha = alphaStack.Pop() };
            }

            return true;
        }

        if (name.Equals("size", StringComparison.OrdinalIgnoreCase))
        {
            if (!closing && value is not null && TryParseSize(value, style.FontSize ?? baseFontSize, out var size))
            {
                sizeStack.Push(style.FontSize);
                style = style with { FontSize = size };
            }
            else if (closing && sizeStack.Count > 0)
            {
                style = style with { FontSize = sizeStack.Pop() };
            }

            return true;
        }

        if (name.Equals("mark", StringComparison.OrdinalIgnoreCase))
        {
            if (!closing && value is not null && TryParseTmpColor(value, out var mark))
            {
                markStack.Push(style.Mark);
                style = style with { Mark = mark };
            }
            else if (closing && markStack.Count > 0)
            {
                style = style with { Mark = markStack.Pop() };
            }

            return true;
        }

        if (name.Equals("b", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("bold", StringComparison.OrdinalIgnoreCase))
        {
            style = style with { Bold = !closing };
            return true;
        }

        if (name.Equals("i", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("italic", StringComparison.OrdinalIgnoreCase))
        {
            style = style with { Italic = !closing };
            return true;
        }

        if (name.Equals("u", StringComparison.OrdinalIgnoreCase))
        {
            style = style with { Underline = !closing };
            return true;
        }

        if (name.Equals("s", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("strike", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("strikethrough", StringComparison.OrdinalIgnoreCase))
        {
            style = style with { Strikethrough = !closing };
            return true;
        }

        if (name.Equals("sub", StringComparison.OrdinalIgnoreCase))
        {
            style = style with { Baseline = closing ? BaselineAlignment.Baseline : BaselineAlignment.Subscript };
            return true;
        }

        if (name.Equals("sup", StringComparison.OrdinalIgnoreCase))
        {
            style = style with { Baseline = closing ? BaselineAlignment.Baseline : BaselineAlignment.Superscript };
            return true;
        }

        return true;
    }

    private static bool IsVoid(string name)
    {
        return name.Equals("br", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("sprite", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("page", StringComparison.OrdinalIgnoreCase);
    }

    private static (string Name, string? Value) SplitTag(ReadOnlySpan<char> raw)
    {
        var eq = -1;
        for (var i = 0; i < raw.Length; i++)
        {
            if (raw[i] == '=')
            {
                eq = i;
                break;
            }

            if (char.IsWhiteSpace(raw[i]))
                break;
        }

        if (eq < 0)
        {
            var space = 0;
            while (space < raw.Length && !char.IsWhiteSpace(raw[space]))
                space++;
            return (raw[..space].ToString(), null);
        }

        var name = raw[..eq].Trim().ToString();
        var value = StripQuotes(raw[(eq + 1)..].Trim()).ToString();
        var spaceInValue = value.IndexOfAny([' ', '\t']);
        if (spaceInValue > 0 && !value.StartsWith('#') && !value.StartsWith('"'))
            value = value[..spaceInValue];
        return (name, value);
    }

    private static ReadOnlySpan<char> StripQuotes(ReadOnlySpan<char> value)
    {
        if (value.Length >= 2 &&
            ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
            return value[1..^1];
        return value;
    }

    public static bool TryParseTmpColor(string value, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        value = StripQuotes(value.AsSpan()).ToString().Trim();
        if (NamedColors.TryGetValue(value, out color))
            return true;

        if (value.StartsWith('#'))
        {
            var hex = value[1..];
            switch (hex.Length)
            {
                case 3:
                case 6:
                    return TryParseColor("#" + hex, out color);
                case 4:
                {
                    if (!TryParseHex(hex.AsSpan(0, 1), out var r) ||
                        !TryParseHex(hex.AsSpan(1, 1), out var g) ||
                        !TryParseHex(hex.AsSpan(2, 1), out var b) ||
                        !TryParseHex(hex.AsSpan(3, 1), out var a))
                        return false;
                    color = Color.FromArgb((byte)(a * 17), (byte)(r * 17), (byte)(g * 17), (byte)(b * 17));
                    return true;
                }
                case 8:
                {
                    if (!byte.TryParse(hex.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture,
                            out var r) ||
                        !byte.TryParse(hex.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture,
                            out var g) ||
                        !byte.TryParse(hex.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture,
                            out var b) ||
                        !byte.TryParse(hex.AsSpan(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture,
                            out var a))
                        return false;
                    color = Color.FromArgb(a, r, g, b);
                    return true;
                }
            }
        }

        return TryParseColor(value, out color);
    }

    /// <summary>WPF 的 Color 没有 TryParse（Avalonia 有）→ 用 ColorConverter 等价实现。</summary>
    private static bool TryParseColor(string value, out Color color)
    {
        color = default;
        try
        {
            if (ColorConverter.ConvertFromString(value) is Color parsed)
            {
                color = parsed;
                return true;
            }
        }
        catch
        {
            // 非法颜色串按解析失败处理（与 Avalonia TryParse 的 false 语义一致）
        }

        return false;
    }

    private static bool TryParseHex(ReadOnlySpan<char> hex, out int value)
    {
        value = 0;
        if (hex.Length != 1)
            return false;
        return int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryParseAlpha(string value, out byte alpha)
    {
        alpha = 255;
        value = StripQuotes(value.AsSpan()).ToString().Trim();
        if (value.StartsWith('#'))
            value = value[1..];

        if (value.Length is 1 or 2 &&
            byte.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex))
        {
            alpha = value.Length == 1 ? (byte)(hex * 17) : hex;
            return true;
        }

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
        {
            alpha = d <= 1.0
                ? (byte)Math.Clamp((int)Math.Round(d * 255), 0, 255)
                : (byte)Math.Clamp((int)Math.Round(d), 0, 255);
            return true;
        }

        return false;
    }

    private static bool TryParseSize(string value, double current, out double size)
    {
        size = current;
        value = StripQuotes(value.AsSpan()).ToString().Trim();
        if (value.EndsWith('%') &&
            double.TryParse(value.AsSpan(0, value.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture,
                out var pct))
        {
            size = current * (pct / 100.0);
            return true;
        }

        if (value.Length > 0 && value[0] is '+' or '-' &&
            double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var delta))
        {
            size = current + delta;
            return true;
        }

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var abs))
        {
            size = abs;
            return true;
        }

        return false;
    }

    private static bool TryAppendEntity(string markup, ref int i, StringBuilder sb)
    {
        var semi = markup.IndexOf(';', i + 1);
        if (semi < 0 || semi - i > 10)
            return false;

        var entity = markup.AsSpan(i, semi - i + 1);
        string? decoded = null;
        if (entity is "&lt;") decoded = "<";
        else if (entity is "&gt;") decoded = ">";
        else if (entity is "&amp;") decoded = "&";
        else if (entity is "&quot;") decoded = "\"";
        else if (entity is "&apos;") decoded = "'";
        else if (entity is "&nbsp;") decoded = " ";
        if (decoded is null)
            return false;

        sb.Append(decoded);
        i = semi + 1;
        return true;
    }

    private readonly record struct StyleState(
        Color? Color,
        byte? Alpha,
        bool Bold,
        bool Italic,
        bool Underline,
        bool Strikethrough,
        double? FontSize,
        Color? Mark,
        BaselineAlignment Baseline);
}
