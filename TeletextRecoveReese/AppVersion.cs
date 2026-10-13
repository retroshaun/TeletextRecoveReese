using System.Reflection;

namespace TeletextRecoveReese;

/// <summary>
/// Application identity backed by the version metadata in TeletextRecoveReese.csproj.
/// Change the Version property there; all UI version labels read the same value here.
/// </summary>
internal static class AppVersion
{
    public const string ProductName = "TeletextRecoveReese";

    public static string InformationalVersion { get; } =
        Assembly.GetEntryAssembly()?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion
        ?? "unknown";

    public static string DisplayVersion => InformationalVersion
        .Split('+', 2)[0]
        .Replace('-', ' ');

    public static string DisplayName => $"{ProductName} {DisplayVersion}";

    public static string AboutVersion
    {
        get
        {
            string[] parts = InformationalVersion.Split('+', 2);
            string revision = parts.Length > 1 ? parts[1] : "unknown";
            string shortRevision = revision[..System.Math.Min(7, revision.Length)];
            return $"{DisplayVersion} ({shortRevision})";
        }
    }
}
