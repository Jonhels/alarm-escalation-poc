using Domain.CallAttempts.Contracts;
using Microsoft.AspNetCore.Mvc;
using Web.Mappers;
using Web.ViewModels;

namespace Web.Controllers;

[ApiController]
public class TestAlarmController : ControllerBase
{
    private readonly ICallOrchestrationService _orchestrationService;

    public TestAlarmController(ICallOrchestrationService orchestrationService)
    {
        _orchestrationService = orchestrationService;
    }

    [HttpPost]
    [Route("api/test-alarm")]
    public async Task<ActionResult<CallAttemptViewModel>> TriggerAlarm(
        [FromBody] TriggerAlarmRequest request,
        CancellationToken cancellationToken)
    {
        var callAttempt = await _orchestrationService.TriggerAlarmCallAsync(
            request.PhoneNumber,
            request.AlarmMessage,
            cancellationToken);

        return Ok(callAttempt.ToViewModel());
    }
}
