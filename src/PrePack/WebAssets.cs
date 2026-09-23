using System.Reflection;
using Microsoft.Web.WebView2.Core;

namespace PrePack;

/// <summary>Serves wwwroot/ from embedded resources — no files on disk, no web server.</summary>
internal static class WebAssets
{
    public const string Host = "prepack.app";
    public const string EntryUrl = "https://" + Host + "/index.html";

    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".html"] = "text/html; charset=utf-8",
        [".js"] = "text/javascript; charset=utf-8",
        [".css"] = "text/css; charset=utf-8",
        [".json"] = "application/json; charset=utf-8",
        [".svg"] = "image/svg+xml",
        [".png"] = "image/png",
        [".ico"] = "image/x-icon",
        [".woff2"] = "font/woff2",
        [".ttf"] = "font/ttf",
    };

    public static void Attach(CoreWebView2 wv)
    {
        wv.AddWebResourceRequestedFilter("https://" + Host + "/*", CoreWebView2WebResourceContext.All);
        wv.WebResourceRequested += (_, e) =>
        {
            var path = new Uri(e.Request.Uri).AbsolutePath.TrimStart('/');
            if (path.Length == 0) path = "index.html";
            var stream = Open("wwwroot/" + path);
            e.Response = stream == null
                ? wv.Environment.CreateWebResourceResponse(null, 404, "Not Found", "")
                : wv.Environment.CreateWebResourceResponse(stream, 200, "OK",
                    "Content-Type: " + ContentType(path) + "\r\nCache-Control: no-store");
        };
    }

    /// <summary>Opens an embedded resource by logical name, or null when missing.</summary>
    public static Stream? Open(string logicalName) =>
        Assembly.GetExecutingAssembly().GetManifestResourceStream(logicalName);

    public static string ReadText(string logicalName)
    {
        using var s = Open(logicalName) ?? throw new FileNotFoundException(logicalName);
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }

    private static string ContentType(string path) =>
        ContentTypes.TryGetValue(Path.GetExtension(path), out var ct) ? ct : "application/octet-stream";
}
