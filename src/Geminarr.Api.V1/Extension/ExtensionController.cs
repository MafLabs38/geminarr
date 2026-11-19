using Geminarr.Core.Extension;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Geminarr.Api.V1.Extension;

/// <summary>What Geminarr answers to a push that it took: the conversations of the list that it wants read now, and whether it asks for a round of reading now or for the account to be signed out (ADR 0022).</summary>
public sealed record ExtensionPushResponse(IReadOnlyList<string> Open, bool Round, bool SignOut, IReadOnlyList<string> Anchored, bool More);

/// <summary>
/// The door of the extension that reads the assistant in the browser container (ADR 0022). It is not the operator's: the extension has no session, it presents a token that it was given by the policy of
/// its browser, so these routes are open to the sign-in and closed to anyone without the token. What a conversation said is never logged or sent back.
/// </summary>
[ApiController]
[Route(ExtensionRoutes.Base)]
public sealed partial class ExtensionController : ControllerBase
{
    /// <summary>A whole conversation is at most <see cref="ExtensionService.MaxConversationLength"/> characters; the body of a push is bounded a little above what that can be once it is JSON.</summary>
    private const int MaxBodyBytes = 8 * 1024 * 1024;

    private readonly ExtensionService _service;
    private readonly ILogger<ExtensionController> _logger;

    public ExtensionController(ExtensionService service, ILogger<ExtensionController> logger)
    {
        _service = service;
        _logger = logger;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The extension sent something that Geminarr does not take ({Kind}).")]
    private partial void LogRefusedContent(string kind);

    // The step is one of three words of ours (the service refuses any other): it says where the page stopped being what the extension knows, never what the page showed.
    [LoggerMessage(Level = LogLevel.Warning, Message = "The page of the assistant is not what the extension knows (step: {Step}).")]
    private partial void LogBroken(string step);

    // How a conversation was read, in one of three words of ours: the whole of it, its end only (it joined what Geminarr holds), or an end that did not join (the whole is asked for next). Never what it said.
    [LoggerMessage(Level = LogLevel.Information, Message = "A conversation was read ({How}).")]
    private partial void LogConversation(string how);

    private static string HowRead(ExtensionPushRequest request, ExtensionPushResult result)
    {
        if (result.More)
        {
            return "end-does-not-join";
        }

        return request.Complete == false ? "end" : "whole";
    }

    /// <summary>What the extension read: that nobody is signed in, the list of conversations, one conversation, or that the page is not what it knows.</summary>
    [HttpPost(ExtensionRoutes.Push)]
    [AllowAnonymous]
    [RequireExtensionToken]
    [RequestSizeLimit(MaxBodyBytes)]
    [ProducesResponseType<ExtensionPushResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Push([FromBody] ExtensionPushRequest? request, CancellationToken cancellationToken)
    {
        var result = await _service.HandleAsync(request ?? new ExtensionPushRequest(null, null, null, null, null, null, null), cancellationToken);
        if (result.Accepted)
        {
            if (request is { Kind: "broken", Step: { } step })
            {
                LogBroken(step);
            }

            if (request is { Kind: "conversation" })
            {
                LogConversation(HowRead(request, result));
            }

            return Ok(new ExtensionPushResponse(result.Open, result.Round, result.SignOut, result.Anchored ?? [], result.More));
        }

        // The kind is one of five words of ours or nothing: what the content said is not repeated.
        LogRefusedContent(request?.Kind is "state" or "list" or "conversation" or "broken" or "unfinished" or "hello" or "page" or "listen" ? request.Kind : "unknown");
        return Problem(statusCode: StatusCodes.Status400BadRequest, title: result.Problem);
    }
}
