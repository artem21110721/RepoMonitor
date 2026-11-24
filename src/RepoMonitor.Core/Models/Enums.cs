namespace RepoMonitor.Core.Models;

public enum GitPlatform
{
    GitHub,
    GitLab,
    Forgejo
}

public enum MonitoringMode
{
    AccountRepositories,
    PrivateRepository,
    PublicRepository
}

public enum RepositoryStatus
{
    Active,
    Syncing,
    Updated,
    RateLimited,
    Unauthorized,
    NotFound,
    NetworkError
}

public enum ActivityType
{
    Commit,
    PullRequest,
    Issue
}
