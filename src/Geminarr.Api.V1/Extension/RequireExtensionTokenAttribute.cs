using Geminarr.Core.Extension;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;

namespace Geminarr.Api.V1.Extension;

/// <summary>
/// The route is for the extension of the browser alone: it answers 401 to a call that does not present the token, <b>before</b> the body is read or parsed (an authorization filter runs before model binding), so that nobody
/// without the token can make the container read and hold a large body.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RequireExtensionTokenAttribute : TypeFilterAttribute
{
    public RequireExtensionTokenAttribute() : base(typeof(ExtensionTokenFilter))
    {
    }
}

internal sealed partial class ExtensionTokenFilter : IAuthorizationFilter
{
    private const string BearerPrefix = "Bearer ";

    private readonly IExtensionToken _token;
    private readonly ILogger<ExtensionTokenFilter> _logger;

    public ExtensionTokenFilter(IExtensionToken token, ILogger<ExtensionTokenFilter> logger)
    {
        _token = token;
        _logger = logger;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The extension was refused: it did not present the token.")]
    private partial void LogRefused();

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var header = context.HttpContext.Request.Headers[HeaderNames.Authorization].ToString();
        var presented = header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase) ? header[BearerPrefix.Length..].Trim() : null;
        if (_token.Verify(presented))
        {
            return;
        }

        LogRefused();
        context.Result = new ObjectResult(new ProblemDetails { Status = StatusCodes.Status401Unauthorized, Title = "The token is not the one of Geminarr." })
        {
            StatusCode = StatusCodes.Status401Unauthorized,
            ContentTypes = { "application/problem+json" },
        };
    }
}
