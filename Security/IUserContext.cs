using System.Security.Claims;

namespace ReverseGeocodeApi.Security;

public interface IUserContext
{
    bool IsAuthenticated { get; }
    string? Email { get; }
    ClaimsPrincipal? User { get; }
}