using Geminarr.Core.Configuration;
using Geminarr.Core.Health;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Geminarr.Core.Test.Health;

public sealed class DataFolderHealthCheckFixture : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("geminarr-health-").FullName;
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 1, 8, 0, 0, TimeSpan.Zero));

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private DataFolderHealthCheck Subject(string folder, Func<string, CancellationToken, Task>? write = null) =>
        new(Options.Create(new GeminarrOptions { DataFolder = folder }), _clock, write ?? ((path, token) => File.WriteAllBytesAsync(path, [], token)));

    private static Task<HealthCheckResult> Check(DataFolderHealthCheck subject, CancellationToken cancellationToken = default) =>
        subject.CheckHealthAsync(new HealthCheckContext(), cancellationToken);

    [Fact]
    public async Task should_be_healthy_and_leave_no_probe_file_when_the_folder_is_writable()
    {
        var result = await Check(Subject(_folder), TestContext.Current.CancellationToken);

        result.Status.Should().Be(HealthStatus.Healthy);
        Directory.GetFileSystemEntries(_folder).Should().BeEmpty();
    }

    [Fact]
    public async Task should_be_unhealthy_without_revealing_the_path_when_the_folder_does_not_exist()
    {
        var missing = Path.Combine(_folder, "missing");

        var result = await Check(Subject(missing), TestContext.Current.CancellationToken);

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Data[HealthReasons.Key].Should().Be(HealthReasons.DataFolderMissing);
        result.Description.Should().Be("The data folder does not exist.").And.NotContain(missing);
    }

    [Fact]
    public async Task should_say_the_folder_is_missing_and_not_throw_when_the_path_is_not_a_valid_one()
    {
        var result = await Check(Subject("not\0a\0path"), TestContext.Current.CancellationToken);

        result.Data[HealthReasons.Key].Should().Be(HealthReasons.DataFolderMissing);
    }

    [Theory]
    [InlineData(typeof(UnauthorizedAccessException))]
    [InlineData(typeof(IOException))]
    public async Task should_be_unhealthy_and_keep_the_cause_when_the_probe_file_cannot_be_written(Type failure)
    {
        var cause = (Exception)Activator.CreateInstance(failure)!;

        var result = await Check(Subject(_folder, (_, _) => Task.FromException(cause)), TestContext.Current.CancellationToken);

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Data[HealthReasons.Key].Should().Be(HealthReasons.DataFolderReadOnly);
        result.Description.Should().Be("The data folder is not writable.").And.NotContain(_folder);
        result.Exception.Should().BeSameAs(cause);
    }

    [Fact]
    public async Task should_not_report_the_folder_as_unusable_when_the_probe_file_cannot_be_removed_afterwards()
    {
        // The probe file is a directory that cannot be deleted as a file: the write "succeeds", the removal fails, and that is no reason to alarm.
        var result = await Check(Subject(_folder, (path, _) => { Directory.CreateDirectory(path); File.WriteAllText(Path.Combine(path, "keep"), "x"); return Task.CompletedTask; }), TestContext.Current.CancellationToken);

        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task should_reuse_the_result_until_it_is_too_old_whether_it_was_healthy_or_not(bool startsHealthy)
    {
        var folder = Path.Combine(_folder, "volume");
        if (startsHealthy)
        {
            Directory.CreateDirectory(folder);
        }

        var subject = Subject(folder);
        var first = (await Check(subject, TestContext.Current.CancellationToken)).Status;
        first.Should().Be(startsHealthy ? HealthStatus.Healthy : HealthStatus.Unhealthy);

        // The volume changes under it: the answer does not, until the result expires.
        if (startsHealthy)
        {
            Directory.Delete(folder);
        }
        else
        {
            Directory.CreateDirectory(folder);
        }

        _clock.Advance(DataFolderHealthCheck.ReuseFor - TimeSpan.FromSeconds(1));
        (await Check(subject, TestContext.Current.CancellationToken)).Status.Should().Be(first);

        _clock.Advance(TimeSpan.FromSeconds(1));
        (await Check(subject, TestContext.Current.CancellationToken)).Status.Should().Be(startsHealthy ? HealthStatus.Unhealthy : HealthStatus.Healthy);
    }

    [Fact]
    public async Task should_probe_once_when_many_requests_arrive_together_after_the_result_expired()
    {
        var probes = 0;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var subject = Subject(_folder, async (_, _) =>
        {
            Interlocked.Increment(ref probes);
            await release.Task;
        });

        var started = 0;
        var checks = Enumerable.Range(0, 25).Select(_ => Task.Run(() =>
        {
            Interlocked.Increment(ref started);
            return Check(subject, TestContext.Current.CancellationToken);
        }, TestContext.Current.CancellationToken)).ToArray();
        // The first probe is held until every caller has asked: without the lock, each of them would write its own probe file meanwhile.
        SpinWait.SpinUntil(() => Volatile.Read(ref started) == checks.Length, TimeSpan.FromSeconds(10)).Should().BeTrue();
        release.SetResult();
        var results = await Task.WhenAll(checks);

        results.Should().OnlyContain(result => result.Status == HealthStatus.Healthy);
        probes.Should().Be(1);
    }

    [Fact]
    public async Task should_stop_waiting_for_its_turn_when_the_caller_gives_up()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var subject = Subject(_folder, (_, _) => release.Task);
        var first = Check(subject, TestContext.Current.CancellationToken);
        using var giveUp = new CancellationTokenSource();
        var second = Check(subject, giveUp.Token);

        await giveUp.CancelAsync();
        var waiting = async () => await second;

        await waiting.Should().ThrowAsync<OperationCanceledException>();
        release.SetResult();
        (await first).Status.Should().Be(HealthStatus.Healthy);
    }
}
