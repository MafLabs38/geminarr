using Geminarr.Core.Extension;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;

namespace Geminarr.Api.V1.Extension;

/// <summary>What Geminarr answers to a push that it took: the conversations of the list that it wants read now.</summary>
public sealed record ExtensionPushResponse(IReadOnlyList<string> Open);

/// <summary>
/// The door of the extension that reads the assistant in the browser container (ADR 0015). It is not the operator's: the extension has no session, it presents a token that it was given by the policy of
/// its browser, so these routes are open to the sign-in and closed to anyone without the token. What a conversation said is never logged or sent back.
/// </summary>
[ApiController]
[Route("api/v1/extension")]
public sealed partial class ExtensionController : ControllerBase
{
    /// <summary>A whole conversation is at most <see cref="ExtensionService.MaxConversationLength"/> characters; the body of a push is bounded a little above what that can be once it is JSON.</summary>
    private const int MaxBodyBytes = 8 * 1024 * 1024;

    private const string BearerPrefix = "Bearer ";
    private readonly IExtensionToken _token;
    private readonly ExtensionService _service;
    private readonly ILogger<ExtensionController> _logger;

    public ExtensionController(IExtensionToken token, ExtensionService service, ILogger<ExtensionController> logger)
    {
        _token = token;
        _service = service;
        _logger = logger;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The extension was refused: it did not present the token.")]
    private partial void LogRefusedToken();

    [LoggerMessage(Level = LogLevel.Warning, Message = "The extension sent something that Geminarr does not take ({Kind}).")]
    private partial void LogRefusedContent(string kind);

    /// <summary>What the extension read: the state of the page, the list of conversations, or one conversation.</summary>
    [HttpPost("push")]
    [AllowAnonymous]
    [RequestSizeLimit(MaxBodyBytes)]
    [ProducesResponseType<ExtensionPushResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult Push([FromBody] ExtensionPushRequest? request)
    {
        if (!_token.Verify(PresentedToken()))
        {
            LogRefusedToken();
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "The token is not the one of Geminarr.");
        }

        var result = _service.Handle(request ?? new ExtensionPushRequest(null, null, null, null, null, null, null));
        if (result.Accepted)
        {
            return Ok(new ExtensionPushResponse(result.Open));
        }

        // The kind is one of three words of ours or nothing: what the content said is not repeated.
        LogRefusedContent(request?.Kind is "state" or "list" or "conversation" ? request.Kind : "unknown");
        return Problem(statusCode: StatusCodes.Status400BadRequest, title: result.Problem);
    }

    private string? PresentedToken()
    {
        var header = Request.Headers[HeaderNames.Authorization].ToString();
        return header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase) ? header[BearerPrefix.Length..].Trim() : null;
    }
}
