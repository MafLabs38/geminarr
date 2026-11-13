namespace Geminarr.Core.Pipeline;

/// <summary>The reader that is configured, or none: Geminarr is not connected to any assistant until a reader exists (the real one comes with the vertical that reads Gemini).</summary>
public sealed class ReaderProvider
{
    public ReaderProvider(IConversationReader? reader)
    {
        Reader = reader;
    }

    public IConversationReader? Reader { get; }
}
