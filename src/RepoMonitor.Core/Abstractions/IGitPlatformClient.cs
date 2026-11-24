using RepoMonitor.Core.Models;

namespace RepoMonitor.Core.Abstractions;

public interface IGitPlatformClient
{
    GitPlatform Platform { get; }
    Task<IReadOnlyList<TrackedRepository>> GetRepositoriesAsync(SourceConfig source, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ActivityItem>> GetActivitiesAsync(SourceConfig source, TrackedRepository repository, CancellationToken cancellationToken = default);
}
