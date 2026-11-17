using Geminarr.Api.V1;
using Geminarr.Browser;
using Geminarr.Core;
using Geminarr.Core.Access;
using Geminarr.Core.SystemInfo;
using Geminarr.Host.Health;
using Geminarr.Host.Http;
using Geminarr.Host.Pipeline;
using Geminarr.Host.Security;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;

// The content root is the binary's folder, whatever the working directory (like the *arr apps).
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

// Settings come from appsettings.json, then environment variables prefixed with GEMINARR__.
builder.Configuration.AddEnvironmentVariables(prefix: "GEMINARR__");

// The desktop of the browser has a port of its own (ADR 0019): it is open when a desktop is configured, and the interface's port is never the desktop's.
if (!string.IsNullOrWhiteSpace(builder.Configuration["Browser:DesktopUrl"]))
{
    var desktopPort = builder.Configuration.GetValue("Browser:DesktopPort", new Geminarr.Core.Browser.BrowserOptions().DesktopPort);
    var urls = (builder.Configuration["Urls"] ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    if (urls.Length > 0 && urls[0].EndsWith($":{desktopPort}", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("Browser:DesktopPort (GEMINARR__BROWSER__DESKTOPPORT) is the port of the interface: the desktop needs a port of its own.");
    }

    if (urls.Length > 0 && !urls.Any(url => url.EndsWith($":{desktopPort}", StringComparison.Ordinal)))
    {
        // Same scheme as the interface's address: the desktop's origin is open the way the interface is, and TLS is the proxy's.
        var scheme = urls[0][..urls[0].IndexOf("://", StringComparison.Ordinal)];
        builder.Configuration["Urls"] = string.Join(';', [.. urls, $"{scheme}://+:{desktopPort}"]);
    }
}

builder.Services.AddGeminarrCore(builder.Configuration);
builder.Services.AddGeminarrBrowser();
builder.Services.AddGeminarrBrowserRelay();
builder.Services.AddSingleton<Geminarr.Core.Browser.IBrowserExposure, Geminarr.Host.Http.ProcBrowserExposure>();
builder.Services.AddGeminarrHealthChecks();
builder.Services.AddHostedService<PassScheduler>();
builder.Services.AddHostedService<SignInWatcher>();
builder.Services.AddGeminarrSecurity();
builder.Services.AddGeminarrApiV1();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ClientErrorExceptionHandler>();
builder.Services.AddOpenApi(options => options.AddDocumentTransformer((document, _, _) =>
{
    document.Info = new OpenApiInfo
    {
        Title = "Geminarr API",
        Version = "v1",
        Description = "The REST API of Geminarr, a satellite of Mnemarr that brings the history of a web assistant into its memory. The interface uses it.",
    };
    return Task.CompletedTask;
}));

var app = builder.Build();

// Singletons are created on first use: resolve the status service now, so that "started at" is the start of
// the application and not the time of the first request for its status.
_ = app.Services.GetRequiredService<ISystemStatusService>();

// A container where nobody can sign in is alive and closed, and its sign-in page only says "wrong": the log is where the reason is told without a session.
if (app.Services.GetRequiredService<IOperatorCredentials>().Status is var operatorStatus and not OperatorStatus.Ready)
{
    app.Logger.LogWarning("Nobody can sign in ({Status}); everything but the probes stays closed. The reason codes are in docs/runbook.md.", operatorStatus);
}

// A lightened guard is said at each start (ADR 0018): a deployment copied from a sandbox must not run like it without a trace.
if (app.Services.GetRequiredService<IOptions<AuthOptions>>().Value.MinPasswordLength < OperatorCredentials.DefaultMinPasswordLength)
{
    app.Logger.LogWarning("The minimum length of the operator's password is lowered (GEMINARR__AUTH__MINPASSWORDLENGTH): this is for a sandbox that nobody else reaches. The operator's password is also the key to the browser's session.");
}

app.UseExceptionHandler();
// The desktop of the browser is another program's: it has an origin of its own, a port that answers for it and for nothing else (ADR 0019).
app.UseGeminarrDesktopOrigin();
app.UseGeminarrSecurityHeaders();
app.UseMiddleware<AllowedHostsMiddleware>();
app.UseStatusCodePages();
app.UseGeminarrFrontendFiles();
app.UseWebSockets();
app.UseRouting();
app.UseGeminarrSecurity();
app.MapControllers();
// The description of the API is behind the sign-in like the rest (the fallback policy).
app.MapOpenApi();
app.MapGeminarrHealthChecks();
app.MapGeminarrFrontendFallback();

await app.RunAsync();

/// <summary>Entry point, exposed for integration tests (WebApplicationFactory).</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S1118:Utility classes should not have public constructors", Justification = "WebApplicationFactory<Program> needs a public type to start the host; it is not a utility class.")]
public partial class Program;
