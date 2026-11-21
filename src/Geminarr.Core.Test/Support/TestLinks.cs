using Geminarr.Core.Accounts;
using Geminarr.Core.Link;
using Geminarr.Core.Pipeline;
using Geminarr.Core.Test.Access;

namespace Geminarr.Core.Test.Support;

/// <summary>What a link is made of in the tests that are about one account: the stores that the test gives, a fixed account, and workspaces of the folder of the test.</summary>
public static class TestLinks
{
    public static LinkStores Stores(IDestinationStore destination, IKeyStore keys, IOutboxStore outbox, string dataFolder) =>
        new(destination, keys, outbox, new FixedAccount(), new AccountWorkspaces(Options.Of(new Geminarr.Core.Configuration.GeminarrOptions { DataFolder = dataFolder })));
}
