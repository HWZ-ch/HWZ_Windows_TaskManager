namespace HWZ.TaskManager.Models;

public sealed record NavItem(string Key, string Label, string IconData);

public sealed record ThemeOption(string Value, string Label, string Description);

public sealed record RefreshOption(int Value, string Label);
