using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Geminarr.Integration.Test;

/// <summary>The real application in memory, with its own data folder (created or not) that is removed afterwards.</summary>
public abstract class GeminarrFactoryBase : WebApplicationFactory<Program>
{
    private readonly bool _createFolder;

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

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Settings of the host under test, as the environment of a real one would give them.
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["DataFolder"] = DataFolder }));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(DataFolder))
        {
            Directory.Delete(DataFolder, recursive: true);
        }
    }
}

/// <summary>A host with a writable data folder, shared by the tests that only read from it.</summary>
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
