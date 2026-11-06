using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using HWZ.TaskManager.Helpers;
using HWZ.TaskManager.Services;

namespace HWZ.TaskManager.Dialogs;

public sealed record DialogDetail(string Title, string Value);

public partial class ModernDialog : Window
{
    private enum DialogKind
    {
        Message,
        Confirm,
        Prompt,
        Details
    }

    private DialogKind _kind = DialogKind.Message;

    private ModernDialog(Window? owner)
    {
        InitializeComponent();
        Owner = owner;
        WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;

        PreviewKeyDown += OnPreviewKeyDown;
        InputBox.TextChanged += OnInputChanged;
        InputBox.PreviewKeyDown += OnInputPreviewKeyDown;
        PrimaryButton.IsDefault = true;
        SecondaryButton.IsCancel = true;
        Loaded += OnLoaded;
    }

    public static bool ShowConfirm(
        Window? owner,
        string title,
        string message,
        string primaryText = "继续",
        bool destructive = false)
    {
        var dialog = new ModernDialog(owner)
        {
            _kind = DialogKind.Confirm
        };

        dialog.TitleText.Text = title;
        dialog.MessageText.Text = message;
        dialog.PrimaryButton.Content = primaryText;
        dialog.SecondaryButton.Content = "取消";
        dialog.SecondaryButton.Visibility = Visibility.Visible;
        dialog.PrimaryButton.Style = (Style)dialog.FindResource(destructive ? "DangerButtonStyle" : "PrimaryButtonStyle");
        dialog.ConfigureIcon(destructive);
        return dialog.ShowDialog() == true;
    }

    public static void ShowMessage(Window? owner, string title, string message)
    {
        var dialog = new ModernDialog(owner)
        {
            _kind = DialogKind.Message
        };

        dialog.TitleText.Text = title;
        dialog.MessageText.Text = message;
        dialog.PrimaryButton.Content = "知道了";
        dialog.SecondaryButton.Visibility = Visibility.Collapsed;
        dialog.ConfigureIcon(false);
        dialog.ShowDialog();
    }

    public static PromptResult? ShowPrompt(
        Window? owner,
        string title,
        string message,
        string initialText = "",
        bool allowRunAsAdmin = false)
    {
        var dialog = new ModernDialog(owner)
        {
            _kind = DialogKind.Prompt
        };

        dialog.TitleText.Text = title;
        dialog.MessageText.Text = message;
        dialog.PromptPanel.Visibility = Visibility.Visible;
        dialog.AdminPanel.Visibility = allowRunAsAdmin ? Visibility.Visible : Visibility.Collapsed;
        dialog.PrimaryButton.Content = "运行";
        dialog.SecondaryButton.Content = "取消";
        dialog.InputBox.Text = initialText;
        dialog.ConfigureIcon(false, terminalIcon: true);

        if (dialog.ShowDialog() != true)
        {
            return null;
        }

        return new PromptResult(
            dialog.InputBox.Text.Trim(),
            dialog.AdminPanel.Visibility == Visibility.Visible && dialog.RunAsAdminCheck.IsChecked == true);
    }

    public static void ShowDetails(
        Window? owner,
        string title,
        string subtitle,
        string? message,
        IReadOnlyList<DialogDetail> details)
    {
        var dialog = new ModernDialog(owner)
        {
            _kind = DialogKind.Details
        };

        dialog.TitleText.Text = title;
        dialog.MessageText.Text = string.IsNullOrWhiteSpace(message) ? subtitle : $"{subtitle}\n{message}";
        dialog.MessageText.Visibility = string.IsNullOrWhiteSpace(dialog.MessageText.Text)
            ? Visibility.Collapsed
            : Visibility.Visible;
        dialog.DetailsPanel.Visibility = Visibility.Visible;
        dialog.DetailsItems.ItemsSource = details;
        dialog.PrimaryButton.Content = "知道了";
        dialog.SecondaryButton.Visibility = Visibility.Collapsed;
        dialog.ConfigureIcon(false);
        dialog.ShowDialog();
    }

    private void ConfigureIcon(bool destructive, bool terminalIcon = false)
    {
        var backgroundKey = destructive ? "DangerSoftBrush" : "AccentSoftBrush";
        var accentKey = destructive ? "DangerBrush" : "AccentBrush";

        IconBackground.Background = (Brush)FindResource(backgroundKey);
        DialogIcon.Stroke = (Brush)FindResource(accentKey);
        DialogIcon.Width = terminalIcon ? 23 : 20;
        DialogIcon.Height = terminalIcon ? 23 : 20;

        if (destructive)
        {
            DialogIcon.Data = Geometry.Parse("M12,3 L22,21 H2 Z M12,9 V14 M12,17 V18");
        }
        else if (terminalIcon)
        {
            DialogIcon.Data = Geometry.Parse("M4,6 L10,12 L4,18 M12,18 H20");
        }
        else
        {
            DialogIcon.Data = Geometry.Parse("M12,3 A9,9 0 1 1 12,21 A9,9 0 1 1 12,3 M12,8 V13 M12,16.6 V17");
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        DwmHelper.Apply(this);
        if (_kind == DialogKind.Prompt)
        {
            InputBox.Focus();
            InputBox.CaretIndex = InputBox.Text.Length;
            PrimaryButton.IsEnabled = !string.IsNullOrWhiteSpace(InputBox.Text);
        }
        else
        {
            PrimaryButton.Focus();
        }
    }

    private void OnInputChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_kind == DialogKind.Prompt)
        {
            PrimaryButton.IsEnabled = !string.IsNullOrWhiteSpace(InputBox.Text);
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key;

        if (key == Key.Escape)
        {
            Close();
            e.Handled = true;
            return;
        }

        if (key == Key.Enter && _kind == DialogKind.Prompt && PrimaryButton.IsEnabled)
        {
            PrimaryButton_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    private void OnInputPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key;
        if (key == Key.Enter && _kind == DialogKind.Prompt && PrimaryButton.IsEnabled)
        {
            PrimaryButton_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    private void PrimaryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_kind == DialogKind.Prompt && string.IsNullOrWhiteSpace(InputBox.Text))
        {
            return;
        }

        DialogResult = true;
    }

    private void SecondaryButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void HeaderArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            try
            {
                DragMove();
            }
            catch (InvalidOperationException)
            {
                // DragMove can throw when the mouse has already been released.
            }
        }
    }
}
