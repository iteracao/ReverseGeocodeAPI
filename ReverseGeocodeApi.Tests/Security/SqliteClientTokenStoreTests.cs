using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using ReverseGeocodeApi.Security;
using ReverseGeocodeApi.Tests.TestSupport;

namespace ReverseGeocodeApi.Tests.Security;

public sealed class SqliteClientTokenStoreTests
{
    [Fact]
    public async Task Issue_Validate_Revoke_Works()
    {
        var fixture = new TokenStoreFixture();
        var store = fixture.CreateStore();

        var email = "user@example.com";
        var token = await store.IssueAsync(email);

        var isValid = await store.IsValidAsync(email, token);
        Assert.True(isValid);

        await store.RevokeAsync(email, token);

        isValid = await store.IsValidAsync(email, token);
        Assert.False(isValid);
    }

    [Fact]
    public async Task Issue_Prevents_Duplicate_Active_Tokens()
    {
        var fixture = new TokenStoreFixture();
        var store = fixture.CreateStore();

        var email = "duplicate@example.com";
        var token1 = await store.IssueAsync(email);
        var token2 = await store.IssueAsync(email);

        Assert.Equal(token1, token2);

        await using var conn = new SqliteConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM ApiClientTokens WHERE Email = $email AND RevokedAtUtc IS NULL;";
        cmd.Parameters.AddWithValue("$email", email);
        var active = Convert.ToInt32(await cmd.ExecuteScalarAsync());

        Assert.Equal(1, active);
    }

    [Fact]
    public async Task Touch_Updates_LastSeen_For_Older_Day()
    {
        var fixture = new TokenStoreFixture();
        var store = fixture.CreateStore();

        var email = "touch@example.com";
        var token = await store.IssueAsync(email);

        await using (var conn = new SqliteConnection(fixture.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE ApiClientTokens
                SET LastSeenAtUtc = $old
                WHERE Email = $email AND TokenHash = $tokenHash AND RevokedAtUtc IS NULL;";
            cmd.Parameters.AddWithValue("$old", DateTime.UtcNow.AddDays(-2).ToString("O"));
            cmd.Parameters.AddWithValue("$email", email);
            cmd.Parameters.AddWithValue("$tokenHash", HashToken(token));
            await cmd.ExecuteNonQueryAsync();
        }

        await store.TouchAsync(email, token);

        await using (var conn = new SqliteConnection(fixture.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT LastSeenAtUtc
                FROM ApiClientTokens
                WHERE Email = $email AND TokenHash = $tokenHash AND RevokedAtUtc IS NULL
                LIMIT 1;";
            cmd.Parameters.AddWithValue("$email", email);
            cmd.Parameters.AddWithValue("$tokenHash", HashToken(token));
            var value = (string?)await cmd.ExecuteScalarAsync();

            Assert.False(string.IsNullOrWhiteSpace(value));
            var parsed = DateTime.Parse(value!, null, System.Globalization.DateTimeStyles.RoundtripKind);
            Assert.True(parsed > DateTime.UtcNow.AddHours(-1));
        }
    }

    [Fact]
    public async Task TryGet_Returns_Active_Token()
    {
        var fixture = new TokenStoreFixture();
        var store = fixture.CreateStore();

        var email = "getter@example.com";
        var issued = await store.IssueAsync(email);

        var got = await store.TryGetAsync(email);

        Assert.Equal(issued, got);
    }

    private static string HashToken(Guid token)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(token.ToString("D"));
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private sealed class TokenStoreFixture
    {
        private readonly string _root;

        public TokenStoreFixture()
        {
            _root = Path.Combine(Path.GetTempPath(), "ReverseGeocodeApi.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            Directory.CreateDirectory(Path.Combine(_root, "App_Data"));

            ConnectionString = new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(_root, "App_Data", "clienttokens.db"),
                Mode = SqliteOpenMode.ReadWriteCreate
            }.ToString();
        }

        public string ConnectionString { get; }

        public SqliteClientTokenStore CreateStore()
        {
            var env = new TestWebHostEnvironment { ContentRootPath = _root };
            var dp = new EphemeralDataProtectionProvider();
            return new SqliteClientTokenStore(env, dp, NullLogger<SqliteClientTokenStore>.Instance);
        }
    }
}
