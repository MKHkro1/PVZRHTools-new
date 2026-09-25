using System.Windows;
using System.Windows.Controls;
using PVZRHTools.Utils;

namespace PVZRHTools.Controls;

/// <summary>
///     WPF 版 TmpRichTextBlock（对应参考版 PVZRHTools\Controls\TmpRichTextBlock.cs）：
///     TextBlock 子类，Markup 依赖属性变化即把 TMP 富文本标签解析成 Inlines 渲染。
///     只动渲染层 —— 绑定源（如 TravelBuff.Text）原文一个字不改（ID 解析依赖原文）。
/// </summary>
public class TmpTextBlock : TextBlock
{
    /// <summary>
    ///     构造时只做**无害的**排版微调；**绝不固定行高**。
    ///
    ///     教训（2026-09-24 两轮实测）：
    ///       ① 仅做 Inlines 转换 + HandyControl <c>DataGrid.Small</c> 紧凑行高 →
    ///          多彩色富文本行的**底部**被裁一点点（"词条文本显示不全"）。
    ///       ② 随后设 <c>LineStackingStrategy=BlockLineHeight</c> + <c>LineHeight=20</c> →
    ///          **更糟**：同一段落含多种 FontSize / 上下标 Run 时，
    ///          固定行高把较高的那段整行裁掉，表现为「描述整行看不见」。
    ///     ⇒ 行高必须由内容决定。此处只留底部 2px 内边距（防贴边），
    ///       行高交给 WPF 默认（MaxHeight 策略）自适应；若需更宽松，
    ///       应放宽承载控件（DataGrid 行/单元格），不要在这里锁死。
    /// </summary>
    public TmpTextBlock()
    {
        // 底部留 2px：紧凑行高下防止最后一行贴边被裁（不影响整行高度）
        Padding = new Thickness(0, 0, 0, 2);
    }

    public static readonly DependencyProperty MarkupProperty = DependencyProperty.Register(
        nameof(Markup),
        typeof(string),
        typeof(TmpTextBlock),
        new PropertyMetadata(null, OnMarkupChanged));

    public string? Markup
    {
        get => (string?)GetValue(MarkupProperty);
        set => SetValue(MarkupProperty, value);
    }

    private static void OnMarkupChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TmpTextBlock block)
            return;

        block.Inlines.Clear();
        foreach (var inline in TmpMarkup.ToInlines(e.NewValue as string))
            block.Inlines.Add(inline);
    }
}
