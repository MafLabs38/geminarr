using Geminarr.Core.Accounts;
using Geminarr.Core.Connection;
using Geminarr.Core.Pipeline;
using Geminarr.Core.Test.Support;
using Microsoft.Extensions.Time.Testing;

namespace Geminarr.Core.Test.Accounts;

public sealed class AccountContextFixture : IDisposable
{
    private readonly TestDatabase _data = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 20, 9, 0, 0, TimeSpan.Zero));
    private readonly AddressedReader _reader = new();
    private readonly ConnectionStore _store;

    public AccountContextFixture()
    {
        _store = new ConnectionStore(_data.Database, _clock);
    }

    public void Dispose() => _data.Dispose();

    private AccountContext Subject() => new(new ReaderProvider(_reader), _store);

    private sealed class AddressedReader : IConversationReader
    {
        public string? Address { get; set; }

        public string Source => ConversationSources.Web;

        public string? AccountAddress => Address;

        public bool SignedInWithoutAccount { get; set; }

        public Task<SessionCheck> CheckSessionAsync(CancellationToken cancellationToken) => Task.FromResult(SessionCheck.SignedIn);

        public Task<IReadOnlyList<ConversationHeader>> ListRecentAsync(int limit, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ConversationHeader>>([]);

        public Task<ConversationContent> OpenAsync(string id, CancellationToken cancellationToken) => Task.FromResult(new ConversationContent([]));
    }

    [Fact]
    public void should_have_no_account_when_nobody_is_signed_in_and_none_was()
    {
        Subject().Current.Should().BeNull();
    }

    [Fact]
    public void should_have_the_account_that_is_signed_in()
    {
        _reader.Address = "jane.doe@example.org";

        var subject = Subject();

        subject.Current.Should().Be(AccountKey.Of("jane.doe@example.org"));
        subject.SignedIn.Should().Be(subject.Current);
    }

    [Fact]
    public void should_keep_the_account_that_was_signed_in_last_when_the_session_ends_and_after_a_restart()
    {
        _reader.Address = "jane.doe@example.org";
        var key = AccountKey.Of("jane.doe@example.org");
        Subject().Remember(key);
        _reader.Address = null;

        Subject().Current.Should().Be(key, "an expired session is still the account whose machine tells Mnemarr");
        Subject().SignedIn.Should().BeNull();
    }

    [Fact]
    public void should_forget_it_when_the_operator_disconnects_and_not_come_back_with_a_restart()
    {
        var subject = Subject();
        subject.Remember(AccountKey.Of("jane.doe@example.org"));

        subject.Forget();

        subject.Current.Should().BeNull();
        Subject().Current.Should().BeNull();
    }

    [Fact]
    public void should_prefer_the_account_that_is_signed_in_to_the_one_that_was_remembered()
    {
        var subject = Subject();
        subject.Remember(AccountKey.Of("jane.doe@example.org"));
        _reader.Address = "john.roe@example.org";

        subject.Current.Should().Be(AccountKey.Of("john.roe@example.org"));
    }

    [Fact]
    public void should_not_write_again_what_it_already_keeps()
    {
        var subject = Subject();
        var key = AccountKey.Of("jane.doe@example.org");
        subject.Remember(key);
        _store.SetAccount("0000000000000000");

        subject.Remember(key);

        _store.Account().Should().Be("0000000000000000", "it knew it already: the second call wrote nothing");
    }

    [Fact]
    public async Task should_keep_the_account_that_a_piece_of_work_took_whatever_happens_to_the_session_afterwards()
    {
        _reader.Address = "jane.doe@example.org";
        var subject = Subject();
        var first = AccountKey.Of("jane.doe@example.org");

        using (subject.Use(first))
        {
            // The operator signs another account in while the work goes on: the work is still for the first one, across awaits and in what it starts.
            _reader.Address = "john.roe@example.org";
            await Task.Yield();
            var seenAfterAwait = subject.Current;
            var seenInATask = await Task.Run(() => subject.Current, TestContext.Current.CancellationToken);

            seenAfterAwait.Should().Be(first);
            seenInATask.Should().Be(first);
        }

        subject.Current.Should().Be(AccountKey.Of("john.roe@example.org"), "once the work is done, the account that is signed in is the one again");
    }

    [Fact]
    public void should_work_on_no_account_when_asked_to_and_come_back_afterwards()
    {
        _reader.Address = "jane.doe@example.org";
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
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // Both have taken their account before either goes on.
        var both = Task.WhenAll(Work(subject, a, gate.Task), Work(subject, b, gate.Task));
        gate.SetResult();
        var seen = await both;

        seen.Should().Equal(a, b);
    }

    private static async Task<string?> Work(AccountContext subject, string key, Task gate)
    {
        using (subject.Use(key))
        {
            await gate;
            return subject.Current;
        }
    }

    [Fact]
    public void should_not_fall_back_to_the_last_account_for_a_session_whose_account_cannot_be_named()
    {
        var subject = Subject();
        subject.Remember(AccountKey.Of("jane.doe@example.org"));
        _reader.SignedInWithoutAccount = true;

        subject.Current.Should().BeNull("a session that is signed in to somebody else is not the account that was signed in last");
        subject.SignedIn.Should().BeNull();
    }

    [Fact]
    public void should_say_that_an_account_is_in_use_until_the_work_that_took_it_is_done()
    {
        var subject = Subject();
        var key = AccountKey.Of("jane.doe@example.org");
        subject.InUse(key).Should().BeFalse();

        var first = subject.Use(key);
        var second = subject.Use(key);
        subject.InUse(key).Should().BeTrue();

        first.Dispose();
        first.Dispose();
        subject.InUse(key).Should().BeTrue("the second piece of work still holds it, and a second dispose of the first frees nothing more");
        second.Dispose();
        subject.InUse(key).Should().BeFalse();
        subject.InUse(AccountKey.Of("john.roe@example.org")).Should().BeFalse();
    }

    [Fact]
    public void should_write_the_account_again_after_it_was_forgotten()
    {
        var subject = Subject();
        var key = AccountKey.Of("jane.doe@example.org");
        subject.Remember(key);
        subject.Forget();

        subject.Remember(key);

        _store.Account().Should().Be(key);
    }
}
