using Common.Options;
using Domain.CallAttempts.Contracts;
using Domain.CallAttempts.Models;
using ExternalServices.Twilio.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExternalServices.Services;

public class CallOrchestrationService : ICallOrchestrationService
{
    private readonly ICallAttemptRepository _repository;
    private readonly ITwilioClient _twilioClient;
    private readonly TwilioOptions _twilioOptions;
    private readonly ILogger<CallOrchestrationService> _logger;

    public CallOrchestrationService(
        ICallAttemptRepository repository,
        ITwilioClient twilioClient,
        IOptions<TwilioOptions> twilioOptions,
        ILogger<CallOrchestrationService> logger)
    {
        _repository = repository;
        _twilioClient = twilioClient;
        _twilioOptions = twilioOptions.Value;
        _logger = logger;
    }

    public async Task<CallAttempt> TriggerAlarmCallAsync(
        string phoneNumber,
        string alarmMessage,
        CancellationToken cancellationToken = default)
    {
        var callAttempt = new CallAttempt
        {
            Id = Guid.NewGuid(),
            PhoneNumber = phoneNumber,
            AlarmMessage = alarmMessage,
            Status = CallAttemptStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        callAttempt = await _repository.CreateAsync(callAttempt, cancellationToken);

        _logger.LogInformation(
            "Call attempt created: Id={CallAttemptId}, Phone={PhoneNumber}",
            callAttempt.Id, phoneNumber);

        try
        {
            var webhookBaseUrl = _twilioOptions.BaseWebhookUrl ?? "";
            var fromPhoneNumber = _twilioOptions.FromPhoneNumber;

            var callSid = await _twilioClient.InitiateCallAsync(
                phoneNumber,
                fromPhoneNumber,
                webhookBaseUrl,
                cancellationToken);

            callAttempt = callAttempt with
            {
                Status = CallAttemptStatus.InProgress,
                TwilioCallSid = callSid
            };

            await _repository.UpdateAsync(callAttempt, cancellationToken);

            _logger.LogInformation(
                "Twilio call initiated: CallAttemptId={CallAttemptId}, CallSid={CallSid}",
                callAttempt.Id, callSid);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to initiate Twilio call for CallAttemptId={CallAttemptId}",
                callAttempt.Id);

            callAttempt = callAttempt with { Status = CallAttemptStatus.Failed };
            await _repository.UpdateAsync(callAttempt, cancellationToken);
        }

        return callAttempt;
    }

    public async Task HandleGatherAsync(
        string callSid,
        string digits,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Gather received: CallSid={CallSid}, Digits={Digits}",
            callSid, digits);

        if (digits == "1")
        {
            var callAttempt = await FindByCallSidAsync(callSid, cancellationToken);

            callAttempt = callAttempt with
            {
                Status = CallAttemptStatus.Acknowledged,
                AcknowledgedAt = DateTime.UtcNow
            };

            await _repository.UpdateAsync(callAttempt, cancellationToken);

            _logger.LogInformation(
                "Alarm acknowledged: CallAttemptId={CallAttemptId}, CallSid={CallSid}",
                callAttempt.Id, callSid);
        }
    }

    public async Task HandleStatusCallbackAsync(
        string callSid,
        string callStatus,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Status callback: CallSid={CallSid}, Status={CallStatus}",
            callSid, callStatus);

        var callAttempt = await FindByCallSidAsync(callSid, cancellationToken);

        var newStatus = callStatus switch
        {
            "completed" => callAttempt.Status, // Keep existing status, don't auto-acknowledge
            "no-answer" => CallAttemptStatus.NoAnswer,
            "failed" => CallAttemptStatus.Failed,
            "busy" => CallAttemptStatus.Failed,
            _ => callAttempt.Status
        };

        if (newStatus != callAttempt.Status)
        {
            callAttempt = callAttempt with { Status = newStatus };
            await _repository.UpdateAsync(callAttempt, cancellationToken);
        }
    }

    private async Task<CallAttempt> FindByCallSidAsync(string callSid, CancellationToken cancellationToken)
    {
        var callAttempt = await _repository.GetByTwilioCallSidAsync(callSid, cancellationToken);

        if (callAttempt is null)
        {
            throw new Domain.Exceptions.CallAttemptNotFoundException(Guid.Empty);
        }

        return callAttempt;
    }
}
