using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ReverseGeocodeApi.Security;

/// <summary>
/// Simple SQLite-backed store for user client tokens (GUID).
/// Stored in App_Data/clienttokens.db (IIS friendly).
/// </summary>
public sealed class SqliteClientTokenStore : IClientTokenStore
{
    private const string DataProtectionPurpose = "ReverseGeocodeApi:ClientTokenStore:v1";

    private readonly string _dbPath;
    private readonly ILogger<SqliteClientTokenStore> _logger;
    private readonly IDataProtector _tokenProtector;
    private readonly SemaphoreSlim _initSemaphore = new(1, 1);
    private volatile bool _initialized;

    public SqliteClientTokenStore(
        IWebHostEnvironment env,
        IDataProtectionProvider dataProtectionProvider,
        ILogger<SqliteClientTokenStore> logger)
    {
        _dbPath = Path.Combine(env.ContentRootPath, "App_Data", "clienttokens.db");
        _logger = logger;
        _tokenProtector = dataProtectionProvider.CreateProtector(DataProtectionPurpose);
    }

    private string ConnectionString => new SqliteConnectionStringBuilder
    {
        DataSource = _dbPath,
        Mode = SqliteOpenMode.ReadWriteCreate,
        Cache = SqliteCacheMode.Private,
        DefaultTimeout = 5
    }.ToString();

    private async Task EnsureInitializedAsync(SqliteConnection conn, CancellationToken ct)
    {
        if (_initialized)
            return;

        await _initSemaphore.WaitAsync(ct);

        try
        {
            if (_initialized)
                return;

            await Execute("PRAGMA journal_mode=WAL;", conn, ct);
            await Execute("PRAGMA synchronous=NORMAL;", conn, ct);
            await Execute(@"
                CREATE TABLE IF NOT EXISTS ApiClientTokens (
                Token TEXT NOT NULL PRIMARY KEY,
                Email TEXT NOT NULL,
                CreatedAtUtc TEXT NOT NULL,
                LastSeenAtUtc TEXT NULL,
                RevokedAtUtc TEXT NULL
                );", conn, ct);
            await Execute(@"
                CREATE INDEX IF NOT EXISTS IX_ApiClientTokens_Email
                ON ApiClientTokens(Email);", conn, ct);
            await Execute(@"
                CREATE UNIQUE INDEX IF NOT EXISTS UX_ApiClientTokens_Email_Active
                ON ApiClientTokens(Email)
                WHERE RevokedAtUtc IS NULL;", conn, ct);

            await EnsureTokenHashColumnAsync(conn, ct);
            await Execute(@"
                CREATE INDEX IF NOT EXISTS IX_ApiClientTokens_TokenHash
                ON ApiClientTokens(TokenHash);", conn, ct);

            _initialized = true;

            _logger.LogInformation("SQLite client token store initialized at {Path}", _dbPath);
        }
        finally
        {
            _initSemaphore.Release();
        }
    }

    public async Task<Guid> IssueAsync(string email, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Email is required.", nameof(email));

        email = email.Trim().ToLowerInvariant();

        await using var conn = new SqliteConnection(ConnectionString);
        await conn.OpenAsync(ct);

        await EnsureInitializedAsync(conn, ct);
        var candidate = Guid.NewGuid();
        var now = DateTime.UtcNow.ToString("O");

        await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var committed = false;

        try
        {
            await using var getCmd = conn.CreateCommand();
            getCmd.Transaction = tx;
            getCmd.CommandText = @"
                SELECT Token, TokenHash
                FROM ApiClientTokens
                WHERE Email = $email AND RevokedAtUtc IS NULL
                ORDER BY CreatedAtUtc ASC
                LIMIT 1;";
            getCmd.Parameters.AddWithValue("$email", email);

            await using var reader = await getCmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
            {
                await InsertNewTokenAsync(conn, tx, email, candidate, now, ct);

                await tx.CommitAsync(ct);
                committed = true;
                _logger.LogInformation("Issued new client token for {Email}", email);
                return candidate;
            }

            var storedToken = reader.GetString(0);
            var storedHash = reader.IsDBNull(1) ? null : reader.GetString(1);
            reader.Close();

            var existingGuid = TryExtractGuid(storedToken);
            if (existingGuid.HasValue)
            {
                await MigrateToEncryptedAsync(conn, tx, email, storedToken, existingGuid.Value, now, ct);
                await tx.CommitAsync(ct);
                committed = true;
                _logger.LogInformation("Reusing existing active client token for {Email}", email);
                return existingGuid.Value;
            }

            if (TryUnprotectGuid(storedToken, out var decryptedGuid))
            {
                if (string.IsNullOrWhiteSpace(storedHash))
                {
                    await UpdateTokenHashAsync(conn, tx, email, storedToken, HashToken(decryptedGuid), ct);
                }

                await tx.CommitAsync(ct);
                committed = true;
                _logger.LogInformation("Reusing existing active client token for {Email}", email);
                return decryptedGuid;
            }

            await ReplaceActiveTokenAsync(conn, tx, email, candidate, now, ct);
            await tx.CommitAsync(ct);
            committed = true;
            _logger.LogWarning("Active token row for {Email} was unreadable. Rotated to a new token.", email);
            return candidate;
        }
        catch
        {
            if (!committed)
            {
                try
                {
                    await tx.RollbackAsync(ct);
                }
                catch (InvalidOperationException)
                {
                    // Transaction may already be completed/closed; preserve original exception.
                }
            }

            throw;
        }
    }

    public async Task<bool> IsValidAsync(string email, Guid token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email)) return false;
        if (token == Guid.Empty) return false;

        email = email.Trim().ToLowerInvariant();
        var tokenHash = HashToken(token);
        var tokenText = token.ToString();

        await using var conn = new SqliteConnection(ConnectionString);
        await conn.OpenAsync(ct);

        await EnsureInitializedAsync(conn, ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT 1
            FROM ApiClientTokens
            WHERE Email = $email
              AND RevokedAtUtc IS NULL
              AND (TokenHash = $tokenHash OR Token = $tokenText)
            LIMIT 1;";
        cmd.Parameters.AddWithValue("$email", email);
        cmd.Parameters.AddWithValue("$tokenHash", tokenHash);
        cmd.Parameters.AddWithValue("$tokenText", tokenText);

        var result = await cmd.ExecuteScalarAsync(ct);
        return result is not null;
    }

    public async Task TouchAsync(string email, Guid token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email)) return;
        if (token == Guid.Empty) return;

        email = email.Trim().ToLowerInvariant();
        var tokenHash = HashToken(token);
        var tokenText = token.ToString();

        await using var conn = new SqliteConnection(ConnectionString);
        await conn.OpenAsync(ct);

        await EnsureInitializedAsync(conn, ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
        UPDATE ApiClientTokens
        SET LastSeenAtUtc = $now
        WHERE Email = $email
          AND RevokedAtUtc IS NULL
          AND (TokenHash = $tokenHash OR Token = $tokenText)
          AND (
              LastSeenAtUtc IS NULL
              OR LastSeenAtUtc < $todayUtc
          );";
        cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$todayUtc", DateTime.UtcNow.Date.ToString("O"));
        cmd.Parameters.AddWithValue("$email", email);
        cmd.Parameters.AddWithValue("$tokenHash", tokenHash);
        cmd.Parameters.AddWithValue("$tokenText", tokenText);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task RevokeAsync(string email, Guid token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email)) return;
        if (token == Guid.Empty) return;

        email = email.Trim().ToLowerInvariant();
        var tokenHash = HashToken(token);
        var tokenText = token.ToString();

        await using var conn = new SqliteConnection(ConnectionString);
        await conn.OpenAsync(ct);

        await EnsureInitializedAsync(conn, ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE ApiClientTokens
            SET RevokedAtUtc = $now
            WHERE Email = $email
              AND RevokedAtUtc IS NULL
              AND (TokenHash = $tokenHash OR Token = $tokenText);";
        cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$email", email);
        cmd.Parameters.AddWithValue("$tokenHash", tokenHash);
        cmd.Parameters.AddWithValue("$tokenText", tokenText);

        var affected = await cmd.ExecuteNonQueryAsync(ct);
        if (affected > 0)
            _logger.LogInformation("Revoked client token for {Email}", email);
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
            SELECT Token, TokenHash
            FROM ApiClientTokens
            WHERE Email = $email AND RevokedAtUtc IS NULL
            ORDER BY CreatedAtUtc ASC
            LIMIT 1;";
        cmd.Parameters.AddWithValue("$email", email);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;

        var storedToken = reader.GetString(0);
        var storedHash = reader.IsDBNull(1) ? null : reader.GetString(1);
        reader.Close();

        var parsedLegacy = TryExtractGuid(storedToken);
        if (parsedLegacy.HasValue)
        {
            await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
            await MigrateToEncryptedAsync(conn, tx, email, storedToken, parsedLegacy.Value, DateTime.UtcNow.ToString("O"), ct);
            await tx.CommitAsync(ct);
            return parsedLegacy;
        }

        if (!TryUnprotectGuid(storedToken, out var decrypted))
            return null;

        if (string.IsNullOrWhiteSpace(storedHash))
        {
            await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
            await UpdateTokenHashAsync(conn, tx, email, storedToken, HashToken(decrypted), ct);
            await tx.CommitAsync(ct);
        }

        return decrypted;
    }

    private async Task EnsureTokenHashColumnAsync(SqliteConnection conn, CancellationToken ct)
    {
        await using var checkCmd = conn.CreateCommand();
        checkCmd.CommandText = @"
            SELECT 1
            FROM pragma_table_info('ApiClientTokens')
            WHERE name = 'TokenHash'
            LIMIT 1;";

        var exists = await checkCmd.ExecuteScalarAsync(ct);
        if (exists is null)
        {
            await Execute("ALTER TABLE ApiClientTokens ADD COLUMN TokenHash TEXT NULL;", conn, ct);
        }
    }

    private async Task InsertNewTokenAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        string email,
        Guid token,
        string now,
        CancellationToken ct)
    {
        await using var insCmd = conn.CreateCommand();
        insCmd.Transaction = tx;
        insCmd.CommandText = @"
            INSERT INTO ApiClientTokens (Token, TokenHash, Email, CreatedAtUtc, LastSeenAtUtc, RevokedAtUtc)
            VALUES ($token, $tokenHash, $email, $created, $lastSeen, NULL);";
        insCmd.Parameters.AddWithValue("$token", ProtectToken(token));
        insCmd.Parameters.AddWithValue("$tokenHash", HashToken(token));
        insCmd.Parameters.AddWithValue("$email", email);
        insCmd.Parameters.AddWithValue("$created", now);
        insCmd.Parameters.AddWithValue("$lastSeen", now);

        await insCmd.ExecuteNonQueryAsync(ct);
    }

    private async Task ReplaceActiveTokenAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        string email,
        Guid token,
        string now,
        CancellationToken ct)
    {
        await using var updateCmd = conn.CreateCommand();
        updateCmd.Transaction = tx;
        updateCmd.CommandText = @"
            UPDATE ApiClientTokens
            SET Token = $token,
                TokenHash = $tokenHash,
                CreatedAtUtc = $created,
                LastSeenAtUtc = $lastSeen
            WHERE Email = $email AND RevokedAtUtc IS NULL;";
        updateCmd.Parameters.AddWithValue("$token", ProtectToken(token));
        updateCmd.Parameters.AddWithValue("$tokenHash", HashToken(token));
        updateCmd.Parameters.AddWithValue("$created", now);
        updateCmd.Parameters.AddWithValue("$lastSeen", now);
        updateCmd.Parameters.AddWithValue("$email", email);

        await updateCmd.ExecuteNonQueryAsync(ct);
    }

    private async Task MigrateToEncryptedAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        string email,
        string oldToken,
        Guid guid,
        string now,
        CancellationToken ct)
    {
        await using var migrateCmd = conn.CreateCommand();
        migrateCmd.Transaction = tx;
        migrateCmd.CommandText = @"
            UPDATE ApiClientTokens
            SET Token = $newToken,
                TokenHash = $tokenHash,
                LastSeenAtUtc = COALESCE(LastSeenAtUtc, $now)
            WHERE Email = $email AND Token = $oldToken AND RevokedAtUtc IS NULL;";
        migrateCmd.Parameters.AddWithValue("$newToken", ProtectToken(guid));
        migrateCmd.Parameters.AddWithValue("$tokenHash", HashToken(guid));
        migrateCmd.Parameters.AddWithValue("$now", now);
        migrateCmd.Parameters.AddWithValue("$email", email);
        migrateCmd.Parameters.AddWithValue("$oldToken", oldToken);

        await migrateCmd.ExecuteNonQueryAsync(ct);
    }

    private async Task UpdateTokenHashAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        string email,
        string storedToken,
        string tokenHash,
        CancellationToken ct)
    {
        await using var updateCmd = conn.CreateCommand();
        updateCmd.Transaction = tx;
        updateCmd.CommandText = @"
            UPDATE ApiClientTokens
            SET TokenHash = $tokenHash
            WHERE Email = $email AND Token = $token AND RevokedAtUtc IS NULL;";
        updateCmd.Parameters.AddWithValue("$tokenHash", tokenHash);
        updateCmd.Parameters.AddWithValue("$email", email);
        updateCmd.Parameters.AddWithValue("$token", storedToken);
        await updateCmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task Execute(string sql, SqliteConnection conn, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private string ProtectToken(Guid token)
        => _tokenProtector.Protect(token.ToString("D"));

    private bool TryUnprotectGuid(string protectedToken, out Guid guid)
    {
        guid = Guid.Empty;

        try
        {
            var plaintext = _tokenProtector.Unprotect(protectedToken);
            return Guid.TryParse(plaintext, out guid);
        }
        catch
        {
            return false;
        }
    }

    private static Guid? TryExtractGuid(string raw)
    {
        return Guid.TryParse(raw, out var guid) ? guid : null;
    }

    private static string HashToken(Guid token)
    {
        var bytes = Encoding.UTF8.GetBytes(token.ToString("D"));
        var hash = SHA256.HashData(bytes);
        var sb = new StringBuilder(hash.Length * 2);
        foreach (var b in hash)
        {
            sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }
}
