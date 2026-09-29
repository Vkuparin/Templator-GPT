using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Templator.Core;

namespace Templator.Views;

/// <summary>Selectable preview rendered by the standard WPF document engine.</summary>
public sealed class PreviewText : RichTextBox
{
    public PreviewText()
    {
        IsReadOnly = true; IsDocumentEnabled = true;
        BorderThickness = new Thickness(0); Padding = new Thickness(0);
        Background = Brushes.Transparent; Foreground = new SolidColorBrush(Color.FromRgb(39, 49, 47));
        FontFamily = new FontFamily("Segoe UI Variable, Segoe UI"); FontSize = 15;
        VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
    }

    public void Render(Template? template)
    {
        var paragraph = new Paragraph { Margin = new Thickness(0), LineHeight = 26 };
        if (template != null)
            foreach (var segment in VariableSyntax.Segments(template.Body, template))
            {
                var run = new Run(segment.Text);
                if (segment.Key != null)
                {
                    run.ToolTip = segment.Key;
                    run.Background = new SolidColorBrush(segment.Missing ? Color.FromRgb(250, 224, 179) : Color.FromRgb(220, 234, 221));
                    run.Foreground = new SolidColorBrush(segment.Missing ? Color.FromRgb(117, 70, 15) : Color.FromRgb(36, 75, 52));
                }
                paragraph.Inlines.Add(run);
            }
        Document = new FlowDocument(paragraph) { PagePadding = new Thickness(0), FontFamily = FontFamily, FontSize = FontSize };
    }
}
