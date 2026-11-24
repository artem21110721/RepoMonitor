using RepoMonitor.Core.Collections;
using RepoMonitor.Core.Models;

namespace RepoMonitor.Core.Services;

public class ActivityAggregator
{
    private readonly int _capacityPerRepo;
    private readonly Dictionary<string, BoundedRingBuffer<ActivityItem>> _buffers = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<ActivityKey> _seenKeys = new();

    public ActivityAggregator(int capacityPerRepo = 50)
    {
        if (capacityPerRepo <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacityPerRepo));
        }

        _capacityPerRepo = capacityPerRepo;
    }

    public IReadOnlyList<ActivityItem> AddRange(IEnumerable<ActivityItem> items)
    {
        List<ActivityItem> added = new();

        foreach (ActivityItem item in items)
        {
            ActivityKey key = new ActivityKey(item);
            if (!_seenKeys.Add(key))
            {
                continue;
            }

            string repoKey = BuildRepoKey(item.Platform, item.RepositoryFullName);
            if (!_buffers.TryGetValue(repoKey, out BoundedRingBuffer<ActivityItem>? buffer))
            {
                buffer = new BoundedRingBuffer<ActivityItem>(_capacityPerRepo);
                _buffers[repoKey] = buffer;
            }

            buffer.Add(item);
            added.Add(item);
        }

        return added;
    }

    public IReadOnlyList<ActivityItem> GetAllSorted()
    {
        return _buffers.Values
            .SelectMany(buffer => buffer)
            .OrderByDescending(item => item.Timestamp)
            .ToList();
    }

    public IReadOnlyList<ActivityItem> GetByRepository(GitPlatform platform, string repositoryFullName)
    {
        string repoKey = BuildRepoKey(platform, repositoryFullName);
        if (!_buffers.TryGetValue(repoKey, out BoundedRingBuffer<ActivityItem>? buffer))
        {
            return Array.Empty<ActivityItem>();
        }

        return buffer
            .OrderByDescending(item => item.Timestamp)
            .ToList();
    }

    private static string BuildRepoKey(GitPlatform platform, string repositoryFullName)
    {
        return $"{platform}:{repositoryFullName}";
    }
}
