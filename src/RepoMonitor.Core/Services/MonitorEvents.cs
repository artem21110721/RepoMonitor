using RepoMonitor.Core.Models;

namespace RepoMonitor.Core.Services;

public class RepositoryStatusChangedEventArgs : EventArgs
{
    public TrackedRepository Repository { get; }

    public RepositoryStatusChangedEventArgs(TrackedRepository repository)
    {
        Repository = repository;
    }
}

public class ActivitiesAddedEventArgs : EventArgs
{
    public TrackedRepository Repository { get; }
    public IReadOnlyList<ActivityItem> Items { get; }

    public ActivitiesAddedEventArgs(TrackedRepository repository, IReadOnlyList<ActivityItem> items)
    {
        Repository = repository;
        Items = items;
    }
}
