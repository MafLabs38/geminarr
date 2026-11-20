using System.Net;
using System.Net.Http.Json;
using Geminarr.Api.V1.Accounts;
using Geminarr.Api.V1.Conversations;
using Geminarr.Core.Accounts;
using Geminarr.Core.Pipeline;
using Microsoft.Extensions.DependencyInjection;

namespace Geminarr.Integration.Test;

/// <summary>The accounts through the API, with the demonstration reader (one account) and a Mnemarr in memory: who is known, what is shown of them, forgetting, and the read-only view of another account (SPEC §8).</summary>
public sealed class AccountsApiFixture
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static readonly string DemoKey = AccountKey.Of(DemoReader.DemoAccount);

    [Fact]
    public async Task should_refuse_the_accounts_without_a_sign_in_and_a_write_without_the_header()
    {
        using var factory = new MnemarrFactory();
        using var anonymous = factory.CreateClient();
        using var withoutHeader = await factory.SignedInClientAsync(sendsTheHeader: false);

        (await anonymous.GetAsync("/api/v1/accounts", Token)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await withoutHeader.DeleteAsync("/api/v1/accounts/00000000", Token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task should_list_the_account_that_is_signed_in_with_its_machine_and_never_its_address_or_its_key()
    {
        using var factory = new MnemarrFactory();
        using var client = await factory.SignedInClientAsync();

        var body = await client.GetStringAsync("/api/v1/accounts", Token);
        var accounts = System.Text.Json.JsonSerializer.Deserialize<AccountsResource>(body, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!.Accounts;

        var account = accounts.Should().ContainSingle().Subject;
        account.Id.Should().Be(AccountService.IdOf(DemoKey));
        account.Machine.Should().Be("geminarr-demo", "the machine that it will be paired as, named from its address");
        (account.Active, account.SignedIn, account.Linked).Should().Be((true, true, false));
        body.Should().NotContain(DemoReader.DemoAccount).And.NotContain(DemoKey).And.NotContain("@");
    }

    [Fact]
    public async Task should_refuse_to_forget_the_account_that_is_being_read_and_say_that_an_unknown_one_is_not_there()
    {
        using var factory = new MnemarrFactory();
        using var client = await factory.SignedInClientAsync();
        var id = AccountService.IdOf(DemoKey);

        (await client.DeleteAsync($"/api/v1/accounts/{id}", Token)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await client.DeleteAsync("/api/v1/accounts/ffffffff", Token)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task should_answer_the_conversations_of_the_account_that_is_named_and_404_for_one_that_is_not_known()
    {
        using var factory = new MnemarrFactory();
        using var client = await factory.SignedInClientAsync();
        var id = AccountService.IdOf(DemoKey);

        var named = await client.GetFromJsonAsync<ConversationsResource>($"/api/v1/conversations?account={id}", Token);
        var unknown = await client.GetAsync("/api/v1/conversations?account=ffffffff", Token);

        named!.Backfill.Should().NotBeNull("the account that is being read has the progress of its history");
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task should_say_that_an_account_must_be_signed_in_when_a_pairing_is_asked_with_none()
    {
        using var factory = new GeminarrFactory();
        using var client = await factory.SignedInClientAsync();

        var response = await client.PostAsync("/api/v1/mnemarr/pairing", content: null, Token);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await client.GetFromJsonAsync<AccountsResource>("/api/v1/accounts", Token))!.Accounts.Should().BeEmpty();
    }

    private static readonly string OtherKey = AccountKey.Of("other.account@example.org");

    /// <summary>An account that is not the one being read, with two conversations in its workspace.</summary>
    private static void SeedOtherAccount(GeminarrFactory factory)
    {
        var now = new DateTimeOffset(2026, 11, 20, 9, 0, 0, TimeSpan.Zero);
        var store = new ConversationStore(factory.Services.GetRequiredService<IAccountWorkspaces>().Database(OtherKey));
        foreach (var id in new[] { "c1", "c2" })
        {
            store.Save(new ConversationRecord(id, null, now, now, now, 0, 2, 2, null, 0, ConversationSources.Web, ConversationStates.Synced, null));
        }
    }

    [Fact]
    public async Task should_show_the_conversations_of_another_account_without_the_progress_of_its_history()
    {
        using var factory = new GeminarrFactory();
        using var client = await factory.SignedInClientAsync();
        SeedOtherAccount(factory);

        var other = await client.GetFromJsonAsync<ConversationsResource>($"/api/v1/conversations?account={AccountService.IdOf(OtherKey)}", Token);

        other!.Total.Should().Be(2);
        other.Backfill.Should().BeNull("only the account that is being read has a history that is walked");
    }

    [Fact]
    public async Task should_forget_an_account_that_is_not_the_one_being_read_and_then_not_know_it()
    {
        using var factory = new GeminarrFactory();
        using var client = await factory.SignedInClientAsync();
        SeedOtherAccount(factory);
        var id = AccountService.IdOf(OtherKey);
        (await client.GetFromJsonAsync<AccountsResource>("/api/v1/accounts", Token))!.Accounts.Should().Contain(account => account.Id == id);

        (await client.DeleteAsync($"/api/v1/accounts/{id}", Token)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await client.GetFromJsonAsync<AccountsResource>("/api/v1/accounts", Token))!.Accounts.Should().NotContain(account => account.Id == id);
        (await client.DeleteAsync($"/api/v1/accounts/{id}", Token)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
