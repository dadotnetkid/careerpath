using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using CareerPath.Client;
using CareerPath.Client.Services;
using MudBlazor.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddMudServices();
builder.Services.AddScoped<AuthService>();

var host = builder.Build();

// Seed the AuthService with the current cookie state so the top bar and any
// guarded pages render correctly on first paint. Failures here (e.g. server
// unreachable on cold start) are swallowed — the user can still try to sign in.
var auth = host.Services.GetRequiredService<AuthService>();
try { await auth.RefreshAsync(); } catch { /* OK on cold start */ }

await host.RunAsync();
