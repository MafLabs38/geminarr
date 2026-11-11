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

    public static HealthCheckResult Unhealthy(string reason, string description, Exception? exception = null) =>
        HealthCheckResult.Unhealthy(description, exception, Data(reason));

    private static Dictionary<string, object> Data(string reason) => new() { [Key] = reason };
}
