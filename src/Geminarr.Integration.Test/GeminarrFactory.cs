using System.Net.Http.Json;
using Geminarr.Core.Access;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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

        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings));
        if (Clock is { } clock)
        {
            builder.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(clock));
        }
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
