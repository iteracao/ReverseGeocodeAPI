namespace ReverseGeocodeApi.Security;

public interface IClientTokenStore
{
    /// <summary>
    /// Returns the existing token for the email, or creates a new one if none exists.
    /// </summary>
    Task<Guid> IssueAsync(string email, CancellationToken ct = default);

    /// <summary>
    /// Validates that the token is active and belongs to the given email.
    /// </summary>
    Task<bool> IsValidAsync(string email, Guid token, CancellationToken ct = default);

    /// <summary>
    /// Updates LastSeenAtUtc for an active token.
    /// </summary>
    Task TouchAsync(string email, Guid token, CancellationToken ct = default);

    /// <summary>
    /// Revokes a token.
    /// </summary>
    Task RevokeAsync(string email, Guid token, CancellationToken ct = default);

    Task<Guid?> TryGetAsync(string email, CancellationToken ct = default);
}