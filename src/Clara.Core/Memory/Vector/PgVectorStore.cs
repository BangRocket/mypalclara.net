using System.Text.Json;
using Clara.Core.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;
using PgVector = Pgvector.Vector;

namespace Clara.Core.Memory.Vector;

/// <summary>
/// pgvector search/insert/delete. Connects to the same table as the Python app.
/// Table schema: id UUID PK, vector vector(1536), payload JSONB
/// </summary>
public sealed class PgVectorStore : IVectorStore
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly string _tableName;
    private readonly ILogger<PgVectorStore> _logger;
    private bool _extensionEnsured;

    public PgVectorStore(ClaraConfig config, ILogger<PgVectorStore> logger)
    {
        var dsBuilder = new NpgsqlDataSourceBuilder(config.Memory.VectorStore.DatabaseUrl);
        dsBuilder.UseVector();
        _dataSource = dsBuilder.Build();
        _tableName = config.Memory.VectorStore.CollectionName;
        _logger = logger;
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken ct)
    {
        var conn = await _dataSource.OpenConnectionAsync(ct);

        if (!_extensionEnsured)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "CREATE EXTENSION IF NOT EXISTS vector";
            await cmd.ExecuteNonQueryAsync(ct);
            await conn.ReloadTypesAsync(ct);
            _extensionEnsured = true;
        }

        return conn;
    }

    public async Task<List<MemoryItem>> SearchAsync(
        float[] embedding,
        Dictionary<string, object?>? filters = null,
        int limit = 20,
        CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(ct);
        var vec = new PgVector(embedding);

        var sql = $"""
            SELECT id, payload, 1 - (vector <=> $1::vector) AS score
            FROM {_tableName}
            WHERE 1=1
            """;

        var parameters = new List<NpgsqlParameter> { new() { Value = vec } };
        int paramIdx = 2;

        if (filters is not null)
        {
            foreach (var (key, value) in filters)
            {
                sql += $" AND payload->>'{key}' = ${paramIdx}";
                parameters.Add(new NpgsqlParameter { Value = value?.ToString() ?? "" });
                paramIdx++;
            }
        }

        sql += $" ORDER BY vector <=> $1::vector LIMIT {limit}";

        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var p in parameters)
            cmd.Parameters.Add(p);

        var items = new List<MemoryItem>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var id = reader.GetGuid(0).ToString();
            var payloadJson = reader.GetString(1);
            var score = reader.GetDouble(2);
            var payload = JsonSerializer.Deserialize<Dictionary<string, object?>>(payloadJson) ?? [];

            var memory = payload.TryGetValue("data", out var data) ? data?.ToString() ?? "" : "";

            items.Add(new MemoryItem
            {
                Id = id,
                Memory = memory,
                Score = score,
                Metadata = payload,
            });
        }

        return items;
    }

    public async Task InsertAsync(string id, float[] embedding, Dictionary<string, object?> payload, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(ct);

        // Ensure table exists
        await EnsureTableAsync(conn, ct);

        var vec = new PgVector(embedding);
        var payloadJson = JsonSerializer.Serialize(payload);

        var sql = $"""
            INSERT INTO {_tableName} (id, vector, payload)
            VALUES ($1, $2::vector, $3::jsonb)
            ON CONFLICT (id) DO UPDATE SET vector = EXCLUDED.vector, payload = EXCLUDED.payload
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(Guid.Parse(id));
        cmd.Parameters.Add(new NpgsqlParameter { Value = vec });
        cmd.Parameters.AddWithValue(NpgsqlTypes.NpgsqlDbType.Jsonb, payloadJson);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(ct);

        var sql = $"DELETE FROM {_tableName} WHERE id = $1";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(Guid.Parse(id));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<List<MemoryItem>> GetAllAsync(
        Dictionary<string, object?>? filters = null,
        int limit = 100,
        CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(ct);

        var sql = $"SELECT id, payload FROM {_tableName} WHERE 1=1";
        var parameters = new List<NpgsqlParameter>();
        int paramIdx = 1;

        if (filters is not null)
        {
            foreach (var (key, value) in filters)
            {
                sql += $" AND payload->>'{key}' = ${paramIdx}";
                parameters.Add(new NpgsqlParameter { Value = value?.ToString() ?? "" });
                paramIdx++;
            }
        }

        sql += $" LIMIT {limit}";

        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var p in parameters)
            cmd.Parameters.Add(p);

        var items = new List<MemoryItem>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var id = reader.GetGuid(0).ToString();
            var payloadJson = reader.GetString(1);
            var payload = JsonSerializer.Deserialize<Dictionary<string, object?>>(payloadJson) ?? [];
            var memory = payload.TryGetValue("data", out var data) ? data?.ToString() ?? "" : "";

            items.Add(new MemoryItem
            {
                Id = id,
                Memory = memory,
                Score = 1.0,
                Metadata = payload,
            });
        }

        return items;
    }

    private async Task EnsureTableAsync(NpgsqlConnection conn, CancellationToken ct)
    {
        var sql = $"""
            CREATE TABLE IF NOT EXISTS {_tableName} (
                id UUID PRIMARY KEY,
                vector vector(1536),
                payload JSONB
            )
            """;
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
