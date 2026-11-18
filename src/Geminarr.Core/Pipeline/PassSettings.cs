using Geminarr.Core.Configuration;
using Microsoft.Extensions.Options;

namespace Geminarr.Core.Pipeline;

/// <summary>What a pass is told by the settings, and the reader that is configured (or none).</summary>
public sealed class PassSettings
{
    private readonly IOptions<ReaderOptions> _reader;
    private readonly IOptions<RetentionOptions> _retention;
    private readonly IOptions<GeminarrOptions> _geminarr;

    public PassSettings(IOptions<ReaderOptions> reader, IOptions<RetentionOptions> retention, IOptions<GeminarrOptions> geminarr, ReaderProvider readers)
    {
        _reader = reader;
        _retention = retention;
        _geminarr = geminarr;
        Reader = readers.Reader;
    }

    /// <summary>The reader that reads the assistant, or null: not connected.</summary>
    public IConversationReader? Reader { get; }

    public ReaderOptions Reading => _reader.Value;

    public int RunsDays => _retention.Value.RunsDays;

    public string? PublicUrl => _geminarr.Value.PublicUrl;
}
