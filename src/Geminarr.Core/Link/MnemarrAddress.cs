using System.Net;

namespace Geminarr.Core.Link;

/// <summary>The address of a Mnemarr: what is accepted, and whether it is safe to talk to.</summary>
public static class MnemarrAddress
{
    /// <summary>The address without its trailing slash, or null when it is not an http or https address with a host, no credentials, no query and no path.</summary>
    public static string? Normalize(string? address)
    {
        if (!Uri.TryCreate(address?.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || uri.Host.Length == 0
            || uri.UserInfo.Length > 0
            || uri.Query.Length > 0
            || uri.Fragment.Length > 0
            || uri.AbsolutePath != "/")
        {
            return null;
        }

        return uri.GetLeftPart(UriPartial.Authority);
    }

    /// <summary>
    /// The address of Geminarr's own page to put in an attention, or null when Mnemarr would not keep it: an absolute https address (or http to the loopback) with a host, no credentials, a path allowed
    /// (a proxy under a sub-path), at most 512 characters, with its trailing slash.
    /// </summary>
    public static string? AttentionLink(string? address)
    {
        if (!Uri.TryCreate(address?.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || uri.Host.Length == 0 || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0)
        {
            return null;
        }

        var link = uri.AbsoluteUri.EndsWith('/') ? uri.AbsoluteUri : uri.AbsoluteUri + "/";
        return link.Length <= 512 && IsSecure(uri.GetLeftPart(UriPartial.Authority)) ? link : null;
    }

    /// <summary>Whether a normalized address is https, or plain http to the loopback (a Mnemarr on the same machine).</summary>
    public static bool IsSecure(string normalized)
    {
        var uri = new Uri(normalized);
        return uri.Scheme == "https" || uri.IsLoopback || (IPAddress.TryParse(uri.Host.Trim('[', ']'), out var ip) && IPAddress.IsLoopback(ip));
    }
}
