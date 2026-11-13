using Geminarr.Core.Browser;
using Geminarr.Core.Test.Access;
using Microsoft.Extensions.Time.Testing;

namespace Geminarr.Core.Test.Browser;

public sealed class LiveViewServiceFixture
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 13, 9, 0, 0, TimeSpan.Zero));

    private LiveViewService Subject(int minutes = 10) => new(Options.Of(new BrowserOptions { LiveViewMinutes = minutes }), _clock);

    [Fact]
    public void should_give_a_ticket_with_a_token_that_cannot_be_guessed_and_the_time_it_ends()
    {
        var subject = Subject();

        var ticket = subject.Start();

        ticket.Id.Should().HaveLength(32);
        ticket.Token.Length.Should().BeGreaterThan(40);
        ticket.ExpiresAt.Should().Be(_clock.GetUtcNow().AddMinutes(10));
        subject.Active.Should().BeTrue();
        Subject().Start().Token.Should().NotBe(ticket.Token);
    }

    [Fact]
    public void should_open_one_at_a_time()
    {
        var subject = Subject();
        var ticket = subject.Start();

        var second = () => subject.Start();
        second.Should().Throw<LiveViewBusyException>();

        using var lease = subject.TryAttach(ticket.Id, ticket.Token)!;
        second.Should().Throw<LiveViewBusyException>("it is open now");
    }

    [Fact]
    public void should_attach_a_viewer_with_the_token_once_and_only_once()
    {
        var subject = Subject();
        var ticket = subject.Start();

        using var lease = subject.TryAttach(ticket.Id, ticket.Token);

        lease.Should().NotBeNull();
        subject.TryAttach(ticket.Id, ticket.Token).Should().BeNull("a token is single-use");
    }

    [Fact]
    public void should_refuse_a_wrong_token_or_a_wrong_id_and_use_the_ticket_up_for_a_wrong_token()
    {
        var subject = Subject();
        var ticket = subject.Start();

        subject.TryAttach("another-id", ticket.Token).Should().BeNull();
        subject.TryAttach(ticket.Id, "not-the-token").Should().BeNull();
        subject.TryAttach(ticket.Id, ticket.Token).Should().BeNull("a guess uses the ticket up: it cannot be tried again");
        subject.Active.Should().BeFalse();
    }

    [Fact]
    public void should_forget_a_ticket_that_nobody_used_within_a_minute()
    {
        var subject = Subject();
        var ticket = subject.Start();

        _clock.Advance(LiveViewService.TokenLife + TimeSpan.FromSeconds(1));

        subject.Active.Should().BeFalse();
        subject.TryAttach(ticket.Id, ticket.Token).Should().BeNull();
        subject.Start().Should().NotBeNull();
    }

    [Fact]
    public void should_end_the_live_view_when_its_time_is_up()
    {
        var subject = Subject(minutes: 2);
        var ticket = subject.Start();
        using var lease = subject.TryAttach(ticket.Id, ticket.Token)!;
        lease.Token.IsCancellationRequested.Should().BeFalse();

        _clock.Advance(TimeSpan.FromMinutes(1));
        lease.Token.IsCancellationRequested.Should().BeFalse();
        _clock.Advance(TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(1));

        lease.Token.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public void should_close_what_is_open_or_waits_and_say_whether_there_was_one()
    {
        var subject = Subject();
        subject.Cancel().Should().BeFalse();

        var ticket = subject.Start();
        subject.Cancel().Should().BeTrue();
        subject.Active.Should().BeFalse();
        subject.TryAttach(ticket.Id, ticket.Token).Should().BeNull();

        var second = subject.Start();
        using var lease = subject.TryAttach(second.Id, second.Token)!;
        subject.Cancel().Should().BeTrue();
        lease.Token.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public void should_let_a_new_live_view_be_asked_for_once_the_viewer_is_gone()
    {
        var subject = Subject();
        var ticket = subject.Start();
        using var lease = subject.TryAttach(ticket.Id, ticket.Token)!;
        subject.Active.Should().BeTrue();

        subject.Release(lease);

        subject.Active.Should().BeFalse();
        subject.Start().Should().NotBeNull();
    }

    [Fact]
    public void should_not_release_a_live_view_that_is_not_the_one_open()
    {
        var subject = Subject();
        var first = subject.Start();
        using var firstLease = subject.TryAttach(first.Id, first.Token)!;
        subject.Release(firstLease);
        var second = subject.Start();
        using var secondLease = subject.TryAttach(second.Id, second.Token)!;

        subject.Release(firstLease);

        subject.Active.Should().BeTrue("the old viewer does not close the new one");
    }
}
