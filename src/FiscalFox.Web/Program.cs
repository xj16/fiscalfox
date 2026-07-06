using FiscalFox.Web.Components;
using FiscalFox.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Typed HttpClient pointing at the FiscalFox API.
var apiBase = builder.Configuration["FiscalFox:ApiBaseUrl"] ?? "http://localhost:5080";
builder.Services.AddHttpClient<FiscalFoxApiClient>(client =>
{
    client.BaseAddress = new Uri(apiBase);
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
