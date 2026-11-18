using Geminarr.Api.V1.Extension;
using Geminarr.Core.Extension;

namespace Geminarr.Integration.Test;

public sealed class ExtensionUpdateManifestFixture
{
    private static readonly ExtensionPackageInfo Package = new("abcdefghijklmnopabcdefghijklmnop", "1.2.3", "geminarr-reader-1.2.3.crx", "/x/geminarr-reader-1.2.3.crx");

    [Fact]
    public void should_name_the_extension_its_version_and_where_the_package_is()
    {
        var xml = ExtensionUpdateManifest.Render(Package, "https://geminarr.homelab.example/api/v1/extension/package/geminarr-reader-1.2.3.crx");

        xml.Should().Contain("appid='abcdefghijklmnopabcdefghijklmnop'").And.Contain("version='1.2.3'").And.Contain("codebase='https://geminarr.homelab.example/api/v1/extension/package/geminarr-reader-1.2.3.crx'");
        System.Xml.Linq.XDocument.Parse(xml).Should().NotBeNull("the browser reads it as XML");
    }

    [Fact]
    public void should_escape_an_address_that_would_break_out_of_the_attribute()
    {
        var xml = ExtensionUpdateManifest.Render(Package, "https://geminarr.homelab.example/a&b'c<d");

        xml.Should().NotContain("a&b'c<d").And.Contain("a&amp;b&apos;c&lt;d");
        System.Xml.Linq.XDocument.Parse(xml).Root!.Descendants().Single(element => element.Name.LocalName == "updatecheck").Attribute("codebase")!.Value.Should().Be("https://geminarr.homelab.example/a&b'c<d");
    }
}
