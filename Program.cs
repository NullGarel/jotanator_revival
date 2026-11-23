// VIBE CODED DSICLAIMER - CLAUDE GENERATED AS OF 26-11-20 @ 22:42 UTC
using Jotanator;

if (string.IsNullOrEmpty(Config.Token))
{
    Console.Error.WriteLine("DISCORD_BOT_TOKEN is not set (check your .env).");
    return 1;
}

await using var bot = new JotaBot();
return await bot.RunAsync(Config.Token);
