using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Geminarr.Core.Health;

/// <summary>
/// One way for every readiness check to say what is wrong: a stable reason code in the data of the result, next to the words. The health
/// resource of the API (F-HLT-01) maps codes; it does not parse English.
/// </summary>
public static class HealthReasons
{
    public const string Key = "reason";

    /// <summary>The data folder does not exist (a volume that was not mounted).</summary>
    public const string DataFolderMissing = "data-folder-missing";

    /// <summary>The data folder exists and cannot be written.</summary>
    public const string DataFolderReadOnly = "data-folder-read-only";

    /// <summary>No operator is configured: nobody can sign in.</summary>
    public const string OperatorNotConfigured = "operator-not-configured";

    /// <summary>The password file of the operator cannot be read, or is empty.</summary>
    public const string OperatorPasswordUnreadable = "operator-password-unreadable";

    /// <summary>The password of the operator is too short: nobody can sign in with it.</summary>
    public const string OperatorPasswordWeak = "operator-password-weak";

    /// <summary>The database cannot be opened or read.</summary>
    public const string DatabaseUnreadable = "database-unreadable";

    /// <summary>The database was written by a newer version than this one knows: it is not opened.</summary>
    public const string DatabaseStepUnknown = "database-step-unknown";

    /// <summary>Something waits for Mnemarr since more than an hour.</summary>
    public const string OutboxGrowing = "outbox-growing";

    /// <summary>Something is wrong but the container does what it must (and is safe): a stable reason code beside the words.</summary>
    public static HealthCheckResult Degraded(string reason, string description) => HealthCheckResult.Degraded(description, data: Data(reason));

    public static HealthCheckResult Unhealthy(string reason, string description, Exception? exception = null) =>
        HealthCheckResult.Unhealthy(description, exception, Data(reason));

    private static Dictionary<string, object> Data(string reason) => new() { [Key] = reason };
}
