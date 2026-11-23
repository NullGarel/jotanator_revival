// VIBE CODED DSICLAIMER - CLAUDE GENERATED AS OF 26-11-20 @ 22:42 UTC
using System.Collections.Concurrent;
using Discord;
using Discord.WebSocket;

namespace Jotanator;

/// <summary>Image search (Google, with Bing/DuckDuckGo fallback).</summary>
public sealed class ImagesCog : Cog
{
    private readonly ConcurrentDictionary<string, ImageView> _views = new();

    public override IEnumerable<CommandDef> BuildCommands()
    {
        yield return new CommandDef
        {
            Name = "img",
            Aliases = ["image"],
            Description = "Search for images. It will fallback to bing if google images fails.",
            Help = "Search for images and browse them with the arrow buttons.",
            Params = [new ParamDef("query", "What to search for", Rest: true)],
            Cooldowns = [new CooldownSpec(1, 5, BucketType.User)],
            Handler = ImgAsync,
        };
    }

    public override Task OnLoadAsync()
    {
        Bot.Client.ButtonExecuted += OnButtonAsync;
        return Task.CompletedTask;
    }

    public override Task OnUnloadAsync()
    {
        Bot.Client.ButtonExecuted -= OnButtonAsync;
        return Task.CompletedTask;
    }

    private async Task ImgAsync(CommandContext ctx)
    {
        var query = ctx.Args.Str("query")!;
        await ctx.DeferAsync();

        var (name, urls, start) = await ImageSearch.FindImagesAsync(Bot.Http, query);
        if (name == null || urls.Count == 0)
        {
            await ctx.SendAsync("Couldn't find any images, all sources failed.");
            return;
        }

        var view = new ImageView(Bot.Http, ctx.Author.Id, query, name, urls, start, id => _views.TryRemove(id, out _));
        _views[view.Id] = view;
        view.Message = await ctx.SendAsync(embed: view.BuildEmbed(), components: view.BuildComponents(false), wantMessage: true);
        view.ResetTimeout();
    }

    private Task OnButtonAsync(SocketMessageComponent component)
    {
        // custom id format: img:<viewId>:<prev|next|close>
        var parts = component.Data.CustomId.Split(':');
        if (parts.Length != 3 || parts[0] != "img") return Task.CompletedTask;

        _ = Task.Run(async () =>
        {
            try
            {
                if (!_views.TryGetValue(parts[1], out var view))
                {
                    await component.RespondAsync("This image browser has expired. Run the command again.", ephemeral: true);
                    return;
                }

                await view.HandleAsync(component, parts[2]);
            }
            catch (Exception e)
            {
                Logger.Error("Image button failed", e);
            }
        });

        return Task.CompletedTask;
    }
}

/// <summary>State of one image browser message (port of the discord.ui.View in cogs/images.py).</summary>
internal sealed class ImageView
{
    public string Id { get; } = Guid.NewGuid().ToString("N");
    public IUserMessage? Message { get; set; }

    private readonly HttpClient _http;
    private readonly ulong _userId;
    private readonly string _query;
    private readonly string _source;
    private readonly List<string> _urls;
    private readonly Dictionary<int, bool> _valid;   // cache of index -> loadable?
    private readonly Action<string> _onDone;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _timeout;
    private int _index;

    public ImageView(HttpClient http, ulong userId, string query, string source,
                     List<string> urls, int start, Action<string> onDone)
    {
        _http = http;
        _userId = userId;
        _query = query;
        _source = source;
        _urls = urls;
        _index = start;
        _valid = new Dictionary<int, bool> { [start] = true };
        _onDone = onDone;
    }

    public Embed BuildEmbed() =>
        new EmbedBuilder()
            .WithTitle(_query.Length > 200 ? _query[..200] : _query)
            .WithImageUrl(_urls[_index])
            .WithFooter($"{_source} • {_index + 1}/{_urls.Count}")
            .Build();

    public MessageComponent BuildComponents(bool disabled) =>
        new ComponentBuilder()
            .WithButton(customId: $"img:{Id}:prev", emote: new Emoji("◀"), style: ButtonStyle.Secondary, disabled: disabled)
            .WithButton(customId: $"img:{Id}:next", emote: new Emoji("▶"), style: ButtonStyle.Secondary, disabled: disabled)
            .WithButton(customId: $"img:{Id}:close", emote: new Emoji("✖"), style: ButtonStyle.Danger, disabled: disabled)
            .Build();

    /// <summary>(Re)starts the inactivity timer, like discord.py's View timeout.</summary>
    public void ResetTimeout()
    {
        var cts = new CancellationTokenSource();
        Interlocked.Exchange(ref _timeout, cts)?.Cancel();

        _ = Task.Run(async () =>
        {
            try { await Task.Delay(TimeSpan.FromSeconds(Config.ViewTimeoutSeconds), cts.Token); }
            catch (OperationCanceledException) { return; }
            await OnTimeoutAsync();
        });
    }

    private void Stop()
    {
        Interlocked.Exchange(ref _timeout, null)?.Cancel();
        _onDone(Id);
    }

    private async Task OnTimeoutAsync()
    {
        _onDone(Id);
        if (Message == null) return;
        try
        {
            await Message.ModifyAsync(m => m.Components = BuildComponents(true));
        }
        catch
        {
            // message deleted or token expired; nothing to do
        }
    }

    public async Task HandleAsync(SocketMessageComponent component, string action)
    {
        // interaction_check: only the author may use the buttons
        if (component.User.Id != _userId)
        {
            await component.RespondAsync($"Run your own `{Config.Prefix}img` to browse.", ephemeral: true);
            return;
        }

        ResetTimeout();

        switch (action)
        {
            case "prev": await StepAsync(component, -1); break;
            case "next": await StepAsync(component, +1); break;
            case "close": await CloseAsync(component); break;
        }
    }

    private async Task StepAsync(SocketMessageComponent component, int direction)
    {
        await component.DeferAsync();
        await _gate.WaitAsync();
        try
        {
            var n = _urls.Count;
            var idx = _index;
            for (var k = 0; k < n - 1; k++)
            {
                idx = ((idx + direction) % n + n) % n;
                if (!_valid.TryGetValue(idx, out var ok))
                {
                    ok = await ImageSearch.IsValidImageAsync(_http, _urls[idx]);
                    _valid[idx] = ok;
                }

                if (ok)
                {
                    _index = idx;
                    break;
                }
            }

            await component.ModifyOriginalResponseAsync(m =>
            {
                m.Embed = BuildEmbed();
                m.Components = BuildComponents(false);
            });
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task CloseAsync(SocketMessageComponent component)
    {
        await component.DeferAsync();
        await ((IDiscordInteraction)component).DeleteOriginalResponseAsync();
        Stop();
    }
}
