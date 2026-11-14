using Geminarr.Core.Browser;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Geminarr.Browser;

public static class BrowserServiceCollectionExtensions
{
    /// <summary>The browser of the host is Chromium, driven with Playwright. Nothing starts until a session is checked or a live view is opened.</summary>
    public static IServiceCollection AddGeminarrBrowser(this IServiceCollection services)
    {
        services.Replace(ServiceDescriptor.Singleton<IBrowserHost, PlaywrightBrowserHost>());
        return services;
    }
}
