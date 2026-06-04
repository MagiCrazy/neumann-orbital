using System.Text.RegularExpressions;

namespace NeumannOrbital;

public record AppConfig(string BaseUrl, string ApiKey)
{
    // Prefer shared config; fall back to neumann-cockpit for zero-migration compat.
    private static readonly string[] CandidatePaths =
    [
        XdgConfig("neumann", "config.toml"),
        XdgConfig("neumann-cockpit", "config.toml"),
    ];

    internal static string XdgConfig(params string[] segments) =>
        Path.Combine(
            [
                Environment.GetEnvironmentVariable("XDG_CONFIG_HOME")
                    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"),
                .. segments
            ]
        );

    public static AppConfig Load()
    {
        var configPath = Array.Find(CandidatePaths, File.Exists)
            ?? throw new FileNotFoundException(
                $"Config not found. Create one of:\n" +
                string.Join("\n", CandidatePaths.Select(p => $"  {p}")) +
                $"\n\nContent:\n  base_url = \"https://neumann-probe.net\"\n  api_key  = \"vng_...\""
            );

        var values = ParseToml(File.ReadAllText(configPath));

        if (!values.TryGetValue("base_url", out var baseUrl))
            throw new InvalidDataException("Missing 'base_url' in config.toml");
        if (!values.TryGetValue("api_key", out var apiKey))
            throw new InvalidDataException("Missing 'api_key' in config.toml");

        return new AppConfig(baseUrl, apiKey);
    }

    // Minimal key = "value" parser — sufficient for a flat config with string values.
    private static Dictionary<string, string> ParseToml(string content)
    {
        var result = new Dictionary<string, string>();
        var pattern = new Regex(@"^\s*(\w+)\s*=\s*""(.*?)""\s*$", RegexOptions.Multiline);
        foreach (Match m in pattern.Matches(content))
            result[m.Groups[1].Value] = m.Groups[2].Value;
        return result;
    }
}
