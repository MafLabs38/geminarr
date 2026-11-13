using Geminarr.Api.V1;
using Geminarr.Core;
using Geminarr.Core.Access;
using Geminarr.Core.SystemInfo;
using Geminarr.Host.Health;
using Geminarr.Host.Http;
using Geminarr.Host.Security;
using Microsoft.OpenApi;

// The content root is the binary's folder, whatever the working directory (like the *arr apps).
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

// Settings come from appsettings.json, then environment variables prefixed with GEMINARR__.
builder.Configuration.AddEnvironmentVariables(prefix: "GEMINARR__");

builder.Services.AddGeminarrCore(builder.Configuration);
builder.Services.AddGeminarrHealthChecks();
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

app.UseExceptionHandler();
app.UseGeminarrSecurityHeaders();
app.UseMiddleware<AllowedHostsMiddleware>();
app.UseStatusCodePages();
app.UseGeminarrFrontendFiles();
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
