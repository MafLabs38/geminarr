using Geminarr.Core.Browser;

namespace Geminarr.Core.Test.Browser;

public sealed class LiveInputParserFixture
{
    private static LiveInput? Parse(string message) => LiveInputParser.Parse(message, 1280, 800);

    [Theory]
    [InlineData("""{"t":"move","x":10,"y":20}""", LiveInputKind.MouseMove, 10.0, 20.0)]
    [InlineData("""{"t":"down","x":0,"y":0,"button":"left"}""", LiveInputKind.MouseDown, 0.0, 0.0)]
    [InlineData("""{"t":"up","x":1280,"y":800,"button":"right"}""", LiveInputKind.MouseUp, 1280.0, 800.0)]
    public void should_read_a_mouse_event_inside_the_screen(string message, LiveInputKind kind, double x, double y)
    {
        var input = Parse(message)!;

        (input.Kind, input.X, input.Y).Should().Be((kind, x, y));
    }

    [Fact]
    public void should_default_the_button_and_keep_the_one_given()
    {
        Parse("""{"t":"down","x":1,"y":1}""")!.Button.Should().Be("left");
        Parse("""{"t":"down","x":1,"y":1,"button":"middle"}""")!.Button.Should().Be("middle");
    }

    [Fact]
    public void should_read_a_wheel_event_and_bound_the_distance()
    {
        var input = Parse("""{"t":"wheel","x":5,"y":6,"dx":-50.5,"dy":999999}""")!;

        (input.Kind, input.DeltaX, input.DeltaY).Should().Be((LiveInputKind.Wheel, -50.5, 2_000));
    }

    [Theory]
    [InlineData("""{"t":"keydown","key":"a"}""", LiveInputKind.KeyDown, "a")]
    [InlineData("""{"t":"keyup","key":"Enter"}""", LiveInputKind.KeyUp, "Enter")]
    [InlineData("""{"t":"keydown","key":" "}""", LiveInputKind.KeyDown, " ")]
    public void should_read_a_key(string message, LiveInputKind kind, string key)
    {
        var input = Parse(message)!;

        (input.Kind, input.Key).Should().Be((kind, key));
    }

    [Fact]
    public void should_read_a_pasted_text()
    {
        var input = Parse("""{"t":"text","text":"a password with spaces"}""")!;

        (input.Kind, input.Text).Should().Be((LiveInputKind.Text, "a password with spaces"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("\"move\"")]
    [InlineData("{}")]
    [InlineData("""{"t":"explode","x":1,"y":1}""")]
    [InlineData("""{"t":"move","x":-1,"y":1}""")]
    [InlineData("""{"t":"move","x":1281,"y":1}""")]
    [InlineData("""{"t":"move","x":1,"y":801}""")]
    [InlineData("""{"t":"move","x":"1","y":1}""")]
    [InlineData("""{"t":"move","x":1}""")]
    [InlineData("""{"t":"down","x":1,"y":1,"button":"fifth"}""")]
    [InlineData("""{"t":"down","x":1,"y":1,"button":5}""")]
    [InlineData("""{"t":"up","x":1,"y":1,"button":null}""")]
    [InlineData("""{"t":"wheel","x":1,"y":1,"dx":1}""")]
    [InlineData("""{"t":"keydown"}""")]
    [InlineData("""{"t":"keydown","key":""}""")]
    [InlineData("""{"t":"keydown","key":"a\u0007"}""")]
    [InlineData("""{"t":"text","text":""}""")]
    [InlineData("""{"t":"text","text":5}""")]
    public void should_refuse_what_is_not_exactly_an_input(string message)
    {
        Parse(message).Should().BeNull();
    }

    [Fact]
    public void should_refuse_a_message_or_a_key_or_a_text_that_is_too_long()
    {
        Parse("""{"t":"keydown","key":"%s"}""".Replace("%s", new string('k', 33))).Should().BeNull();
        Parse("""{"t":"text","text":"%s"}""".Replace("%s", new string('t', LiveInputParser.MaxTextChars + 1))).Should().BeNull();
        Parse(new string(' ', LiveInputParser.MaxMessageChars + 1)).Should().BeNull();
        Parse("""{"t":"text","text":"%s"}""".Replace("%s", new string('t', LiveInputParser.MaxTextChars))).Should().NotBeNull();
    }
}
