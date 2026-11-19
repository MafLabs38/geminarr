using System.Security;
using Geminarr.Core.Extension;

namespace Geminarr.Api.V1.Extension;

/// <summary>The update manifest of the browser's own format (the "gupdate" document): which version of which extension is where.</summary>
public static class ExtensionUpdateManifest
{
    public static string Render(ExtensionPackageInfo package, string codebase) =>
        $"""
        <?xml version='1.0' encoding='UTF-8'?>
        <gupdate xmlns='http://www.google.com/update2/response' protocol='2.0'>
          <app appid='{package.Id}'>
            <updatecheck codebase='{SecurityElement.Escape(codebase)}' version='{package.Version}' />
          </app>
        </gupdate>
        """;
}
