// VIBE CODED DSICLAIMER - CLAUDE GENERATED AS OF 26-11-28 @ 19:27 UTC
using Discord;
using Discord.WebSocket;

namespace Jotanator;

/// <summary>One round of craps for J$: against the house, or shooter against fader.</summary>
public sealed class CrapsCog : GameCog
{
    protected override string Key => "craps";

    private static readonly string[] Sides = ["pass", "dontpass"];

    public override IEnumerable<CommandDef> BuildCommands()
    {
        yield return new CommandDef
        {
            Name = "craps",
            Description = "Roll the dice for J$: pass line or don't pass, against the bot or a member",
            Help = "You are the shooter. On the first (come-out) roll, a 7 or 11 wins for the pass line and 2, 3 or 12 loses; " +
                   "any other number becomes the point, and you keep rolling until the point comes up (pass wins) or a 7 (pass loses). " +
                   "Bet on `dontpass` to win the other way round (12 on the come-out is a push). Pays 1:1.\n" +
                   "Mention a member to play against them: you shoot on the pass line and they fade you (a 12 is a push).\n" +
                   "Examples: `j.craps`, `j.craps 50`, `j.craps 50 dontpass`, `j.craps 25 @friend`",
            Params =
            [
                GameParams.Bet(),
                GameParams.Opponent(),
                new ParamDef("side", "pass (default) or dontpass (only against the bot)", Required: false,
                    Autocomplete: (_, current) => Sides.Where(s => s.Contains(current, StringComparison.OrdinalIgnoreCase))),
            ],
            Cooldowns = [new CooldownSpec(1, 5, BucketType.User)],
            Handler = PlayAsync,
        };
    }

    private async Task PlayAsync(CommandContext ctx)
    {
        var bet = ctx.Args.Int("bet") ?? 10;
        var opponent = ResolveOpponent(ctx, ctx.Args.User("opponent"));

        var raw = ctx.Args.Str("side")?.Trim().ToLowerInvariant().Replace("'", "").Replace(" ", "");
        var dontPass = string.IsNullOrEmpty(raw) || raw is "pass" or "p" ? false
            : raw is "dontpass" or "dont" or "dp" or "d" ? true
            : throw new BadArgumentException("Side must be `pass` or `dontpass`.");

        await LaunchAsync(ctx, new CrapsSession(Bot, ctx.Author, opponent, bet, dontPass, Remove));
    }
}

internal sealed class CrapsSession : GameSession
{
    private readonly CrapsRound _round = new();
    private readonly bool _dontPass;   // only meaningful against the bot
    private readonly List<string> _log = new();

    public CrapsSession(JotaBot bot, IUser p1, IUser? p2, long bet, bool dontPass, Action<string> onDone)
        : base(bot, "craps", "Craps", p1, p2, stake: bet, friendlyStake: false, onDone)
    {
        _dontPass = p2 == null && dontPass;
    }

    protected override string Content()
    {
        var bets = VsBot
            ? $"{P1.Mention} shoots and bets on the **{(_dontPass ? "Don't pass" : "Pass line")}**"
            : $"{P1.Mention} shoots (**Pass**) against {P2!.Mention} (**Don't pass**)";

        var point = _round.Point is int p ? $"Point: **{p}**" : "Come-out roll";
        var log = _log.Count == 0 ? "" : "\n" + string.Join("\n", _log.TakeLast(6));
        var footer = Over ? ResultText : $"{P1.Mention}, roll the dice!";

        return $"{Header()}\n{bets}\n{point}{log}\n{footer}";
    }

    protected override MessageComponent Components() =>
        new ComponentBuilder()
            .WithButton("Roll", CId("roll"), ButtonStyle.Success, new Emoji("🎲"), disabled: Over)
            .Build();

    private static string Describe(CrapsStatus status, int sum) => status switch
    {
        CrapsStatus.Natural => "natural!",
        CrapsStatus.Craps => "craps!",
        CrapsStatus.Twelve => "boxcars!",
        CrapsStatus.PointHit => "the point!",
        CrapsStatus.SevenOut => "seven out!",
        _ => $"the point is now {sum}",
    };

    protected override async Task OnActionAsync(SocketMessageComponent c, string action)
    {
        if (action != "roll" || !await RequireTurnAsync(c, 1)) return;

        var d1 = Random.Shared.Next(1, 7);
        var d2 = Random.Shared.Next(1, 7);
        var status = _round.Roll(d1, d2);
        _log.Add($"🎲 {d1} + {d2} = **{d1 + d2}**, {Describe(status, d1 + d2)}");

        if (_round.Finished)
        {
            var side = VsBot ? SideAgainstBot(status) : SideAgainstPlayer(status);
            var detail = side switch
            {
                Side.Player1 => $"🏆 {P1.Mention} wins!",
                Side.Player2 => $"{PlayerLabel(2)} wins.",
                _ => "🤝 Push!",
            };
            await FinishAsync(side, detail);
        }

        await UpdateAsync(c);
    }

    private Side SideAgainstBot(CrapsStatus status)
    {
        var result = _dontPass ? CrapsRound.DontPassResult(status) : CrapsRound.PassResult(status);
        return result switch { Outcome.Win => Side.Player1, Outcome.Loss => Side.Player2, _ => Side.Draw };
    }

    /// <summary>Shooter (pass) against fader (don't pass): a 12 on the come-out is a push for both.</summary>
    private static Side SideAgainstPlayer(CrapsStatus status) => status switch
    {
        CrapsStatus.Natural or CrapsStatus.PointHit => Side.Player1,
        CrapsStatus.Twelve => Side.Draw,
        _ => Side.Player2,
    };
}
