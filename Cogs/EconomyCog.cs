// VIBE CODED DSICLAIMER - CLAUDE GENERATED AS OF 26-11-28 @ 19:27 UTC
using Discord;

namespace Jotanator;

/// <summary>Jotacoin (J$): balance, daily, pay, leaderboard, stats, shop and the inflation report.</summary>
public sealed class EconomyCog : Cog
{
    private static readonly Dictionary<string, string> GameNames = new()
    {
        ["ttt"] = "Tic Tac Toe",
        ["c4"] = "Connect Four",
        ["mines"] = "Minesweeper",
        ["hang"] = "Hangman",
        ["bj"] = "Blackjack",
        ["craps"] = "Craps",
        ["horse"] = "Horse Race",
        ["fillet"] = "Five Finger Fillet",
    };

    private CancellationTokenSource? _loop;

    public override string Category => "Economy";

    public override IEnumerable<CommandDef> BuildCommands()
    {
        yield return new CommandDef
        {
            Name = "balance",
            Aliases = ["bal", "wallet"],
            Description = "Show your Jotacoin (J$) balance, or someone else's",
            Help = "Shows a balance with its rank, badges and title. Games and `daily` are the ways to earn J$.",
            Params = [new ParamDef("user", "Whose balance to show (default: you)", ParamKind.User, Required: false)],
            Handler = BalanceAsync,
        };

        yield return new CommandDef
        {
            Name = "daily",
            Description = "Claim your daily J$ (a random 32 to 512)",
            Help = "Once per UTC day you can claim a random amount between J$32 and J$512. A Lucky Charm doubles the next one.",
            Handler = DailyAsync,
        };

        yield return new CommandDef
        {
            Name = "pay",
            Description = "Send some of your J$ to another member",
            Help = $"Send J$ to someone. A {Config.PayFeePercent}% fee is burned on every payment (it fights alt-account farming and keeps the economy honest).\n" +
                   "Example: `j.pay @friend 100`",
            Params =
            [
                new ParamDef("user", "Who gets the J$", ParamKind.User),
                new ParamDef("amount", "How much J$ to send", ParamKind.Int, Min: 1, Max: 1_000_000_000),
            ],
            Cooldowns = [new CooldownSpec(1, 3, BucketType.User)],
            Handler = PayAsync,
        };

        yield return new CommandDef
        {
            Name = "top",
            Aliases = ["leaderboard", "lb"],
            Description = "The richest players",
            Help = "The top 10 balances across every server the bot is in.",
            Handler = TopAsync,
        };

        yield return new CommandDef
        {
            Name = "stats",
            Description = "Win/loss record for every game",
            Help = "Wins, losses, draws and the J$ you've won or lost in each game.",
            Params = [new ParamDef("user", "Whose stats to show (default: you)", ParamKind.User, Required: false)],
            Handler = StatsAsync,
        };

        yield return new CommandDef
        {
            Name = "inflation",
            Description = "How the Jotacoin economy and prices have changed",
            Help = "Prices (shop items and game amounts) follow a price index: it rises when the average player holds more J$ " +
                   "and falls when they hold less, with the occasional random market event. This shows today's index, the 14-day trend and the money supply.",
            Handler = InflationAsync,
        };

        yield return new CommandDef
        {
            Name = "shop",
            Description = "See what you can buy with J$",
            Help = "Lists the shop with today's prices (they move with inflation). Buy with `j.buy <item>`.",
            Handler = ShopAsync,
        };

        yield return new CommandDef
        {
            Name = "buy",
            Description = "Buy an item from the shop",
            Help = "Buy something from `j.shop`, for example `j.buy charm`.",
            Params =
            [
                new ParamDef("item", "Which item to buy", Autocomplete: (_, current) =>
                    Shop.Items.Select(i => i.Id).Where(id => id.Contains(current, StringComparison.OrdinalIgnoreCase))),
            ],
            Cooldowns = [new CooldownSpec(1, 3, BucketType.User)],
            Handler = BuyAsync,
        };

        yield return new CommandDef
        {
            Name = "inventory",
            Aliases = ["inv"],
            Description = "Show the items you own",
            Help = "Your shop items, badges and profile title.",
            Handler = InventoryAsync,
        };

        yield return new CommandDef
        {
            Name = "title",
            Description = "Set your profile title (needs the Custom Title item)",
            Help = "Sets the title shown on your `j.balance`. Leave the text empty to clear it. Buy the Custom Title in the shop first.",
            Params = [new ParamDef("text", "Your title (up to 32 characters; empty to clear)", Required: false, Rest: true)],
            Handler = TitleAsync,
        };
    }

    // ---------------------------------------------------- background snapshots

    public override Task OnLoadAsync()
    {
        _loop = new CancellationTokenSource();
        var token = _loop.Token;
        _ = Task.Run(() => SnapshotLoopAsync(token));
        return Task.CompletedTask;
    }

    public override Task OnUnloadAsync()
    {
        _loop?.Cancel();
        return Task.CompletedTask;
    }

    /// <summary>Makes sure today's economy snapshot exists (and the price index is current).</summary>
    private async Task SnapshotLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (await Bot.Economy.EnsureSnapshotAsync())
                    Logger.Info($"New price index: x{Bot.Economy.PriceIndex:0.00}" +
                                (Bot.Economy.MarketEvent != null ? $" ({Bot.Economy.MarketEvent})" : ""));
            }
            catch (Exception e)
            {
                Logger.Error("Economy snapshot failed", e);
            }

            try { await Task.Delay(TimeSpan.FromMinutes(30), ct); }
            catch (OperationCanceledException) { return; }
        }
    }

    // ------------------------------------------------------------- commands

    private static IUser PickTarget(CommandContext ctx)
    {
        var target = ctx.Args.User("user") ?? ctx.Author;
        if (target.IsBot) throw new BadArgumentException("Bots don't have wallets.");
        return target;
    }

    private async Task BalanceAsync(CommandContext ctx)
    {
        var target = PickTarget(ctx);
        var account = await Bot.Economy.GetAccountAsync(target.Id, target.Username);
        var rank = await Bot.Economy.RankAsync(account.Balance);
        var badges = await Bot.Economy.GetBadgesAsync(target.Id);

        var embed = new EmbedBuilder()
            .WithTitle($"{(badges.Length > 0 ? badges + " " : "")}{target.Username}")
            .WithThumbnailUrl(target.GetAvatarUrl() ?? target.GetDefaultAvatarUrl())
            .AddField("Balance", $"**{Fmt.J(account.Balance)}**", inline: true)
            .AddField("Rank", $"#{rank}", inline: true);

        if (!string.IsNullOrEmpty(account.Title))
            embed.WithDescription($"*{Format.Sanitize(account.Title)}*");

        await ctx.SendAsync(embed: embed.Build());
    }

    private async Task DailyAsync(CommandContext ctx)
    {
        var result = await Bot.Economy.ClaimDailyAsync(ctx.Author.Id, ctx.Author.Username);

        if (!result.Claimed)
        {
            await ctx.SendAsync($"⏳ You already claimed today's J$. Come back in **{Fmt.Duration(result.NextIn)}** (it resets at midnight UTC).",
                ephemeral: true);
            return;
        }

        await ctx.SendAsync($"💰 You claimed **{Fmt.J(result.Amount)}**" +
                            (result.Doubled ? " 🍀 (doubled by your Lucky Charm!)" : "") +
                            $"\nBalance: {Fmt.J(result.Balance)}");
    }

    private async Task PayAsync(CommandContext ctx)
    {
        var target = ctx.Args.User("user")!;
        var amount = ctx.Args.Int("amount")!.Value;

        if (target.Id == ctx.Author.Id) throw new BadArgumentException("You can't pay yourself.");
        if (target.IsBot) throw new BadArgumentException("Bots don't have wallets.");

        await Bot.Economy.GetAccountAsync(ctx.Author.Id, ctx.Author.Username);
        var result = await Bot.Economy.TransferAsync(ctx.Author.Id, target.Id, amount, Config.PayFeePercent);

        if (!result.Ok)
        {
            var balance = await Bot.Economy.GetBalanceAsync(ctx.Author.Id);
            throw new BadArgumentException($"You only have {Fmt.J(balance)}.");
        }

        await ctx.SendAsync(
            $"💸 {ctx.Author.Mention} sent **{Fmt.J(amount)}** to {target.Mention}" +
            (result.Fee > 0 ? $" ({Fmt.J(result.Fee)} fee, they receive {Fmt.J(result.Received)})." : "."),
            allowedMentions: new AllowedMentions { UserIds = new List<ulong> { target.Id } });
    }

    private async Task TopAsync(CommandContext ctx)
    {
        var entries = await Bot.Economy.TopAsync(10);
        if (entries.Count == 0)
        {
            await ctx.SendAsync("Nobody has any J$ yet. Try `daily`!");
            return;
        }

        var medals = new[] { "🥇", "🥈", "🥉" };
        var lines = entries.Select(e =>
            $"{(e.Rank <= 3 ? medals[e.Rank - 1] : $"**{e.Rank}.**")} {(e.Badges.Length > 0 ? e.Badges + " " : "")}" +
            $"{Format.Sanitize(string.IsNullOrEmpty(e.Name) ? "unknown" : e.Name)} — {Fmt.J(e.Balance)}");

        await ctx.SendAsync(embed: new EmbedBuilder()
            .WithTitle("🏆 Richest players")
            .WithDescription(string.Join("\n", lines))
            .Build());
    }

    private async Task StatsAsync(CommandContext ctx)
    {
        var target = PickTarget(ctx);
        var stats = await Bot.Economy.GetStatsAsync(target.Id);
        if (stats.Count == 0)
        {
            await ctx.SendAsync($"{Format.Sanitize(target.Username)} hasn't played any games yet.");
            return;
        }

        var embed = new EmbedBuilder().WithTitle($"🎮 {target.Username}'s stats");
        foreach (var s in stats)
        {
            var name = GameNames.GetValueOrDefault(s.Game, s.Game);
            embed.AddField(name, $"{s.Wins}W / {s.Losses}L / {s.Draws}D\nNet: {Fmt.Signed(s.Net)}", inline: true);
        }

        embed.WithFooter($"Total: {Fmt.Signed(stats.Sum(s => s.Net))}");
        await ctx.SendAsync(embed: embed.Build());
    }

    private async Task InflationAsync(CommandContext ctx)
    {
        var eco = Bot.Economy;
        await eco.EnsureSnapshotAsync();
        var snapshots = await eco.GetSnapshotsAsync(14);

        var embed = new EmbedBuilder()
            .WithTitle("📊 Jotacoin inflation")
            .WithDescription($"Prices are at **x{eco.PriceIndex:0.00}** of normal." +
                             (eco.MarketEvent != null ? $"\n{eco.MarketEvent}" : ""));

        if (snapshots.Count > 0)
        {
            var latest = snapshots[^1];
            var trend = Fmt.Sparkline(snapshots.Select(s => s.PriceIndex).ToList());
            var change = snapshots.Count > 1 && snapshots[0].PriceIndex > 0
                ? $" ({(latest.PriceIndex / snapshots[0].PriceIndex - 1):+0.0%;-0.0%;0.0%} over {snapshots.Count} days)"
                : "";

            embed.AddField("Price index", $"`{trend}`{change}");
            embed.AddField("Money supply", $"{Fmt.J(latest.Supply)}\n{latest.Accounts} accounts, {latest.Active} active this week", inline: true);
            embed.AddField("Today", $"Wager for member duels: {Fmt.J(eco.Scaled(10))}\nBot win bonus (medium): {Fmt.J(eco.Scaled(8))}", inline: true);
        }

        embed.AddField("Shop", string.Join("\n", Shop.Items.Select(i => $"{i.Emoji} {i.Name}: {Fmt.J(eco.Scaled(i.BasePrice))}")));
        embed.WithFooter("The index tracks J$ per active player, smoothed day by day. Money you spend in the shop leaves the economy.");
        await ctx.SendAsync(embed: embed.Build());
    }

    private async Task ShopAsync(CommandContext ctx)
    {
        var lines = Shop.Items.Select(i =>
            $"{i.Emoji} **{i.Name}** (`{i.Id}`): {Fmt.J(Bot.Economy.Scaled(i.BasePrice))}\n{i.Description}");

        await ctx.SendAsync(embed: new EmbedBuilder()
            .WithTitle("🛒 Jotacoin shop")
            .WithDescription(string.Join("\n\n", lines))
            .WithFooter($"Buy with {Config.Prefix}buy <item>. Prices move with {Config.Prefix}inflation.")
            .Build());
    }

    private async Task BuyAsync(CommandContext ctx)
    {
        var item = Shop.Find(ctx.Args.Str("item") ?? "")
                   ?? throw new BadArgumentException($"There's no item called `{ctx.Args.Str("item")}`. See `{Config.Prefix}shop`.");
        var price = Bot.Economy.Scaled(item.BasePrice);

        var result = await Bot.Economy.BuyAsync(ctx.Author.Id, ctx.Author.Username, item, price);
        switch (result)
        {
            case BuyResult.Ok:
                var hint = item.Id == "title" ? $" Now set it with `{Config.Prefix}title <text>`." : "";
                await ctx.SendAsync($"🛍️ You bought {item.Emoji} **{item.Name}** for **{Fmt.J(price)}**.{hint}");
                break;
            case BuyResult.AlreadyOwned:
                await ctx.SendAsync($"You already own {item.Emoji} {item.Name}.", ephemeral: true);
                break;
            default:
                var balance = await Bot.Economy.GetBalanceAsync(ctx.Author.Id);
                await ctx.SendAsync($"{item.Emoji} {item.Name} costs {Fmt.J(price)} and you have {Fmt.J(balance)}.", ephemeral: true);
                break;
        }
    }

    private async Task InventoryAsync(CommandContext ctx)
    {
        var account = await Bot.Economy.GetAccountAsync(ctx.Author.Id, ctx.Author.Username);
        var owned = await Bot.Economy.GetInventoryAsync(ctx.Author.Id);

        var lines = Shop.Items.Where(i => owned.ContainsKey(i.Id))
            .Select(i => $"{i.Emoji} {i.Name}" + (owned[i.Id] > 1 ? $" x{owned[i.Id]}" : ""))
            .ToList();

        var embed = new EmbedBuilder()
            .WithTitle($"🎒 {ctx.Author.Username}'s inventory")
            .WithDescription(lines.Count > 0 ? string.Join("\n", lines) : "Nothing yet. Have a look at `j.shop`.");

        if (!string.IsNullOrEmpty(account.Title))
            embed.AddField("Title", Format.Sanitize(account.Title));

        await ctx.SendAsync(embed: embed.Build());
    }

    private async Task TitleAsync(CommandContext ctx)
    {
        var text = ctx.Args.Str("text")?.Trim();
        if (string.IsNullOrEmpty(text)) text = null;
        if (text != null && text.Length > 32) throw new BadArgumentException("Titles can be at most 32 characters.");

        await Bot.Economy.GetAccountAsync(ctx.Author.Id, ctx.Author.Username);
        if (!await Bot.Economy.SetTitleAsync(ctx.Author.Id, text))
            throw new BadArgumentException($"You need the Custom Title item first. See `{Config.Prefix}shop`.");

        await ctx.SendAsync(text == null ? "Title cleared." : $"Title set to *{Format.Sanitize(text)}*.", allowedMentions: AllowedMentions.None);
    }
}
