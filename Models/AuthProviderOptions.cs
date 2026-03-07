namespace ReverseGeocodeApi.Models;

public sealed class GoogleAuthOptions
{
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
}

public sealed class MicrosoftAuthOptions
{
    public string TenantId { get; set; } = "common";
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
}
