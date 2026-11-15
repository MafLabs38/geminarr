using Geminarr.Core.Browser;

namespace Geminarr.Core.Test.Browser;

public sealed class ListenerAuditFixture
{
    private const string Header = "  sl  local_address rem_address   st tx_queue rx_queue tr tm->when retrnsmt   uid  timeout inode";

    private static string Line(string local, string state = "0A") => $"   0: {local} 00000000:0000 {state} 00000000:00000000 00:00000000 00000000  1000        0 12345 1 0000000000000000 100 0 0 10 0";

    [Theory]
    [InlineData("0100007F:0BB8", false)]
    [InlineData("0200007F:2382", false)]
    [InlineData("00000000:0BB8", true)]
    [InlineData("0A00020F:0BB8", true)]
    [InlineData("00000000000000000000000001000000:0BB8", false)]
    [InlineData("0000000000000000FFFF00000100007F:0BB8", false)]
    [InlineData("00000000000000000000000000000000:0BB8", true)]
    [InlineData("0000000000000000FFFF00000A00020F:0BB8", true)]
    [InlineData("000080FE00000000FF1234567890ABCD:0BB8", true)]
    public void should_tell_a_listener_on_the_loopback_from_one_that_is_not(string local, bool off)
    {
        ListenerAudit.OffTheLoopback([Line(local)], new HashSet<int>()).Should().Equal(off ? [3000] : []);
    }

    [Fact]
    public void should_look_only_at_the_sockets_that_listen()
    {
        var lines = new[] { Header, Line("00000000:0BB8", state: "01"), Line("00000000:0BB9", state: "06"), Line("00000000:0BBA") };

        ListenerAudit.OffTheLoopback(lines, new HashSet<int>()).Should().Equal(3002);
    }

    [Fact]
    public void should_leave_out_the_ports_that_are_allowed_and_say_each_port_once_in_order()
    {
        var lines = new[] { Line("00000000:2092"), Line("00000000:0BB8"), Line("00000000000000000000000000000000:0BB8"), Line("00000000:0BB9") };

        ListenerAudit.OffTheLoopback(lines, new HashSet<int> { 8338 }).Should().Equal(3000, 3001);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a listing")]
    [InlineData("   0: nonsense 00000000:0000 0A")]
    [InlineData("   0: 00000000:ZZZZ 00000000:0000 0A")]
    public void should_ignore_what_is_not_a_listing(string line)
    {
        ListenerAudit.OffTheLoopback([line], new HashSet<int>()).Should().BeEmpty();
    }
}
