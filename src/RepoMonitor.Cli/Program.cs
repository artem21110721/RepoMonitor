using System.Text;
using RepoMonitor.Core.Abstractions;
using RepoMonitor.Core.Clients;
using RepoMonitor.Core.Http;
using RepoMonitor.Core.Models;
using RepoMonitor.Core.Services;

string tempConfigPath = Path.Combine(Path.GetTempPath(), "repomonitor-cli-test.json");
ConfigurationRepository configRepo = new ConfigurationRepository(new DemoSecretProtector(), tempConfigPath);

AppConfig testConfig = new AppConfig
{
    PollIntervalSeconds = 60,
    Sources = new List<SourceConfig>
    {
        new SourceConfig
        {
            Platform = GitPlatform.GitHub,
            Mode = MonitoringMode.PublicRepository,
            BaseUrl = "https://api.github.com",
            RepositoryPath = "https://github.com/microsoft/calculator",
            DisplayName = "Calculator"
        },
        new SourceConfig
        {
            Platform = GitPlatform.GitLab,
            Mode = MonitoringMode.PublicRepository,
            BaseUrl = "https://gitlab.com",
            RepositoryPath = "https://gitlab.com/inkscape/inkscape",
            DisplayName = "Inkscape"
        },
        new SourceConfig
        {
            Platform = GitPlatform.Forgejo,
            Mode = MonitoringMode.PublicRepository,
            BaseUrl = "https://codeberg.org",
            RepositoryPath = "https://codeberg.org/forgejo/forgejo",
            DisplayName = "Forgejo"
        }
    }
};

configRepo.Save(testConfig);
AppConfig loadedConfig = configRepo.Load();
Console.WriteLine($"Конфигурация сохранена и загружена: источников = {loadedConfig.Sources.Count}");
if (File.Exists(tempConfigPath))
{
    File.Delete(tempConfigPath);
}

using HttpClient httpClient = new HttpClient
{
    Timeout = TimeSpan.FromSeconds(15)
};
ETagCache etagCache = new ETagCache();
ConditionalHttpClient conditionalHttp = new ConditionalHttpClient(httpClient, etagCache);

IGitPlatformClient[] clients =
{
    new GitHubPlatformClient(conditionalHttp),
    new GitLabPlatformClient(conditionalHttp),
    new ForgejoPlatformClient(conditionalHttp)
};

ActivityAggregator aggregator = new ActivityAggregator(50);
using RepositoryMonitorEngine engine = new RepositoryMonitorEngine(clients, aggregator);

int syncStarts = 0;
EventHandler<bool> onSyncState = (_, isSyncing) =>
{
    if (isSyncing)
    {
        syncStarts++;
    }
    Console.WriteLine($"[Engine] Syncing = {isSyncing}");
};

EventHandler<RepositoryStatusChangedEventArgs> onStatusChanged = (_, args) =>
{
    string msg = string.IsNullOrEmpty(args.Repository.StatusMessage) ? "" : $" ({args.Repository.StatusMessage})";
    Console.WriteLine($"[Status] {args.Repository.Platform} {args.Repository.FullName}: {args.Repository.Status}{msg}");
};

EventHandler<ActivitiesAddedEventArgs> onActivitiesAdded = (_, args) =>
{
    Console.WriteLine($"[Added] {args.Repository.Platform} {args.Repository.ShortName}: +{args.Items.Count}");
};

engine.SyncStateChanged += onSyncState;
engine.RepositoryStatusChanged += onStatusChanged;
engine.ActivitiesAdded += onActivitiesAdded;

engine.Configure(loadedConfig.Sources, TimeSpan.FromSeconds(loadedConfig.PollIntervalSeconds));

Console.WriteLine();
Console.WriteLine("Проход 1 (два параллельных TriggerRefreshAsync для проверки SemaphoreSlim):");
await Task.WhenAll(engine.TriggerRefreshAsync(), engine.TriggerRefreshAsync());
Console.WriteLine($"Фактических запусков цикла за параллельный вызов: {syncStarts}");

Console.WriteLine();
Console.WriteLine("Проход 2 (повторный опрос, проверка ETag и дедупликации):");
await engine.TriggerRefreshAsync();

Console.WriteLine();
Console.WriteLine("Итоговые статусы репозиториев:");
foreach (TrackedRepository repo in engine.GetRepositories())
{
    Console.WriteLine($"{repo.Platform} | {repo.FullName} | {repo.Status} | Limit: {repo.RateLimitRemaining?.ToString() ?? "n/a"}");
}

Console.WriteLine();
Console.WriteLine("Последние 15 событий со всех платформ:");
foreach (ActivityItem item in engine.GetActivities().Take(15))
{
    Console.WriteLine($"{item.Timestamp:dd.MM HH:mm} [{item.Platform}] {item.RepositoryShortName} {item.Type} {item.ExternalId} {item.Author}: {item.Title}");
}

engine.SyncStateChanged -= onSyncState;
engine.RepositoryStatusChanged -= onStatusChanged;
engine.ActivitiesAdded -= onActivitiesAdded;

class DemoSecretProtector : ISecretProtector
{
    public string Protect(string plainText)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(plainText));
    }

    public string Unprotect(string protectedText)
    {
        return Encoding.UTF8.GetString(Convert.FromBase64String(protectedText));
    }
}
