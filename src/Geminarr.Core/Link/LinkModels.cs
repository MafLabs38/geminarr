namespace Geminarr.Core.Link;

/// <summary>The destination as it is resolved from the settings, the page and the key file: where to send, as which machine, with which key.</summary>
public sealed record ResolvedDestination(string Url, string Machine, string Key);

/// <summary>The codes that say what is wrong with the destination: the same as the health and the page (SPEC §10.3).</summary>
public static class DestinationProblems
{
    public const string NotLinked = "mnemarr-not-linked";
    public const string InsecureAddress = "mnemarr-insecure-address";
    public const string Refused = "mnemarr-refused";
    public const string Unreachable = "mnemarr-unreachable";
    public const string Rejected = "mnemarr-rejected";
}

/// <summary>The destination for the page: where, as which machine, whether it holds a key, whether the settings give the address, what is wrong and when it last answered.</summary>
public sealed record DestinationStatus(string? Url, string Machine, bool Linked, bool AddressFromSettings, bool KeyFromFile, string? Problem, DateTimeOffset? LastHeartbeatAt);

/// <summary>A pairing that waits for a person to approve it in Mnemarr. The secret to ask with never leaves the server.</summary>
public sealed record PairingStatus(string State, string? UserCode, string? VerificationUrl, DateTimeOffset? ExpiresAt);

public static class PairingStates
{
    public const string None = "none";
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Expired = "expired";
}
