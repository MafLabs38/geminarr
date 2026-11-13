using Geminarr.Core.Link;
using Geminarr.Core.Pipeline;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Geminarr.Api.V1.Mnemarr;

/// <summary>The Mnemarr that Geminarr sends to: where, as which machine, how it was linked (pairing by asking, or enrolment), and the way to unlink (SPEC §5.1, F-LNK-01 and F-LNK-04).</summary>
[ApiController]
[Route("api/v1/mnemarr")]
public sealed class MnemarrController : ControllerBase
{
    private readonly LinkService _link;
    private readonly IOutboxStore _outbox;

    public MnemarrController(LinkService link, IOutboxStore outbox)
    {
        _link = link;
        _outbox = outbox;
    }

    /// <summary>The destination as it is now.</summary>
    [HttpGet]
    [ProducesResponseType<MnemarrResource>(StatusCodes.Status200OK)]
    public MnemarrResource Get() => MnemarrResource.From(_link.Status(), _outbox.PendingLines());

    /// <summary>Gives the address of the Mnemarr to send to. Refused when the deployment gives it, or when a key is held (unlink first).</summary>
    [HttpPut]
    [ProducesResponseType<MnemarrResource>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public ActionResult<MnemarrResource> SetAddress(MnemarrAddressRequest request)
    {
        if (MnemarrAddress.Normalize(request.Url) is not { } address)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "That is not the address of a Mnemarr: write it as https://mnemarr.homelab.example.");
        }

        try
        {
            _link.SetAddress(address);
        }
        catch (InvalidOperationException ex)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: ex.Message);
        }

        return Get();
    }

    /// <summary>Unlinks: the key and the machine are forgotten, and nothing is sent any more. What Mnemarr already holds stays there.</summary>
    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public IActionResult Unlink()
    {
        try
        {
            _link.Unlink();
        }
        catch (InvalidOperationException ex)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: ex.Message);
        }

        return NoContent();
    }

    /// <summary>Starts a pairing by asking: Mnemarr gives a code and a page where a person approves it, and the key comes once they have.</summary>
    [HttpPost("pairing")]
    [ProducesResponseType<PairingResource>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PairingResource>> StartPairing(CancellationToken cancellationToken)
    {
        try
        {
            return PairingResource.From(await _link.StartPairingAsync(cancellationToken));
        }
        catch (SinkException ex)
        {
            return Failure(ex);
        }
    }

    /// <summary>The pairing that waits, as it was started (nothing is asked of Mnemarr).</summary>
    [HttpGet("pairing")]
    [ProducesResponseType<PairingResource>(StatusCodes.Status200OK)]
    public PairingResource GetPairing() => PairingResource.From(_link.Pairing());

    /// <summary>Asks Mnemarr what became of the pairing. When a person approved it, the key is kept and the machine is linked.</summary>
    [HttpPost("pairing/check")]
    [ProducesResponseType<PairingResource>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PairingResource>> CheckPairing(CancellationToken cancellationToken)
    {
        try
        {
            return PairingResource.From(await _link.PollPairingAsync(cancellationToken));
        }
        catch (SinkException ex)
        {
            return Failure(ex);
        }
    }

    /// <summary>Gives the pairing up.</summary>
    [HttpDelete("pairing")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public IActionResult CancelPairing()
    {
        _link.CancelPairing();
        return NoContent();
    }

    private ObjectResult Failure(SinkException exception) => exception.Failure switch
    {
        SinkFailure.NotLinked => Problem(statusCode: StatusCodes.Status409Conflict, title: "Give the address of Mnemarr first."),
        SinkFailure.InsecureAddress => Problem(statusCode: StatusCodes.Status400BadRequest, title: "The address of Mnemarr must be https (or http to this machine)."),
        _ => Problem(statusCode: StatusCodes.Status502BadGateway, title: exception.Message),
    };
}
