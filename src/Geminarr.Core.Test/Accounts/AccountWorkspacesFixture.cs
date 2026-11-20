using Geminarr.Core.Accounts;
using Geminarr.Core.Configuration;
using Geminarr.Core.Connection;
using Geminarr.Core.Pipeline;
using Geminarr.Core.Test.Access;
using Geminarr.Core.Test.Support;

namespace Geminarr.Core.Test.Accounts;

public sealed class AccountWorkspacesFixture : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 9, 0, 0, TimeSpan.Zero);
    private static readonly string A = AccountKey.Of("a@example.org");
    private static readonly string B = AccountKey.Of("b@example.org");

    private readonly TestDatabase _data = new();
    private readonly AccountContext _context;
    private readonly AccountWorkspaces _workspaces;
    private readonly ConversationStore _conversations;

    public AccountWorkspacesFixture()
    {
        _context = new AccountContext(new ReaderProvider(null), new ConnectionStore(_data.Database, TimeProvider.System));
        _workspaces = new AccountWorkspaces(Options.Of(_data.Settings));
        _conversations = new ConversationStore(new AccountDatabase(_context, _workspaces));
    }

    public void Dispose() => _data.Dispose();

    private static ConversationRecord Record(string id, int turns) => new(id, null, Now, Now, Now, 0, turns, turns, null, 0, ConversationSources.Web, ConversationStates.Synced, null);

    [Fact]
    public void should_keep_the_same_conversation_of_two_accounts_apart()
    {
        using (_context.Use(A))
        {
            _conversations.Save(Record("c1", 3));
        }

        using (_context.Use(B))
        {
            _conversations.Save(Record("c1", 5));
        }

        using (_context.Use(A))
        {
            _conversations.Get("c1")!.TurnsKnown.Should().Be(3);
            _conversations.Count().Should().Be(1);
        }

        using (_context.Use(B))
        {
            _conversations.Get("c1")!.TurnsKnown.Should().Be(5);
        }
    }

    [Fact]
    public void should_follow_a_switch_of_account_with_the_same_store()
    {
        using (_context.Use(A))
        {
            _conversations.Save(Record("c1", 3));
        }

        using (_context.Use(B))
        {
            _conversations.Count().Should().Be(0, "the other account has nothing yet");
        }

        using (_context.Use(A))
        {
            _conversations.Count().Should().Be(1);
        }
    }

    [Fact]
    public void should_find_nothing_and_keep_nothing_with_no_account()
    {
        using (_context.Use(null))
        {
            _conversations.Save(Record("c1", 3));

            _conversations.Count().Should().Be(0, "what is written for nobody is gone");
            _conversations.Get("c1").Should().BeNull();
        }

        _workspaces.Known().Should().BeEmpty("no workspace is made for nobody");
    }

    [Fact]
    public void should_make_the_workspace_of_an_account_at_its_first_use_and_list_the_known_ones_in_order()
    {
        _workspaces.Exists(A).Should().BeFalse();

        using (_context.Use(B))
        {
            _conversations.Count();
        }

        using (_context.Use(A))
        {
            _conversations.Count();
        }

        _workspaces.Known().Should().Equal(new[] { A, B }.Order(StringComparer.Ordinal));
        File.Exists(Path.Combine(_workspaces.Folder(A), "geminarr.db")).Should().BeTrue();
    }

    [Fact]
    public void should_delete_a_workspace_with_its_database_and_leave_the_others()
    {
        using (_context.Use(A))
        {
            _conversations.Save(Record("c1", 3));
        }

        using (_context.Use(B))
        {
            _conversations.Save(Record("c1", 5));
        }

        _workspaces.Delete(A);

        _workspaces.Exists(A).Should().BeFalse();
        using (_context.Use(B))
        {
            _conversations.Get("c1")!.TurnsKnown.Should().Be(5);
        }

        using (_context.Use(A))
        {
            _conversations.Count().Should().Be(0, "an account that was forgotten starts again as a new one");
        }
    }

    [Theory]
    [InlineData("../x")]
    [InlineData("")]
    [InlineData("not-a-key")]
    [InlineData("0123456789ABCDEF")]
    public void should_refuse_to_name_a_folder_with_what_is_not_a_key(string key)
    {
        var folder = () => _workspaces.Folder(key);
        var open = () => _workspaces.Database(key);
        var delete = () => _workspaces.Delete(key);

        folder.Should().Throw<ArgumentException>();
        open.Should().Throw<ArgumentException>();
        delete.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_not_make_a_workspace_in_a_data_folder_that_is_not_there()
    {
        var missing = new AccountWorkspaces(Options.Of(new GeminarrOptions { DataFolder = Path.Combine(_data.Folder, "not-mounted") }));

        var open = () => missing.Database(A);

        open.Should().Throw<DirectoryNotFoundException>();
    }

    [Fact]
    public void should_keep_a_workspace_to_its_owner_alone()
    {
        using (_context.Use(A))
        {
            _conversations.Count();
        }

        if (!OperatingSystem.IsWindows())
        {
            File.GetUnixFileMode(_workspaces.Folder(A)).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.GetUnixFileMode(Path.Combine(_workspaces.Folder(A), "geminarr.db")).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    [Fact]
    public void should_leave_the_root_database_without_what_belongs_to_an_account()
    {
        using (_context.Use(A))
        {
            _conversations.Save(Record("c1", 3));
        }

        new ConversationStore(_data.Database).Count().Should().Be(0);
    }
}
