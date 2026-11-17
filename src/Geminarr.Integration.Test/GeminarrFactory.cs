using System.Net.Http.Json;
using Geminarr.Core.Access;
using Geminarr.Core.Link;
using Microsoft.AspNetCore.Builder;
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

    /// <summary>What the host takes for the ports that listen off the loopback of the namespace it shares with the browser: none.</summary>
    public Support.FakeExposure Exposure { get; } = new();

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
            // What listens on the machine that runs the tests is not what a test is about: nothing is exposed unless a test says so.
            services.AddSingleton<Geminarr.Core.Browser.IBrowserExposure>(Exposure);
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

/// <summary>A host whose reader is the browser (a fake one, in memory), as a deployment with the browser container is.</summary>
public sealed class BrowserFactory : GeminarrFactoryBase
{
    public BrowserFactory()
        : base(createFolder: true)
    {
        Browser = new Support.FakeBrowserHost();
    }

    public Support.FakeBrowserHost Browser { get; }

    protected override void AddSettings(Dictionary<string, string?> settings)
    {
        settings["Reader:Kind"] = "Web";
        settings["Browser:CdpUrl"] = "http://127.0.0.1:9222";
        settings["Browser:DesktopUrl"] = "http://127.0.0.1:3000";
    }

    protected override void ConfigureServices(IServiceCollection services) =>
        services.AddSingleton<Geminarr.Core.Browser.IBrowserHost>(Browser);
}

/// <summary>
/// A host that serves the web desktop of a browser (a fake one, started by the test) on an origin of its own, a port apart from the interface's (ADR 0019). The in-memory host has no port: a request says which one it arrived
/// on by a header that the test adds, as a real listener would say it.
/// </summary>
public sealed class RelayFactory : GeminarrFactoryBase
{
    public const string LocalPortHeader = "X-Test-Local-Port";

    private readonly string _desktopUrl;
    private readonly string? _allowedHostNames;
    private readonly string? _desktopPublicUrl;

    private readonly bool _secureCookie;

    public RelayFactory(string desktopUrl, string? allowedHostNames = null, bool movableClock = false, string? desktopPublicUrl = null, bool secureCookie = false)
        : base(createFolder: true)
    {
        _secureCookie = secureCookie;
        _desktopUrl = desktopUrl;
        _allowedHostNames = allowedHostNames;
        _desktopPublicUrl = desktopPublicUrl;
        DesktopPort = FreePort();
        if (movableClock)
        {
            Clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 15, 12, 0, 0, TimeSpan.Zero));
        }
    }

    protected override CookieSecurityMode CookieSecurity => _secureCookie ? CookieSecurityMode.Always : CookieSecurityMode.SameAsRequest;

    /// <summary>The port of the desktop's origin.</summary>
    public int DesktopPort { get; }

    private static int FreePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
    }

    protected override void AddSettings(Dictionary<string, string?> settings)
    {
        settings["Browser:DesktopUrl"] = _desktopUrl;
        settings["Browser:DesktopPort"] = DesktopPort.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (_desktopPublicUrl is not null)
        {
            settings["Browser:DesktopPublicUrl"] = _desktopPublicUrl;
        }

        if (_allowedHostNames is not null)
        {
            settings["AllowedHostNames"] = _allowedHostNames;
        }

        settings["Browser:CdpUrl"] = "http://127.0.0.1:9222";
        settings["Reader:Kind"] = "Web";
    }

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<Geminarr.Core.Browser.IBrowserHost>(new Support.FakeBrowserHost());
        services.AddSingleton<Microsoft.AspNetCore.Hosting.IStartupFilter>(new LocalPortFilter());
    }

    /// <summary>The operator signs in, asks for the address of the desktop, and the ticket in it is returned.</summary>
    public static async Task<string> TicketAsync(HttpClient signedIn)
    {
        using var response = await signedIn.PostAsync("/api/v1/connection/desktop", content: null, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var url = (await response.Content.ReadFromJsonAsync<Geminarr.Api.V1.Connection.DesktopResource>(TestContext.Current.CancellationToken))!.Url;
        return System.Web.HttpUtility.ParseQueryString(new Uri(url).Query)["ticket"]!;
    }

    /// <summary>A client of the desktop's origin: it has exchanged a ticket of a signed-in operator for the cookie of that origin, as the window that the person opens does. It follows no redirect.</summary>
    public async Task<HttpClient> DesktopClientAsync(HttpClient? operatorClient = null)
    {
        using var own = operatorClient is null ? await SignedInClientAsync() : null;
        var ticket = await TicketAsync(operatorClient ?? own!);
        var desktop = Anonymous();
        using var entered = await desktop.GetAsync($"/enter?ticket={Uri.EscapeDataString(ticket)}", TestContext.Current.CancellationToken);
        entered.StatusCode.Should().Be(System.Net.HttpStatusCode.Redirect);
        return desktop;
    }

    /// <summary>A client of the desktop's origin that has no cookie of it.</summary>
    public HttpClient Anonymous()
    {
        var client = CreateClient(new() { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(LocalPortHeader, DesktopPort.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return client;
    }

    private sealed class LocalPortFilter : Microsoft.AspNetCore.Hosting.IStartupFilter
    {
        public Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> Configure(Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> next) => app =>
        {
            app.Use((context, following) =>
            {
                if (int.TryParse(context.Request.Headers[LocalPortHeader], out var port))
                {
                    context.Connection.LocalPort = port;
                }

                return following(context);
            });
            next(app);
        };
    }
}
