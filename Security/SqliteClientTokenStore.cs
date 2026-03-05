using Microsoft.Data.Sqlite;

namespace ReverseGeocodeApi.Security;

/// <summary>
/// Simple SQLite-backed store for user client tokens (GUID).
/// Stored in App_Data/clienttokens.db (IIS friendly).
/// </summary>
public sealed class SqliteClientTokenStore : IClientTokenStore
{
    private readonly string _dbPath;
    private readonly object _initLock = new();
    private volatile bool _initialized;

    public SqliteClientTokenStore(IWebHostEnvironment env)
    {
        _dbPath = Path.Combine(env.ContentRootPath, "App_Data", "clienttokens.db");
    }

    private string ConnectionString => new SqliteConnectionStringBuilder
    {
        DataSource = _dbPath,
        Mode = SqliteOpenMode.ReadWriteCreate,
        Cache = SqliteCacheMode.Private,
        DefaultTimeout = 5 // segundos
    }.ToString();

    private async Task EnsureInitializedAsync(SqliteConnection conn, CancellationToken ct)
    {
        if (_initialized) return;

        // Double-check lock
        lock (_initLock)
        {
            if (_initialized) return;
            _initialized = true; // mark early to prevent duplicates
        }

        // conn já vem aberta
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
PRAGMA journal_mode=WAL;
PRAGMA synchronous=NORMAL;
CREATE TABLE IF NOT EXISTS ApiClientTokens (
  Token TEXT NOT NULL PRIMARY KEY,
  Email TEXT NOT NULL,
  CreatedAtUtc TEXT NOT NULL,
  LastSeenAtUtc TEXT NULL,
  RevokedAtUtc TEXT NULL
);

CREATE INDEX IF NOT EXISTS IX_ApiClientTokens_Email
ON ApiClientTokens(Email);

CREATE UNIQUE INDEX IF NOT EXISTS UX_ApiClientTokens_Email_Active
ON ApiClientTokens(Email)
WHERE RevokedAtUtc IS NULL;
";
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<Guid> IssueAsync(string email, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Email is required.", nameof(email));

        email = email.Trim().ToLowerInvariant();

        await using var conn = new SqliteConnection(ConnectionString);
        await conn.OpenAsync(ct);

        await EnsureInitializedAsync(conn, ct);

        // 1) verificar se já existe token ativo
        await using (var getCmd = conn.CreateCommand())
        {
            getCmd.CommandText = @"
SELECT Token
FROM ApiClientTokens
WHERE Email = $email AND RevokedAtUtc IS NULL
ORDER BY CreatedAtUtc ASC
LIMIT 1;";
            getCmd.Parameters.AddWithValue("$email", email);

            var existing = (string?)await getCmd.ExecuteScalarAsync(ct);
            if (!string.IsNullOrWhiteSpace(existing) && Guid.TryParse(existing, out var g))
                return g;
        }

        // 2) criar novo
        var token = Guid.NewGuid();
        var now = DateTime.UtcNow.ToString("O");

        try
        {
            await using var insCmd = conn.CreateCommand();
            insCmd.CommandText = @"
INSERT INTO ApiClientTokens (Token, Email, CreatedAtUtc, LastSeenAtUtc, RevokedAtUtc)
VALUES ($token, $email, $created, $lastSeen, NULL);";
            insCmd.Parameters.AddWithValue("$token", token.ToString());
            insCmd.Parameters.AddWithValue("$email", email);
            insCmd.Parameters.AddWithValue("$created", now);
            insCmd.Parameters.AddWithValue("$lastSeen", now);

            await insCmd.ExecuteNonQueryAsync(ct);
            return token;
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            // corrida concorrente → ler token existente
            await using var retryCmd = conn.CreateCommand();
            retryCmd.CommandText = @"
SELECT Token
FROM ApiClientTokens
WHERE Email = $email AND RevokedAtUtc IS NULL
LIMIT 1;";
            retryCmd.Parameters.AddWithValue("$email", email);

            var existing = (string?)await retryCmd.ExecuteScalarAsync(ct);
            if (!string.IsNullOrWhiteSpace(existing) && Guid.TryParse(existing, out var g))
                return g;

            throw;
        }
    }

    public async Task<bool> IsValidAsync(string email, Guid token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email)) return false;
        if (token == Guid.Empty) return false;

        email = email.Trim().ToLowerInvariant();

        await using var conn = new SqliteConnection(ConnectionString);
        await conn.OpenAsync(ct);

        await EnsureInitializedAsync(conn, ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT 1
FROM ApiClientTokens
WHERE Email = $email AND Token = $token AND RevokedAtUtc IS NULL
LIMIT 1;";
        cmd.Parameters.AddWithValue("$email", email);
        cmd.Parameters.AddWithValue("$token", token.ToString());

        var result = await cmd.ExecuteScalarAsync(ct);
        return result is not null;
    }

    public async Task TouchAsync(string email, Guid token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email)) return;
        if (token == Guid.Empty) return;

        email = email.Trim().ToLowerInvariant();

        await using var conn = new SqliteConnection(ConnectionString);
        await conn.OpenAsync(ct);

        await EnsureInitializedAsync(conn, ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
UPDATE ApiClientTokens
SET LastSeenAtUtc = $now
WHERE Email = $email AND Token = $token AND RevokedAtUtc IS NULL;";
        cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$email", email);
        cmd.Parameters.AddWithValue("$token", token.ToString());

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task RevokeAsync(string email, Guid token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email)) return;
        if (token == Guid.Empty) return;

        email = email.Trim().ToLowerInvariant();

        await using var conn = new SqliteConnection(ConnectionString);
        await conn.OpenAsync(ct);

        await EnsureInitializedAsync(conn, ct); 

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
UPDATE ApiClientTokens
SET RevokedAtUtc = $now
WHERE Email = $email AND Token = $token AND RevokedAtUtc IS NULL;";
        cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$email", email);
        cmd.Parameters.AddWithValue("$token", token.ToString());

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<Guid?> TryGetAsync(string email, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email))
            return null;

        email = email.Trim().ToLowerInvariant();

        await using var conn = new SqliteConnection(ConnectionString);
        await conn.OpenAsync(ct);

        await EnsureInitializedAsync(conn, ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT Token
FROM ApiClientTokens
WHERE Email = $email AND RevokedAtUtc IS NULL
ORDER BY CreatedAtUtc ASC
LIMIT 1;";
        cmd.Parameters.AddWithValue("$email", email);

        var existing = (string?)await cmd.ExecuteScalarAsync(ct);
        if (!string.IsNullOrWhiteSpace(existing) && Guid.TryParse(existing, out var g))
            return g;

        return null;
    }
}