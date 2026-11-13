using System.Net.Http.Json;
using Geminarr.Core.Access;
using Geminarr.Core.Link;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace Geminarr.Integration.Test;

/// <summary>The real application in memory, with its own data folder (created or not), an operator, and a password file that are removed afterwards.</summary>
public abstract class GeminarrFactoryBase : WebApplicationFactory<Program>
{
    public const string OperatorName = "operator-name";
    public const string OperatorPassword = "a long enough password";

    private readonly bool _createFolder;
    private readonly string _passwordFile = Path.Combine(Path.GetTempPath(), $"geminarr-password-{Guid.NewGuid():N}");

    protected GeminarrFactoryBase(bool createFolder)
    {
        _createFolder = createFolder;
        DataFolder = Path.Combine(Path.GetTempPath(), $"geminarr-test-{Guid.NewGuid():N}");
        if (_createFolder)
        {
            Directory.CreateDirectory(DataFolder);
        }
    }

    public string DataFolder { get; }

    /// <summary>Whether the host starts with an operator, as a deployment with its name and its password file does.</summary>
    protected virtual bool HasOperator => true;

    /// <summary>The cookie of the test hosts is not <c>Secure</c> (they speak plain HTTP) unless a test asks for the default.</summary>
    protected virtual CookieSecurityMode CookieSecurity => CookieSecurityMode.SameAsRequest;

    protected virtual int SessionHours => 12;

    /// <summary>What a variant of the host changes in its services, after the application's own.</summary>
    protected virtual void ConfigureServices(IServiceCollection services)
    {
    }

    /// <summary>What a variant of the host adds to its configuration.</summary>
    protected virtual void AddSettings(Dictionary<string, string?> settings)
    {
    }

    /// <summary>A clock the test moves, for the tests of lifetimes; none by default (the real one).</summary>
    public FakeTimeProvider? Clock { get; protected init; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var settings = new Dictionary<string, string?>
        {
            ["DataFolder"] = DataFolder,
            ["Auth:CookieSecurity"] = CookieSecurity.ToString(),
            ["Auth:SessionHours"] = SessionHours.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        if (HasOperator)
        {
            File.WriteAllText(_passwordFile, OperatorPassword + "\n");
            settings["Auth:AdminUser"] = OperatorName;
            settings["Auth:AdminPasswordFile"] = _passwordFile;
        }

        AddSettings(settings);
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings));
        builder.ConfigureTestServices(services =>
        {
            // The tests run passes themselves, when they want one: a scheduler running behind them would make them depend on the clock of the machine.
            services.RemoveAll<Microsoft.Extensions.Hosting.IHostedService>();
            ConfigureServices(services);
            if (Clock is { } clock)
            {
                services.AddSingleton<TimeProvider>(clock);
            }
        });
    }

    /// <summary>A client that has signed in as the operator: its cookie jar holds the session, and (unless asked not to) every request it sends carries the header of a write.</summary>
    public async Task<HttpClient> SignedInClientAsync(bool sendsTheHeader = true)
    {
        var client = CreateClient();
        using var login = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login") { Content = JsonContent.Create(new { name = OperatorName, password = OperatorPassword }) };
        login.Headers.Add("X-Geminarr-Request", "1");
        (await client.SendAsync(login, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        if (sendsTheHeader)
        {
            client.DefaultRequestHeaders.Add("X-Geminarr-Request", "1");
        }

        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            if (Directory.Exists(DataFolder))
            {
                Directory.Delete(DataFolder, recursive: true);
            }

            File.Delete(_passwordFile);
        }
    }
}

/// <summary>A host with a writable data folder and an operator, shared by the tests that only read from it.</summary>
public sealed class GeminarrFactory : GeminarrFactoryBase
{
    public GeminarrFactory() : base(createFolder: true)
    {
    }
}

/// <summary>A host whose data folder does not exist, as a container started without its volume.</summary>
public sealed class MissingDataFolderFactory : GeminarrFactoryBase
{
    public MissingDataFolderFactory() : base(createFolder: false)
    {
    }
}

/// <summary>A host with nobody configured to sign in: alive and closed.</summary>
public sealed class NoOperatorFactory : GeminarrFactoryBase
{
    public NoOperatorFactory() : base(createFolder: true)
    {
    }

    protected override bool HasOperator => false;
}

/// <summary>A host with the default cookie: <c>Secure</c> and with the <c>__Host-</c> prefix.</summary>
public sealed class SecureCookieFactory : GeminarrFactoryBase
{
    public SecureCookieFactory() : base(createFolder: true)
    {
    }

    protected override CookieSecurityMode CookieSecurity => CookieSecurityMode.Always;
}

/// <summary>A host whose sessions last two hours on a clock that the test moves.</summary>
public sealed class ShortSessionFactory : GeminarrFactoryBase
{
    public ShortSessionFactory() : base(createFolder: true)
    {
        Clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 13, 8, 0, 0, TimeSpan.Zero));
    }

    protected override int SessionHours => 2;
}

/// <summary>A host for the tests of lockout: they fail sign-ins on purpose, so each class has its own.</summary>
public sealed class LockoutFactory : GeminarrFactoryBase
{
    public LockoutFactory() : base(createFolder: true)
    {
    }
}

/// <summary>A host whose deployment gives a name that is too long: it must not start.</summary>
public sealed class BadIdentityFactory : GeminarrFactoryBase
{
    public BadIdentityFactory() : base(createFolder: true)
    {
    }

    protected override void AddSettings(Dictionary<string, string?> settings) => settings["Identity:Name"] = new string('a', 41);
}

/// <summary>A host whose deployment says who it is: a name, two colors and a logo file (a png) that is removed afterwards.</summary>
public sealed class IdentityFactory : GeminarrFactoryBase
{
    public static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];

    private readonly string _logo = Path.Combine(Path.GetTempPath(), $"geminarr-logo-{Guid.NewGuid():N}.png");

    public IdentityFactory() : base(createFolder: true)
    {
    }

    protected override void AddSettings(Dictionary<string, string?> settings)
    {
        File.WriteAllBytes(_logo, Png);
        settings["Identity:Name"] = "Lab";
        settings["Identity:Colors:Name"] = "#3ee0c5";
        settings["Identity:Colors:Version"] = "inkMuted";
        settings["Identity:LogoFile"] = _logo;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            File.Delete(_logo);
        }
    }
}

/// <summary>A host whose deployment gives it a Mnemarr (a fake, in memory), a demonstration reader and a public address, as a sandbox does.</summary>
public sealed class MnemarrFactory : GeminarrFactoryBase
{
    public MnemarrFactory(bool urlFromSettings = false, string? enrolmentCode = null, bool blockTheReader = false)
        : base(createFolder: true)
    {
        UrlFromSettings = urlFromSettings;
        EnrolmentCode = enrolmentCode;
        Mnemarr = new Support.FakeMnemarr();
        Blocker = blockTheReader ? new Support.BlockingReader() : null;
    }

    public Support.FakeMnemarr Mnemarr { get; }

    /// <summary>A reader that waits until the test lets it go, to hold a pass open.</summary>
    public Support.BlockingReader? Blocker { get; }

    private bool UrlFromSettings { get; }

    private string? EnrolmentCode { get; }

    protected override void AddSettings(Dictionary<string, string?> settings)
    {
        settings["Reader:Kind"] = "Demo";
        settings["Reader:LoginFailures"] = "2";
        settings["Reader:DemoGrowSeconds"] = "1";
        settings["PublicUrl"] = "https://geminarr.homelab.example";
        if (UrlFromSettings)
        {
            settings["Mnemarr:Url"] = "https://mnemarr.test";
        }

        if (EnrolmentCode is not null)
        {
            var file = Path.Combine(DataFolder, "enrolment-code");
            File.WriteAllText(file, EnrolmentCode);
            settings["Mnemarr:EnrolmentCodeFile"] = file;
        }
    }

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddHttpClient(MnemarrClient.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => Mnemarr);
        if (Blocker is not null)
        {
            services.AddSingleton(new Geminarr.Core.Pipeline.ReaderProvider(Blocker));
        }
    }
}

/// <summary>A host whose reader is the browser (a fake one, in memory), as a deployment with the live view is.</summary>
public sealed class BrowserFactory : GeminarrFactoryBase
{
    public BrowserFactory(bool detectByPage = false)
        : base(createFolder: true)
    {
        Browser = new Support.FakeBrowserHost();
        DetectByPage = detectByPage;
    }

    private bool DetectByPage { get; }

    public Support.FakeBrowserHost Browser { get; }

    protected override void AddSettings(Dictionary<string, string?> settings)
    {
        settings["Reader:Kind"] = "Web";
        settings["Browser:LiveViewMinutes"] = "1";
        if (DetectByPage)
        {
            settings["Browser:SignInDetection"] = "Page";
        }
    }

    protected override void ConfigureServices(IServiceCollection services) =>
        services.AddSingleton<Geminarr.Core.Browser.IBrowserHost>(Browser);

    /// <summary>The cookie of a signed-in operator, as a header value: what the WebSocket of the live view presents.</summary>
    public async Task<string> SessionCookieAsync()
    {
        using var client = CreateClient();
        using var login = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login") { Content = JsonContent.Create(new { name = OperatorName, password = OperatorPassword }) };
        login.Headers.Add("X-Geminarr-Request", "1");
        using var response = await client.SendAsync(login, TestContext.Current.CancellationToken);
        return response.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
    }
}
