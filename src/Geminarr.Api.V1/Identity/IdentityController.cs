using Geminarr.Api.V1.Security;
using Geminarr.Core.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace Geminarr.Api.V1.Identity;

/// <summary>How this Geminarr presents itself (ADR 0013): the name, the logo and the version shown at the bottom of the menu. The operator who is signed in reads it; the deployment sets it.</summary>
[ApiController]
[Route("api/v1/identity")]
[Produces("application/json")]
public sealed class IdentityController : ControllerBase
{
    private readonly IdentityService _identity;

    public IdentityController(IdentityService identity)
    {
        _identity = identity;
    }

    /// <summary>The name, the colors, the logo and the version of this Geminarr.</summary>
    [HttpGet]
    [ProducesResponseType<IdentityResource>(StatusCodes.Status200OK)]
    public IdentityResource Get() => IdentityResource.From(_identity.Get());

    /// <summary>
    /// The logo, served like an icon: a policy that forbids scripts and styles, so that opening an SVG directly cannot run code in this origin. With the <c>v</c> of <c>logoUrl</c>
    /// (the hash of the file), the answer is cached for a year.
    /// </summary>
    /// <param name="v">The hash from <c>logoUrl</c>.</param>
    [HttpGet("logo")]
    [KeepsItsOwnHeaders]
    [Produces("image/png", "image/webp", "image/svg+xml")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetLogo([FromQuery] string? v)
    {
        if (_identity.Get().Logo is not { } logo)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = v == logo.Hash ? "private, max-age=31536000, immutable" : "private, max-age=300";
        Response.Headers.ContentSecurityPolicy = "default-src 'none'; sandbox";
        Response.Headers.XContentTypeOptions = "nosniff";
        // File() answers 304 to a browser that has the image (If-None-Match, If-Modified-Since): it is not sent again to be told it has not changed.
        return File(logo.Content, logo.ContentType, logo.Modified, new EntityTagHeaderValue($"\"{logo.Hash}\""));
    }
}
