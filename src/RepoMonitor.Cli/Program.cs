using RepoMonitor.Core.Clients;
using RepoMonitor.Core.Http;
using RepoMonitor.Core.Models;
using RepoMonitor.Core.Services;

using HttpClient httpClient = new HttpClient();
ETagCache cache = new ETagCache();
ConditionalHttpClient conditionalHttp = new ConditionalHttpClient(httpClient, cache);
GitHubPlatformClient client = new GitHubPlatformClient(conditionalHttp);
ActivityAggregator aggregator = new ActivityAggregator(50);

SourceConfig source = new SourceConfig
{
    Platform = GitPlatform.GitHub,
    Mode = MonitoringMode.PublicRepository,
    BaseUrl = "https://api.github.com",
    RepositoryPath = "https://github.com/microsoft/calculator",
    DisplayName = "Calculator"
};

PollResult<IReadOnlyList<TrackedRepository>> reposResult = await client.GetRepositoriesAsync(source);
if (!reposResult.IsSuccess || reposResult.Data == null || reposResult.Data.Count == 0)
{
    Console.WriteLine($"Ошибка: {reposResult.Status} {reposResult.ErrorMessage}");
    return;
}

TrackedRepository repo = reposResult.Data[0];
Console.WriteLine($"Репозиторий: {repo.FullName}, статус: {repo.Status}");

PollResult<IReadOnlyList<ActivityItem>> activitiesResult = await client.GetActivitiesAsync(source, repo);
if (!activitiesResult.IsSuccess || activitiesResult.Data == null)
{
    Console.WriteLine($"Ошибка опроса: {activitiesResult.Status} {activitiesResult.ErrorMessage}");
    return;
}

aggregator.AddRange(activitiesResult.Data);
foreach (ActivityItem item in aggregator.GetAllSorted().Take(5))
{
    Console.WriteLine($"{item.Timestamp:dd.MM HH:mm} {item.Platform} {item.RepositoryShortName} {item.Type} {item.ExternalId} {item.Author}: {item.Title}");
}
