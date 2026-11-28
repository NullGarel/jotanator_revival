// VIBE CODED DSICLAIMER - CLAUDE GENERATED AS OF 26-11-28 @ 19:27 UTC
using System.Data.Common;
using Microsoft.Data.Sqlite;

namespace Jotanator;

/// <summary>
/// Thin wrapper around SQLite: one global lock (the bot's load is tiny), short-lived connections,
/// and optional transactions. All SQLite API usage in the bot lives in this file.
/// </summary>
public sealed class Db
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public Db(string path) => _connectionString = $"Data Source={path}";

    /// <summary>Runs work on its own connection (no transaction).</summary>
    public async Task<T> RunAsync<T>(Func<DbSession, Task<T>> work)
    {
        await _lock.WaitAsync();
        try
        {
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();
            return await work(new DbSession(conn, null));
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Runs work in one transaction: committed if it returns, rolled back if it throws.</summary>
    public async Task<T> TxAsync<T>(Func<DbSession, Task<T>> work)
    {
        await _lock.WaitAsync();
        try
        {
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();
            using var tx = conn.BeginTransaction();
            var result = await work(new DbSession(conn, tx));
            tx.Commit();
            return result;
        }
        finally
        {
            _lock.Release();
        }
    }
}

/// <summary>A connection (and maybe a transaction) handed to Db.RunAsync / TxAsync callbacks.</summary>
public sealed class DbSession
{
    private readonly SqliteConnection _conn;
    private readonly SqliteTransaction? _tx;

    internal DbSession(SqliteConnection conn, SqliteTransaction? tx)
    {
        _conn = conn;
        _tx = tx;
    }

    private SqliteCommand Make(string sql, (string Name, object? Value)[] ps)
    {
        var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        if (_tx != null) cmd.Transaction = _tx;
        foreach (var (name, value) in ps)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return cmd;
    }

    /// <summary>Runs a statement and returns the number of rows it changed.</summary>
    public async Task<int> ExecAsync(string sql, params (string Name, object? Value)[] ps)
    {
        using var cmd = Make(sql, ps);
        return await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>First column of the first row (null / DBNull when there is none).</summary>
    public async Task<object?> ScalarAsync(string sql, params (string Name, object? Value)[] ps)
    {
        using var cmd = Make(sql, ps);
        return await cmd.ExecuteScalarAsync();
    }

    /// <summary>Like ScalarAsync but as a number (0 when there is no row or the value is NULL).</summary>
    public async Task<long> ScalarLongAsync(string sql, params (string Name, object? Value)[] ps)
    {
        var value = await ScalarAsync(sql, ps);
        return value is null or DBNull ? 0 : Convert.ToInt64(value);
    }

    public async Task<List<T>> QueryAsync<T>(string sql, Func<DbDataReader, T> map, params (string Name, object? Value)[] ps)
    {
        using var cmd = Make(sql, ps);
        await using var reader = await cmd.ExecuteReaderAsync();
        var list = new List<T>();
        while (await reader.ReadAsync()) list.Add(map(reader));
        return list;
    }
}
