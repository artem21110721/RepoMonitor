namespace RepoMonitor.Core.Models;

public class PollResult<T>
{
    public bool IsSuccess { get; set; }
    public bool IsNotModified { get; set; }
    public RepositoryStatus Status { get; set; } = RepositoryStatus.Active;
    public string? ErrorMessage { get; set; }
    public T? Data { get; set; }
    public int? RateLimitRemaining { get; set; }
    public DateTimeOffset? RateLimitResetAt { get; set; }

    public static PollResult<T> Ok(T data, bool isNotModified = false, int? remaining = null, DateTimeOffset? resetAt = null)
    {
        return new PollResult<T>
        {
            IsSuccess = true,
            IsNotModified = isNotModified,
            Data = data,
            RateLimitRemaining = remaining,
            RateLimitResetAt = resetAt
        };
    }

    public static PollResult<T> Fail(RepositoryStatus status, string error, int? remaining = null, DateTimeOffset? resetAt = null)
    {
        return new PollResult<T>
        {
            IsSuccess = false,
            Status = status,
            ErrorMessage = error,
            RateLimitRemaining = remaining,
            RateLimitResetAt = resetAt
        };
    }
}
