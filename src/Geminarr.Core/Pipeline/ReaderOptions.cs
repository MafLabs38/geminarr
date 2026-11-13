using Microsoft.Extensions.Options;

namespace Geminarr.Core.Pipeline;

/// <summary>Which reader reads the assistant.</summary>
public enum ReaderKind
{
    /// <summary>None yet: Geminarr is not connected to any assistant, and a pass only sends what is waiting and says so to Mnemarr.</summary>
    None,

    /// <summary>A demonstration reader with synthetic conversations that grow: for a sandbox and the tests, never for a real deployment.</summary>
    Demo,
}

/// <summary>How a pass reads and how often (SPEC §6, §10.2), from the <c>Reader</c> section.</summary>
public sealed class ReaderOptions
{
    public const string SectionName = "Reader";

    public ReaderKind Kind { get; set; } = ReaderKind.None;

    /// <summary>Minutes between two passes (a random 20 % either way is added).</summary>
    public int PollMinutes { get; set; } = 20;

    /// <summary>How many conversations of the list are looked at.</summary>
    public int MaxListed { get; set; } = 40;

    /// <summary>How many conversations are opened in a pass; the others wait for the next.</summary>
    public int MaxOpenPerPass { get; set; } = 5;

    /// <summary>A pass never lasts longer than this.</summary>
    public int PassTimeoutMinutes { get; set; } = 5;

    /// <summary>A pass that fails doubles the wait up to this.</summary>
    public int MaxBackoffMinutes { get; set; } = 360;

    /// <summary>Passes in a row that find nobody signed in before the state is <c>needs-login</c>.</summary>
    public int LoginFailures { get; set; } = 3;

    /// <summary>The demonstration reader only: seconds between two new turns of the active conversation.</summary>
    public int DemoGrowSeconds { get; set; } = 60;
}

public sealed class ReaderOptionsValidator : IValidateOptions<ReaderOptions>
{
    public ValidateOptionsResult Validate(string? name, ReaderOptions options)
    {
        var problem =
            Below("PollMinutes", options.PollMinutes, 1, 1440)
            ?? Below("MaxListed", options.MaxListed, 1, 500)
            ?? Below("MaxOpenPerPass", options.MaxOpenPerPass, 1, 100)
            ?? Below("PassTimeoutMinutes", options.PassTimeoutMinutes, 1, 120)
            ?? Below("MaxBackoffMinutes", options.MaxBackoffMinutes, 1, 10080)
            ?? Below("LoginFailures", options.LoginFailures, 1, 100)
            ?? Below("DemoGrowSeconds", options.DemoGrowSeconds, 1, 86400);
        return problem is null ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(problem);
    }

    private static string? Below(string setting, int value, int min, int max) =>
        value >= min && value <= max ? null : $"Reader:{setting} (GEMINARR__READER__{setting.ToUpperInvariant()}) must be between {min} and {max}.";
}

/// <summary>How long what is kept is kept, from the <c>Retention</c> section.</summary>
public sealed class RetentionOptions
{
    public const string SectionName = "Retention";

    /// <summary>How many days the runs are kept.</summary>
    public int RunsDays { get; set; } = 90;
}

public sealed class RetentionOptionsValidator : IValidateOptions<RetentionOptions>
{
    public ValidateOptionsResult Validate(string? name, RetentionOptions options) =>
        options.RunsDays is >= 1 and <= 3650 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail("Retention:RunsDays (GEMINARR__RETENTION__RUNSDAYS) must be between 1 and 3650.");
}
