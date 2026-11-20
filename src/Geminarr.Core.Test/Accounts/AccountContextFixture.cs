using Geminarr.Core.Accounts;
using Geminarr.Core.Extension;
using Geminarr.Core.Pipeline;

namespace Geminarr.Core.Test.Accounts;

public sealed class AccountContextFixture
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 9, 0, 0, TimeSpan.Zero);

    private readonly ExtensionInbox _inbox = new();

    private AccountContext Subject() => new(_inbox);

    private void SignedIn(string address) =>
        _inbox.RecordList([new ConversationHeader("c1", "T", 0)], "1.0.0", Now, [$"Google Account: Jane Doe ({address})"]);

    [Fact]
    public void should_have_no_account_when_nobody_is_signed_in()
    {
        Subject().Current.Should().BeNull();
    }

    [Fact]
    public void should_have_the_account_that_is_signed_in()
    {
        SignedIn("jane.doe@example.org");

        Subject().Current.Should().Be(AccountKey.Of("jane.doe@example.org"));
    }

    [Fact]
    public void should_have_no_account_again_once_it_is_signed_out()
    {
        SignedIn("jane.doe@example.org");
        _inbox.RecordSession(false, "1.0.0", Now.AddMinutes(1));

        Subject().Current.Should().BeNull();
    }

    [Fact]
    public async Task should_keep_the_account_that_a_piece_of_work_took_whatever_happens_to_the_session_afterwards()
    {
        SignedIn("jane.doe@example.org");
        var subject = Subject();
        var first = AccountKey.Of("jane.doe@example.org");

        using (subject.Use(first))
        {
            // The operator signs another account in while the work goes on: the work is still for the first one, across awaits and in what it starts.
            SignedIn("john.roe@example.org");
            await Task.Yield();
            var seenAfterAwait = subject.Current;
            var seenInATask = await Task.Run(() => subject.Current);

            seenAfterAwait.Should().Be(first);
            seenInATask.Should().Be(first);
        }

        subject.Current.Should().Be(AccountKey.Of("john.roe@example.org"), "once the work is done, the account that is signed in is the one again");
    }

    [Fact]
    public void should_work_on_no_account_when_asked_to_and_come_back_afterwards()
    {
        SignedIn("jane.doe@example.org");
        var subject = Subject();

        using (subject.Use(null))
        {
            subject.Current.Should().BeNull();
        }

        subject.Current.Should().Be(AccountKey.Of("jane.doe@example.org"));
    }

    [Fact]
    public async Task should_not_let_two_pieces_of_work_in_turn_see_each_others_account()
    {
        var subject = Subject();
        var a = AccountKey.Of("a@example.org");
        var b = AccountKey.Of("b@example.org");

        var seen = await Task.WhenAll(Work(subject, a), Work(subject, b));

        seen.Should().Equal(a, b);
    }

    private static async Task<string?> Work(AccountContext subject, string key)
    {
        using (subject.Use(key))
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
            return subject.Current;
        }
    }
}
