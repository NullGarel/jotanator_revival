// VIBE CODED DSICLAIMER - CLAUDE GENERATED AS OF 26-11-28 @ 19:27 UTC
using System.Net;
using System.Reflection;
using Discord;
using Discord.WebSocket;

namespace Jotanator;

/// <summary>
/// The bot: owns the Discord client, the shared HttpClient, the loaded cogs and the command dispatcher
/// (port of core/bot.py). Handles both "j.command" messages and "/command" slash interactions.
/// </summary>
public sealed class JotaBot : IAsyncDisposable
{
    public DiscordSocketClient Client { get; }
    public HttpClient Http { get; }

    /// <summary>The Jotacoin economy (SQLite). Shared by every cog that touches J$.</summary>
    public Economy Economy { get; }

    /// <summary>Raised for every failed command (the Errors cog subscribes to this).</summary>
    public event Func<CommandContext, Exception, Task>? CommandError;

    private readonly object _lock = new();
    private readonly Dictionary<string, Cog> _cogs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CommandDef> _lookup = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<ulong>? _owners;
    private int _readyOnce;

    public JotaBot()
    {
        Client = new DiscordSocketClient(new DiscordSocketConfig
        {
            // Default (unprivileged) intents + message content, which prefix commands need
            // (enable "Message Content Intent" in the Developer Portal too).
            GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMessages | GatewayIntents.DirectMessages | GatewayIntents.MessageContent,
            AlwaysDownloadUsers = false,
            MessageCacheSize = 0,
            LogLevel = LogSeverity.Info,
            UseInteractionSnowflakeDate = false,
        });

        Economy = new Economy(Config.DbPath);

        Http = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All })
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
    }

    // ------------------------------------------------------------ registry

    public IReadOnlyList<Cog> Cogs
    {
        get { lock (_lock) return _cogs.Values.OrderBy(c => c.Name, StringComparer.Ordinal).ToList(); }
    }

    public IReadOnlyList<CommandDef> Commands
    {
        get { lock (_lock) return _cogs.Values.SelectMany(c => c.Commands).ToList(); }
    }

    /// <summary>Finds a command by name or alias (case-insensitive).</summary>
    public CommandDef? GetCommand(string name)
    {
        lock (_lock) return _lookup.GetValueOrDefault(name);
    }

    public async Task LoadCogAsync(Type type)
    {
        var cog = (Cog)Activator.CreateInstance(type)!;
        cog.Bot = this;

        var commands = cog.BuildCommands().ToList();
        foreach (var c in commands) c.Cog = cog;
        cog.Commands = commands;

        lock (_lock)
        {
            if (_cogs.ContainsKey(cog.Name))
                throw new InvalidOperationException($"Cog '{cog.Name}' is already loaded.");

            foreach (var c in commands)
                foreach (var n in new[] { c.Name }.Concat(c.Aliases))
                    if (_lookup.ContainsKey(n))
                        throw new InvalidOperationException($"Command '{n}' is already registered.");

            _cogs[cog.Name] = cog;
            foreach (var c in commands)
                foreach (var n in new[] { c.Name }.Concat(c.Aliases))
                    _lookup[n.ToLowerInvariant()] = c;
        }

        await cog.OnLoadAsync();
    }

    public async Task UnloadCogAsync(Cog cog)
    {
        lock (_lock)
        {
            _cogs.Remove(cog.Name);
            foreach (var c in cog.Commands)
                foreach (var n in new[] { c.Name }.Concat(c.Aliases))
                    _lookup.Remove(n);
        }

        await cog.OnUnloadAsync();
    }

    /// <summary>Re-creates a cog (resets its state). Code changes still need a restart in C#.</summary>
    public async Task ReloadCogAsync(string name)
    {
        Cog? cog;
        lock (_lock) cog = _cogs.GetValueOrDefault(name);
        if (cog == null) throw new BadArgumentException($"No cog called `{name}`.");

        var type = cog.GetType();
        await UnloadCogAsync(cog);
        await LoadCogAsync(type);
    }

    /// <summary>Loads every Cog subclass in the assembly that isn't abstract or [Disabled].</summary>
    private async Task LoadAllCogsAsync()
    {
        var types = Assembly.GetExecutingAssembly().GetTypes()
            .Where(t => typeof(Cog).IsAssignableFrom(t) && !t.IsAbstract && t.GetCustomAttribute<DisabledAttribute>() == null)
            .OrderBy(t => t.Name, StringComparer.Ordinal);

        foreach (var type in types)
        {
            try
            {
                await LoadCogAsync(type);
                Logger.Info($"Loaded {type.Name}");
            }
            catch (Exception e)
            {
                Logger.Error($"Failed to load {type.Name}", e);
            }
        }
    }

    // ------------------------------------------------------------ lifecycle

    public async Task<int> RunAsync(string token)
    {
        try
        {
            var refunded = await Economy.InitAsync();
            Logger.Info($"Economy ready ({Config.DbPath})" + (refunded > 0 ? $", refunded {refunded} unfinished game stake(s)" : ""));
        }
        catch (Exception e)
        {
            Logger.Error("Could not open the economy database", e);
            return 1;
        }

        await LoadAllCogsAsync();

        Client.Log += Logger.FromDiscord;
        Client.Ready += OnReady;
        Client.MessageReceived += m => { _ = Task.Run(() => OnMessageAsync(m)); return Task.CompletedTask; };
        Client.SlashCommandExecuted += c => { _ = Task.Run(() => OnSlashAsync(c)); return Task.CompletedTask; };
        Client.AutocompleteExecuted += a => { _ = Task.Run(() => OnAutocompleteAsync(a)); return Task.CompletedTask; };

        try
        {
            await Client.LoginAsync(TokenType.Bot, token);
            await Client.StartAsync();
        }
        catch (Exception e)
        {
            Logger.Error("Could not log in", e);
            return 1;
        }

        var stop = new TaskCompletionSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.TrySetResult(); };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => stop.TrySetResult();
        await stop.Task;

        Logger.Info("Shutting down...");
        await Client.StopAsync();
        return 0;
    }

    private Task OnReady()
    {
        Logger.Info($"Logged in as {Client.CurrentUser.Username} (prefix: {Config.Prefix})");

        if (Interlocked.Exchange(ref _readyOnce, 1) == 0 && Config.SyncOnStart)
        {
            _ = Task.Run(async () =>
            {
                try { Logger.Info($"Synced {await SyncAsync()} slash commands"); }
                catch (Exception e) { Logger.Error("Slash command sync failed", e); }
            });
        }

        return Task.CompletedTask;
    }

    /// <summary>Pushes every slash-enabled command to Discord (global commands).</summary>
    public async Task<int> SyncAsync()
    {
        var props = Commands
            .Where(c => c.Slash)
            .Select(c => (ApplicationCommandProperties)c.BuildSlash().Build())
            .ToArray();
        var result = await Client.BulkOverwriteGlobalApplicationCommandsAsync(props);
        return result.Count;
    }

    public async Task<bool> IsOwnerAsync(IUser user)
    {
        if (_owners == null)
        {
            var app = await Client.GetApplicationInfoAsync();
            var set = new HashSet<ulong>();
            if (app.Team != null)
                foreach (var member in app.Team.TeamMembers) set.Add(member.User.Id);
            else
                set.Add(app.Owner.Id);
            _owners = set;
        }

        return _owners.Contains(user.Id);
    }

    // ------------------------------------------------------------ dispatch

    private (string Prefix, string Clean)? MatchPrefix(string content)
    {
        var me = Client.CurrentUser;
        if (me != null)
        {
            foreach (var mention in new[] { $"<@{me.Id}>", $"<@!{me.Id}>" })
            {
                if (content.Length > mention.Length &&
                    content.StartsWith(mention, StringComparison.Ordinal) &&
                    char.IsWhiteSpace(content[mention.Length]))
                    return (mention, $"@{me.Username} ");
            }
        }

        foreach (var p in Config.Prefixes)
            if (content.StartsWith(p, StringComparison.Ordinal))
                return (p, p);

        return null;
    }

    private async Task OnMessageAsync(SocketMessage raw)
    {
        try
        {
            if (raw is not SocketUserMessage message || message.Author.IsBot) return;

            var matched = MatchPrefix(message.Content);
            if (matched == null) return;

            // strip_after_prefix: "j. ping" works too
            var rest = message.Content[matched.Value.Prefix.Length..].TrimStart();
            var split = rest.IndexOfAny([' ', '\t', '\r', '\n']);
            var word = split < 0 ? rest : rest[..split];
            var argText = split < 0 ? "" : rest[split..];
            if (word.Length == 0) return;

            var command = GetCommand(word);
            if (command == null) return;   // CommandNotFound is silently ignored

            var guild = (message.Channel as SocketGuildChannel)?.Guild;
            using var ctx = new CommandContext(this, command, message, guild, matched.Value.Clean);
            await ExecuteAsync(ctx, argText);
        }
        catch (Exception e)
        {
            Logger.Error("Error while handling a message", e);
        }
    }

    private async Task OnSlashAsync(SocketSlashCommand interaction)
    {
        try
        {
            var command = GetCommand(interaction.CommandName);
            if (command == null) return;

            var guild = interaction.GuildId.HasValue ? Client.GetGuild(interaction.GuildId.Value) : null;
            using var ctx = new CommandContext(this, command, interaction, guild);
            await ExecuteAsync(ctx, null);
        }
        catch (Exception e)
        {
            Logger.Error("Error while handling a slash command", e);
        }
    }

    private async Task ExecuteAsync(CommandContext ctx, string? rawArgs)
    {
        var command = ctx.Command;
        try
        {
            await command.CheckAsync(ctx);
            command.ConsumeCooldowns(ctx);
            ctx.Args = ctx.Interaction != null
                ? command.FromInteraction(ctx)
                : await command.ParsePrefixAsync(ctx, rawArgs ?? "");
            await command.Handler(ctx);
        }
        catch (Exception e)
        {
            await RaiseCommandErrorAsync(ctx, e);
        }
    }

    private async Task RaiseCommandErrorAsync(CommandContext ctx, Exception error)
    {
        var handlers = CommandError?.GetInvocationList();
        if (handlers == null || handlers.Length == 0)
        {
            Logger.Error($"Unhandled error in {ctx.Command.Name}", error);
            return;
        }

        foreach (var handler in handlers)
        {
            try { await ((Func<CommandContext, Exception, Task>)handler)(ctx, error); }
            catch (Exception e) { Logger.Error("Error handler failed", e); }
        }
    }

    private async Task OnAutocompleteAsync(SocketAutocompleteInteraction interaction)
    {
        try
        {
            var command = GetCommand(interaction.Data.CommandName);
            var param = command?.Params.FirstOrDefault(p => p.Name == interaction.Data.Current.Name);
            if (param?.Autocomplete == null) return;

            var current = interaction.Data.Current.Value?.ToString() ?? "";
            var results = param.Autocomplete(this, current)
                .Take(25)
                .Select(n => new AutocompleteResult(n, n));
            await interaction.RespondAsync(results);
        }
        catch (Exception e)
        {
            Logger.Error("Autocomplete failed", e);
        }
    }

    public async ValueTask DisposeAsync()
    {
        try { await Client.StopAsync(); } catch { /* already stopped */ }
        await Client.DisposeAsync();
        Http.Dispose();
    }
}
