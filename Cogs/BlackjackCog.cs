// VIBE CODED DSICLAIMER - CLAUDE GENERATED AS OF 26-11-28 @ 19:27 UTC
using Discord;
using Discord.WebSocket;

namespace Jotanator;

/// <summary>Blackjack for J$: against the bot as dealer, or a 21 duel against another member.</summary>
public sealed class BlackjackCog : GameCog
{
    protected override string Key => "bj";

    public override IEnumerable<CommandDef> BuildCommands()
    {
        yield return new CommandDef
        {
            Name = "blackjack",
            Aliases = ["bj", "pontoon", "21"],
            Description = "Play blackjack for J$ against the bot, or a 21 duel against a member",
            Help = "Get as close to 21 as you can without going over. Against me, I'm the dealer (I stand on 17): " +
                   "a win pays 1:1 and a natural blackjack pays 3:2. " +
                   "Mention a member for a duel: you both get hidden hands (see yours with **View hand**), " +
                   "play one after the other, and the closest to 21 takes the pot.\n" +
                   "Examples: `j.bj`, `j.bj 50`, `j.bj 50 @friend`",
            Params = [GameParams.Bet(), GameParams.Opponent()],
            Cooldowns = [new CooldownSpec(1, 5, BucketType.User)],
            Handler = PlayAsync,
        };
    }

    private async Task PlayAsync(CommandContext ctx)
    {
        var bet = ctx.Args.Int("bet") ?? 10;
        var opponent = ResolveOpponent(ctx, ctx.Args.User("opponent"));
        await LaunchAsync(ctx, new BlackjackSession(Bot, ctx.Author, opponent, bet, Remove));
    }
}

internal sealed class BlackjackSession : GameSession
{
    private readonly Deck _deck = new();
    private readonly List<Card> _h1 = new();
    private readonly List<Card> _h2 = new();   // the dealer's hand, or player 2's
    private int _turn = 1;
    private bool _natural;

    public BlackjackSession(JotaBot bot, IUser p1, IUser? p2, long bet, Action<string> onDone)
        : base(bot, "bj", "Blackjack", p1, p2, stake: bet, friendlyStake: false, onDone)
    {
    }

    protected override int TurnPlayer => _turn;

    /// <summary>A natural blackjack against the dealer pays 3:2; other wins pay 1:1.</summary>
    protected override long BotPayout(Side side) => side switch
    {
        Side.Player1 => _natural ? Stake + Stake * 3 / 2 : Stake * 2,
        Side.Draw => Stake,
        _ => 0,
    };

    protected override async Task OnStartAsync()
    {
        _h1.Add(_deck.Draw());
        _h2.Add(_deck.Draw());
        _h1.Add(_deck.Draw());
        _h2.Add(_deck.Draw());

        var natural1 = Blackjack.IsBlackjack(_h1);
        var natural2 = Blackjack.IsBlackjack(_h2);

        if (natural1 && natural2)
        {
            await FinishAsync(Side.Draw, "🃏 Both have blackjack: push!");
        }
        else if (natural1)
        {
            _natural = VsBot;
            await FinishAsync(Side.Player1, $"🃏 **Blackjack!** {P1.Mention} wins.");
        }
        else if (natural2)
        {
            await FinishAsync(Side.Player2, $"🃏 **Blackjack** for {PlayerLabel(2)}!");
        }
    }

    private List<Card> HandOf(int player) => player == 1 ? _h1 : _h2;

    private string HandText(int player)
    {
        var hand = HandOf(player);
        return $"{Blackjack.Show(hand)} (**{Blackjack.Value(hand).Total}**)";
    }

    protected override string Content()
    {
        string lines;

        if (VsBot)
        {
            var dealer = Over ? HandText(2) : $"`{_h2[0]}` `🂠`";
            lines = $"{P1.Mention}: {HandText(1)}\n{PlayerLabel(2)} (dealer): {dealer}";
        }
        else if (Over)
        {
            lines = $"{P1.Mention}: {HandText(1)}\n{P2!.Mention}: {HandText(2)}";
        }
        else
        {
            string Status(int p) => _turn == p ? "🤔 deciding" : _turn > p ? "✋ stands" : "⏳ waiting";
            lines = $"{P1.Mention}: 2+ cards, {Status(1)}\n{P2!.Mention}: 2+ cards, {Status(2)}";
        }

        var footer = Over ? ResultText
            : VsBot ? "Hit or stand?"
            : $"{PlayerLabel(_turn)}, hit or stand? Use **View hand** to see your cards.";

        return $"{Header()}\n{lines}\n{footer}";
    }

    protected override MessageComponent Components()
    {
        var builder = new ComponentBuilder()
            .WithButton("Hit", CId("hit"), ButtonStyle.Primary, new Emoji("🃏"), disabled: Over)
            .WithButton("Stand", CId("stand"), ButtonStyle.Success, new Emoji("✋"), disabled: Over);

        if (!VsBot)
            builder.WithButton("View hand", CId("view"), ButtonStyle.Secondary, new Emoji("👁️"), disabled: Over);

        return builder.Build();
    }

    protected override async Task OnActionAsync(SocketMessageComponent c, string action)
    {
        var clicker = PlayerNumber(c.User.Id);

        if (action == "view")
        {
            await c.RespondAsync($"Your hand: {HandText(clicker)}", ephemeral: true);
            return;
        }

        if (action is not ("hit" or "stand") || !await RequireTurnAsync(c, _turn)) return;

        if (action == "hit")
        {
            var hand = HandOf(_turn);
            hand.Add(_deck.Draw());
            var total = Blackjack.Value(hand).Total;

            if (total > 21)
            {
                await FinishAsync(_turn == 1 ? Side.Player2 : Side.Player1, $"💥 {PlayerLabel(_turn)} busts with {total}!");
            }
            else if (total == 21)
            {
                await StandAsync();
            }
        }
        else
        {
            await StandAsync();
        }

        await UpdateAsync(c);

        // in a duel the cards stay secret, so the player who just drew gets their hand privately
        if (!VsBot && !Over)
            await c.FollowupAsync($"Your hand: {HandText(clicker)}", ephemeral: true);
    }

    private async Task StandAsync()
    {
        if (VsBot)
        {
            while (Blackjack.Value(_h2).Total < 17) _h2.Add(_deck.Draw());

            var player = Blackjack.Value(_h1).Total;
            var dealer = Blackjack.Value(_h2).Total;

            if (dealer > 21) await FinishAsync(Side.Player1, $"🏆 The dealer busts with {dealer}! You win.");
            else if (player > dealer) await FinishAsync(Side.Player1, $"🏆 {player} beats {dealer}. You win!");
            else if (player < dealer) await FinishAsync(Side.Player2, $"The dealer wins with {dealer} against your {player}.");
            else await FinishAsync(Side.Draw, $"🤝 Push: you both have {player}.");
        }
        else if (_turn == 1)
        {
            _turn = 2;
        }
        else
        {
            var t1 = Blackjack.Value(_h1).Total;
            var t2 = Blackjack.Value(_h2).Total;

            if (t1 > t2) await FinishAsync(Side.Player1, $"🏆 {P1.Mention} wins with {t1} against {t2}!");
            else if (t2 > t1) await FinishAsync(Side.Player2, $"🏆 {P2!.Mention} wins with {t2} against {t1}!");
            else await FinishAsync(Side.Draw, $"🤝 Push: you both have {t1}.");
        }
    }
}
