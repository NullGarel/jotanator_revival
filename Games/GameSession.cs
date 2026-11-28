// VIBE CODED DSICLAIMER - CLAUDE GENERATED AS OF 26-11-28 @ 19:27 UTC
using Discord;
using Discord.WebSocket;

namespace Jotanator;

public enum Phase { Pending, Playing, Done }

/// <summary>Who won a game. Player2 is the opponent: another member, or the bot.</summary>
public enum Side { Player1, Player2, Draw }

/// <summary>
/// One running game in one message. Handles everything games have in common: the challenge
/// (accept/decline) flow, stakes held in escrow, paying out, stats, timeouts and forfeits.
/// A game only implements its own rules (OnActionAsync) and drawing (Content / Components).
/// </summary>
public abstract class GameSession
{
    private readonly Action<string> _onDone;
    private readonly bool _friendlyStake;
    private CancellationTokenSource? _timeout;
    private bool _started;

    protected GameSession(JotaBot bot, string key, string title, IUser p1, IUser? p2,
                          long stake, bool friendlyStake, Action<string> onDone)
    {
        Bot = bot;
        Key = key;
        Title = title;
        P1 = p1;
        P2 = p2;
        Stake = stake;
        _friendlyStake = friendlyStake;
        _onDone = onDone;
    }

    public string Id { get; } = Guid.NewGuid().ToString("N")[..10];

    /// <summary>Custom-id prefix, stats key, and the prefix command alias (e.g. "ttt").</summary>
    public string Key { get; }

    public string Title { get; }
    public JotaBot Bot { get; }
    public IUser P1 { get; }

    /// <summary>The opponent, or null when playing against the bot.</summary>
    public IUser? P2 { get; }

    public bool VsBot => P2 is null;

    /// <summary>J$ each human player has put in. 0 = none.</summary>
    public long Stake { get; protected set; }

    public Phase Phase { get; protected set; } = Phase.Playing;
    public IUserMessage? Message { get; set; }

    protected SemaphoreSlim Gate { get; } = new(1, 1);
    protected string ResultText { get; private set; } = "";
    protected bool Over => Phase == Phase.Done;

    // ------------------------------------------------------------ game hooks

    /// <summary>Message text while the game is running or finished.</summary>
    protected abstract string Content();

    /// <summary>Buttons / menus while the game is running or finished (disable them when Over).</summary>
    protected abstract MessageComponent Components();

    /// <summary>A button or menu was used by one of the players. Respond to the interaction here.</summary>
    protected abstract Task OnActionAsync(SocketMessageComponent c, string action);

    /// <summary>Play begins (stakes are held). Deal cards, set up the board, let the bot move first...</summary>
    protected virtual Task OnStartAsync() => Task.CompletedTask;

    /// <summary>Whose turn it is (1 or 2); that player loses on timeout in a two-player game.</summary>
    protected virtual int TurnPlayer => 1;

    /// <summary>J$ won against the bot when there is no stake (scaled by the price index; a loss costs half).</summary>
    protected virtual long BotWinReward => 0;

    /// <summary>What a player who staked J$ gets back against the bot (stake included).</summary>
    protected virtual long BotPayout(Side side) =>
        side == Side.Player1 ? Stake * 2 : side == Side.Draw ? Stake : 0;

    // -------------------------------------------------------------- helpers

    protected string CId(string action) => $"{Key}:{Id}:{action}";

    protected string BotName => Bot.Client.CurrentUser?.Username ?? "Jotanator";

    protected string PlayerLabel(int player) =>
        player == 1 ? P1.Mention : P2?.Mention ?? $"🤖 {BotName}";

    protected int PlayerNumber(ulong userId) => userId == P1.Id ? 1 : 2;

    private bool IsPlayer(ulong id) => id == P1.Id || id == P2?.Id;

    protected string Header() =>
        $"**{Title}**" + (Stake > 0 ? $" • stake {Fmt.J(Stake)}" + (VsBot ? "" : " each") : "");

    /// <summary>Replies "not your turn" and returns false unless the click came from the player whose turn it is.</summary>
    protected async Task<bool> RequireTurnAsync(SocketMessageComponent c, int player)
    {
        var expected = player == 2 && P2 != null ? P2.Id : P1.Id;
        if (c.User.Id == expected) return true;

        await c.RespondAsync("It's not your turn.", ephemeral: true);
        return false;
    }

    // -------------------------------------------------------------- rendering

    private string PendingContent() =>
        $"{P2!.Mention}, {P1.Mention} challenges you to **{Title}**!" +
        (_friendlyStake ? "\nA small friendly wager applies." : Stake > 0 ? $"\nStake: {Fmt.J(Stake)} each." : "");

    private MessageComponent PendingComponents() =>
        new ComponentBuilder()
            .WithButton("Accept", CId("accept"), ButtonStyle.Success)
            .WithButton("Decline", CId("decline"), ButtonStyle.Danger)
            .Build();

    protected string BuildContent() =>
        _started ? Content() : Phase == Phase.Pending ? PendingContent() : ResultText;

    protected MessageComponent BuildComponents() =>
        _started ? Components() : Phase == Phase.Pending ? PendingComponents() : new ComponentBuilder().Build();

    /// <summary>Answers the interaction by redrawing the game message.</summary>
    protected Task UpdateAsync(SocketMessageComponent c) =>
        c.UpdateAsync(m =>
        {
            m.Content = BuildContent();
            m.Components = BuildComponents();
        });

    /// <summary>Redraws the game message outside of an interaction (race frames, timeouts).</summary>
    protected async Task EditAsync()
    {
        if (Message == null) return;
        try
        {
            await Message.ModifyAsync(m =>
            {
                m.Content = BuildContent();
                m.Components = BuildComponents();
            });
        }
        catch
        {
            // message deleted or the interaction token expired; nothing to do
        }
    }

    // -------------------------------------------------------------- lifecycle

    /// <summary>Sends the game message: a challenge if there's an opponent, otherwise starts playing right away.</summary>
    public async Task StartAsync(CommandContext ctx)
    {
        if (P2 != null)
        {
            Phase = Phase.Pending;
            Message = await ctx.SendAsync(BuildContent(), components: BuildComponents(),
                allowedMentions: new AllowedMentions(AllowedMentionTypes.Users), wantMessage: true);
            ResetTimeout(60);
            return;
        }

        var error = await BeginAsync();
        if (error != null)
        {
            _onDone(Id);
            throw new BadArgumentException(error);
        }

        Message = await ctx.SendAsync(BuildContent(), components: BuildComponents(), wantMessage: true);
        if (!Over) ResetTimeout();
    }

    /// <summary>Holds the stakes and starts play. Returns an error text if somebody can't pay.</summary>
    internal async Task<string?> BeginAsync()
    {
        var eco = Bot.Economy;

        if (_friendlyStake && P2 != null)
        {
            // a small wager, never more than either player can pay
            var b1 = await eco.GetBalanceAsync(P1.Id, P1.Username);
            var b2 = await eco.GetBalanceAsync(P2.Id, P2.Username);
            Stake = Math.Min(eco.Scaled(10), Math.Min(b1, b2));
        }

        if (Stake > 0)
        {
            if (!await eco.ReserveAsync(Id, P1.Id, Stake))
                return $"{P1.Username} can't afford a {Fmt.J(Stake)} stake.";

            if (P2 != null && !await eco.ReserveAsync(Id, P2.Id, Stake))
            {
                await eco.SettleAsync(Id, P1.Id, Stake, "refund");
                return $"{P2.Username} can't afford a {Fmt.J(Stake)} stake.";
            }
        }

        _started = true;
        Phase = Phase.Playing;
        await OnStartAsync();
        return null;
    }

    /// <summary>Entry point for every button / menu click on this game's message.</summary>
    public async Task HandleAsync(SocketMessageComponent c, string action)
    {
        await Gate.WaitAsync();
        try
        {
            switch (Phase)
            {
                case Phase.Pending:
                    await HandlePendingAsync(c, action);
                    break;

                case Phase.Playing:
                    if (!IsPlayer(c.User.Id))
                    {
                        await c.RespondAsync($"This isn't your game. Start your own with `{Config.Prefix}{Key}`.", ephemeral: true);
                        break;
                    }

                    ResetTimeout();
                    await OnActionAsync(c, action);
                    break;

                default:
                    await c.DeferAsync();
                    break;
            }
        }
        finally
        {
            Gate.Release();
        }
    }

    private async Task HandlePendingAsync(SocketMessageComponent c, string action)
    {
        var isP1 = c.User.Id == P1.Id;
        var isP2 = c.User.Id == P2!.Id;

        if (action == "decline" && (isP1 || isP2))
        {
            Phase = Phase.Done;
            ResultText = isP1 ? "Challenge withdrawn." : "Challenge declined.";
            Stop();
            await UpdateAsync(c);
        }
        else if (action == "accept" && isP2)
        {
            var error = await BeginAsync();
            if (error != null)
            {
                Phase = Phase.Done;
                ResultText = "❌ " + error;
                Stop();
            }
            else if (!Over)
            {
                ResetTimeout();
            }

            await UpdateAsync(c);
        }
        else
        {
            await c.RespondAsync(isP1 ? "Waiting for your opponent to accept." : $"Only {P2.Username} can answer this challenge.",
                ephemeral: true);
        }
    }

    // -------------------------------------------------------------- timeouts

    protected void ResetTimeout(int seconds = Config.ViewTimeoutSeconds)
    {
        var cts = new CancellationTokenSource();
        Interlocked.Exchange(ref _timeout, cts)?.Cancel();

        _ = Task.Run(async () =>
        {
            try { await Task.Delay(TimeSpan.FromSeconds(seconds), cts.Token); }
            catch (OperationCanceledException) { return; }
            await OnTimeoutAsync();
        });
    }

    protected void Stop()
    {
        Interlocked.Exchange(ref _timeout, null)?.Cancel();
        _onDone(Id);
    }

    private async Task OnTimeoutAsync()
    {
        await Gate.WaitAsync();
        try
        {
            if (Phase == Phase.Done) return;

            if (Phase == Phase.Pending)
            {
                Phase = Phase.Done;
                ResultText = "⏰ Challenge expired.";
                Stop();
            }
            else
            {
                await ForfeitAsync(TurnPlayer, "⏰ Timed out.", penalize: false);
            }

            await EditAsync();
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>
    /// A player gives up (or runs out of time). Against a human the opponent wins. Against the bot the player loses
    /// if they quit on purpose or had J$ at stake; a plain timeout in a free game just ends it with no changes.
    /// </summary>
    protected async Task ForfeitAsync(int player, string reason, bool penalize)
    {
        if (Phase == Phase.Done) return;

        if (!VsBot)
        {
            await FinishAsync(player == 1 ? Side.Player2 : Side.Player1, reason);
        }
        else if (penalize || Stake > 0)
        {
            await FinishAsync(Side.Player2, reason);
        }
        else
        {
            Phase = Phase.Done;
            ResultText = reason + " No changes to your balance.";
            Stop();
        }
    }

    // ------------------------------------------------------------ settlement

    /// <summary>Ends the game: pays out stakes or rewards, records stats, and stores the result text.</summary>
    protected async Task FinishAsync(Side side, string? detail = null)
    {
        if (Phase == Phase.Done) return;
        Phase = Phase.Done;

        var lines = new List<string>();
        if (!string.IsNullOrEmpty(detail)) lines.Add(detail);

        try
        {
            lines.AddRange(await SettleAsync(side));
        }
        catch (Exception e)
        {
            Logger.Error("Settlement failed", e);
            lines.Add("⚠️ Couldn't update balances.");
        }

        ResultText = string.Join("\n", lines);
        Stop();
    }

    private static Outcome OutcomeFor(Side side, int player) => side switch
    {
        Side.Draw => Outcome.Draw,
        Side.Player1 => player == 1 ? Outcome.Win : Outcome.Loss,
        _ => player == 2 ? Outcome.Win : Outcome.Loss,
    };

    private async Task<List<string>> SettleAsync(Side side)
    {
        var eco = Bot.Economy;
        var lines = new List<string>();

        if (Stake > 0 && VsBot)
        {
            var payout = BotPayout(side);
            await eco.SettleAsync(Id, P1.Id, payout);
            var net = payout - Stake;
            await eco.RecordAsync(P1.Id, Key, OutcomeFor(side, 1), net);
            lines.Add($"{P1.Mention}: **{Fmt.Signed(net)}**");
        }
        else if (Stake > 0)
        {
            var pot = Stake * 2;
            var p1 = side == Side.Player1 ? pot : side == Side.Draw ? Stake : 0;
            var p2 = side == Side.Player2 ? pot : side == Side.Draw ? Stake : 0;

            await eco.SettleAsync(Id, P1.Id, p1);
            await eco.SettleAsync(Id, P2!.Id, p2);
            await eco.RecordAsync(P1.Id, Key, OutcomeFor(side, 1), p1 - Stake);
            await eco.RecordAsync(P2.Id, Key, OutcomeFor(side, 2), p2 - Stake);

            lines.Add(side == Side.Draw
                ? "🤝 Stakes returned."
                : $"{PlayerLabel(side == Side.Player1 ? 1 : 2)} wins **{Fmt.J(Stake)}** from {PlayerLabel(side == Side.Player1 ? 2 : 1)}.");
        }
        else if (VsBot)
        {
            long delta = 0;
            if (BotWinReward > 0)
            {
                var amount = eco.Scaled(BotWinReward);
                delta = side == Side.Player1 ? amount : side == Side.Player2 ? -((amount + 1) / 2) : 0;
            }

            var applied = delta == 0 ? 0 : await eco.AdjustAsync(P1.Id, delta, "game", Key) ?? 0;
            await eco.RecordAsync(P1.Id, Key, OutcomeFor(side, 1), applied);
            if (BotWinReward > 0 && side != Side.Draw) lines.Add($"{P1.Mention}: **{Fmt.Signed(applied)}**");
        }
        else
        {
            // two players who were too broke for a wager: just keep the score
            await eco.RecordAsync(P1.Id, Key, OutcomeFor(side, 1), 0);
            await eco.RecordAsync(P2!.Id, Key, OutcomeFor(side, 2), 0);
        }

        return lines;
    }
}
