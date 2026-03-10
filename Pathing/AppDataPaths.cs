namespace ReverseGeocodeApi.Pathing;

public static class AppDataPaths
{
    public static string GetAppDataPath(string contentRootPath)
    {
        var home = Environment.GetEnvironmentVariable("HOME");

        return !string.IsNullOrWhiteSpace(home)
            ? Path.Combine(home, "data")
            : Path.Combine(contentRootPath, "App_Data");
    }

    public static string GetKeysPath(string contentRootPath)
        => Path.Combine(GetAppDataPath(contentRootPath), "keys");

    public static string GetClientTokensDatabasePath(string contentRootPath)
        => Path.Combine(GetAppDataPath(contentRootPath), "clienttokens.db");
}
