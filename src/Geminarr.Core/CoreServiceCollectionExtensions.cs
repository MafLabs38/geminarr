using Geminarr.Core.Access;
using Geminarr.Core.Browser;
using Geminarr.Core.Configuration;
using Geminarr.Core.Connection;
using Geminarr.Core.Extension;
using Geminarr.Core.Identity;
using Geminarr.Core.Link;
using Geminarr.Core.Pipeline;
using Geminarr.Core.Storage;
using Geminarr.Core.SystemInfo;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Geminarr.Core;

public static class CoreServiceCollectionExtensions
{
    public static IServiceCollection AddGeminarrCore(this IServiceCollection services, IConfiguration configuration)
    {
        // DataFolder stays at the root; the settings that come with later verticals get sections of their own.
        services.AddSingleton<IValidateOptions<GeminarrOptions>, GeminarrOptionsValidator>();
        services.AddOptions<GeminarrOptions>().Bind(configuration).ValidateOnStart();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<ISystemStatusService, SystemStatusService>();
        services.AddSingleton<IValidateOptions<AuthOptions>, AuthOptionsValidator>();
        services.AddSingleton<IValidateOptions<AuthOptions>, AuthOptionsBrowserValidator>();
        services.AddOptions<AuthOptions>().Bind(configuration.GetSection("Auth")).ValidateOnStart();
        services.AddSingleton<IOperatorCredentials, OperatorCredentials>();
        services.AddSingleton<LoginThrottle>();
        services.AddSingleton<DesktopAccess>();
        services.AddSingleton<SessionRegistry>();
        services.AddSingleton<SignInService>();
        services.AddSingleton<AllowedHostList>();
        services.AddSingleton<IValidateOptions<IdentityOptions>, IdentityOptionsValidator>();
        services.AddOptions<IdentityOptions>().Bind(configuration.GetSection(IdentityOptions.SectionName)).ValidateOnStart();
        services.AddSingleton<IdentityService>();
        services.AddSingleton<IDatabase, Database>();
        services.AddSingleton<IConnectionStore, ConnectionStore>();
        services.AddSingleton<IConversationStore, ConversationStore>();
        services.AddSingleton<IOutboxStore, OutboxStore>();
        services.AddSingleton<IRunStore, RunStore>();
        services.AddSingleton<IDestinationStore, DestinationStore>();
        services.AddSingleton<IKeyStore, KeyStore>();
        services.AddSingleton<IValidateOptions<MnemarrOptions>, MnemarrOptionsValidator>();
        services.AddOptions<MnemarrOptions>().Bind(configuration.GetSection(MnemarrOptions.SectionName)).ValidateOnStart();
        services.AddSingleton<IValidateOptions<ReaderOptions>, ReaderOptionsValidator>();
        services.AddOptions<ReaderOptions>().Bind(configuration.GetSection(ReaderOptions.SectionName)).ValidateOnStart();
        services.AddSingleton<IValidateOptions<RetentionOptions>, RetentionOptionsValidator>();
        services.AddOptions<RetentionOptions>().Bind(configuration.GetSection(RetentionOptions.SectionName)).ValidateOnStart();
        // No redirect is followed (a key must not follow one to another host), and a call is bounded.
        services.AddHttpClient(MnemarrClient.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(30))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
        services.AddHttpClient(nameof(DesktopBrowserHost)).ConfigurePrimaryHttpMessageHandler(DesktopBrowserHost.NewHandler);
        services.AddSingleton<MnemarrClient>();
        services.AddSingleton<LinkService>();
        services.AddSingleton<IConversationSink, MnemarrSink>();
        services.AddSingleton<IValidateOptions<BrowserOptions>, BrowserOptionsValidator>();
        services.AddSingleton<IValidateOptions<BrowserOptions>, DesktopOriginValidator>();
        services.AddOptions<BrowserOptions>().Bind(configuration.GetSection(BrowserOptions.SectionName)).ValidateOnStart();
        services.TryAddSingleton<IBrowserHost, DesktopBrowserHost>();
        services.TryAddSingleton<IBrowserExposure, NoBrowserExposure>();
        services.AddSingleton<ConnectionTracker>();
        services.AddSingleton<ConnectionService>();
        // The kind is read when the reader is first needed, not when the services are registered: the configuration is final by then.
        services.AddSingleton<IValidateOptions<ExtensionOptions>, ExtensionOptionsValidator>();
        services.AddOptions<ExtensionOptions>().Bind(configuration.GetSection(ExtensionOptions.SectionName)).ValidateOnStart();
        services.AddSingleton<IExtensionToken, ExtensionToken>();
        services.AddSingleton<IExtensionPackage, ExtensionPackage>();
        // A disconnect that the extension had not confirmed when Geminarr stopped is asked again: the record says so.
        services.AddSingleton(provider =>
        {
            var tracker = provider.GetRequiredService<ConnectionTracker>();
            return ExtensionInbox.Restored(tracker.Current(), () => tracker.Current().State == ConnectionState.Paused, stillPending => tracker.SignOutSettled(stillPending));
        });
        services.AddSingleton<ExtensionService>();
        services.AddSingleton(provider => new ReaderProvider(provider.GetRequiredService<IOptions<ReaderOptions>>().Value.Kind switch
        {
            ReaderKind.Demo => ActivatorUtilities.CreateInstance<DemoReader>(provider),
            ReaderKind.Web => ActivatorUtilities.CreateInstance<ExtensionReader>(provider),
            _ => null,
        }));
        services.AddSingleton(provider => new PassStores(provider.GetRequiredService<IConversationStore>(), provider.GetRequiredService<IOutboxStore>(), provider.GetRequiredService<IRunStore>()));
        services.AddSingleton<PassSettings>();
        services.AddSingleton<PassTrigger>();
        services.AddSingleton<PassRunner>();
        services.AddSingleton<ISettingsSection, ReaderSettingsSection>();
        services.AddSingleton<ISettingsSection, BrowserSettingsSection>();
        services.AddSingleton<ISettingsSection, ExtensionSettingsSection>();
        services.AddSingleton<ISettingsSection, RetentionSettingsSection>();
        services.AddSingleton<EffectiveSettings>();
        return services;
    }
}
