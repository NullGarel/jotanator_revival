// VIBE CODED DSICLAIMER - CLAUDE GENERATED AS OF 26-11-20 @ 22:42 UTC
namespace Jotanator;

/// <summary>Settings from the environment / .env file (port of config.py).</summary>
public static class Config
{
    public static readonly string Token;
    public static readonly string SerpApiKey;
    public static readonly bool SyncOnStart;

    public const string Prefix = "j.";
    // Both cases accepted; "@Bot command" also works (added in JotaBot).
    public static readonly string[] Prefixes = { "j.", "J." };

    // Image search
    public const int TimeoutSeconds = 8;        // per provider
    public const int MaxResults = 30;           // images kept per search
    public const int ViewTimeoutSeconds = 120;  // seconds before buttons disable themselves

    static Config()
    {
        LoadDotEnv();
        Token = Environment.GetEnvironmentVariable("DISCORD_BOT_TOKEN") ?? "";
        SerpApiKey = Environment.GetEnvironmentVariable("SERP_API_KEY") ?? "";
        SyncOnStart = (Environment.GetEnvironmentVariable("SYNC_ON_START") ?? "1") == "1";
    }

    /// <summary>Tiny .env loader: finds the nearest .env walking upwards; never overrides real env vars.</summary>
    private static void LoadDotEnv()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
            {
                var path = Path.Combine(dir.FullName, ".env");
                if (!File.Exists(path)) continue;
                foreach (var raw in File.ReadAllLines(path))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith('#')) continue;
                    if (line.StartsWith("export ", StringComparison.Ordinal)) line = line[7..].TrimStart();
                    var eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    var key = line[..eq].Trim();
                    var value = line[(eq + 1)..].Trim();
                    if (value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[^1] == value[0])
                        value = value[1..^1];
                    else
                    {
                        var hash = value.IndexOf(" #", StringComparison.Ordinal);
                        if (hash >= 0) value = value[..hash].TrimEnd();
                    }
                    if (Environment.GetEnvironmentVariable(key) == null)
                        Environment.SetEnvironmentVariable(key, value);
                }
                return;
            }
        }
    }
}
