using RepoMonitor.Core.Models;

namespace RepoMonitor.Core.Services;

public readonly struct ActivityKey : IEquatable<ActivityKey>
{
    public GitPlatform Platform { get; }
    public string RepositoryFullName { get; }
    public ActivityType Type { get; }
    public string ExternalId { get; }

    public ActivityKey(ActivityItem item)
    {
        Platform = item.Platform;
        RepositoryFullName = item.RepositoryFullName;
        Type = item.Type;
        ExternalId = item.ExternalId;
    }

    public bool Equals(ActivityKey other)
    {
        return Platform == other.Platform
            && Type == other.Type
            && StringComparer.OrdinalIgnoreCase.Equals(RepositoryFullName, other.RepositoryFullName)
            && StringComparer.OrdinalIgnoreCase.Equals(ExternalId, other.ExternalId);
    }

    public override bool Equals(object? obj)
    {
        return obj is ActivityKey other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(
            Platform,
            Type,
            StringComparer.OrdinalIgnoreCase.GetHashCode(RepositoryFullName),
            StringComparer.OrdinalIgnoreCase.GetHashCode(ExternalId));
    }
}
