// VIBE CODED DSICLAIMER - CLAUDE GENERATED AS OF 26-11-28 @ 19:27 UTC
using System.Data.Common;
using System.Globalization;

namespace Jotanator;

public enum Outcome { Win, Loss, Draw }

public sealed record Account(ulong Id, string Name, long Balance, string? Title, string? LastDaily);
public sealed record DailyResult(bool Claimed, long Amount, bool Doubled, long Balance, TimeSpan NextIn);
public sealed record TransferResult(bool Ok, long Received, long Fee);
public sealed record TopEntry(int Rank, ulong Id, string Name, long Balance, string Badges);
public sealed record GameStats(string Game, long Wins, long Losses, long Draws, long Net);
public sealed record Snapshot(string Day, long Supply, int Accounts, int Active, double PriceIndex, double EventMult, string? Event);
public enum BuyResult { Ok, NoFunds, AlreadyOwned }

/// <summary>
/// Jotacoin (J$): balances, game stakes (escrow), stats, the shop and the price index.
/// Everything is stored in one SQLite file. No Discord types in here on purpose.
/// </summary>
public sealed class Economy
{
    private const double BaselineAverage = 600;      // average J$ per active player that means "prices are normal"
    private const double EventChance = 0.12;         // chance per day of a random market event

    private static readonly (string Text, double Mult)[] MarketEvents =
    [
        ("📈 Market boom: prices are up", 1.25),
        ("📉 Market crash: prices are down", 0.75),
        ("🔥 Shortage: everything is pricier", 1.35),
        ("🎉 Mega sale: everything is cheaper", 0.70),
    ];

    private readonly Db _db;
    private double _priceIndex = 1.0;

    public Economy(string path) => _db = new Db(path);

    // ------------------------------------------------------------------ setup

    /// <summary>Creates the tables, refunds stakes left over from a crash and loads the price index. Returns refunds made.</summary>
    public async Task<int> InitAsync()
    {
        await _db.RunAsync(async s =>
        {
            await s.ScalarAsync("PRAGMA journal_mode=WAL;");
            foreach (var sql in Schema) await s.ExecAsync(sql);
            return 0;
        });

        var refunded = await _db.TxAsync(async s =>
        {
            var held = await s.QueryAsync("SELECT game, user_id, amount FROM escrow",
                r => (Game: r.GetString(0), User: r.GetInt64(1), Amount: r.GetInt64(2)));

            foreach (var h in held)
            {
                await s.ExecAsync("UPDATE users SET balance = balance + @a WHERE id = @id", ("@a", h.Amount), ("@id", h.User));
                await LogAsync(s, (ulong)h.User, h.Amount, "refund", $"unfinished game {h.Game}");
            }

            await s.ExecAsync("DELETE FROM escrow");
            return held.Count;
        });

        var latest = await _db.RunAsync(s => s.QueryAsync(
            "SELECT day, price_index, event FROM snapshots ORDER BY day DESC LIMIT 1",
            r => (Day: r.GetString(0), Index: r.GetDouble(1), Event: r.IsDBNull(2) ? null : r.GetString(2))));

        if (latest.Count > 0)
        {
            Volatile.Write(ref _priceIndex, latest[0].Index);
            MarketEvent = latest[0].Day == Today() ? latest[0].Event : null;
        }

        return refunded;
    }

    private static readonly string[] Schema =
    [
        """
        CREATE TABLE IF NOT EXISTS users (
            id INTEGER PRIMARY KEY,
            name TEXT NOT NULL DEFAULT '',
            balance INTEGER NOT NULL DEFAULT 0 CHECK (balance >= 0),
            last_daily TEXT,
            title TEXT,
            created TEXT NOT NULL
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS inventory (
            user_id INTEGER NOT NULL,
            item TEXT NOT NULL,
            qty INTEGER NOT NULL DEFAULT 0,
            PRIMARY KEY (user_id, item)
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS escrow (
            game TEXT NOT NULL,
            user_id INTEGER NOT NULL,
            amount INTEGER NOT NULL,
            PRIMARY KEY (game, user_id)
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS stats (
            user_id INTEGER NOT NULL,
            game TEXT NOT NULL,
            wins INTEGER NOT NULL DEFAULT 0,
            losses INTEGER NOT NULL DEFAULT 0,
            draws INTEGER NOT NULL DEFAULT 0,
            net INTEGER NOT NULL DEFAULT 0,
            PRIMARY KEY (user_id, game)
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS ledger (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            ts TEXT NOT NULL,
            user_id INTEGER,
            delta INTEGER NOT NULL,
            kind TEXT NOT NULL,
            note TEXT
        )
        """,
        "CREATE INDEX IF NOT EXISTS ledger_ts ON ledger (ts)",
        """
        CREATE TABLE IF NOT EXISTS snapshots (
            day TEXT PRIMARY KEY,
            supply INTEGER NOT NULL,
            accounts INTEGER NOT NULL,
            active INTEGER NOT NULL,
            base_index REAL NOT NULL,
            event_mult REAL NOT NULL,
            price_index REAL NOT NULL,
            event TEXT
        )
        """,
    ];

    // --------------------------------------------------------------- helpers

    private static string Now() => DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    private static string Today() => DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static Task LogAsync(DbSession s, ulong? user, long delta, string kind, string? note) =>
        s.ExecAsync("INSERT INTO ledger (ts, user_id, delta, kind, note) VALUES (@ts, @u, @d, @k, @n)",
            ("@ts", Now()), ("@u", user is ulong u ? (long)u : null), ("@d", delta), ("@k", kind), ("@n", note));

    /// <summary>Creates the account on first sight (with the starting balance) and keeps the stored name fresh.</summary>
    private static async Task EnsureAsync(DbSession s, ulong id, string? name)
    {
        var created = await s.ExecAsync(
            "INSERT OR IGNORE INTO users (id, name, balance, created) VALUES (@id, @name, @bal, @now)",
            ("@id", (long)id), ("@name", name ?? ""), ("@bal", Config.StartBalance), ("@now", Now()));

        if (created > 0)
        {
            if (Config.StartBalance > 0) await LogAsync(s, id, Config.StartBalance, "start", null);
        }
        else if (!string.IsNullOrEmpty(name))
        {
            await s.ExecAsync("UPDATE users SET name = @name WHERE id = @id AND name <> @name",
                ("@name", name), ("@id", (long)id));
        }
    }

    private static async Task<Account> ReadAccountAsync(DbSession s, ulong id)
    {
        var rows = await s.QueryAsync(
            "SELECT id, name, balance, title, last_daily FROM users WHERE id = @id",
            r => new Account((ulong)r.GetInt64(0), r.GetString(1), r.GetInt64(2),
                r.IsDBNull(3) ? null : r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4)),
            ("@id", (long)id));
        return rows[0];
    }

    // --------------------------------------------------------------- balances

    public Task<Account> GetAccountAsync(ulong id, string? name = null) =>
        _db.TxAsync(async s =>
        {
            await EnsureAsync(s, id, name);
            return await ReadAccountAsync(s, id);
        });

    public async Task<long> GetBalanceAsync(ulong id, string? name = null) => (await GetAccountAsync(id, name)).Balance;

    /// <summary>
    /// Adds (or removes) J$. With clamp, a loss is limited to what the user has; without it the change is
    /// refused (null) when the balance would go below zero. Returns the amount actually applied.
    /// </summary>
    public Task<long?> AdjustAsync(ulong id, long delta, string kind, string? note = null, bool clamp = true) =>
        _db.TxAsync<long?>(async s =>
        {
            await EnsureAsync(s, id, null);
            var balance = await s.ScalarLongAsync("SELECT balance FROM users WHERE id = @id", ("@id", (long)id));

            var applied = delta;
            if (balance + delta < 0)
            {
                if (!clamp) return null;
                applied = -balance;
            }

            if (applied != 0)
            {
                await s.ExecAsync("UPDATE users SET balance = balance + @d WHERE id = @id", ("@d", applied), ("@id", (long)id));
                await LogAsync(s, id, applied, kind, note);
            }

            return applied;
        });

    /// <summary>Moves J$ between users. The fee (a percentage of the amount) is destroyed.</summary>
    public Task<TransferResult> TransferAsync(ulong from, ulong to, long amount, int feePercent) =>
        _db.TxAsync(async s =>
        {
            await EnsureAsync(s, from, null);
            await EnsureAsync(s, to, null);

            var fee = amount * feePercent / 100;
            var received = amount - fee;

            var took = await s.ExecAsync("UPDATE users SET balance = balance - @a WHERE id = @id AND balance >= @a",
                ("@a", amount), ("@id", (long)from));
            if (took == 0) return new TransferResult(false, 0, 0);

            await s.ExecAsync("UPDATE users SET balance = balance + @a WHERE id = @id", ("@a", received), ("@id", (long)to));
            await LogAsync(s, from, -amount, "pay_out", $"to {to}");
            await LogAsync(s, to, received, "pay_in", $"from {from}");
            return new TransferResult(true, received, fee);
        });

    // ---------------------------------------------------------------- escrow

    /// <summary>Takes a stake out of the balance and holds it for a game. False if the user can't afford it.</summary>
    public Task<bool> ReserveAsync(string game, ulong id, long amount) =>
        _db.TxAsync(async s =>
        {
            await EnsureAsync(s, id, null);
            var took = await s.ExecAsync("UPDATE users SET balance = balance - @a WHERE id = @id AND balance >= @a",
                ("@a", amount), ("@id", (long)id));
            if (took == 0) return false;

            await s.ExecAsync("INSERT INTO escrow (game, user_id, amount) VALUES (@g, @id, @a)",
                ("@g", game), ("@id", (long)id), ("@a", amount));
            await LogAsync(s, id, -amount, "stake", game);
            return true;
        });

    /// <summary>
    /// Ends a held stake and pays out `payout` (0 = lost it, the stake = got it back, more = won).
    /// Returns the payout, or -1 if that stake was already settled.
    /// </summary>
    public Task<long> SettleAsync(string game, ulong id, long payout, string kind = "payout") =>
        _db.TxAsync(async s =>
        {
            var removed = await s.ExecAsync("DELETE FROM escrow WHERE game = @g AND user_id = @id",
                ("@g", game), ("@id", (long)id));
            if (removed == 0) return -1L;

            if (payout > 0)
            {
                await s.ExecAsync("UPDATE users SET balance = balance + @a WHERE id = @id", ("@a", payout), ("@id", (long)id));
                await LogAsync(s, id, payout, kind, game);
            }

            return payout;
        });

    // ----------------------------------------------------------------- stats

    public Task RecordAsync(ulong id, string game, Outcome outcome, long net) =>
        _db.TxAsync(async s =>
        {
            await EnsureAsync(s, id, null);
            await s.ExecAsync(
                """
                INSERT INTO stats (user_id, game, wins, losses, draws, net) VALUES (@u, @g, @w, @l, @d, @n)
                ON CONFLICT (user_id, game) DO UPDATE SET
                    wins = wins + @w, losses = losses + @l, draws = draws + @d, net = net + @n
                """,
                ("@u", (long)id), ("@g", game),
                ("@w", outcome == Outcome.Win ? 1 : 0),
                ("@l", outcome == Outcome.Loss ? 1 : 0),
                ("@d", outcome == Outcome.Draw ? 1 : 0),
                ("@n", net));
            return 0;
        });

    public Task<List<GameStats>> GetStatsAsync(ulong id) =>
        _db.RunAsync(s => s.QueryAsync(
            "SELECT game, wins, losses, draws, net FROM stats WHERE user_id = @id ORDER BY game",
            r => new GameStats(r.GetString(0), r.GetInt64(1), r.GetInt64(2), r.GetInt64(3), r.GetInt64(4)),
            ("@id", (long)id)));

    // ----------------------------------------------------------------- daily

    /// <summary>Once per UTC day: a random 32..512 J$ (doubled if you own a Lucky Charm, which is used up).</summary>
    public Task<DailyResult> ClaimDailyAsync(ulong id, string name) =>
        _db.TxAsync(async s =>
        {
            await EnsureAsync(s, id, name);
            var account = await ReadAccountAsync(s, id);
            var nextIn = DateTime.UtcNow.Date.AddDays(1) - DateTime.UtcNow;

            if (account.LastDaily == Today())
                return new DailyResult(false, 0, false, account.Balance, nextIn);

            var amount = Random.Shared.NextInt64(32, 513);

            var charms = await s.ScalarLongAsync("SELECT qty FROM inventory WHERE user_id = @id AND item = 'charm'", ("@id", (long)id));
            var doubled = charms > 0;
            if (doubled)
            {
                await s.ExecAsync("UPDATE inventory SET qty = qty - 1 WHERE user_id = @id AND item = 'charm'", ("@id", (long)id));
                amount *= 2;
            }

            await s.ExecAsync("UPDATE users SET balance = balance + @a, last_daily = @d WHERE id = @id",
                ("@a", amount), ("@d", Today()), ("@id", (long)id));
            await LogAsync(s, id, amount, "daily", doubled ? "lucky charm" : null);

            return new DailyResult(true, amount, doubled, account.Balance + amount, nextIn);
        });

    // ----------------------------------------------------------- leaderboard

    public async Task<List<TopEntry>> TopAsync(int count)
    {
        var rows = await _db.RunAsync(s => s.QueryAsync(
            "SELECT id, name, balance FROM users ORDER BY balance DESC, id LIMIT @n",
            r => (Id: (ulong)r.GetInt64(0), Name: r.GetString(1), Balance: r.GetInt64(2)),
            ("@n", count)));

        var badges = await _db.RunAsync(s => s.QueryAsync(
            "SELECT user_id, item FROM inventory WHERE qty > 0 AND item IN ('star', 'crown', 'gem')",
            r => (User: (ulong)r.GetInt64(0), Item: r.GetString(1))));

        return rows.Select((r, i) => new TopEntry(i + 1, r.Id, r.Name, r.Balance,
            BadgeString(badges.Where(b => b.User == r.Id).Select(b => b.Item)))).ToList();
    }

    public Task<long> RankAsync(long balance) =>
        _db.RunAsync(s => s.ScalarLongAsync("SELECT COUNT(*) + 1 FROM users WHERE balance > @b", ("@b", balance)));

    private static string BadgeString(IEnumerable<string> owned)
    {
        var set = owned.ToHashSet();
        return string.Concat(Shop.Badges.Where(b => set.Contains(b.Id)).Select(b => b.Emoji));
    }

    public async Task<string> GetBadgesAsync(ulong id)
    {
        var owned = await GetInventoryAsync(id);
        return BadgeString(owned.Keys);
    }

    // ------------------------------------------------------------------ shop

    public Task<Dictionary<string, long>> GetInventoryAsync(ulong id) =>
        _db.RunAsync(async s =>
        {
            var rows = await s.QueryAsync("SELECT item, qty FROM inventory WHERE user_id = @id AND qty > 0",
                r => (Item: r.GetString(0), Qty: r.GetInt64(1)), ("@id", (long)id));
            return rows.ToDictionary(r => r.Item, r => r.Qty);
        });

    public Task<BuyResult> BuyAsync(ulong id, string name, ShopItem item, long price) =>
        _db.TxAsync(async s =>
        {
            await EnsureAsync(s, id, name);

            if (item.Kind != ItemKind.Consumable)
            {
                var owned = await s.ScalarLongAsync("SELECT qty FROM inventory WHERE user_id = @id AND item = @i",
                    ("@id", (long)id), ("@i", item.Id));
                if (owned > 0) return BuyResult.AlreadyOwned;
            }

            var took = await s.ExecAsync("UPDATE users SET balance = balance - @p WHERE id = @id AND balance >= @p",
                ("@p", price), ("@id", (long)id));
            if (took == 0) return BuyResult.NoFunds;

            await s.ExecAsync(
                """
                INSERT INTO inventory (user_id, item, qty) VALUES (@id, @i, 1)
                ON CONFLICT (user_id, item) DO UPDATE SET qty = qty + 1
                """,
                ("@id", (long)id), ("@i", item.Id));
            await LogAsync(s, id, -price, "shop", item.Id);
            return BuyResult.Ok;
        });

    /// <summary>Sets (or clears, with null) the profile title. False if the user doesn't own the Custom Title perk.</summary>
    public Task<bool> SetTitleAsync(ulong id, string? title) =>
        _db.TxAsync(async s =>
        {
            var owned = await s.ScalarLongAsync("SELECT qty FROM inventory WHERE user_id = @id AND item = 'title'", ("@id", (long)id));
            if (owned == 0) return false;
            await s.ExecAsync("UPDATE users SET title = @t WHERE id = @id", ("@t", title), ("@id", (long)id));
            return true;
        });

    // ------------------------------------------------------------- inflation

    /// <summary>Multiplier applied to shop prices and game amounts. 1.0 = normal.</summary>
    public double PriceIndex => Volatile.Read(ref _priceIndex);

    /// <summary>Today's random market event, if there is one.</summary>
    public string? MarketEvent { get; private set; }

    /// <summary>A base amount adjusted by the price index (never below 1).</summary>
    public long Scaled(long baseAmount) =>
        Math.Max(1, (long)Math.Round(baseAmount * PriceIndex, MidpointRounding.AwayFromZero));

    /// <summary>
    /// Records today's economy snapshot (money supply, active players) and computes the new price index:
    /// a slow-moving average of "J$ per active player" against a baseline, times today's market event.
    /// Does nothing if today already has one. Returns true when a snapshot was created.
    /// </summary>
    public Task<bool> EnsureSnapshotAsync() =>
        _db.TxAsync(async s =>
        {
            var today = Today();
            if (await s.ScalarLongAsync("SELECT COUNT(*) FROM snapshots WHERE day = @d", ("@d", today)) > 0)
                return false;

            var supply = await s.ScalarLongAsync("SELECT COALESCE(SUM(balance), 0) FROM users")
                       + await s.ScalarLongAsync("SELECT COALESCE(SUM(amount), 0) FROM escrow");
            var accounts = (int)await s.ScalarLongAsync("SELECT COUNT(*) FROM users");

            var cutoff = DateTime.UtcNow.AddDays(-7).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            var active = (int)await s.ScalarLongAsync(
                "SELECT COUNT(DISTINCT user_id) FROM ledger WHERE ts >= @c AND user_id IS NOT NULL AND kind NOT IN ('start', 'admin', 'refund')",
                ("@c", cutoff));

            var previous = await s.QueryAsync("SELECT base_index FROM snapshots ORDER BY day DESC LIMIT 1", r => r.GetDouble(0));
            var previousBase = previous.Count > 0 ? previous[0] : 1.0;

            var raw = supply / (double)Math.Max(1, active) / BaselineAverage;
            var baseIndex = Math.Clamp(0.85 * previousBase + 0.15 * raw, 0.5, 4.0);

            string? ev = null;
            var mult = 1.0;
            if (Random.Shared.NextDouble() < EventChance)
            {
                var pick = MarketEvents[Random.Shared.Next(MarketEvents.Length)];
                ev = pick.Text;
                mult = pick.Mult;
            }

            var price = baseIndex * mult;
            await s.ExecAsync(
                """
                INSERT INTO snapshots (day, supply, accounts, active, base_index, event_mult, price_index, event)
                VALUES (@d, @s, @a, @act, @b, @m, @p, @e)
                """,
                ("@d", today), ("@s", supply), ("@a", accounts), ("@act", active),
                ("@b", baseIndex), ("@m", mult), ("@p", price), ("@e", ev));

            Volatile.Write(ref _priceIndex, price);
            MarketEvent = ev;
            return true;
        });

    /// <summary>The most recent snapshots, oldest first.</summary>
    public async Task<List<Snapshot>> GetSnapshotsAsync(int days)
    {
        var rows = await _db.RunAsync(s => s.QueryAsync(
            "SELECT day, supply, accounts, active, price_index, event_mult, event FROM snapshots ORDER BY day DESC LIMIT @n",
            r => new Snapshot(r.GetString(0), r.GetInt64(1), r.GetInt32(2), r.GetInt32(3), r.GetDouble(4), r.GetDouble(5),
                r.IsDBNull(6) ? null : r.GetString(6)),
            ("@n", days)));
        rows.Reverse();
        return rows;
    }
}
