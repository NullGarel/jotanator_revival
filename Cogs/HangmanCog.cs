// VIBE CODED DSICLAIMER - CLAUDE GENERATED AS OF 26-11-28 @ 19:27 UTC
using Discord;
using Discord.WebSocket;

namespace Jotanator;

/// <summary>Hangman with letter menus. English or Portuguese words.</summary>
public sealed class HangmanCog : GameCog
{
    protected override string Key => "hang";

    private static readonly string[] Languages = ["en", "pt"];

    public override IEnumerable<CommandDef> BuildCommands()
    {
        yield return new CommandDef
        {
            Name = "hangman",
            Aliases = ["hang"],
            Description = "Guess the word before the drawing is finished",
            Help = "Pick letters from the two menus. Six wrong guesses and you lose. Language: en (default) or pt.\n" +
                   "Examples: `j.hangman`, `j.hangman pt`",
            Params =
            [
                new ParamDef("language", "en (English, default) or pt (Portuguese)", Required: false,
                    Autocomplete: (_, current) => Languages.Where(l => l.Contains(current, StringComparison.OrdinalIgnoreCase))),
            ],
            Cooldowns = [new CooldownSpec(1, 5, BucketType.User)],
            Handler = PlayAsync,
        };
    }

    private async Task PlayAsync(CommandContext ctx)
    {
        var raw = ctx.Args.Str("language")?.Trim().ToLowerInvariant();
        var language = string.IsNullOrEmpty(raw) || raw is "en" or "english" ? "en"
            : raw is "pt" or "pt-br" or "ptbr" or "portuguese" or "portugues" or "português" ? "pt"
            : throw new BadArgumentException("Language must be `en` or `pt`.");

        await LaunchAsync(ctx, new HangmanSession(Bot, ctx.Author, language, Remove));
    }
}

internal sealed class HangmanSession : GameSession
{
    private readonly HangmanGame _game;
    private readonly string _language;

    public HangmanSession(JotaBot bot, IUser player, string language, Action<string> onDone)
        : base(bot, "hang", "Hangman", player, null, stake: 0, friendlyStake: false, onDone)
    {
        _language = language;
        _game = new HangmanGame(HangmanGame.RandomWord(language));
    }

    protected override long BotWinReward => 8;

    protected override string Content()
    {
        var wrong = string.Join(" ", _game.WrongLetters.Select(c => char.ToUpperInvariant(c)));
        var text = $"{Header()} ({_language})\n```\n{_game.Gallows()}\n```\n" +
                   $"`{_game.Mask(reveal: Over)}`\n" +
                   $"Wrong ({_game.Wrong}/{HangmanGame.MaxWrong}): {(wrong.Length == 0 ? "none" : wrong)}";
        return Over ? $"{text}\n{ResultText}" : text;
    }

    private SelectMenuBuilder? Menu(string action, string placeholder, char from, char to)
    {
        var letters = Enumerable.Range(from, to - from + 1).Select(i => (char)i).Where(ch => !_game.Has(ch)).ToList();
        if (letters.Count == 0) return null;

        var menu = new SelectMenuBuilder()
            .WithCustomId(CId(action))
            .WithPlaceholder(placeholder)
            .WithMinValues(1)
            .WithMaxValues(1);

        foreach (var ch in letters)
            menu.AddOption(label: char.ToUpperInvariant(ch).ToString(), value: ch.ToString());

        return menu;
    }

    protected override MessageComponent Components()
    {
        var builder = new ComponentBuilder();
        var row = 0;

        if (!Over)
        {
            foreach (var menu in new[] { Menu("a", "Letters A-M", 'a', 'm'), Menu("b", "Letters N-Z", 'n', 'z') })
                if (menu != null) builder.WithSelectMenu(menu, row: row++);
        }

        builder.WithButton("Give up", CId("quit"), ButtonStyle.Secondary, disabled: Over, row: row);
        return builder.Build();
    }

    protected override async Task OnActionAsync(SocketMessageComponent c, string action)
    {
        if (action == "quit")
        {
            await ForfeitAsync(1, $"🏳️ You gave up. The word was **{_game.Word.ToUpperInvariant()}**.", penalize: true);
            await UpdateAsync(c);
            return;
        }

        if (action is not ("a" or "b") || c.Data.Values.FirstOrDefault() is not { Length: 1 } letter)
        {
            await c.DeferAsync();
            return;
        }

        _game.Guess(letter[0]);

        if (_game.Won)
            await FinishAsync(Side.Player1, $"🏆 You got it! The word was **{_game.Word.ToUpperInvariant()}**.");
        else if (_game.Lost)
            await FinishAsync(Side.Player2, $"💀 Out of guesses. The word was **{_game.Word.ToUpperInvariant()}**.");

        await UpdateAsync(c);
    }
}
