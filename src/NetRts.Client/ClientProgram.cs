using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using NetRts.Client;
using NetRts.Client.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Add Blazored LocalStorage
builder.Services.AddBlazoredLocalStorage();

// Register a base HttpClient without authentication (used by AuthService for login/register)
// This is the default HttpClient that gets injected when you just inject HttpClient
builder.Services.AddScoped(sp =>
{
    return new HttpClient
    {
        BaseAddress = new Uri(builder.HostEnvironment.BaseAddress)
    };
});

// Register AuthService as scoped to maintain authentication state across the session
builder.Services.AddScoped<AuthService>();

// Register an authenticated HttpClient wrapper service for pages that need auth headers
builder.Services.AddScoped<AuthenticatedHttpClient>();

var host = builder.Build();

// Initialize auth state from local storage
var authService = host.Services.GetRequiredService<AuthService>();
await authService.InitializeAsync();

await host.RunAsync();
