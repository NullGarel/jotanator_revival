// VIBE CODED DSICLAIMER - CLAUDE GENERATED AS OF 26-11-28 @ 19:27 UTC
using Discord;
using Discord.WebSocket;

namespace Jotanator;

/// <summary>Five finger fillet for J$: a press-your-luck knife game, against the house or a member.</summary>
public sealed class FilletCog : GameCog
{
    protected override string Key => "fillet";

    public override IEnumerable<CommandDef> BuildCommands()
    {
        yield return new CommandDef
        {
            Name = "fillet",
            Aliases = ["ffm", "fivefingerfillet"],
            Description = "Five finger fillet: stab faster for bigger winnings, but don't cut yourself",
            Help = "Stab the knife between your fingers. Each successful stab multiplies your winnings: slow stabs are safe but pay little, " +
                   "fast stabs are risky but pay a lot. Cash out any time after your first stab; cut yourself and you lose your bet.\n" +
                   "Mention a member for a duel: take turns, the first player banks a multiplier, then the other has to beat it without getting cut.\n" +
                   "Examples: `j.fillet`, `j.fillet 50`, `j.fillet 25 @friend`",
            Params = [GameParams.Bet(), GameParams.Opponent()],
            Cooldowns = [new CooldownSpec(1, 5, BucketType.User)],
            Handler = PlayAsync,
        };
    }

    private async Task PlayAsync(CommandContext ctx)
    {
        var bet = ctx.Args.Int("bet") ?? 10;
        var opponent = ResolveOpponent(ctx, ctx.Args.User("opponent"));
        await LaunchAsync(ctx, new FilletSession(Bot, ctx.Author, opponent, bet, Remove));
    }
}

internal sealed class FilletSession : GameSession
{
    private static readonly string[] Hits =
    [
        "🔪 *thunk!* The blade lands between two fingers.",
        "🔪 Clean stab. Not a scratch.",
        "🔪 *tak-tak-tak!* Smooth as butter.",
        "🔪 The knife dances across the table. You're still in one piece.",
    ];

    private int _turn = 1;
    private double _mult = 1.0;
    private int _stabs;
    private double _target;   // duel: the multiplier player 1 banked, which player 2 has to beat
    private string _flavor = "";

    public FilletSession(JotaBot bot, IUser p1, IUser? p2, long bet, Action<string> onDone)
        : base(bot, "fillet", "Five Finger Fillet", p1, p2, stake: bet, friendlyStake: false, onDone)
    {
    }

    protected override int TurnPlayer => _turn;

    protected override long BotPayout(Side side) => side == Side.Player1 ? (long)Math.Floor(Stake * _mult) : 0;

    protected override string Content()
    {
        if (Over) return $"{Header()}\n{(_flavor.Length > 0 ? _flavor + "\n" : "")}{ResultText}";

        var who = PlayerLabel(_turn);
        var info = VsBot
            ? $"✋ {who} has the knife.\nMultiplier: **x{_mult:0.00}** (cash out: {Fmt.J((long)Math.Floor(Stake * _mult))})"
            : _turn == 1
                ? $"✋ {who} has the knife. Bank a multiplier, then {PlayerLabel(2)} must beat it.\nMultiplier: **x{_mult:0.00}**"
                : $"✋ {who} has the knife and must beat **x{_target:0.00}** without getting cut.\nMultiplier: **x{_mult:0.00}**";

        return $"{Header()}\n{info} • stab {_stabs}/{Fillet.MaxStabs}\n{(_flavor.Length > 0 ? _flavor + "\n" : "")}Choose your speed!";
    }

    protected override MessageComponent Components()
    {
        string Label(string name, KnifeSpeed s) => $"{name} ({Fillet.SuccessChance(s):P0})";

        // player 1 (or the only player) may cash out after the first stab; in a duel player 2 wins by beating the target instead
        var canCash = !Over && _stabs > 0 && _turn == 1;

        return new ComponentBuilder()
            .WithButton(Label("Slow", KnifeSpeed.Slow), CId("slow"), ButtonStyle.Success, disabled: Over, row: 0)
            .WithButton(Label("Medium", KnifeSpeed.Medium), CId("medium"), ButtonStyle.Primary, disabled: Over, row: 0)
            .WithButton(Label("Fast", KnifeSpeed.Fast), CId("fast"), ButtonStyle.Danger, disabled: Over, row: 0)
            .WithButton("Cash out", CId("cash"), ButtonStyle.Secondary, new Emoji("💰"), disabled: !canCash, row: 1)
            .Build();
    }

    protected override async Task OnActionAsync(SocketMessageComponent c, string action)
    {
        if (!await RequireTurnAsync(c, _turn)) return;

        if (action == "cash")
        {
            if (_stabs == 0 || _turn != 1)
            {
                await c.DeferAsync();
                return;
            }

            await CashOutAsync();
            await UpdateAsync(c);
            return;
        }

        KnifeSpeed speed;
        switch (action)
        {
            case "slow": speed = KnifeSpeed.Slow; break;
            case "medium": speed = KnifeSpeed.Medium; break;
            case "fast": speed = KnifeSpeed.Fast; break;
            default:
                await c.DeferAsync();
                return;
        }

        if (!Fillet.Stab(speed))
        {
            _flavor = "🩸 **Ouch!** The knife slips and you cut yourself.";
            await FinishAsync(_turn == 1 ? Side.Player2 : Side.Player1, $"{PlayerLabel(_turn)} lost it all.");
            await UpdateAsync(c);
            return;
        }

        _mult = Fillet.Next(_mult, speed);
        _stabs++;
        _flavor = Hits[Random.Shared.Next(Hits.Length)];

        if (!VsBot && _turn == 2 && _mult > _target)
        {
            await FinishAsync(Side.Player2, $"🏆 {PlayerLabel(2)} beat **x{_target:0.00}** with **x{_mult:0.00}**!");
        }
        else if (_stabs >= Fillet.MaxStabs)
        {
            if (_turn == 1)
                await CashOutAsync();
            else
                await FinishAsync(Side.Player1, $"{PlayerLabel(2)} ran out of stabs without beating **x{_target:0.00}**.");
        }

        await UpdateAsync(c);
    }

    private async Task CashOutAsync()
    {
        if (VsBot)
        {
            await FinishAsync(Side.Player1, $"💰 {P1.Mention} cashes out at **x{_mult:0.00}**!");
            return;
        }

        // duel: player 1 banks the multiplier and player 2 takes the knife
        _target = _mult;
        _turn = 2;
        _mult = 1.0;
        _stabs = 0;
        _flavor = $"💰 {P1.Mention} banked **x{_target:0.00}**.";
    }
}
