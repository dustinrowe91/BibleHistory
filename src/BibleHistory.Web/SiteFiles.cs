using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;

namespace BibleHistory.Web;

/// <summary>
/// The public site: the same HTML pages, scripts, icons and study files that GitHub Pages serves today, read from the
/// folder named by <c>Site:Root</c>. In development that is the repository root; in a published build it is the
/// <c>site</c> folder the project file copies them into. Only these files are ever served, so the rest of the
/// repository (source code, tools, .git) is never exposed even when the site root is the repository itself.
/// </summary>
public static class SiteFiles
{
    static readonly HashSet<string> RootFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "site.css", "site.js", "sw.js", "manifest.webmanifest", "favicon.svg"
    };

    static readonly string[] Folders = ["icons/", "studies/"];

    /// <summary>True when a request path (such as "/kings.html" or "/icons/icon-192.png") is part of the public site.</summary>
    public static bool IsPublic(string path)
    {
        var p = path.TrimStart('/');
        if (p.Length == 0 || p.Contains("..") || p.Contains('\\')) return false;
        if (!p.Contains('/')) return p.EndsWith(".html", StringComparison.OrdinalIgnoreCase) || RootFiles.Contains(p);
        return Folders.Any(f => p.StartsWith(f, StringComparison.OrdinalIgnoreCase));
    }

    public static IFileProvider CreateProvider(IWebHostEnvironment env, IConfiguration config)
    {
        var root = Path.GetFullPath(Path.Combine(env.ContentRootPath, config["Site:Root"] ?? "site"));
        if (!File.Exists(Path.Combine(root, "index.html")))
            throw new InvalidOperationException($"The site files were not found in '{root}'. Set Site:Root to the folder that holds index.html.");
        return new PublicOnly(new PhysicalFileProvider(root));
    }

    public static FileExtensionContentTypeProvider ContentTypes()
    {
        var types = new FileExtensionContentTypeProvider();
        types.Mappings[".webmanifest"] = "application/manifest+json";
        return types;
    }

    sealed class PublicOnly(PhysicalFileProvider inner) : IFileProvider
    {
        public IFileInfo GetFileInfo(string subpath) =>
            IsPublic(subpath) ? inner.GetFileInfo(subpath) : new NotFoundFileInfo(subpath);

        // Only the site root is listed, so "/" can find index.html; no other folder can be browsed.
        public IDirectoryContents GetDirectoryContents(string subpath) =>
            subpath is "" or "/" ? new Filtered(inner.GetDirectoryContents(subpath)) : NotFoundDirectoryContents.Singleton;

        public IChangeToken Watch(string filter) => inner.Watch(filter);
    }

    sealed class Filtered(IDirectoryContents inner) : IDirectoryContents
    {
        public bool Exists => inner.Exists;
        public IEnumerator<IFileInfo> GetEnumerator() => inner.Where(f => !f.IsDirectory && IsPublic(f.Name)).GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
