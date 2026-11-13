namespace Geminarr.Core.Link;

/// <summary>The Mnemarr this Geminarr sends to, as it was given on the page, and how the last talk with it went. The key is not here: it is a file.</summary>
public sealed record DestinationRecord(string Url, string? Machine, DateTimeOffset? LinkedAt, DateTimeOffset? LastHeartbeatAt, string? LastProblem);
