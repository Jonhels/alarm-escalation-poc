using Domain.CallAttempts.Contracts;
using Microsoft.AspNetCore.Mvc;
using Web.Mappers;
using Web.ViewModels;

namespace Web.Controllers;

[ApiController]
public class CallAttemptsController : ControllerBase
{
    private readonly ICallAttemptRepository _repository;

    public CallAttemptsController(ICallAttemptRepository repository)
    {
        _repository = repository;
    }

    [HttpGet]
    [Route("api/call-attempts")]
    public async Task<ActionResult<IReadOnlyList<CallAttemptViewModel>>> GetRecent(
        [FromQuery] int count = 20,
        CancellationToken cancellationToken = default)
    {
        var attempts = await _repository.GetRecentAsync(count, cancellationToken);
        return Ok(attempts.Select(a => a.ToViewModel()).ToList());
    }

    [HttpGet]
    [Route("api/call-attempts/{id:guid}")]
    public async Task<ActionResult<CallAttemptViewModel>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var attempt = await _repository.GetByIdAsync(id, cancellationToken);

        if (attempt is null)
        {
            return NotFound();
        }

        return Ok(attempt.ToViewModel());
    }
}
