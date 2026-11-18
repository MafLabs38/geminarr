using Geminarr.Core.Extension;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Geminarr.Api.V1.Extension;

/// <summary>Where the browser installs and updates the extension from (ADR 0015): the update manifest and the signed package. Public, since the browser asks without any session and the package holds no secret.</summary>
[ApiController]
[Route("api/v1/extension")]
[AllowAnonymous]
public sealed class ExtensionPackageController : ControllerBase
{
    private const string UpdateContentType = "application/xml";
    private const string PackageContentType = "application/x-chrome-extension";

    private readonly IExtensionPackage _package;
    private readonly IOptions<ExtensionOptions> _options;

    public ExtensionPackageController(IExtensionPackage package, IOptions<ExtensionOptions> options)
    {
        _package = package;
        _options = options;
    }

    /// <summary>The update manifest that the browser reads to install and to update the extension. Public: the browser asks without any session, and the package holds no secret.</summary>
    [HttpGet("update.xml")]
    [Produces(UpdateContentType)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult UpdateManifest()
    {
        var package = _package.Current();
        if (package is null)
        {
            return NotFound();
        }

        var codebase = $"{BaseUrl()}/api/v1/extension/package/{package.FileName}";
        var xml = ExtensionUpdateManifest.Render(package, codebase);
        return Content(xml, UpdateContentType);
    }

    /// <summary>The signed package. Only the file that the description names is served: a name of the request is compared to it, never used as a path.</summary>
    [HttpGet("package/{fileName}")]
    [Produces(PackageContentType)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult Package(string fileName)
    {
        var package = _package.Current();
        return package is null || !string.Equals(fileName, package.FileName, StringComparison.Ordinal)
            ? NotFound()
            : PhysicalFile(package.FilePath, PackageContentType);
    }

    private string BaseUrl() =>
        string.IsNullOrWhiteSpace(_options.Value.PublicUrl) ? $"{Request.Scheme}://{Request.Host.Value}" : _options.Value.PublicUrl.TrimEnd('/');
}
