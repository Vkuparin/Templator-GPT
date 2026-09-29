using System.Windows;
using System.Windows.Controls;

namespace Templator.Views;

public sealed record DialogChoice(string Id, string Title, string Detail, bool Enabled = true);

public sealed class ChoiceDialog : Window
{
    public string? Choice { get; private set; }
    public ChoiceDialog(Window owner, string title, string description, params DialogChoice[] choices)
    {
        Style = (Style)FindResource(typeof(Window));
        Owner = owner; Title = title; Width = 500; SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Templator.Services.WindowTheme.Apply(this);
        var root = new StackPanel { Margin = new Thickness(28) };
        root.Children.Add(new TextBlock { Text = title, FontSize = 23, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) });
        root.Children.Add(new TextBlock { Text = description, Foreground = (System.Windows.Media.Brush)FindResource("Muted"), Margin = new Thickness(0, 0, 0, 24) });
        foreach (var option in choices)
        {
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = option.Title, FontWeight = FontWeights.SemiBold });
            text.Children.Add(new TextBlock { Text = option.Detail, FontSize = 12, Margin = new Thickness(0, 6, 0, 0), MaxWidth = 370 });
            var button = new Button { Tag = option.Id, Content = text, Margin = new Thickness(0, 0, 0, 10), Padding = new Thickness(16), IsEnabled = option.Enabled };
            button.Click += (_, _) => { Choice = option.Id; DialogResult = true; };
            root.Children.Add(button);
        }
        var cancel = new Button { Content = "Cancel", IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right, Style = (Style)FindResource("Quiet") };
        root.Children.Add(cancel); Content = root;
    }
}
