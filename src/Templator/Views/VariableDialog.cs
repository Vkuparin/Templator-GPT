using System.Windows;
using System.Windows.Controls;
using Templator.Core;
using Templator.Services;

namespace Templator.Views;

/// <summary>A human name is the only thing needed to create a reusable field.</summary>
public sealed class VariableDialog : Window
{
    public TextBox NameInput { get; } = new() { Tag = "e.g. Customer name", MaxLength = 80 };
    public TextBox ExampleInput { get; } = new() { Tag = "e.g. Nordic Components Oy" };
    public CheckBox RequiredInput { get; } = new() { Content = "Must be filled before opening a draft", IsChecked = true };
    public string VariableName => NameInput.Text.Trim();
    public string Example => ExampleInput.Text;
    public bool Required => RequiredInput.IsChecked == true;
    public bool DeleteRequested { get; private set; }

    public VariableDialog(Window owner, Template template, Variable? variable = null)
    {
        Owner = owner; Style = (Style)FindResource(typeof(Window));
        Title = variable == null ? "New variable" : "Edit variable";
        Width = 460; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
        WindowTheme.Apply(this);
        var panel = new StackPanel { Margin = new Thickness(28) };
        panel.Children.Add(new TextBlock { Text = Title, FontSize = 24, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = "Give it a name, then drag it into your email. One value fills every use of the same variable.", Margin = new Thickness(0, 10, 0, 24), Style = (Style)FindResource("Caption") });
        panel.Children.Add(new TextBlock { Text = "Name", Style = (Style)FindResource("Caption") });
        panel.Children.Add(NameInput);
        panel.Children.Add(new TextBlock { Text = "Example hint (optional)", Style = (Style)FindResource("Caption"), Margin = new Thickness(0, 18, 0, 8) });
        panel.Children.Add(ExampleInput);
        RequiredInput.Margin = new Thickness(0, 18, 0, 18); panel.Children.Add(RequiredInput);
        var error = new TextBlock { Foreground = (System.Windows.Media.Brush)FindResource("Warning"), Margin = new Thickness(0, 0, 0, 12) };
        panel.Children.Add(error);
        var actions = new DockPanel();
        var save = new Button { Content = variable == null ? "Create variable" : "Save changes", IsDefault = true, Style = (Style)FindResource("Primary") };
        DockPanel.SetDock(save, Dock.Right); actions.Children.Add(save);
        var cancel = new Button { Content = "Cancel", IsCancel = true, Style = (Style)FindResource("Quiet"), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 12, 0) };
        DockPanel.SetDock(cancel, Dock.Right); actions.Children.Add(cancel);
        if (variable != null)
        {
            NameInput.Text = variable.Label; ExampleInput.Text = variable.Example; RequiredInput.IsChecked = variable.Required;
            var delete = new Button { Content = "Delete…", Style = (Style)FindResource("Quiet"), HorizontalAlignment = HorizontalAlignment.Left };
            delete.Click += (_, _) => { DeleteRequested = true; DialogResult = true; }; actions.Children.Add(delete);
        }
        panel.Children.Add(actions); Content = panel;
        save.Click += (_, _) =>
        {
            if (VariableName.Length == 0) { error.Text = "Give the variable a name first."; NameInput.Focus(); return; }
            if (template.Variables.Any(v => v != variable && v.Label.Equals(VariableName, StringComparison.OrdinalIgnoreCase)))
            { error.Text = "That name is already in use. Choose another name."; return; }
            DialogResult = true;
        };
        Loaded += (_, _) => { NameInput.Focus(); NameInput.SelectAll(); };
    }
}

public sealed class VariableValueDialog : Window
{
    public TextBox ValueInput { get; } = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 80, MaxHeight = 220, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    public string Value => ValueInput.Text;
    public VariableValueDialog(Window owner, Variable variable)
    {
        Owner = owner; Style = (Style)FindResource(typeof(Window)); Title = variable.Label;
        Width = 440; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
        WindowTheme.Apply(this);
        var panel = new StackPanel { Margin = new Thickness(28) };
        panel.Children.Add(new TextBlock { Text = variable.Label, FontSize = 23, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = "Updates everywhere this variable appears in your email.", Style = (Style)FindResource("Caption"), Margin = new Thickness(0, 10, 0, 20) });
        ValueInput.Text = variable.Value; ValueInput.Tag = variable.Example;
        panel.Children.Add(ValueInput);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        actions.Children.Add(new Button { Content = "Cancel", IsCancel = true, Style = (Style)FindResource("Quiet"), Margin = new Thickness(0, 0, 10, 0) });
        var save = new Button { Content = "Use value", Style = (Style)FindResource("Primary") };
        save.Click += (_, _) => DialogResult = true; actions.Children.Add(save); panel.Children.Add(actions); Content = panel;
        Loaded += (_, _) => { ValueInput.Focus(); ValueInput.SelectAll(); };
    }
}
