// VIBE CODED DSICLAIMER - CLAUDE GENERATED AS OF 26-11-28 @ 19:52 UTC
using System.Collections.Concurrent;
using Discord;
using Discord.WebSocket;

namespace Jotanator;

/// <summary>
/// Slot machine for J$: three reels, weighted symbols, an animated spin and a "Spin again" button.
/// Drop this file into the Cogs folder; it is loaded automatically. (Optional: add ["slots"] = "Slots"
/// to GameNames in EconomyCog.cs so j.stats shows a nice name.)
/// </summary>
public sealed class SlotsCog : Cog
{
    private const string Key = "slots";
    private const long MaxBet = 1_000_000;

    private readonly ConcurrentDictionary<ulong, byte> _spinning = new();

    public override string Category => "Games";

    public override IEnumerable<CommandDef> BuildCommands()
    {
        yield return new CommandDef
        {
            Name = "slots",
            Aliases = ["slot", "spin"],
            Description = "Spin the slot machine for J$",
            Help = "Spin three reels for J$. Your bet comes back multiplied by the prize:\n" +
                   SlotMachine.PrizeTable() + "\n" +
                   $"Average return: {SlotMachine.Rtp:P1}. Use the **Spin again** button to repeat the same bet.\n" +
                   "Examples: `j.slots`, `j.slots 50`",
            Params =
            [
                new ParamDef("bet", "How much J$ to bet (default 10)", ParamKind.Int, Required: false, Min: 1, Max: MaxBet),
            ],
            Cooldowns = [new CooldownSpec(1, 3, BucketType.User)],
            Handler = SlotsAsync,
        };
    }

    public override Task OnLoadAsync()
    {
        Bot.Client.ButtonExecuted += OnButtonAsync;
        return Task.CompletedTask;
    }

    public override Task OnUnloadAsync()
    {
        Bot.Client.ButtonExecuted -= OnButtonAsync;
        return Task.CompletedTask;
    }

    // --------------------------------------------------------------- entry points

    private async Task SlotsAsync(CommandContext ctx)
    {
        var bet = ctx.Args.Int("bet") ?? 10;

        var (ok, error, gameId) = await BeginAsync(ctx.Author, bet);
        if (!ok) throw new BadArgumentException(error!);

        IUserMessage? message = null;
        await PlayAsync(ctx.Author, bet, gameId, async (text, components) =>
        {
            if (message == null)
            {
                message = await ctx.SendAsync(text, components: components, allowedMentions: AllowedMentions.None, wantMessage: true);
            }
            else
            {
                await message.ModifyAsync(m =>
                {
                    m.Content = text;
                    m.Components = components ?? new ComponentBuilder().Build();
                });
            }
        });
    }

    /// <summary>The "Spin again" button. Custom id: slots:&lt;userId&gt;:&lt;bet&gt;.</summary>
    private Task OnButtonAsync(SocketMessageComponent component)
    {
        var parts = component.Data.CustomId.Split(':');
        if (parts.Length != 3 || parts[0] != Key) return Task.CompletedTask;

        _ = Task.Run(async () =>
        {
            try
            {
                if (!ulong.TryParse(parts[1], out var ownerId) || !long.TryParse(parts[2], out var bet) || bet is < 1 or > MaxBet)
                {
                    await component.DeferAsync();
                    return;
                }

                if (component.User.Id != ownerId)
                {
                    await component.RespondAsync($"This isn't your slot machine. Start your own with `{Config.Prefix}slots`.", ephemeral: true);
                    return;
                }

                var (ok, error, gameId) = await BeginAsync(component.User, bet);
                if (!ok)
                {
                    await component.RespondAsync(error, ephemeral: true);
                    return;
                }

                await component.DeferAsync();
                await PlayAsync(component.User, bet, gameId, (text, components) =>
                    component.ModifyOriginalResponseAsync(m =>
                    {
                        m.Content = text;
                        m.Components = components ?? new ComponentBuilder().Build();
                    }));
            }
            catch (Exception e)
            {
                Logger.Error("Slots button failed", e);
            }
        });

        return Task.CompletedTask;
    }

    // ------------------------------------------------------------------- a spin

    /// <summary>Takes the bet out of the balance (held in escrow) and locks the user so spins can't overlap.</summary>
    private async Task<(bool Ok, string? Error, string GameId)> BeginAsync(IUser user, long bet)
    {
        if (!_spinning.TryAdd(user.Id, 0))
            return (false, "Your last spin is still going!", "");

        var gameId = $"slots-{Guid.NewGuid():N}"[..16];
        try
        {
            if (await Bot.Economy.ReserveAsync(gameId, user.Id, bet))
                return (true, null, gameId);

            var balance = await Bot.Economy.GetBalanceAsync(user.Id, user.Username);
            _spinning.TryRemove(user.Id, out _);
            return (false, $"You only have {Fmt.J(balance)}, but you tried to bet {Fmt.J(bet)}.", "");
        }
        catch
        {
            _spinning.TryRemove(user.Id, out _);
            throw;
        }
    }

    /// <summary>Animates the reels, pays out and shows the result. `show` sends or edits the message.</summary>
    private async Task PlayAsync(IUser user, long bet, string gameId, Func<string, MessageComponent?, Task> show)
    {
        try
        {
            // the outcome is decided up front; the animation is only for show
            var reels = SlotMachine.Spin();
            var multiplier = SlotMachine.Multiplier(reels);
            var payout = (long)Math.Floor(bet * multiplier);

            try
            {
                for (var locked = 0; locked < 3; locked++)
                {
                    await show(SlotMachine.Frame(user.Username, bet, reels, locked), null);
                    await Task.Delay(900);
                }
            }
            catch (Exception e)
            {
                Logger.Warn($"Slots animation failed: {e.Message}");   // keep going: the bet must still be settled
            }

            long? balance = null;
            try
            {
                await Bot.Economy.SettleAsync(gameId, user.Id, payout);
                var outcome = payout > bet ? Outcome.Win : payout == bet ? Outcome.Draw : Outcome.Loss;
                await Bot.Economy.RecordAsync(user.Id, Key, outcome, payout - bet);
                balance = await Bot.Economy.GetBalanceAsync(user.Id);
            }
            catch (Exception e)
            {
                Logger.Error("Slots settlement failed", e);
            }

            var net = payout - bet;
            var text = SlotMachine.Frame(user.Username, bet, reels, 3) + "\n" +
                       $"{SlotMachine.Describe(reels)}\n" +
                       (payout > 0 ? $"💰 Won **{Fmt.J(payout)}** ({Fmt.Signed(net)})" : $"💸 Lost **{Fmt.J(bet)}**") +
                       (balance != null ? $" • Balance: {Fmt.J(balance.Value)}" : "\n⚠️ Couldn't update your balance.");

            var again = new ComponentBuilder()
                .WithButton($"Spin again ({Fmt.J(bet)})", $"{Key}:{user.Id}:{bet}", ButtonStyle.Success, new Emoji("🎰"))
                .Build();

            try
            {
                await show(text, again);
            }
            catch (Exception e)
            {
                Logger.Warn($"Slots result message failed: {e.Message}");   // the bet is already settled
            }
        }
        finally
        {
            _spinning.TryRemove(user.Id, out _);
        }
    }
}

/// <summary>Reels, prizes and odds. Pure logic, no Discord code.</summary>
internal static class SlotMachine
{
    internal sealed record Symbol(string Emoji, string Name, int Weight, int Triple);

    /// <summary>Weight = how often it lands on a reel; Triple = prize multiplier for three in a row.</summary>
    internal static readonly Symbol[] Symbols =
    [
        new("🍒", "cherries", 20, 5),
        new("🍋", "lemons", 18, 9),
        new("🍊", "oranges", 14, 14),
        new("🍇", "grapes", 10, 22),
        new("🔔", "bells", 6, 45),
        new("⭐", "stars", 3, 90),
        new("💎", "diamonds", 2, 250),
        new("7️⃣", "sevens", 1, 1000),
    ];

    private const double TwoCherries = 2.0;
    private const double OneCherry = 0.5;

    private static readonly int TotalWeight = Symbols.Sum(s => s.Weight);

    /// <summary>One random symbol index, weighted.</summary>
    internal static int Pick(Random? rng = null)
    {
        var roll = (rng ?? Random.Shared).Next(TotalWeight);
        for (var i = 0; i < Symbols.Length; i++)
        {
            roll -= Symbols[i].Weight;
            if (roll < 0) return i;
        }
        return Symbols.Length - 1;
    }

    internal static int[] Spin(Random? rng = null) => [Pick(rng), Pick(rng), Pick(rng)];

    /// <summary>Prize multiplier of a spin (stake included): 0 = lost the bet.</summary>
    internal static double Multiplier(int[] reels)
    {
        if (reels[0] == reels[1] && reels[1] == reels[2]) return Symbols[reels[0]].Triple;

        return reels.Count(r => r == 0) switch
        {
            2 => TwoCherries,
            1 => OneCherry,
            _ => 0,
        };
    }

    /// <summary>Exact average return over all possible spins (about 95%).</summary>
    internal static readonly double Rtp = ComputeRtp();

    private static double ComputeRtp()
    {
        double total = 0;
        for (var a = 0; a < Symbols.Length; a++)
            for (var b = 0; b < Symbols.Length; b++)
                for (var c = 0; c < Symbols.Length; c++)
                {
                    var p = (double)Symbols[a].Weight * Symbols[b].Weight * Symbols[c].Weight / ((double)TotalWeight * TotalWeight * TotalWeight);
                    total += p * Multiplier([a, b, c]);
                }
        return total;
    }

    internal static string PrizeTable() =>
        string.Join("\n", Symbols.OrderByDescending(s => s.Triple).Select(s => $"{s.Emoji}{s.Emoji}{s.Emoji} x{s.Triple}")) +
        $"\n🍒🍒 x{TwoCherries:0.#}  •  🍒 x{OneCherry:0.#}";

    internal static string Describe(int[] reels)
    {
        if (reels[0] == reels[1] && reels[1] == reels[2])
            return reels[0] == Symbols.Length - 1 ? "🎉 **JACKPOT!** Triple sevens!" : $"✨ Three {Symbols[reels[0]].Name}!";

        return reels.Count(r => r == 0) switch
        {
            2 => "🍒 Two cherries!",
            1 => "🍒 A lone cherry gets you half your bet back.",
            _ => "No luck this time.",
        };
    }

    /// <summary>The machine with the first `locked` reels stopped and the rest still spinning.</summary>
    internal static string Frame(string player, long bet, int[] reels, int locked)
    {
        string Reel(int i) => i < locked ? Symbols[reels[i]].Emoji : Symbols[Pick()].Emoji;
        var status = locked >= 3 ? "" : "\n🔄 Spinning...";
        return $"🎰 **Slots** • {Discord.Format.Sanitize(player)} • bet {Fmt.J(bet)}\n▶ {Reel(0)} ┃ {Reel(1)} ┃ {Reel(2)} ◀{status}";
    }
}