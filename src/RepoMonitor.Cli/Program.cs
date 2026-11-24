using RepoMonitor.Cli;
using RepoMonitor.Core.Abstractions;
using RepoMonitor.Core.Collections;
using RepoMonitor.Core.Models;
using RepoMonitor.Core.Services;

Console.WriteLine("Кольцевой буфер (емкость 4):");
var ring = new BoundedRingBuffer<int>(4);
for (int i = 1; i <= 6; i++)
{
    ring.Add(i);
    Console.WriteLine($"Добавил {i}: {string.Join(", ", ring)} (всего {ring.Count})");
}

Console.WriteLine();
Console.WriteLine("Проверка агрегатора:");

IGitPlatformClient client = new MockGitPlatformClient();
var aggregator = new ActivityAggregator(50);

var source = new SourceConfig
{
    Platform = GitPlatform.GitHub,
    Mode = MonitoringMode.AccountRepositories,
    BaseUrl = "https://api.github.com",
    DisplayName = "Test GitHub"
};

var repos = await client.GetRepositoriesAsync(source);

int firstCount = 0;
foreach (var repo in repos)
{
    var items = await client.GetActivitiesAsync(source, repo);
    firstCount += aggregator.AddRange(items).Count;
}

int secondCount = 0;
foreach (var repo in repos)
{
    var items = await client.GetActivitiesAsync(source, repo);
    secondCount += aggregator.AddRange(items).Count;
}

Console.WriteLine($"Первый опрос, новых: {firstCount}");
Console.WriteLine($"Повторный опрос, новых: {secondCount}");
Console.WriteLine();
Console.WriteLine("События:");

foreach (var item in aggregator.GetAllSorted())
{
    Console.WriteLine($"{item.Timestamp:HH:mm} {item.Platform} {item.RepositoryShortName} {item.Type} {item.ExternalId} {item.Author}: {item.Title}");
}
