// VIBE CODED DSICLAIMER - CLAUDE GENERATED AS OF 26-11-28 @ 19:27 UTC
using Discord;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Jotanator;

/// <summary>
/// Image edits. Each command works on an attached image, the image of the message you reply to,
/// or the latest image posted in the channel. Animated GIFs keep their animation.
/// </summary>
public sealed class ImageToolsCog : Cog
{
    private ImageService? _images;

    public override string Category => "Images";

    private ImageService Images => _images ??= new ImageService(Bot.Http);

    public override IEnumerable<CommandDef> BuildCommands()
    {
        yield return Tool("togif", "Convert an image to a GIF", "Converts an image (PNG, JPG, WebP, animated WebP...) to a GIF.",
            (_, _) => { }, forceGif: true);

        yield return Tool("fliph", "Flip an image horizontally (mirror it)", "Mirrors the image left to right.",
            (img, _) => ImageService.FlipHorizontal(img));

        yield return Tool("flipv", "Flip an image vertically (upside down)", "Flips the image top to bottom.",
            (img, _) => ImageService.FlipVertical(img));

        yield return Tool("neg", "Invert the colours of an image", "Turns the image into its negative.",
            (img, _) => ImageService.Negative(img), aliases: ["negative", "invert"]);

        yield return Tool("grayscale", "Make an image black and white", "Converts the image to shades of grey.",
            (img, _) => ImageService.Grayscale(img), aliases: ["gray", "greyscale"]);

        yield return Tool("rchannel", "Keep only the red channel", "Zeroes out the green and blue channels so only red is left.",
            (img, _) => ImageService.KeepChannel(img, 0), aliases: ["red"]);

        yield return Tool("gchannel", "Keep only the green channel", "Zeroes out the red and blue channels so only green is left.",
            (img, _) => ImageService.KeepChannel(img, 1), aliases: ["green"]);

        yield return Tool("bchannel", "Keep only the blue channel", "Zeroes out the red and green channels so only blue is left.",
            (img, _) => ImageService.KeepChannel(img, 2), aliases: ["blue"]);

        yield return Tool("hue", "Rotate the hue of an image (0 to 360 degrees)", "Shifts every colour around the colour wheel by the given number of degrees (0-360).",
            (img, args) => ImageService.RotateHue(img, args.Int("degrees")!.Value),
            extra: [new ParamDef("degrees", "Degrees to rotate the hue (0-360)", ParamKind.Int, Min: 0, Max: 360)]);
    }

    private CommandDef Tool(string name, string description, string help,
        Action<Image<Rgba32>, Args> edit, string[]? aliases = null, ParamDef[]? extra = null, bool forceGif = false)
    {
        var parameters = new List<ParamDef>(extra ?? []);
        parameters.Add(new ParamDef("image", "Image to edit (default: the latest image in the chat)", ParamKind.Attachment, Required: false));

        return new CommandDef
        {
            Name = name,
            Aliases = aliases ?? [],
            Description = description,
            Help = help + "\nUse it with an attached image, by replying to a picture, or on its own to edit the latest image in the chat.",
            Params = parameters.ToArray(),
            Cooldowns = [new CooldownSpec(1, 5, BucketType.User)],
            Handler = ctx => RunAsync(ctx, name, edit, forceGif),
        };
    }

    private async Task RunAsync(CommandContext ctx, string name, Action<Image<Rgba32>, Args> edit, bool forceGif)
    {
        await ctx.DeferAsync();

        var url = await Images.FindSourceUrlAsync(ctx);
        if (url == null)
        {
            await ctx.SendAsync("I couldn't find an image. Attach one, reply to one, or post one in this channel first.", ephemeral: true);
            return;
        }

        var input = await Images.DownloadAsync(url);
        var (data, extension) = await Images.ProcessAsync(input, img => edit(img, ctx.Args), forceGif);

        var limit = (long)(ctx.Guild?.MaxUploadLimit ?? 10UL * 1024 * 1024);
        if (data.Length > limit)
            throw new BadArgumentException($"The result is {data.Length / 1024 / 1024.0:0.0} MB, which is over this server's upload limit.");

        await ctx.SendFilesAsync([new FileAttachment(new MemoryStream(data), $"{name}.{extension}")]);
    }
}
