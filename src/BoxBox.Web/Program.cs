using BoxBox.Web;
using BoxBox.Web.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

// Program.cs is the app's entry point and its "composition root": it builds the host, then registers the
// services that components can ask for. Everything here runs inside the browser, in WebAssembly.
var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");              // mount the root component on <div id="app">
builder.RootComponents.Add<HeadOutlet>("head::after"); // lets components set <title> etc.

// The web app is served from /next/ but the API lives at /api/ on the same origin, so point the
// HttpClient at the origin root (scheme + host), not at the /next/ base address.
var origin = new Uri(new Uri(builder.HostEnvironment.BaseAddress).GetLeftPart(UriPartial.Authority));
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = origin });

// Dependency injection: components write `@inject BoxBoxApi Api` and the framework hands them this instance.
builder.Services.AddScoped<BoxBoxApi>();

await builder.Build().RunAsync();
