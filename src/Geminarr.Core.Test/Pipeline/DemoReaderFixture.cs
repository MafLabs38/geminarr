using Geminarr.Core.Configuration;
using Geminarr.Core.Pipeline;
using Geminarr.Core.Test.Access;
using Microsoft.Extensions.Time.Testing;

namespace Geminarr.Core.Test.Pipeline;

public sealed class DemoReaderFixture : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("geminarr-demo-").FullName;
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 13, 9, 0, 0, TimeSpan.Zero));

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private DemoReader Subject(int growSeconds = 60) => new(_clock, Options.Of(new GeminarrOptions { DataFolder = _folder }), Options.Of(new ReaderOptions { DemoGrowSeconds = growSeconds }));

    [Fact]
    public async Task should_list_its_conversations_with_the_active_one_first_and_stable_ids()
    {
        var headers = await Subject().ListRecentAsync(10, TestContext.Current.CancellationToken);

        headers.Select(header => header.Rank).Should().Equal(0, 1, 2);
        headers.Select(header => header.Id).Should().OnlyHaveUniqueItems().And.OnlyContain(id => id.StartsWith("demo-c", StringComparison.Ordinal));
        headers.Should().OnlyContain(header => header.Title!.StartsWith("Demo:", StringComparison.Ordinal));
        (await Subject().ListRecentAsync(2, TestContext.Current.CancellationToken)).Should().HaveCount(2);
    }

    [Fact]
    public async Task should_only_ever_grow_a_conversation_and_never_change_what_it_already_said()
    {
        var subject = Subject();
        var id = (await subject.ListRecentAsync(1, TestContext.Current.CancellationToken))[0].Id;
        var before = await subject.OpenAsync(id, TestContext.Current.CancellationToken);

        for (var step = 0; step < 40; step++)
        {
            _clock.Advance(TimeSpan.FromSeconds(60));
            var after = await subject.OpenAsync(id, TestContext.Current.CancellationToken);

            after.Count.Should().BeGreaterThanOrEqualTo(before.Count);
            after.Take(before.Count).Select(turn => (turn.Role, turn.Text)).Should().Equal(before.Select(turn => (turn.Role, turn.Text)));
            before = after;
        }
    }

    [Fact]
    public async Task should_start_with_a_few_turns_and_keep_the_same_conversations_across_a_restart()
    {
        var first = Subject();
        var id = (await first.ListRecentAsync(1, TestContext.Current.CancellationToken))[0].Id;
        var started = await first.OpenAsync(id, TestContext.Current.CancellationToken);
        _clock.Advance(TimeSpan.FromMinutes(5));
        var later = await first.OpenAsync(id, TestContext.Current.CancellationToken);

        var restarted = Subject();
        var afterRestart = await restarted.OpenAsync(id, TestContext.Current.CancellationToken);

        started.Count.Should().BeLessThan(10);
        later.Count.Should().BeGreaterThan(started.Count);
        afterRestart.Select(turn => turn.Text).Should().Equal(later.Select(turn => turn.Text), "the start of the demonstration is remembered in the data folder");
        File.Exists(Path.Combine(_folder, DemoReader.StartedFile)).Should().BeTrue();
    }

    [Fact]
    public async Task should_start_again_when_the_remembered_start_is_not_a_date()
    {
        await File.WriteAllTextAsync(Path.Combine(_folder, DemoReader.StartedFile), "not a date", TestContext.Current.CancellationToken);

        var turns = await Subject().OpenAsync((await Subject().ListRecentAsync(1, TestContext.Current.CancellationToken))[0].Id, TestContext.Current.CancellationToken);

        turns.Count.Should().BeLessThan(10);
    }

    [Fact]
    public async Task should_give_alternating_roles_a_moment_and_some_text_to_every_turn()
    {
        var subject = Subject();
        var id = (await subject.ListRecentAsync(1, TestContext.Current.CancellationToken))[0].Id;

        var turns = await subject.OpenAsync(id, TestContext.Current.CancellationToken);

        turns.Select(turn => turn.Role).Take(4).Should().Equal("user", "assistant", "user", "assistant");
        turns.Should().OnlyContain(turn => turn.At != null && turn.Text.Length > 0);
    }

    [Fact]
    public async Task should_say_the_session_ended_when_the_file_is_there_and_back_when_it_is_gone()
    {
        var subject = Subject();
        (await subject.CheckSessionAsync(TestContext.Current.CancellationToken)).Should().Be(SessionCheck.SignedIn);

        File.WriteAllText(Path.Combine(_folder, DemoReader.SignedOutFile), string.Empty);
        (await subject.CheckSessionAsync(TestContext.Current.CancellationToken)).Should().Be(SessionCheck.SignedOut);

        File.Delete(Path.Combine(_folder, DemoReader.SignedOutFile));
        (await subject.CheckSessionAsync(TestContext.Current.CancellationToken)).Should().Be(SessionCheck.SignedIn);
    }

    [Fact]
    public async Task should_say_it_cannot_read_a_conversation_it_does_not_have()
    {
        var open = () => Subject().OpenAsync("not-a-demo", TestContext.Current.CancellationToken);

        (await open.Should().ThrowAsync<ReaderException>()).Which.Reason.Should().Be("reader-broken");
    }

    [Fact]
    public async Task should_stop_growing_at_a_limit()
    {
        var subject = Subject(growSeconds: 1);
        var id = (await subject.ListRecentAsync(1, TestContext.Current.CancellationToken))[0].Id;
        _clock.Advance(TimeSpan.FromDays(400));

        (await subject.OpenAsync(id, TestContext.Current.CancellationToken)).Count.Should().BeLessThanOrEqualTo(1000);
    }
}
