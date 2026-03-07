using Avalonia.Controls;
using Avalonia.Input;
using MyAvaloniaApp.ViewModels;

namespace MyAvaloniaApp.Views;

public partial class CommandPaletteWindow : Window
{
    public CommandPaletteWindow()
    {
        InitializeComponent();

        Opened += (_, _) =>
        {
            QueryBox.Focus();
            QueryBox.SelectAll();
        };
    }

    private CommandPaletteViewModel Vm => (CommandPaletteViewModel)DataContext!;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            if (Vm.SelectedItem?.ExecuteOnEnter == true)
            {
                Vm.ExecuteSelected();
                Close();
            }
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Down)
        {
            ResultsList.SelectedIndex = Math.Min(ResultsList.ItemCount - 1, ResultsList.SelectedIndex + 1);
            if (ResultsList.SelectedItem is not null)
            {
                ResultsList.ScrollIntoView(ResultsList.SelectedItem);
            }
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Up)
        {
            ResultsList.SelectedIndex = Math.Max(0, ResultsList.SelectedIndex - 1);
            if (ResultsList.SelectedItem is not null)
            {
                ResultsList.ScrollIntoView(ResultsList.SelectedItem);
            }
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    private void ResultsList_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (Vm.SelectedItem is null)
        {
            return;
        }

        Vm.ExecuteSelected();
        Close();
        e.Handled = true;
    }
}
