using System.Text.RegularExpressions;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Net.Http.Headers;

namespace BoxBox.Api;

/// <summary>
/// Static file serving for the two front ends: the classic page at "/" and the Blazor WebAssembly app at "/next/".
/// Blazor's publish output ships a pre-compressed copy of every file (.br and .gz). This serves those when the
/// browser accepts them (7.8 MB of runtime files becomes about 2.5 MB over the wire) and caches the
/// fingerprinted files for a year, so a phone only downloads the runtime once.
/// </summary>
public static class StaticAssets
{
    private const string BlazorPrefix = "/next";

    // Published Blazor files carry a 10-character fingerprint, e.g. dotnet.native.6ilbvndvcf.js
    private static readonly Regex Fingerprinted = new(@"\.[a-z0-9]{10}\.", RegexOptions.Compiled);

    public static WebApplication UseBoxBoxStaticFiles(this WebApplication app)
    {
        var types = new FileExtensionContentTypeProvider();
        // Let the static file middleware find the pre-compressed files; the real type is restored in OnPrepareResponse.
        types.Mappings[".br"] = "application/octet-stream";
        types.Mappings[".gz"] = "application/octet-stream";

        // The app's base href is /next/, so /next must become /next/ or relative URLs would break.
        app.Use(async (ctx, next) =>
        {
            if (ctx.Request.Path == BlazorPrefix && HttpMethods.IsGet(ctx.Request.Method))
            {
                ctx.Response.Redirect(BlazorPrefix + "/" + ctx.Request.QueryString);
                return;
            }
            await next();
        });

        app.Use(async (ctx, next) =>
        {
            if (HttpMethods.IsGet(ctx.Request.Method) && ctx.Request.Path.StartsWithSegments(BlazorPrefix))
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

        app.UseDefaultFiles(); // "/" -> /index.html, "/next/" -> /next/index.html
        app.UseStaticFiles(new StaticFileOptions
        {
            ContentTypeProvider = types,
            ServeUnknownFileTypes = true, // Blazor's ICU data files (.dat) have no registered type
            OnPrepareResponse = ctx =>
            {
                var response = ctx.Context.Response;
                if (ctx.Context.Items["originalContentType"] is string type) response.ContentType = type;

                var path = ctx.Context.Request.Path.Value ?? "";
                if (path.StartsWith(BlazorPrefix + "/_framework/", StringComparison.Ordinal) && Fingerprinted.IsMatch(path))
                    response.Headers.CacheControl = "public,max-age=31536000,immutable";
                else if (path.StartsWith(BlazorPrefix, StringComparison.Ordinal))
                    response.Headers.CacheControl = "no-cache"; // index.html etc.: always revalidate so updates show up
            }
        });
        return app;
    }
}
