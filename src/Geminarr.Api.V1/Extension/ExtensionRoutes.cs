namespace Geminarr.Api.V1.Extension;

/// <summary>Where the extension speaks: one place for the controller that answers and the middleware that lets the extension's origin read the answer, so that they cannot drift apart.</summary>
public static class ExtensionRoutes
{
    public const string Base = "api/v1/extension";
    public const string Push = "push";
    public const string PushPath = $"/{Base}/{Push}";
}
