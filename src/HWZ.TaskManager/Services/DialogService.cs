using System.Windows;
using HWZ.TaskManager.Dialogs;

namespace HWZ.TaskManager.Services;

public sealed class DialogService
{
    public DialogService(Window owner)
    {
        Owner = owner;
    }

    public Window Owner { get; }

    public bool Confirm(string title, string message, string primaryText = "继续", bool destructive = false)
        => ModernDialog.ShowConfirm(Owner, title, message, primaryText, destructive);

    public PromptResult? Prompt(string title, string message, string initialText = "", bool allowRunAsAdmin = false)
        => ModernDialog.ShowPrompt(Owner, title, message, initialText, allowRunAsAdmin);

    public void ShowMessage(string title, string message)
        => ModernDialog.ShowMessage(Owner, title, message);

    public void ShowDetails(string title, string subtitle, string? message, IReadOnlyList<DialogDetail> details)
        => ModernDialog.ShowDetails(Owner, title, subtitle, message, details);
}

public sealed record PromptResult(string Text, bool RunAsAdministrator);
