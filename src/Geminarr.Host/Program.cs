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

builder.Services.AddGeminarrCore(builder.Configuration);
builder.Services.AddGeminarrHealthChecks();
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

app.UseExceptionHandler();
app.UseGeminarrSecurityHeaders();
app.UseStatusCodePages();
app.UseGeminarrFrontendFiles();
app.UseRouting();
app.MapControllers();
app.MapOpenApi();
app.MapGeminarrHealthChecks();
app.MapGeminarrFrontendFallback();

await app.RunAsync();

/// <summary>Entry point, exposed for integration tests (WebApplicationFactory).</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S1118:Utility classes should not have public constructors", Justification = "WebApplicationFactory<Program> needs a public type to start the host; it is not a utility class.")]
public partial class Program;
