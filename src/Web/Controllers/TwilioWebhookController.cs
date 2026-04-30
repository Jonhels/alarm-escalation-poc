using Domain.CallAttempts.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace Web.Controllers;

[ApiController]
public class TwilioWebhookController : ControllerBase
{
    private readonly ICallOrchestrationService _orchestrationService;

    public TwilioWebhookController(ICallOrchestrationService orchestrationService)
    {
        _orchestrationService = orchestrationService;
    }

    [HttpPost]
    [Route("api/twilio/voice")]
    [Produces("application/xml")]
    public ContentResult HandleVoice([FromForm] string CallSid)
    {
        var twiml = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<Response>
    <Say voice=""alice"">This is an alarm notification. Press 1 to acknowledge the alarm.</Say>
    <Gather numDigits=""1"" action=""/api/twilio/gather"" method=""POST"" timeout=""10"">
        <Say voice=""alice"">Press 1 to acknowledge.</Say>
    </Gather>
    <Say voice=""alice"">You did not press any key. Goodbye.</Say>
</Response>";

        return Content(twiml, "application/xml");
    }

    [HttpPost]
    [Route("api/twilio/gather")]
    [Produces("application/xml")]
    public async Task<ContentResult> HandleGather(
        [FromForm] string CallSid,
        [FromForm] string Digits,
        CancellationToken cancellationToken)
    {
        await _orchestrationService.HandleGatherAsync(CallSid, Digits, cancellationToken);

        var answer = Digits == "1" ? "The alarm has been acknowledged. Thank you." : "Input not recognized.";
        var twiml = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<Response>
    <Say voice=""alice"">{answer}</Say>
    <Hangup/>
</Response>";

        return Content(twiml, "application/xml");
    }

    [HttpPost]
    [Route("api/twilio/status")]
    public async Task<ActionResult> HandleStatusCallback(
        [FromForm] string CallSid,
        [FromForm] string CallStatus,
        CancellationToken cancellationToken)
    {
        await _orchestrationService.HandleStatusCallbackAsync(CallSid, CallStatus, cancellationToken);
        return Ok();
    }
}
