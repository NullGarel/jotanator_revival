// VIBE CODED DSICLAIMER - CLAUDE GENERATED AS OF 26-11-28 @ 19:27 UTC
using System.Text;
using Discord;

namespace Jotanator;

/// <summary>Grab avatars, emoji and server icons as image files.</summary>
public sealed class UtilsCog : Cog
{
    private const string Cdn = "https://cdn.discordapp.com";

    /// <summary>Sizes to try, biggest first, until the file fits in the server's upload limit.</summary>
    private static readonly int[] Sizes = [4096, 1024, 256];

    public override string Category => "Utilities";

    public override IEnumerable<CommandDef> BuildCommands()
    {
        yield return new CommandDef
        {
            Name = "av",
            Aliases = ["avatar"],
            Description = "Get someone's avatar as an image file",
            Help = "Sends the avatar as a PNG (or GIF if it's animated). If the member has a different avatar just for this server, you get both.\n" +
                   "Examples: `j.av`, `j.av @someone`",
            Params = [new ParamDef("user", "Whose avatar (default: you)", ParamKind.User, Required: false)],
            Cooldowns = [new CooldownSpec(1, 3, BucketType.User)],
            Handler = AvatarAsync,
        };

        yield return new CommandDef
        {
            Name = "e",
            Aliases = ["emoji"],
            Description = "Get a custom emoji (or a standard one) as an image file",
            Help = "Sends the emoji as a PNG, or GIF if it's animated. Works with custom server emoji and ordinary Unicode emoji.\n" +
                   "Example: `j.e :pepe:` (use the emoji itself, not just its name)",
            Params = [new ParamDef("emoji", "The emoji to download")],
            Cooldowns = [new CooldownSpec(1, 3, BucketType.User)],
            Handler = EmojiAsync,
        };

        yield return new CommandDef
        {
            Name = "sav",
            Aliases = ["servericon"],
            Description = "Get this server's icon as an image file",
            Help = "Sends the server icon as a PNG (or GIF if it's animated).",
            GuildOnly = true,
            Cooldowns = [new CooldownSpec(1, 3, BucketType.User)],
            Handler = ServerIconAsync,
        };
    }

    /// <summary>The largest version of a CDN image that fits the upload limit, or null if none does.</summary>
    private async Task<byte[]?> DownloadAsync(CommandContext ctx, string urlWithoutSize)
    {
        var limit = (long)(ctx.Guild?.MaxUploadLimit ?? 10UL * 1024 * 1024);

        foreach (var size in Sizes)
        {
            var url = urlWithoutSize + (urlWithoutSize.Contains('?') ? "&" : "?") + $"size={size}";
            try
            {
                using var response = await Bot.Http.GetAsync(url);
                if (!response.IsSuccessStatusCode) continue;

                var bytes = await response.Content.ReadAsByteArrayAsync();
                if (bytes.Length <= limit) return bytes;
            }
            catch (HttpRequestException)
            {
                // try the next size
            }
        }

        return null;
    }

    private static string Extension(string? hash) => hash != null && hash.StartsWith("a_", StringComparison.Ordinal) ? "gif" : "png";

    private static string SafeName(string name)
    {
        var cleaned = new string(name.Where(ch => char.IsLetterOrDigit(ch) || ch is '_' or '-').ToArray());
        return cleaned.Length == 0 ? "image" : cleaned;
    }

    private async Task AvatarAsync(CommandContext ctx)
    {
        await ctx.DeferAsync();

        var user = ctx.Args.User("user") ?? ctx.Author;
        var files = new List<FileAttachment>();
        var names = new List<string>();

        var globalUrl = user.AvatarId != null
            ? $"{Cdn}/avatars/{user.Id}/{user.AvatarId}.{Extension(user.AvatarId)}"
            : user.GetDefaultAvatarUrl();
        var global = await DownloadAsync(ctx, globalUrl);
        if (global != null)
        {
            var ext = user.AvatarId != null ? Extension(user.AvatarId) : "png";
            files.Add(new FileAttachment(new MemoryStream(global), $"{SafeName(user.Username)}_avatar.{ext}"));
            names.Add("avatar");
        }

        // a server-specific avatar (Nitro) as well, when there is one
        if (ctx.Guild != null)
        {
            var member = user as IGuildUser ?? ctx.Guild.GetUser(user.Id);
            if (member?.GuildAvatarId is string hash)
            {
                var local = await DownloadAsync(ctx, $"{Cdn}/guilds/{ctx.Guild.Id}/users/{user.Id}/avatars/{hash}.{Extension(hash)}");
                if (local != null)
                {
                    files.Add(new FileAttachment(new MemoryStream(local), $"{SafeName(user.Username)}_server_avatar.{Extension(hash)}"));
                    names.Add("server avatar");
                }
            }
        }

        if (files.Count == 0)
        {
            await ctx.SendAsync("Couldn't download that avatar.");
            return;
        }

        await ctx.SendFilesAsync(files, $"**{Format.Sanitize(user.Username)}**'s {string.Join(" and ", names)}", allowedMentions: AllowedMentions.None);
    }

    private async Task EmojiAsync(CommandContext ctx)
    {
        await ctx.DeferAsync();

        var raw = (ctx.Args.Str("emoji") ?? "").Trim();
        string url;
        string fileName;

        if (Emote.TryParse(raw, out var custom))
        {
            url = $"{Cdn}/emojis/{custom.Id}.{(custom.Animated ? "gif" : "png")}";
            fileName = $"{SafeName(custom.Name)}.{(custom.Animated ? "gif" : "png")}";
        }
        else if (TwemojiCode(raw) is string code)
        {
            url = $"https://cdn.jsdelivr.net/gh/twitter/twemoji@14.0.2/assets/72x72/{code}.png";
            fileName = $"emoji_{code}.png";
        }
        else
        {
            throw new BadArgumentException("Send the emoji itself, like a custom server emoji or a normal one such as 😀.");
        }

        var bytes = await DownloadAsync(ctx, url);
        if (bytes == null)
        {
            await ctx.SendAsync("Couldn't download that emoji.");
            return;
        }

        await ctx.SendFilesAsync([new FileAttachment(new MemoryStream(bytes), fileName)]);
    }

    /// <summary>"😀" -> "1f600" (Twemoji file name). Null if the text isn't an emoji.</summary>
    private static string? TwemojiCode(string text)
    {
        if (text.Length == 0) return null;

        var runes = text.EnumerateRunes().Where(r => r.Value != 0xFE0F).ToList();
        if (runes.Count == 0 || runes.Count > 12) return null;

        // must look like an emoji: pictographs, keycaps, flags or similar symbols
        var first = runes[0].Value;
        var looksLikeEmoji = first >= 0x1F000 || first is >= 0x2190 and <= 0x2BFF || first is '#' or '*' or >= '0' and <= '9' && runes.Count > 1;
        if (!looksLikeEmoji) return null;

        return string.Join("-", runes.Select(r => r.Value.ToString("x")));
    }

    private async Task ServerIconAsync(CommandContext ctx)
    {
        var guild = ctx.Guild!;
        if (guild.IconId == null)
        {
            await ctx.SendAsync("This server has no icon.", ephemeral: true);
            return;
        }

        await ctx.DeferAsync();
        var ext = Extension(guild.IconId);
        var bytes = await DownloadAsync(ctx, $"{Cdn}/icons/{guild.Id}/{guild.IconId}.{ext}");
        if (bytes == null)
        {
            await ctx.SendAsync("Couldn't download the server icon.");
            return;
        }

        await ctx.SendFilesAsync([new FileAttachment(new MemoryStream(bytes), $"{SafeName(guild.Name)}_icon.{ext}")],
            $"**{Format.Sanitize(guild.Name)}**'s icon", allowedMentions: AllowedMentions.None);
    }
}
