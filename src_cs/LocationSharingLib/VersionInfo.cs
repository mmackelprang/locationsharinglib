using System.IO;

namespace LocationSharingLib;

public static class VersionInfo
{
    public static string Version { get; } = LoadVersion();

    private static string LoadVersion()
    {
        try
        {
            // Attempt to read root .VERSION similar to Python implementation.
            var root = Directory.GetCurrentDirectory();
            var candidate = Path.Combine(root, ".VERSION");
            if (File.Exists(candidate)) return File.ReadAllText(candidate).Trim();
        }
        catch { }
        return "5.0.3"; // fallback
    }
}
