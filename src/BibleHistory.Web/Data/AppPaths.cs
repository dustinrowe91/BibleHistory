namespace BibleHistory.Web.Data;

/// <summary>
/// Where the database and the sign-in cookie keys live. This must be outside the deployed app folder, so a deploy
/// never replaces or deletes them:
/// <list type="bullet">
/// <item>Data:Directory, when set.</item>
/// <item>On Azure App Service: $HOME/data (/home/data on Linux). /home is the app's persistent storage; deploys only
/// replace /home/site/wwwroot.</item>
/// <item>Otherwise (your machine): App_Data next to the project, which git ignores.</item>
/// </list>
/// </summary>
public static class AppPaths
{
    public static string DataDirectory(IConfiguration config, IHostEnvironment env)
    {
        var dir = config["Data:Directory"];
        if (string.IsNullOrWhiteSpace(dir))
        {
            var home = Environment.GetEnvironmentVariable("HOME");
            var onAppService = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WEBSITE_INSTANCE_ID"));
            dir = onAppService && !string.IsNullOrEmpty(home)
                ? Path.Combine(home, "data")
                : Path.Combine(env.ContentRootPath, "App_Data");
        }
        dir = Path.GetFullPath(dir);
        Directory.CreateDirectory(dir);
        return dir;
    }
}
