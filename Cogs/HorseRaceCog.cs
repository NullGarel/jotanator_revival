// VIBE CODED DSICLAIMER - CLAUDE GENERATED AS OF 26-11-28 @ 19:27 UTC
using Discord;
using Discord.WebSocket;

namespace Jotanator;

/// <summary>Horse racing for J$: back a horse against the house (with odds), or pick a horse against another member.</summary>
public sealed class HorseRaceCog : GameCog
{
    protected override string Key => "horse";

    public override IEnumerable<CommandDef> BuildCommands()
    {
        yield return new CommandDef
        {
            Name = "horserace",
            Aliases = ["horse", "race"],
            Description = "Bet J$ on a horse: against the house, or head to head with a member",
            Help = "Five horses, each with its own odds. Against me, pick a horse and your bet pays out at its odds if it wins. " +
                   "Mention a member for a head-to-head: you each pick a different horse, and whoever's horse finishes ahead takes the pot.\n" +
                   "Examples: `j.horse`, `j.horse 50`, `j.horse 25 @friend`",
            Params = [GameParams.Bet(), GameParams.Opponent()],
            Cooldowns = [new CooldownSpec(1, 10, BucketType.User)],
            Handler = PlayAsync,
        };
    }

    private async Task PlayAsync(CommandContext ctx)
    {
        var bet = ctx.Args.Int("bet") ?? 10;
        var opponent = ResolveOpponent(ctx, ctx.Args.User("opponent"));
        await LaunchAsync(ctx, new HorseSession(Bot, ctx.Author, opponent, bet, Remove));
    }
}

internal sealed class HorseSession : GameSession
{
    private const int TrackWidth = 20;

    private static readonly string[] Dots = ["🔴", "🔵", "🟢", "🟡", "🟣"];
    private static readonly string[] Names = ["Red Rocket", "Blue Thunder", "Green Gallop", "Golden Gale", "Purple Haze"];
    private static readonly string[] Medals = ["🥇", "🥈", "🥉", "4️⃣", "5️⃣"];

    private readonly HorseRace _race = new();
    private double[] _odds = new double[HorseRace.Horses];
    private int[] _final = [];
    private int _pick1 = -1;
    private int _pick2 = -1;
    private bool _racing;

    public HorseSession(JotaBot bot, IUser p1, IUser? p2, long bet, Action<string> onDone)
        : base(bot, "horse", "Horse Race", p1, p2, stake: bet, friendlyStake: false, onDone)
    {
    }

    protected override int TurnPlayer => _pick1 < 0 ? 1 : 2;

    /// <summary>Against the house a winning bet pays its odds (stake included).</summary>
    protected override long BotPayout(Side side) =>
        side == Side.Player1 && _pick1 >= 0 ? (long)Math.Floor(Stake * _odds[_pick1]) : 0;

    protected override Task OnStartAsync()
    {
        _odds = _race.Odds();
        return Task.CompletedTask;
    }

    private string Horse(int i) => $"{Dots[i]} **#{i + 1} {Names[i]}**";

    private string Track()
    {
        var lines = new List<string>();
        for (var i = 0; i < HorseRace.Horses; i++)
        {
            var progress = Math.Min(_race.Position[i] / HorseRace.FinishLine, 1.0);
            var before = (int)Math.Round(progress * TrackWidth);
            lines.Add($"{i + 1} {new string('·', before)}🐎{new string('·', TrackWidth - before)}🏁");
        }
        return "```\n" + string.Join("\n", lines) + "\n```";
    }

    protected override string Content()
    {
        if (Over && _final.Length > 0)
        {
            var standings = string.Join("\n", _final.Select((h, place) => $"{Medals[place]} {Horse(h)}"));
            return $"{Header()}\n{Track()}\n{standings}\n{ResultText}";
        }

        if (_racing)
            return $"{Header()}\n🏁 And they're off!\n{Track()}";

        if (Over) return $"{Header()}\n{ResultText}";

        var picks = new List<string>();
        if (_pick1 >= 0) picks.Add($"{P1.Mention} picked {Horse(_pick1)}");
        if (_pick2 >= 0) picks.Add($"{P2!.Mention} picked {Horse(_pick2)}");

        var field = string.Join("\n", Enumerable.Range(0, HorseRace.Horses)
            .Select(i => $"{Horse(i)}" + (VsBot ? $" pays **{_odds[i]:0.0}x**" : "")));

        var prompt = $"{PlayerLabel(TurnPlayer)}, pick your horse!";
        return $"{Header()}\n{field}\n{(picks.Count > 0 ? string.Join("\n", picks) + "\n" : "")}{prompt}";
    }

    protected override MessageComponent Components()
    {
        var builder = new ComponentBuilder();
        for (var i = 0; i < HorseRace.Horses; i++)
        {
            var taken = i == _pick1 || i == _pick2;
            builder.WithButton(
                label: VsBot ? $"#{i + 1}  {_odds[i]:0.0}x" : $"#{i + 1}",
                customId: CId(i.ToString()),
                style: taken ? ButtonStyle.Success : ButtonStyle.Secondary,
                emote: new Emoji(Dots[i]),
                disabled: Over || _racing || taken);
        }
        return builder.Build();
    }

    protected override async Task OnActionAsync(SocketMessageComponent c, string action)
    {
        if (_racing || !int.TryParse(action, out var horse) || horse is < 0 or >= HorseRace.Horses)
        {
            await c.DeferAsync();
            return;
        }

        var player = TurnPlayer;
        if (!await RequireTurnAsync(c, player)) return;

        if (horse == _pick1)
        {
            await c.RespondAsync("That horse is already taken, pick another one.", ephemeral: true);
            return;
        }

        if (player == 1) _pick1 = horse;
        else _pick2 = horse;

        if (!VsBot && _pick2 < 0)
        {
            await UpdateAsync(c);   // now it's the opponent's turn to pick
            return;
        }

        _racing = true;
        await UpdateAsync(c);
        await RunRaceAsync();
    }

    private async Task RunRaceAsync()
    {
        bool done;
        do
        {
            await Task.Delay(1300);
            done = _race.Step();
            await EditAsync();
        }
        while (!done);

        _racing = false;
        _final = _race.Ranking();
        var winner = _final[0];

        Side side;
        string detail;
        if (VsBot)
        {
            side = winner == _pick1 ? Side.Player1 : Side.Player2;
            detail = side == Side.Player1
                ? $"🏆 {Horse(winner)} wins! You collect **{Fmt.J(BotPayout(Side.Player1))}**."
                : $"{Horse(winner)} wins. Your horse didn't make it.";
        }
        else
        {
            side = Array.IndexOf(_final, _pick1) < Array.IndexOf(_final, _pick2) ? Side.Player1 : Side.Player2;
            detail = $"🏆 {PlayerLabel(side == Side.Player1 ? 1 : 2)}'s horse finished ahead!";
        }

        await FinishAsync(side, detail);
        await EditAsync();
    }
}
