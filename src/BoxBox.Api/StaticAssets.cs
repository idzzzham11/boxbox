using System.Text.RegularExpressions;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Net.Http.Headers;

namespace BoxBox.Api;

/// <summary>
/// Static file serving. The Blazor WebAssembly app is the main front end at "/"; the original single-file page
/// is kept at "/classic/" as a fallback.
/// <para>
/// Blazor's publish output ships a pre-compressed copy of every file (.br and .gz). This serves those when the
/// browser accepts them (7.8 MB of runtime files becomes about 2.5 MB over the wire) and caches the
/// fingerprinted runtime files for a year, so a phone only downloads them once.
/// </para>
/// </summary>
public static class StaticAssets
{
    // Published Blazor files carry a 10-character fingerprint, e.g. dotnet.native.6ilbvndvcf.js
    private static readonly Regex Fingerprinted = new(@"\.[a-z0-9]{10}\.", RegexOptions.Compiled);

    public static WebApplication UseBoxBoxStaticFiles(this WebApplication app)
    {
        var types = new FileExtensionContentTypeProvider();
        // Let the static file middleware find the pre-compressed files; the real type is restored in OnPrepareResponse.
        types.Mappings[".br"] = "application/octet-stream";
        types.Mappings[".gz"] = "application/octet-stream";

        app.Use(async (ctx, next) =>
        {
            if (HttpMethods.IsGet(ctx.Request.Method))
            {
                // The Blazor app used to live at /next/. Keep old bookmarks and home-screen icons working.
                if (ctx.Request.Path.StartsWithSegments("/next"))
                {
                    ctx.Response.Redirect("/");
                    return;
                }
                // Relative URLs on the classic page need the trailing slash.
                if (ctx.Request.Path == "/classic")
                {
                    ctx.Response.Redirect("/classic/" + ctx.Request.QueryString);
                    return;
                }
            }
            await next();
        });

        app.Use(async (ctx, next) =>
        {
            if (HttpMethods.IsGet(ctx.Request.Method) && !ctx.Request.Path.StartsWithSegments("/api"))
            {
                var path = ctx.Request.Path.Value!;
                if (path.EndsWith('/')) path += "index.html";

                var accepted = ctx.Request.Headers.AcceptEncoding.ToString();
                foreach (var (encoding, extension) in new[] { ("br", ".br"), ("gzip", ".gz") })
                {
                    if (!accepted.Contains(encoding, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!app.Environment.WebRootFileProvider.GetFileInfo(path + extension).Exists) continue;

                    types.TryGetContentType(path, out var original);
                    ctx.Items["originalContentType"] = original ?? "application/octet-stream";
                    ctx.Request.Path = path + extension;
                    ctx.Response.Headers.ContentEncoding = encoding;
                    ctx.Response.Headers.Append(HeaderNames.Vary, HeaderNames.AcceptEncoding);
                    break;
                }
            }
            await next();
        });

        app.UseDefaultFiles(); // "/" -> /index.html, "/classic/" -> /classic/index.html
        app.UseStaticFiles(new StaticFileOptions
        {
            ContentTypeProvider = types,
            ServeUnknownFileTypes = true, // Blazor's ICU data files (.dat) have no registered type
            OnPrepareResponse = ctx =>
            {
                var response = ctx.Context.Response;
                if (ctx.Context.Items["originalContentType"] is string type) response.ContentType = type;

                // Judge caching by the logical file, not the .br/.gz variant that is actually being sent.
                var path = ctx.Context.Request.Path.Value ?? "";
                if (path.EndsWith(".br") || path.EndsWith(".gz")) path = path[..^3];

                if (path.StartsWith("/_framework/", StringComparison.Ordinal) && Fingerprinted.IsMatch(path))
                    response.Headers.CacheControl = "public,max-age=31536000,immutable";
                else if (path is "/index.html" or "/css/app.css" or "/manifest.webmanifest" || path.StartsWith("/classic/", StringComparison.Ordinal))
                    response.Headers.CacheControl = "no-cache"; // entry files: always revalidate so updates show up
            }
        });
        return app;
    }
}
