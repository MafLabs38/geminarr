using Geminarr.Host.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Geminarr.Integration.Test;

/// <summary>A request the server could not read (a body larger than the limit of the call) is the caller's mistake, not the server's: it gets the status the server found, as a problem document.</summary>
public sealed class ClientErrorExceptionHandlerFixture
{
    private sealed class RecordingProblems : IProblemDetailsService
    {
        public ProblemDetailsContext? Written { get; private set; }

        public ValueTask WriteAsync(ProblemDetailsContext context)
        {
            Written = context;
            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> TryWriteAsync(ProblemDetailsContext context)
        {
            Written = context;
            return ValueTask.FromResult(true);
        }
    }

    [Fact]
    public async Task should_answer_a_request_that_could_not_be_read_with_the_status_the_server_found_and_never_its_cause()
    {
        var problems = new RecordingProblems();
        var context = new DefaultHttpContext();

        var handled = await new ClientErrorExceptionHandler(problems).TryHandleAsync(context, new BadHttpRequestException("secret cause", StatusCodes.Status413PayloadTooLarge), TestContext.Current.CancellationToken);

        handled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status413PayloadTooLarge);
        problems.Written!.ProblemDetails.Status.Should().Be(StatusCodes.Status413PayloadTooLarge);
        problems.Written.ProblemDetails.Title.Should().Be("The request cannot be read.").And.NotContain("secret");
    }

    [Fact]
    public async Task should_leave_any_other_failure_to_the_server_error_page()
    {
        var problems = new RecordingProblems();

        var handled = await new ClientErrorExceptionHandler(problems).TryHandleAsync(new DefaultHttpContext(), new InvalidOperationException("a bug"), TestContext.Current.CancellationToken);

        handled.Should().BeFalse();
        problems.Written.Should().BeNull();
    }
}
