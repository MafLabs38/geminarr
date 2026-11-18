using Geminarr.Core.Access;
using Geminarr.Core.Configuration;
using Geminarr.Core.Identity;
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
        services.AddOptions<AuthOptions>().Bind(configuration.GetSection("Auth")).ValidateOnStart();
        services.AddSingleton<IOperatorCredentials, OperatorCredentials>();
        services.AddSingleton<LoginThrottle>();
        services.AddSingleton<SessionRegistry>();
        services.AddSingleton<SignInService>();
        services.AddSingleton<AllowedHostList>();
        services.AddSingleton<IValidateOptions<IdentityOptions>, IdentityOptionsValidator>();
        services.AddOptions<IdentityOptions>().Bind(configuration.GetSection(IdentityOptions.SectionName)).ValidateOnStart();
        services.AddSingleton<IdentityService>();
        services.AddSingleton<EffectiveSettings>();
        return services;
    }
}
