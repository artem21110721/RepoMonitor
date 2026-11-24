namespace RepoMonitor.Core.Models;

public class TrackedRepository
{
    public string Id { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public GitPlatform Platform { get; set; }
    public string BaseUrl { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string ShortName { get; set; } = string.Empty;
    public RepositoryStatus Status { get; set; } = RepositoryStatus.Active;
    public string? StatusMessage { get; set; }
    public DateTimeOffset? LastSyncedAt { get; set; }
    public int? RateLimitRemaining { get; set; }
    public DateTimeOffset? RateLimitResetAt { get; set; }
    public bool HasUnread { get; set; }
}
