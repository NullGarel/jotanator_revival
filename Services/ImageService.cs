// VIBE CODED DSICLAIMER - CLAUDE GENERATED AS OF 26-11-28 @ 19:27 UTC
using Discord;
using SixLabors.ImageSharp;
using Image = SixLabors.ImageSharp.Image;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Jotanator;

/// <summary>Finds the image a command should work on, downloads it safely, and edits it with ImageSharp.</summary>
public sealed class ImageService
{
    /// <summary>Largest input we'll download.</summary>
    public const long MaxDownloadBytes = 20 * 1024 * 1024;

    /// <summary>Largest input in pixels (stops "decompression bombs").</summary>
    public const long MaxPixels = 25_000_000;

    public const int MaxFrames = 200;

    private const int HistoryDepth = 50;

    private readonly HttpClient _http;
    private readonly SemaphoreSlim _busy = new(2, 2);   // at most two edits at the same time

    public ImageService(HttpClient http) => _http = http;

    // ------------------------------------------------------------ finding

    /// <summary>
    /// The image to edit: an attachment on the command, else on the message being replied to,
    /// else the latest image posted in the channel. Returns null if there isn't one.
    /// </summary>
    public async Task<string?> FindSourceUrlAsync(CommandContext ctx, string attachmentParam = "image")
    {
        if (ctx.Args.Attachment(attachmentParam) is { } attachment && IsImage(attachment))
            return attachment.Url;

        var channel = await ctx.GetChannelAsync();
        if (channel == null) return null;

        // a reply: "j.neg" while replying to a picture
        if (ctx.Message?.Reference?.MessageId.GetValueOrDefault() is ulong replyId and not 0)
        {
            try
            {
                var replied = await channel.GetMessageAsync(replyId);
                if (replied != null && ImageUrlOf(replied) is { } fromReply) return fromReply;
            }
            catch
            {
                // no permission to read it; fall through to the history
            }
        }

        try
        {
            var recent = await channel.GetMessagesAsync(HistoryDepth).FlattenAsync();
            foreach (var message in recent.OrderByDescending(m => m.Timestamp))
            {
                if (ctx.Message != null && message.Id == ctx.Message.Id) continue;
                if (ImageUrlOf(message) is { } url) return url;
            }
        }
        catch
        {
            // missing Read Message History
        }

        return null;
    }

    private static bool IsImage(IAttachment a) =>
        a.ContentType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true || a.Width.HasValue;

    /// <summary>An image URL from a message's attachments, or from its embeds (proxied by Discord).</summary>
    private static string? ImageUrlOf(IMessage message)
    {
        var attachment = message.Attachments.FirstOrDefault(IsImage);
        if (attachment != null) return attachment.Url;

        foreach (var embed in message.Embeds)
        {
            if (embed.Image is { } image) return image.ProxyUrl ?? image.Url;
            if (embed.Type is EmbedType.Image or EmbedType.Gifv && embed.Thumbnail is { } thumb) return thumb.ProxyUrl ?? thumb.Url;
        }

        return null;
    }

    // ---------------------------------------------------------- downloading

    /// <summary>Only Discord's own CDN hosts are fetched (attachments and Discord-proxied embeds).</summary>
    public static bool IsAllowedHost(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return false;
        var host = uri.Host.ToLowerInvariant();
        return host.EndsWith(".discordapp.com", StringComparison.Ordinal)
               || host.EndsWith(".discordapp.net", StringComparison.Ordinal)
               || host is "discordapp.com" or "discordapp.net";
    }

    public async Task<byte[]> DownloadAsync(string url)
    {
        if (!IsAllowedHost(url)) throw new BadArgumentException("I can only work with images that are hosted on Discord.");

        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode) throw new BadArgumentException("I couldn't download that image.");

        if (response.Content.Headers.ContentLength > MaxDownloadBytes)
            throw new BadArgumentException($"That image is too big (limit {MaxDownloadBytes / 1024 / 1024} MB).");

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > MaxDownloadBytes)
                throw new BadArgumentException($"That image is too big (limit {MaxDownloadBytes / 1024 / 1024} MB).");
        }

        return buffer.ToArray();
    }

    // -------------------------------------------------------------- editing

    /// <summary>
    /// Applies an edit to every frame and encodes the result: GIF for animated images (or when asked), PNG otherwise.
    /// Returns the file bytes and its extension.
    /// </summary>
    public async Task<(byte[] Data, string Extension)> ProcessAsync(
        byte[] input, Action<Image<Rgba32>> edit, bool forceGif = false)
    {
        await _busy.WaitAsync();
        try
        {
            return await Task.Run(() => Process(input, edit, forceGif));
        }
        finally
        {
            _busy.Release();
        }
    }

    public static (byte[] Data, string Extension) Process(byte[] input, Action<Image<Rgba32>> edit, bool forceGif)
    {
        ImageInfo info;
        try
        {
            info = Image.Identify(input);
        }
        catch (Exception e) when (e is UnknownImageFormatException or InvalidImageContentException or NotSupportedException)
        {
            throw new BadArgumentException("That doesn't look like an image I can read.");
        }

        if ((long)info.Width * info.Height > MaxPixels)
            throw new BadArgumentException("That image has too many pixels for me.");
        if (info.FrameMetadataCollection.Count > MaxFrames)
            throw new BadArgumentException($"That animation has too many frames (limit {MaxFrames}).");

        Image<Rgba32> image;
        try
        {
            image = Image.Load<Rgba32>(input);
        }
        catch (Exception e) when (e is UnknownImageFormatException or InvalidImageContentException or NotSupportedException)
        {
            throw new BadArgumentException("That doesn't look like an image I can read.");
        }

        using (image)
        {
            edit(image);

            using var output = new MemoryStream();
            var animated = image.Frames.Count > 1;
            if (animated || forceGif)
            {
                image.SaveAsGif(output);
                return (output.ToArray(), "gif");
            }

            image.SaveAsPng(output);
            return (output.ToArray(), "png");
        }
    }

    // ------------------------------------------------------------- the edits

    public static void FlipHorizontal(Image<Rgba32> image) => image.Mutate(x => x.Flip(FlipMode.Horizontal));

    public static void FlipVertical(Image<Rgba32> image) => image.Mutate(x => x.Flip(FlipMode.Vertical));

    public static void Negative(Image<Rgba32> image) => image.Mutate(x => x.Invert());

    public static void Grayscale(Image<Rgba32> image) => image.Mutate(x => x.Grayscale());

    public static void RotateHue(Image<Rgba32> image, float degrees) => image.Mutate(x => x.Hue(degrees));

    /// <summary>Keeps only the chosen channel (0 = red, 1 = green, 2 = blue); the other two become 0 on every frame.</summary>
    public static void KeepChannel(Image<Rgba32> image, int channel)
    {
        foreach (var frame in image.Frames)
        {
            frame.ProcessPixelRows(accessor =>
            {
                for (var y = 0; y < accessor.Height; y++)
                {
                    foreach (ref var pixel in accessor.GetRowSpan(y))
                    {
                        if (channel != 0) pixel.R = 0;
                        if (channel != 1) pixel.G = 0;
                        if (channel != 2) pixel.B = 0;
                    }
                }
            });
        }
    }
}
