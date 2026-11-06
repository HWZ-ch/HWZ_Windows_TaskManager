using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media;
using HWZ.TaskManager.Helpers;
using HWZ.TaskManager.Models;
using HWZ.TaskManager.Services;
using HWZ.TaskManager.ViewModels;

namespace HWZ.TaskManager;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(SettingsService settingsService, AppSettings settings)
    {
        InitializeComponent();

        Width = Math.Max(MinWidth, Math.Min(1380, SystemParameters.WorkArea.Width - 64));
        Height = Math.Max(MinHeight, Math.Min(880, SystemParameters.WorkArea.Height - 64));

        _viewModel = new MainViewModel(settingsService, settings, new DialogService(this));
        DataContext = _viewModel;

        SourceInitialized += (_, _) => DwmHelper.Apply(this);
        Loaded += OnLoaded;
        StateChanged += OnStateChanged;
        PreviewKeyDown += OnPreviewKeyDown;
        ThemeManager.ThemeChanged += OnThemeChanged;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F5)
        {
            if (_viewModel.RefreshCommand.CanExecute(null))
            {
                _viewModel.RefreshCommand.Execute(null);
            }

            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers != ModifierKeys.Control)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.D1:
            case Key.NumPad1:
                _viewModel.CurrentPage = "overview";
                e.Handled = true;
                break;
            case Key.D2:
            case Key.NumPad2:
                _viewModel.CurrentPage = "processes";
                e.Handled = true;
                break;
            case Key.D3:
            case Key.NumPad3:
                _viewModel.CurrentPage = "performance";
                e.Handled = true;
                break;
            case Key.D4:
            case Key.NumPad4:
                _viewModel.CurrentPage = "startup";
                e.Handled = true;
                break;
            case Key.D5:
            case Key.NumPad5:
                _viewModel.CurrentPage = "services";
                e.Handled = true;
                break;
            case Key.D6:
            case Key.NumPad6:
                _viewModel.CurrentPage = "settings";
                e.Handled = true;
                break;
            case Key.R:
                if (_viewModel.RefreshCommand.CanExecute(null))
                {
                    _viewModel.RefreshCommand.Execute(null);
                }

                e.Handled = true;
                break;
            case Key.P:
                _viewModel.TogglePauseCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.T:
                _viewModel.ToggleThemeCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        DwmHelper.Apply(this);
        UpdateMaximizeGlyph();
        await _viewModel.InitializeAsync();
    }

    protected override void OnClosed(EventArgs e)
    {
        ThemeManager.ThemeChanged -= OnThemeChanged;
        _viewModel.Shutdown();
        base.OnClosed(e);
    }

    private void OnThemeChanged(bool dark)
    {
        Dispatcher.BeginInvoke(() => DwmHelper.Apply(this));
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        UpdateMaximizeGlyph();
    }

    private void UpdateMaximizeGlyph()
    {
        var resourceKey = WindowState == WindowState.Maximized ? "IconRestore" : "IconMaximize";
        if (FindResource(resourceKey) is Geometry geometry)
        {
            MaximizeIcon.Data = geometry;
        }

        var label = WindowState == WindowState.Maximized ? "还原" : "最大化";
        MaximizeButton.ToolTip = label;
        AutomationProperties.SetName(MaximizeButton, label);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        if (WindowState == WindowState.Normal)
        {
            try
            {
                DragMove();
            }
            catch (InvalidOperationException)
            {
                // DragMove can throw if the mouse button was released mid-drag.
            }
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleMaximize();
    }

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
