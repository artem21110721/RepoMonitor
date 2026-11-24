namespace RepoMonitor.Core.Models;

public record ActivityItem
{
    public GitPlatform Platform { get; init; }
    public string RepositoryFullName { get; init; } = string.Empty;
    public string RepositoryShortName { get; init; } = string.Empty;
    public ActivityType Type { get; init; }
    public string ExternalId { get; init; } = string.Empty;
    public string Author { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public DateTimeOffset Timestamp { get; init; }
    public string Url { get; init; } = string.Empty;
}
