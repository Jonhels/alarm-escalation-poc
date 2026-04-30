namespace Domain.Exceptions;

public class CallAttemptNotFoundException : Exception
{
    public CallAttemptNotFoundException(Guid callAttemptId, Exception? innerException = null)
        : base($"Call attempt with id {callAttemptId} was not found", innerException)
    {
    }
}
