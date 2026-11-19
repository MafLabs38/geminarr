namespace Geminarr.Host.Http;

/// <summary>Where the API lives. Two rules depend on it today (the headers of an answer, the files of the interface) and a third will (the header of a write, F-ACC-04): they must stay in step.</summary>
public static class ApiPaths
{
    public const string Prefix = "/api";

    /// <summary>The description of the API is held to the same rules as the API itself: nothing it says is cached.</summary>
    public const string OpenApi = "/openapi";

    public static bool IsApi(PathString path) =>
        path.StartsWithSegments(Prefix, StringComparison.OrdinalIgnoreCase) || path.StartsWithSegments(OpenApi, StringComparison.OrdinalIgnoreCase);
}
