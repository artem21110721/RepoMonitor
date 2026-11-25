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

IReadOnlyList<TrackedRepository> repos = await client.GetRepositoriesAsync(source);
TrackedRepository repo = repos[0];

Console.WriteLine($"Репозиторий: {repo.FullName}, статус: {repo.Status}");
Console.WriteLine();

for (int pass = 1; pass <= 2; pass++)
{
    Console.WriteLine($"Опрос {pass}:");
    IReadOnlyList<ActivityItem> items = await client.GetActivitiesAsync(source, repo);
    IReadOnlyList<ActivityItem> added = aggregator.AddRange(items);

    foreach (HttpResponseInfo resp in client.LastResponses)
    {
        Console.WriteLine($"URL: {resp.Url}");
        Console.WriteLine($"Status: {(int)resp.StatusCode} {resp.StatusCode}");
        Console.WriteLine($"If-None-Match: {resp.SentETag ?? "none"}");
        Console.WriteLine($"ETag: {resp.ReceivedETag ?? "none"}");
        Console.WriteLine($"X-RateLimit-Remaining: {resp.RateLimitRemaining}");
        Console.WriteLine();
    }

    Console.WriteLine($"Новых событий: {added.Count}");
    Console.WriteLine();
}

Console.WriteLine("Последние события:");
foreach (ActivityItem item in aggregator.GetAllSorted().Take(10))
{
    Console.WriteLine($"{item.Timestamp:dd.MM HH:mm} {item.Platform} {item.RepositoryShortName} {item.Type} {item.ExternalId} {item.Author}: {item.Title}");
}
