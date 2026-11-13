using System.Text;
using Geminarr.Core.Identity;

namespace Geminarr.Core.Test.Identity;

public sealed class IdentityRulesFixture
{
    internal static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];

    private const string Svg = "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 10 10\"><circle cx=\"5\" cy=\"5\" r=\"4\" fill=\"#fff\"/></svg>";

    private static (LogoFormat? Format, string? Error) Inspect(string content, string extension) => IdentityRules.InspectLogo(Encoding.UTF8.GetBytes(content), extension);

    [Theory]
    [InlineData("Lab")]
    [InlineData("Example Lab")]
    public void should_accept_a_name_of_one_line(string name)
    {
        IdentityRules.ValidateName(name).Should().BeNull();
    }

    [Fact]
    public void should_refuse_a_name_that_is_too_long()
    {
        IdentityRules.ValidateName(new string('a', IdentityRules.MaxNameLength + 1)).Should().Contain("at most");
    }

    [Theory]
    [InlineData("two\nlines")]
    [InlineData("zero​width")]
    [InlineData("right‏to-left")]
    public void should_refuse_a_name_that_is_not_one_line_of_visible_text(string name)
    {
        IdentityRules.ValidateName(name).Should().Contain("one line");
    }

    [Theory]
    [InlineData("#fff", true)]
    [InlineData("#3ee0c5", true)]
    [InlineData("accent", true)]
    [InlineData("inkMuted", true)]
    [InlineData("red", false)]
    [InlineData("#ffff", false)]
    [InlineData("red; background: url(x)", false)]
    [InlineData("var(--ink)", false)]
    public void should_take_a_hex_color_or_a_token_and_nothing_else(string value, bool expected)
    {
        IdentityRules.IsColor(value).Should().Be(expected);
        (IdentityRules.ValidateColor("name", value) is null).Should().Be(expected);
    }

    [Theory]
    [InlineData("png")]
    [InlineData(".PNG")]
    public void should_know_a_png_by_its_signature(string extension)
    {
        IdentityRules.InspectLogo(Png, extension).Format!.ContentType.Should().Be("image/png");
    }

    [Fact]
    public void should_refuse_a_file_that_is_not_what_its_extension_says()
    {
        IdentityRules.InspectLogo("not a png"u8, "png").Error.Should().Contain("not a valid png");
        IdentityRules.InspectLogo(Png, "gif").Error.Should().Contain("must be a");
        IdentityRules.InspectLogo(ReadOnlySpan<byte>.Empty, "png").Error.Should().Contain("between 1 byte");
        IdentityRules.InspectLogo(new byte[IdentityRules.MaxLogoBytes + 1], "png").Error.Should().Contain("between 1 byte");
    }

    [Fact]
    public void should_know_a_webp_by_its_header()
    {
        var webp = Encoding.ASCII.GetBytes("RIFF\0\0\0\0WEBPVP8 ");

        IdentityRules.InspectLogo(webp, "webp").Format!.ContentType.Should().Be("image/webp");
        IdentityRules.InspectLogo("RIFF....WAVEfmt  "u8, "webp").Error.Should().NotBeNull();
    }

    [Theory]
    [InlineData("RIFF\0\0\0\0WEBP", false)]
    [InlineData("RIFF\0\0\0\0WEBPV", true)]
    [InlineData("RIFF\0\0\0\0WAVEV", false)]
    public void should_need_more_than_the_header_of_a_webp(string content, bool valid)
    {
        (IdentityRules.InspectLogo(Encoding.ASCII.GetBytes(content), "webp").Format is not null).Should().Be(valid);
    }

    [Fact]
    public void should_accept_a_plain_svg()
    {
        Inspect(Svg, "svg").Format!.ContentType.Should().Be("image/svg+xml");
    }

    [Theory]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><foreignObject/></svg>")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\" onload=\"alert(1)\"/>")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><a href=\"javascript:alert(1)\"/></svg>")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><image href=\"https://elsewhere.example/x.png\"/></svg>")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><rect fill=\"url(https://elsewhere.example/x)\"/></svg>")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><rect fill=\"url( 'https://elsewhere.example/x' )\"/></svg>")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\" ONLOAD=\"alert(1)\"/>")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><set attributeName=\"href\" to=\"javascript:alert(1)\"/></svg>")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><image href=\"data:text/html,x\"/></svg>")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><style><![CDATA[@import url(https://elsewhere.example/x.css);]]></style></svg>")]
    [InlineData("<!-- nothing here -->")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><style>@import url(https://elsewhere.example/x.css);</style></svg>")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><style>a{background:url(https://elsewhere.example/x.png)}</style></svg>")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><rect style=\"fill:url(https://elsewhere.example/x)\"/></svg>")]
    [InlineData("<!DOCTYPE svg [<!ENTITY x \"y\">]><svg xmlns=\"http://www.w3.org/2000/svg\"/>")]
    [InlineData("<html xmlns=\"http://www.w3.org/1999/xhtml\"/>")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\">")]
    public void should_refuse_an_svg_that_can_run_code_or_load_anything(string content)
    {
        Inspect(content, "svg").Error.Should().Contain("not a valid svg");
    }

    [Fact]
    public void should_allow_a_style_sheet_and_a_link_to_a_fragment_of_the_same_document()
    {
        const string svg = "<svg xmlns=\"http://www.w3.org/2000/svg\"><style>.a{fill:url(#g)}</style><defs><linearGradient id=\"g\"/></defs><use href=\"#g\"/></svg>";

        Inspect(svg, "svg").Format.Should().NotBeNull();
    }
}
