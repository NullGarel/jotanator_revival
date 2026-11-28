// VIBE CODED DSICLAIMER - CLAUDE GENERATED AS OF 26-11-28 @ 19:27 UTC
using Discord;
using Discord.WebSocket;

namespace Jotanator;

/// <summary>
/// Unifies a prefix message and a slash interaction (what discord.py calls a hybrid Context),
/// so each command is written once and works as both "j.name" and "/name".
/// </summary>
public sealed class CommandContext : IDisposable
{
    public JotaBot Bot { get; }
    public CommandDef Command { get; }
    public IUser Author { get; }
    public SocketGuild? Guild { get; }
    public IMessageChannel? Channel { get; }
    public SocketUserMessage? Message { get; }
    public SocketSlashCommand? Interaction { get; }
    public string CleanPrefix { get; }
    public Args Args { get; internal set; } = new();

    private bool _responded;
    private IDisposable? _typing;

    internal CommandContext(JotaBot bot, CommandDef command, SocketUserMessage message, SocketGuild? guild, string cleanPrefix)
    {
        Bot = bot;
        Command = command;
        Message = message;
        Author = message.Author;
        Guild = guild;
        Channel = message.Channel;
        CleanPrefix = cleanPrefix;
    }

    internal CommandContext(JotaBot bot, CommandDef command, SocketSlashCommand interaction, SocketGuild? guild)
    {
        Bot = bot;
        Command = command;
        Interaction = interaction;
        Author = interaction.User;
        Guild = guild;
        Channel = interaction.Channel;
        CleanPrefix = "/";
    }

    /// <summary>Slash: defers the interaction. Prefix: shows the typing indicator until the command finishes.</summary>
    public async Task DeferAsync()
    {
        if (Interaction != null)
        {
            if (_responded) return;
            _responded = true;
            await Interaction.DeferAsync();
        }
        else if (Channel != null && _typing == null)
        {
            _typing = Channel.EnterTypingState();
        }
    }

    /// <summary>
    /// Sends a reply. "ephemeral" only has an effect for slash commands (same as discord.py).
    /// Set wantMessage when you need the sent message back (e.g. to edit it later).
    /// </summary>
    public async Task<IUserMessage?> SendAsync(
        string? text = null,
        Embed? embed = null,
        MessageComponent? components = null,
        bool ephemeral = false,
        AllowedMentions? allowedMentions = null,
        bool wantMessage = false)
    {
        if (Interaction != null)
        {
            if (!_responded)
            {
                _responded = true;
                await Interaction.RespondAsync(text, embed: embed, components: components,
                    ephemeral: ephemeral, allowedMentions: allowedMentions);
                return wantMessage ? await Interaction.GetOriginalResponseAsync() : null;
            }

            return await Interaction.FollowupAsync(text, embed: embed, components: components,
                ephemeral: ephemeral, allowedMentions: allowedMentions);
        }

        if (Channel == null) return null;
        return await Channel.SendMessageAsync(text, embed: embed, components: components,
            allowedMentions: allowedMentions);
    }

    /// <summary>Sends one or more files (as attachments) with an optional text.</summary>
    public async Task<IUserMessage?> SendFilesAsync(
        IEnumerable<FileAttachment> files,
        string? text = null,
        bool ephemeral = false,
        AllowedMentions? allowedMentions = null)
    {
        var list = files.ToList();

        if (Interaction != null)
        {
            if (!_responded)
            {
                _responded = true;
                await Interaction.RespondWithFilesAsync(list, text, ephemeral: ephemeral, allowedMentions: allowedMentions);
                return null;
            }

            return await Interaction.FollowupWithFilesAsync(list, text, ephemeral: ephemeral, allowedMentions: allowedMentions);
        }

        if (Channel == null) return null;
        return await Channel.SendFilesAsync(list, text, allowedMentions: allowedMentions);
    }

    /// <summary>The channel the command was used in, fetching it if it isn't cached (slash commands).</summary>
    public async Task<IMessageChannel?> GetChannelAsync()
    {
        if (Channel != null) return Channel;
        var id = Interaction?.ChannelId;
        if (id == null) return null;
        return await Bot.Client.GetChannelAsync(id.Value) as IMessageChannel;
    }

    public void Dispose()
    {
        _typing?.Dispose();
        _typing = null;
    }
}
