using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BibleHistory.Web.Tests;

public class SiteTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    static readonly string RepoRoot = FindRepoRoot();

    static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "BibleHistory.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found");
    }

    public static TheoryData<string> Pages()
    {
        var data = new TheoryData<string>();
        foreach (var f in Directory.GetFiles(RepoRoot, "*.html").Order()) data.Add("/" + Path.GetFileName(f));
        return data;
    }

    // Every file sw.js saves for offline use must exist, or installing the service worker fails
    public static TheoryData<string> OfflineFiles()
    {
        var sw = File.ReadAllText(Path.Combine(RepoRoot, "sw.js"));
        var core = Regex.Match(sw, @"const CORE = \[(.*?)\];", RegexOptions.Singleline).Groups[1].Value;
        var data = new TheoryData<string>();
        foreach (Match m in Regex.Matches(core, "\"([^\"]+)\"")) data.Add("/" + m.Groups[1].Value.TrimStart('.', '/'));
        return data;
    }

    [Theory, MemberData(nameof(Pages))]
    public async Task Every_page_is_served(string path)
    {
        var res = await factory.CreateClient().GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("text/html", res.Content.Headers.ContentType?.MediaType);
        Assert.Contains("no-cache", res.Headers.CacheControl?.ToString());
        Assert.Contains("<script src=\"site.js\"></script>", await res.Content.ReadAsStringAsync());
    }

    [Theory, MemberData(nameof(OfflineFiles))]
    public async Task Every_offline_file_exists(string path)
    {
        var res = await factory.CreateClient().GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task Home_is_the_timeline()
    {
        var html = await factory.CreateClient().GetStringAsync("/");
        Assert.Contains("<title>Bible Story Timeline</title>", html);
    }

    [Theory]
    [InlineData("/site.js", "text/javascript")]
    [InlineData("/sw.js", "text/javascript")]
    [InlineData("/site.css", "text/css")]
    [InlineData("/manifest.webmanifest", "application/manifest+json")]
    [InlineData("/favicon.svg", "image/svg+xml")]
    [InlineData("/studies/index.json", "application/json")]
    public async Task Assets_have_the_right_content_type(string path, string type)
    {
        var res = await factory.CreateClient().GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(type, res.Content.Headers.ContentType?.MediaType);
    }

    // The site root in development is the repository itself: nothing but the public site may be served from it
    [Theory]
    [InlineData("/README.md")]
    [InlineData("/BibleHistory.slnx")]
    [InlineData("/global.json")]
    [InlineData("/.gitignore")]
    [InlineData("/.git/config")]
    [InlineData("/.git/HEAD")]
    [InlineData("/src/BibleHistory.Web/Program.cs")]
    [InlineData("/src/BibleHistory.Web/appsettings.json")]
    [InlineData("/tools/StudyGenerator/Program.cs")]
    [InlineData("/tools/StudyGenerator/prompt.md")]
    [InlineData("/icons/../README.md")]
    [InlineData("/icons/")]
    [InlineData("/no-such-page.html")]
    public async Task Nothing_else_in_the_repository_is_served(string path)
    {
        var res = await factory.CreateClient().GetAsync(path);
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Health_check_answers()
    {
        var res = await factory.CreateClient().GetAsync("/healthz");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("Healthy", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Security_headers_are_set()
    {
        var res = await factory.CreateClient().GetAsync("/index.html");
        Assert.Equal("nosniff", res.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("SAMEORIGIN", res.Headers.GetValues("X-Frame-Options").Single());
    }

    [Theory]
    [InlineData("/kings.html", true)]
    [InlineData("/icons/icon-192.png", true)]
    [InlineData("/studies/JHN/1.json", true)]
    [InlineData("/site.css", true)]
    [InlineData("/README.md", false)]
    [InlineData("/src/x.html", false)]
    [InlineData("/../secret.html", false)]
    [InlineData("/", false)]
    public void IsPublic_matches_the_site(string path, bool expected) => Assert.Equal(expected, SiteFiles.IsPublic(path));
}
