using Geminarr.Api.V1;
using Geminarr.Core;
using Geminarr.Core.SystemInfo;
using Geminarr.Host.Health;
using Geminarr.Host.Http;
using Microsoft.OpenApi;

// The content root is the binary's folder, whatever the working directory (like the *arr apps).
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

// Settings come from appsettings.json, then environment variables prefixed with GEMINARR__.
builder.Configuration.AddEnvironmentVariables(prefix: "GEMINARR__");
// The command line keeps the last word, as in every .NET application: appsettings.json, then GEMINARR__*, then the arguments.
builder.Configuration.AddCommandLine(args);

builder.Services.AddGeminarrCore(builder.Configuration);
builder.Services.AddGeminarrHealthChecks();
builder.Services.AddGeminarrApiV1();
builder.Services.AddProblemDetails();
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

app.UseExceptionHandler();
app.UseGeminarrSecurityHeaders();
app.UseStatusCodePages();
app.UseGeminarrFrontendFiles();
app.UseRouting();
app.MapControllers();
// The description of the API is served as it is while nothing sensitive exists; it goes behind the sign-in with the first vertical (docs/SPEC.md, F-ACC-03).
app.MapOpenApi();
app.MapGeminarrHealthChecks();
app.MapGeminarrFrontendFallback();

await app.RunAsync();

/// <summary>Entry point, exposed for integration tests (WebApplicationFactory).</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S1118:Utility classes should not have public constructors", Justification = "WebApplicationFactory<Program> needs a public type to start the host; it is not a utility class.")]
public partial class Program;
