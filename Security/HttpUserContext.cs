using System.Security.Claims;

namespace ReverseGeocodeApi.Security;

public sealed class HttpUserContext : IUserContext
{
    private readonly IHttpContextAccessor _accessor;

    public HttpUserContext(IHttpContextAccessor accessor)
    {
        _accessor = accessor;
    }

    public ClaimsPrincipal? User => _accessor.HttpContext?.User;

    public bool IsAuthenticated => User?.Identity?.IsAuthenticated == true;

    public string? Email
    {
        get
        {
            var u = User;
            if (u is null) return null;

            // Robust across Google + Microsoft tokens/cookies
            return u.FindFirstValue(ClaimTypes.Email)
                ?? u.FindFirstValue("email")
                ?? u.FindFirstValue("preferred_username")
                ?? u.FindFirstValue("upn")
                ?? u.Identity?.Name;
        }
    }
}