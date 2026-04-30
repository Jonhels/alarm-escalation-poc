namespace Domain.CallAttempts.Models;

public enum CallAttemptStatus
{
    Pending,
    InProgress,
    Acknowledged,
    Failed,
    NoAnswer
}
