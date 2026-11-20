using Geminarr.Core.Accounts;
using Geminarr.Core.Link;
using Geminarr.Core.Test.Access;
using Geminarr.Core.Test.Support;

namespace Geminarr.Core.Test.Link;

/// <summary>A key that the deployment gives is one machine: it serves the one account that exists and never a second, whose notes would go to the machine of the first (SPEC §8.2).</summary>
public sealed class KeyFileAccountsFixture : IDisposable
{
    private const string Jane = "jane.doe@example.org";
    private const string John = "john.roe@example.org";

    private readonly TestDatabase _data = new();
    private readonly SwitchableAccount _account = new();
    private readonly AccountWorkspaces _workspaces;
    private readonly KeyStore _subject;

    public KeyFileAccountsFixture()
    {
        var file = Path.Combine(_data.Folder, "given-key");
        File.WriteAllText(file, "mnm_given\n");
        _workspaces = new AccountWorkspaces(Options.Of(_data.Settings));
        _subject = new KeyStore(Options.Of(new MnemarrOptions { KeyFile = file }), _account, _workspaces);
    }

    public void Dispose() => _data.Dispose();

    [Fact]
    public void should_serve_nobody_in_particular_while_no_account_is_signed_in()
    {
        _subject.Read().Should().Be("mnm_given", "the page says that a key is there from the first start");
    }

    [Fact]
    public void should_serve_the_account_that_exists_and_refuse_a_second_one()
    {
        _account.SignIn(Jane);
        _workspaces.Ensure(AccountKey.Of(Jane));
        _subject.Read().Should().Be("mnm_given");
        _subject.ExistsFor(AccountKey.Of(Jane)).Should().BeTrue();

        _account.SignIn(John);

        _subject.Read().Should().BeNull("the key is the machine of the first account");
        _subject.ExistsFor(AccountKey.Of(John)).Should().BeFalse();
        var write = () => _subject.Write("mnm_other", "https://mnemarr.homelab.example");
        write.Should().Throw<InvalidOperationException>();
        File.ReadAllText(Path.Combine(_data.Folder, "given-key")).Trim().Should().Be("mnm_given");
    }

    [Fact]
    public void should_serve_the_second_account_once_the_first_is_forgotten()
    {
        _account.SignIn(Jane);
        _workspaces.Ensure(AccountKey.Of(Jane));
        _account.SignIn(John);
        _subject.Read().Should().BeNull();

        _workspaces.Delete(AccountKey.Of(Jane));

        _subject.Read().Should().Be("mnm_given");
    }
}
