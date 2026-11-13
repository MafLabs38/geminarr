using Geminarr.Core.Access;
using Microsoft.Extensions.Options;

namespace Geminarr.Core.Test.Access;

/// <summary>The operator of a test: the name and the password are what the test says, and the status can be set.</summary>
internal sealed class FakeCredentials : IOperatorCredentials
{
    private readonly string? _password;

    public FakeCredentials(string? name = "operator-name", string? password = "a long enough password", OperatorStatus status = OperatorStatus.Ready)
    {
        Name = name;
        _password = password;
        Status = status;
    }

    public OperatorStatus Status { get; }

    public string? Name { get; }

    public bool Verify(string name, string password) => Status == OperatorStatus.Ready && name == Name && password == _password;
}

internal static class Options
{
    public static IOptions<T> Of<T>(T value) where T : class => Microsoft.Extensions.Options.Options.Create(value);
}
