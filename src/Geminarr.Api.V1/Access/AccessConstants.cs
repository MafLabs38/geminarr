namespace Geminarr.Api.V1.Access;

/// <summary>The names the cookie authentication and its claim go by: one place, so that the controller that signs in and the host that validates agree.</summary>
public static class AccessConstants
{
    public const string Scheme = "Geminarr.Cookie";

    /// <summary>The claim that holds the identifier of the session in the registry: a cookie without a live session behind it is refused.</summary>
    public const string SessionClaim = "geminarr:session";
}
