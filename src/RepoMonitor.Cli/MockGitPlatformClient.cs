using RepoMonitor.Core.Abstractions;
using RepoMonitor.Core.Models;

namespace RepoMonitor.Cli;

public class MockGitPlatformClient : IGitPlatformClient
{
    public GitPlatform Platform => GitPlatform.GitHub;

    public Task<IReadOnlyList<TrackedRepository>> GetRepositoriesAsync(
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
            },
            new TrackedRepository
            {
                Id = "github:org/web-client",
                SourceId = source.Id,
                Platform = GitPlatform.GitHub,
                BaseUrl = "https://api.github.com",
                FullName = "org/web-client",
                ShortName = "web-client",
                Status = RepositoryStatus.Active
            }
        };

        return Task.FromResult(repos);
    }

    public Task<IReadOnlyList<ActivityItem>> GetActivitiesAsync(
        SourceConfig source,
        TrackedRepository repository,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = DateTimeOffset.Now;

        if (repository.FullName == "org/backend-api")
        {
            IReadOnlyList<ActivityItem> backendItems = new List<ActivityItem>
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
                    Timestamp = now.AddMinutes(-20),
                    Url = "https://github.com/org/backend-api/commit/a1b2c3d"
                },
                new ActivityItem
                {
                    Platform = GitPlatform.GitHub,
                    RepositoryFullName = repository.FullName,
                    RepositoryShortName = repository.ShortName,
                    Type = ActivityType.PullRequest,
                    ExternalId = "42",
                    Author = "olga",
                    Title = "Fix token refresh timeout",
                    Timestamp = now.AddMinutes(-5),
                    Url = "https://github.com/org/backend-api/pull/42"
                },
                new ActivityItem
                {
                    Platform = GitPlatform.GitHub,
                    RepositoryFullName = repository.FullName,
                    RepositoryShortName = repository.ShortName,
                    Type = ActivityType.Commit,
                    ExternalId = "a1b2c3d",
                    Author = "ivan",
                    Title = "Add user authentication endpoint",
                    Timestamp = now.AddMinutes(-20),
                    Url = "https://github.com/org/backend-api/commit/a1b2c3d"
                }
            };

            return Task.FromResult(backendItems);
        }

        IReadOnlyList<ActivityItem> clientItems = new List<ActivityItem>
        {
            new ActivityItem
            {
                Platform = GitPlatform.GitHub,
                RepositoryFullName = repository.FullName,
                RepositoryShortName = repository.ShortName,
                Type = ActivityType.Issue,
                ExternalId = "15",
                Author = "dmitry",
                Title = "Layout breaks on narrow screens",
                Timestamp = now.AddMinutes(-10),
                Url = "https://github.com/org/web-client/issues/15"
            }
        };

        return Task.FromResult(clientItems);
    }
}
