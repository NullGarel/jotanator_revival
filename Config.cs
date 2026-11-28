// VIBE CODED DSICLAIMER - CLAUDE GENERATED AS OF 26-11-28 @ 19:27 UTC
namespace Jotanator;

/// <summary>Settings from the environment / .env file (port of config.py).</summary>
public static class Config
{
    public static readonly string Token;
    public static readonly string SerpApiKey;
    public static readonly bool SyncOnStart;

    // Economy (Jotacoin, J$)
    public static readonly string DbPath;          // SQLite file with balances, stats and history
    public static readonly long StartBalance;      // what a brand-new account starts with
    public static readonly int PayFeePercent;      // burned on every j.pay (anti alt-farming, and a money sink)

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
        DbPath = Environment.GetEnvironmentVariable("DB_PATH") is { Length: > 0 } db ? db : "jotanator.db";
        StartBalance = long.TryParse(Environment.GetEnvironmentVariable("START_BALANCE"), out var sb) && sb >= 0 ? sb : 100;
        PayFeePercent = int.TryParse(Environment.GetEnvironmentVariable("PAY_FEE_PERCENT"), out var fee) && fee is >= 0 and <= 50 ? fee : 5;
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
