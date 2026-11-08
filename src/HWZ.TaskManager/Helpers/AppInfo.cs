using System.Reflection;

namespace HWZ.TaskManager.Helpers;

public static class AppInfo
{
    public const string RepositoryUrl = "https://github.com/HWZ-ch/HWZ_Windows_TaskManager";
    public const string ReleasesUrl = RepositoryUrl + "/releases";

    public static Version Version { get; } = ResolveVersion();

    public static string VersionText => Version.ToString(3);

    public static Version NormalizeVersion(Version version)
        => new(version.Major, Math.Max(0, version.Minor), Math.Max(0, version.Build));

    private static Version ResolveVersion()
    {
        var assembly = typeof(AppInfo).Assembly;
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        // The SDK appends the source revision after '+'. Compare only the semantic version.
        var semanticPart = informational?.Split('+', 2)[0].Trim();
        if (Version.TryParse(semanticPart, out var parsed))
        {
            return NormalizeVersion(parsed);
        }

        return NormalizeVersion(assembly.GetName().Version ?? new Version(1, 0, 0, 0));
    }
}
