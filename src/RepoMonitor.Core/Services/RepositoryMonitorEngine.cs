using RepoMonitor.Core.Abstractions;
using RepoMonitor.Core.Models;

namespace RepoMonitor.Core.Services;

public class RepositoryMonitorEngine : IDisposable
{
    private readonly Dictionary<GitPlatform, IGitPlatformClient> _clients = new();
    private readonly ActivityAggregator _aggregator;
    private readonly SemaphoreSlim _pollLock = new(1, 1);
    private readonly object _stateLock = new();
    private readonly Dictionary<string, TrackedRepository> _repositories = new();

    private List<SourceConfig> _sources = new();
    private TimeSpan _pollInterval = TimeSpan.FromSeconds(60);
    private CancellationTokenSource? _loopCts;
    private bool _disposed;

    public event EventHandler<RepositoryStatusChangedEventArgs>? RepositoryStatusChanged;
    public event EventHandler<ActivitiesAddedEventArgs>? ActivitiesAdded;
    public event EventHandler<bool>? SyncStateChanged;

    public RepositoryMonitorEngine(IEnumerable<IGitPlatformClient> clients, ActivityAggregator aggregator)
    {
        foreach (IGitPlatformClient client in clients)
        {
            _clients[client.Platform] = client;
        }

        _aggregator = aggregator;
    }

    public void Configure(IEnumerable<SourceConfig> sources, TimeSpan pollInterval)
    {
        lock (_stateLock)
        {
            _sources = sources.ToList();
            _pollInterval = pollInterval < TimeSpan.FromSeconds(5)
                ? TimeSpan.FromSeconds(5)
                : pollInterval;

            var activeSourceIds = _sources.Select(s => s.Id).ToHashSet();
            List<string> toRemove = _repositories
                .Where(pair => !activeSourceIds.Contains(pair.Value.SourceId))
                .Select(pair => pair.Key)
                .ToList();

            foreach (string key in toRemove)
            {
                _repositories.Remove(key);
            }
        }
    }

    public IReadOnlyList<TrackedRepository> GetRepositories()
    {
        lock (_stateLock)
        {
            return _repositories.Values.ToList();
        }
    }

    public IReadOnlyList<ActivityItem> GetActivities()
    {
        lock (_stateLock)
        {
            return _aggregator.GetAllSorted().ToList();
        }
    }

    public void Start()
    {
        if (_disposed)
        {
            return;
        }

        Stop();
        _loopCts = new CancellationTokenSource();
        CancellationToken token = _loopCts.Token;
        _ = Task.Run(() => RunLoopAsync(token), token);
    }

    public void Stop()
    {
        if (_loopCts != null)
        {
            _loopCts.Cancel();
            _loopCts.Dispose();
            _loopCts = null;
        }
    }

    public async Task TriggerRefreshAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return;
        }

        bool entered = false;
        try
        {
            entered = await _pollLock.WaitAsync(0, cancellationToken);
            if (!entered)
            {
                return;
            }

            SyncStateChanged?.Invoke(this, true);
            await PollAllSourcesAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        finally
        {
            if (entered)
            {
                SyncStateChanged?.Invoke(this, false);
                try
                {
                    _pollLock.Release();
                }
                catch (ObjectDisposedException)
                {
                }
            }
        }
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        PeriodicTimer? timer = null;
        try
        {
            await TriggerRefreshAsync(cancellationToken);

            TimeSpan currentInterval = GetPollInterval();
            timer = new PeriodicTimer(currentInterval);

            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await TriggerRefreshAsync(cancellationToken);

                TimeSpan nextInterval = GetPollInterval();
                if (nextInterval != currentInterval)
                {
                    timer.Dispose();
                    currentInterval = nextInterval;
                    timer = new PeriodicTimer(currentInterval);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            timer?.Dispose();
        }
    }

    private TimeSpan GetPollInterval()
    {
        lock (_stateLock)
        {
            return _pollInterval;
        }
    }

    private async Task PollAllSourcesAsync(CancellationToken cancellationToken)
    {
        List<SourceConfig> sourcesSnapshot;
        lock (_stateLock)
        {
            sourcesSnapshot = _sources.ToList();
        }

        foreach (SourceConfig source in sourcesSnapshot)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            if (!_clients.TryGetValue(source.Platform, out IGitPlatformClient? client))
            {
                continue;
            }

            PollResult<IReadOnlyList<TrackedRepository>> reposResult = await client.GetRepositoriesAsync(source, cancellationToken);
            if (!IsSourceStillActive(source))
            {
                continue;
            }

            if (!reposResult.IsSuccess || reposResult.Data == null)
            {
                HandleSourceError(source, reposResult);
                continue;
            }

            List<TrackedRepository> reposToPoll = ResolveRepositories(source, reposResult);
            foreach (TrackedRepository repo in reposToPoll)
            {
                if (cancellationToken.IsCancellationRequested || !IsSourceStillActive(source))
                {
                    break;
                }

                repo.Status = RepositoryStatus.Syncing;
                SaveRepository(repo);
                RepositoryStatusChanged?.Invoke(this, new RepositoryStatusChangedEventArgs(repo));

                PollResult<IReadOnlyList<ActivityItem>> activitiesResult = await client.GetActivitiesAsync(source, repo, cancellationToken);
                if (!IsSourceStillActive(source))
                {
                    break;
                }

                if (activitiesResult.IsSuccess && activitiesResult.Data != null)
                {
                    IReadOnlyList<ActivityItem> added;
                    lock (_stateLock)
                    {
                        added = _aggregator.AddRange(activitiesResult.Data);
                    }

                    repo.Status = added.Count > 0 ? RepositoryStatus.Updated : RepositoryStatus.Active;
                    repo.StatusMessage = null;
                    SaveRepository(repo);

                    if (added.Count > 0)
                    {
                        ActivitiesAdded?.Invoke(this, new ActivitiesAddedEventArgs(repo, added));
                    }
                }
                else
                {
                    repo.Status = activitiesResult.Status;
                    repo.StatusMessage = activitiesResult.ErrorMessage;
                    SaveRepository(repo);
                }

                RepositoryStatusChanged?.Invoke(this, new RepositoryStatusChangedEventArgs(repo));
            }
        }
    }

    private List<TrackedRepository> ResolveRepositories(
        SourceConfig source,
        PollResult<IReadOnlyList<TrackedRepository>> reposResult)
    {
        lock (_stateLock)
        {
            if (reposResult.IsNotModified && (reposResult.Data == null || reposResult.Data.Count == 0))
            {
                return _repositories.Values.Where(r => r.SourceId.Equals(source.Id)).ToList();
            }

            List<TrackedRepository> result = new();
            foreach (TrackedRepository fresh in reposResult.Data!)
            {
                if (_repositories.TryGetValue(fresh.Id, out TrackedRepository? existing))
                {
                    existing.FullName = fresh.FullName;
                    existing.ShortName = fresh.ShortName;
                    existing.BaseUrl = fresh.BaseUrl;
                    existing.LastSyncedAt = fresh.LastSyncedAt;
                    existing.RateLimitRemaining = fresh.RateLimitRemaining ?? existing.RateLimitRemaining;
                    existing.RateLimitResetAt = fresh.RateLimitResetAt ?? existing.RateLimitResetAt;
                    result.Add(existing);
                }
                else
                {
                    _repositories[fresh.Id] = fresh;
                    result.Add(fresh);
                }
            }

            return result;
        }
    }

    private void HandleSourceError(SourceConfig source, PollResult<IReadOnlyList<TrackedRepository>> errorResult)
    {
        List<TrackedRepository> affected;
        lock (_stateLock)
        {
            affected = _repositories.Values.Where(r => r.SourceId.Equals(source.Id)).ToList();
            if (affected.Count == 0)
            {
                string name = string.IsNullOrWhiteSpace(source.DisplayName)
                    ? (string.IsNullOrWhiteSpace(source.RepositoryPath) ? source.Platform.ToString() : source.RepositoryPath)
                    : source.DisplayName;

                TrackedRepository placeholder = new TrackedRepository
                {
                    Id = $"source:{source.Id}",
                    SourceId = source.Id,
                    Platform = source.Platform,
                    BaseUrl = source.BaseUrl ?? string.Empty,
                    FullName = name,
                    ShortName = name
                };
                _repositories[placeholder.Id] = placeholder;
                affected.Add(placeholder);
            }

            foreach (TrackedRepository repo in affected)
            {
                repo.Status = errorResult.Status;
                repo.StatusMessage = errorResult.ErrorMessage;
                repo.LastSyncedAt = DateTimeOffset.Now;
                if (errorResult.RateLimitRemaining.HasValue)
                {
                    repo.RateLimitRemaining = errorResult.RateLimitRemaining;
                }
                if (errorResult.RateLimitResetAt.HasValue)
                {
                    repo.RateLimitResetAt = errorResult.RateLimitResetAt;
                }
            }
        }

        foreach (TrackedRepository repo in affected)
        {
            RepositoryStatusChanged?.Invoke(this, new RepositoryStatusChangedEventArgs(repo));
        }
    }

    private bool IsSourceStillActive(SourceConfig source)
    {
        lock (_stateLock)
        {
            return _sources.Any(s => s.Id.Equals(source.Id));
        }
    }

    private void SaveRepository(TrackedRepository repo)
    {
        lock (_stateLock)
        {
            if (_sources.Any(s => s.Id.Equals(repo.SourceId)))
            {
                _repositories[repo.Id] = repo;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
        _pollLock.Dispose();
    }
}
