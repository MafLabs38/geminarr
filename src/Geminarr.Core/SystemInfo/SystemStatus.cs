namespace Geminarr.Core.SystemInfo;

/// <summary>What the running Geminarr can say about itself without revealing anything about its host.</summary>
public sealed record SystemStatus(string Version, string RuntimeVersion, DateTimeOffset StartedAt, TimeSpan Uptime);
