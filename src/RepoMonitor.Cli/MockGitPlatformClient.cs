using RepoMonitor.Core.Abstractions;
using RepoMonitor.Core.Models;

namespace RepoMonitor.Cli;

public class MockGitPlatformClient : IGitPlatformClient
{
    public GitPlatform Platform => GitPlatform.GitHub;

    public Task<PollResult<IReadOnlyList<TrackedRepository>>> GetRepositoriesAsync(
        SourceConfig source,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<TrackedRepository> repos = new List<TrackedRepository>
        {
            new TrackedRepository
            {
                Id = "github:org/backend-api",
                SourceId = source.Id,
                Platform = GitPlatform.GitHub,
                BaseUrl = "https://api.github.com",
                FullName = "org/backend-api",
                ShortName = "backend-api",
                Status = RepositoryStatus.Active
            }
        };

        return Task.FromResult(PollResult<IReadOnlyList<TrackedRepository>>.Ok(repos));
    }

    public Task<PollResult<IReadOnlyList<ActivityItem>>> GetActivitiesAsync(
        SourceConfig source,
        TrackedRepository repository,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ActivityItem> items = new List<ActivityItem>
        {
            new ActivityItem
            {
                Platform = GitPlatform.GitHub,
                RepositoryFullName = repository.FullName,
                RepositoryShortName = repository.ShortName,
                Type = ActivityType.Commit,
                ExternalId = "a1b2c3d",
                Author = "ivan",
                Title = "Add user authentication endpoint",
                Timestamp = DateTimeOffset.Now.AddMinutes(-10),
                Url = "https://github.com/org/backend-api/commit/a1b2c3d"
            }
        };

        return Task.FromResult(PollResult<IReadOnlyList<ActivityItem>>.Ok(items));
    }
}
