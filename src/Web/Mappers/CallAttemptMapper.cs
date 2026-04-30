using Domain.CallAttempts.Models;
using Web.ViewModels;

namespace Web.Mappers;

public static class CallAttemptMapper
{
    public static CallAttemptViewModel ToViewModel(this CallAttempt callAttempt)
    {
        return new CallAttemptViewModel
        {
            Id = callAttempt.Id,
            PhoneNumber = callAttempt.PhoneNumber,
            AlarmMessage = callAttempt.AlarmMessage,
            Status = callAttempt.Status.ToString(),
            CreatedAt = callAttempt.CreatedAt,
            AcknowledgedAt = callAttempt.AcknowledgedAt,
            TwilioCallSid = callAttempt.TwilioCallSid
        };
    }
}
