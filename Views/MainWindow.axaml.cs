using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Mynote.Services;

namespace MyAvaloniaApp.Views;

public partial class MainWindow : Window
{
    private Point? _dragStartPoint;
    private object? _dragItem;
    private bool _paletteOpen;

    public MainWindow()
    {
        InitializeComponent();
        Closing += MainWindow_Closing;
    }

    private async void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        if ((e.KeyModifiers & KeyModifiers.Control) != 0 && e.Key == Key.P)
        {
            if (DataContext is not ViewModels.MainViewModel main || _paletteOpen)
            {
                return;
            }

            _paletteOpen = true;
            try
            {
                var palette = new CommandPaletteWindow
                {
                    DataContext = new ViewModels.CommandPaletteViewModel(main),
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                };
                await palette.ShowDialog(this);
            }
            finally
            {
                _paletteOpen = false;
            }

            e.Handled = true;
            return;
        }

        if ((e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Shift)) == (KeyModifiers.Control | KeyModifiers.Shift) &&
            e.Key == Key.F)
        {
            SearchBox?.Focus();
            if (SearchBox is not null)
            {
                var len = SearchBox.Text?.Length ?? 0;
                SearchBox.SelectionStart = 0;
                SearchBox.SelectionEnd = len;
            }

            e.Handled = true;
            return;
        }

        if ((e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Shift)) == (KeyModifiers.Control | KeyModifiers.Shift) &&
            e.Key == Key.N)
        {
            await CreateFolderFromPromptAsync();
            e.Handled = true;
            return;
        }

        if ((e.KeyModifiers & KeyModifiers.Control) != 0 && e.Key == Key.K)
        {
            InsertWikiLinkAtSelection();
            e.Handled = true;
            return;
        }
    }

    private void SwitchProject_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            return;
        }

        var picker = new ProjectPickerWindow(new AppSettingsStore())
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen
        };

        desktop.MainWindow = picker;
        picker.Show();
        Close();
    }

    private void Editor_LostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not ViewModels.MainViewModel vm)
        {
            return;
        }

        if (!vm.SaveOnBlur)
        {
            return;
        }

        if (vm.SaveSelectedNoteCommand.CanExecute(null))
        {
            vm.SaveSelectedNoteCommand.Execute(null);
        }
    }

    private void MainWindow_Closing(object? sender, WindowClosingEventArgs e)
    {
        if (DataContext is not ViewModels.MainViewModel vm)
        {
            return;
        }

        if (!vm.SaveOnClose)
        {
            return;
        }

        if (vm.SaveSelectedNoteCommand.CanExecute(null))
        {
            vm.SaveSelectedNoteCommand.Execute(null);
        }
    }

    private void NewNote_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not ViewModels.MainViewModel vm)
        {
            return;
        }

        if (vm.AddNoteCommand.CanExecute(null))
        {
            vm.AddNoteCommand.Execute(null);
        }
    }

    private async void NewFolder_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        await CreateFolderFromPromptAsync();
    }

    private async Task CreateFolderFromPromptAsync()
    {
        if (DataContext is not ViewModels.MainViewModel vm)
        {
            return;
        }

        var name = await PromptTextAsync("New folder", "Folder name");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        vm.NewFolderName = name.Trim();
        if (vm.AddFolderCommand.CanExecute(null))
        {
            vm.AddFolderCommand.Execute(null);
        }
    }

    private void InsertWikiLinkAtSelection()
    {
        if (ContentBox is null)
        {
            return;
        }

        var text = ContentBox.Text ?? string.Empty;
        var start = ContentBox.SelectionStart;
        var end = ContentBox.SelectionEnd;
        if (end < start)
        {
            (start, end) = (end, start);
        }

        if (start < 0 || start > text.Length || end < 0 || end > text.Length)
        {
            return;
        }

        if (end > start)
        {
            var selected = text.Substring(start, end - start);
            ContentBox.Text = text.Substring(0, start) + "[[" + selected + "]]" + text.Substring(end);
            ContentBox.SelectionStart = start + 2;
            ContentBox.SelectionEnd = start + 2 + selected.Length;
            return;
        }

        ContentBox.Text = text.Substring(0, start) + "[[]]" + text.Substring(start);
        ContentBox.SelectionStart = start + 2;
        ContentBox.SelectionEnd = start + 2;
    }

    private void DragSource_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed == false)
        {
            return;
        }

        if (sender is not Control control)
        {
            return;
        }

        _dragItem = control.DataContext;
        _dragStartPoint = e.GetPosition(this);
    }

    private async void DragSource_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragStartPoint is null || _dragItem is null)
        {
            return;
        }

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _dragStartPoint = null;
            _dragItem = null;
            return;
        }

        var pos = e.GetPosition(this);
        var delta = pos - _dragStartPoint.Value;
        if (Math.Abs(delta.X) < 8 && Math.Abs(delta.Y) < 8)
        {
            return;
        }

        var data = new DataObject();
        if (_dragItem is ViewModels.NoteViewModel note)
        {
            data.Set("mynote/note-id", note.Id.ToString("D"));
        }
        else if (_dragItem is ViewModels.KanbanCardViewModel card)
        {
            data.Set("mynote/card-id", card.Id.ToString("D"));
        }
        else
        {
            _dragStartPoint = null;
            _dragItem = null;
            return;
        }

        await DragDrop.DoDragDrop(e, data, DragDropEffects.Move);
        _dragStartPoint = null;
        _dragItem = null;
    }

    private void Folder_DragOver(object? sender, DragEventArgs e)
    {
        if (!e.Data.Contains("mynote/note-id"))
        {
            e.DragEffects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.DragEffects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void Folder_Drop(object? sender, DragEventArgs e)
    {
        if (DataContext is not ViewModels.MainViewModel vm)
        {
            return;
        }

        if (sender is not Control control || control.DataContext is not ViewModels.FolderFilterItem filter)
        {
            return;
        }

        if (!e.Data.Contains("mynote/note-id"))
        {
            return;
        }

        var raw = e.Data.Get("mynote/note-id") as string;
        if (!Guid.TryParse(raw, out var noteId))
        {
            return;
        }

        Guid? folderId = null;
        if (filter.IsUnfiled)
        {
            folderId = null;
        }
        else if (filter.FolderId is Guid id)
        {
            folderId = id;
        }
        else
        {
            // "All Notes" drop: keep current folder assignment.
            return;
        }

        vm.MoveNoteToFolder(noteId, folderId);
    }

    private void KanbanColumn_DragOver(object? sender, DragEventArgs e)
    {
        if (!e.Data.Contains("mynote/card-id"))
        {
            e.DragEffects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.DragEffects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void KanbanColumn_Drop(object? sender, DragEventArgs e)
    {
        if (DataContext is not ViewModels.MainViewModel vm)
        {
            return;
        }

        if (sender is not Control control || control.DataContext is not ViewModels.ColumnViewModel column)
        {
            return;
        }

        if (!e.Data.Contains("mynote/card-id"))
        {
            return;
        }

        var raw = e.Data.Get("mynote/card-id") as string;
        if (!Guid.TryParse(raw, out var cardId))
        {
            return;
        }

        vm.MoveCardToColumn(cardId, column.Id);
    }

    private async Task<string?> PromptTextAsync(string title, string watermark)
    {
        var appBg = ResolveBrush("AppBg");
        var surfaceBg = ResolveBrush("SurfaceBg");
        var border = ResolveBrush("Border");
        var borderSoft = ResolveBrush("BorderSoft");
        var textPrimary = ResolveBrush("TextPrimary");
        var textSecondary = ResolveBrush("TextSecondary");

        var dialog = new Window
        {
            Title = title,
            Width = 520,
            Height = 240,
            Background = appBg,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            TransparencyLevelHint = new[] { WindowTransparencyLevel.None },
        };

        var input = new TextBox
        {
            Watermark = watermark,
            Padding = new Thickness(10),
            CornerRadius = new CornerRadius(10),
            BorderBrush = borderSoft,
            BorderThickness = new Thickness(1),
            Background = surfaceBg,
            Foreground = textPrimary,
        };

        string? result = null;

        var ok = new Button
        {
            Content = "Create",
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Padding = new Thickness(12, 8),
            Background = textPrimary,
            Foreground = surfaceBg,
            BorderThickness = new Thickness(0),
        };
        ok.Click += (_, __) =>
        {
            result = input.Text ?? string.Empty;
            dialog.Close();
        };

        var cancel = new Button
        {
            Content = "Cancel",
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Padding = new Thickness(12, 8),
            Background = surfaceBg,
            BorderBrush = borderSoft,
            BorderThickness = new Thickness(1),
        };
        cancel.Click += (_, __) =>
        {
            result = null;
            dialog.Close();
        };

        dialog.Content = new Border
        {
            Background = surfaceBg,
            BorderBrush = border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(16),
            Margin = new Thickness(12),
            Child = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = "Create a new folder.", Foreground = textSecondary },
                    input,
                    new StackPanel
                    {
                        Orientation = Avalonia.Layout.Orientation.Horizontal,
                        Spacing = 10,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                        Children = { cancel, ok }
                    }
                }
            }
        };

        await dialog.ShowDialog(this);
        return result;
    }

    private static IBrush ResolveBrush(string key)
    {
        if (Application.Current is { } app &&
            app.TryGetResource(key, app.RequestedThemeVariant, out var value) &&
            value is IBrush brush)
        {
            return brush;
        }

        return key switch
        {
            "AppBg" => new SolidColorBrush(ResolveFallbackColor("#f5f6f8", "#0f1116")),
            "SurfaceBg" => new SolidColorBrush(ResolveFallbackColor("#ffffff", "#161a22")),
            "Border" => new SolidColorBrush(ResolveFallbackColor("#dfe2e8", "#2b3242")),
            "BorderSoft" => new SolidColorBrush(ResolveFallbackColor("#e1e2e5", "#2b3242")),
            "TextPrimary" => new SolidColorBrush(ResolveFallbackColor("#1f232b", "#e8ecf4")),
            "TextSecondary" => new SolidColorBrush(ResolveFallbackColor("#6f7680", "#a6afbf")),
            _ => Brushes.Transparent
        };
    }

    private static Color ResolveFallbackColor(string lightHex, string darkHex)
    {
        var isDark = Application.Current?.RequestedThemeVariant == ThemeVariant.Dark;
        return Color.Parse(isDark ? darkHex : lightHex);
    }
}
