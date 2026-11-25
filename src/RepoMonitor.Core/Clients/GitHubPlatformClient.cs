using System.Net;
using System.Text.Json;
using RepoMonitor.Core.Abstractions;
using RepoMonitor.Core.Http;
using RepoMonitor.Core.Models;

namespace RepoMonitor.Core.Clients;

public class GitHubPlatformClient : IGitPlatformClient
{
    private readonly ConditionalHttpClient _httpClient;

    public GitPlatform Platform => GitPlatform.GitHub;
    public List<HttpResponseInfo> LastResponses { get; } = new();

    public GitHubPlatformClient(ConditionalHttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<TrackedRepository>> GetRepositoriesAsync(
        SourceConfig source,
        CancellationToken cancellationToken = default)
    {
        string fullName = NormalizeRepoPath(source.RepositoryPath);
        string shortName = fullName.Contains('/') ? fullName.Split('/').Last() : fullName;
        string baseUrl = GetBaseUrl(source.BaseUrl);

        TrackedRepository repo = new TrackedRepository
        {
            Id = $"github:{fullName}",
            SourceId = source.Id,
            Platform = GitPlatform.GitHub,
            BaseUrl = baseUrl,
            FullName = fullName,
            ShortName = shortName,
            Status = RepositoryStatus.Active
        };

        string url = $"{baseUrl}/repos/{fullName}";
        HttpResponseInfo response = await _httpClient.GetAsync(url, cancellationToken);
        ApplyResponseMeta(repo, response);

        if (response.StatusCode == HttpStatusCode.OK && !string.IsNullOrEmpty(response.Body))
        {
            using JsonDocument doc = JsonDocument.Parse(response.Body);
            if (doc.RootElement.TryGetProperty("full_name", out JsonElement fullNameEl))
            {
                repo.FullName = fullNameEl.GetString() ?? repo.FullName;
            }
            if (doc.RootElement.TryGetProperty("name", out JsonElement nameEl))
            {
                repo.ShortName = nameEl.GetString() ?? repo.ShortName;
            }
        }

        return new List<TrackedRepository> { repo };
    }

    public async Task<IReadOnlyList<ActivityItem>> GetActivitiesAsync(
        SourceConfig source,
        TrackedRepository repository,
        CancellationToken cancellationToken = default)
    {
        LastResponses.Clear();
        List<ActivityItem> result = new();
        string baseUrl = GetBaseUrl(string.IsNullOrEmpty(repository.BaseUrl) ? source.BaseUrl : repository.BaseUrl);

        string commitsUrl = $"{baseUrl}/repos/{repository.FullName}/commits?per_page=5";
        HttpResponseInfo commitsResp = await _httpClient.GetAsync(commitsUrl, cancellationToken);
        LastResponses.Add(commitsResp);
        ApplyResponseMeta(repository, commitsResp);
        if (!commitsResp.IsNotModified && commitsResp.StatusCode == HttpStatusCode.OK && !string.IsNullOrEmpty(commitsResp.Body))
        {
            ParseCommits(commitsResp.Body, repository, result);
        }

        string pullsUrl = $"{baseUrl}/repos/{repository.FullName}/pulls?state=all&per_page=5";
        HttpResponseInfo pullsResp = await _httpClient.GetAsync(pullsUrl, cancellationToken);
        LastResponses.Add(pullsResp);
        ApplyResponseMeta(repository, pullsResp);
        if (!pullsResp.IsNotModified && pullsResp.StatusCode == HttpStatusCode.OK && !string.IsNullOrEmpty(pullsResp.Body))
        {
            ParsePullRequests(pullsResp.Body, repository, result);
        }

        string issuesUrl = $"{baseUrl}/repos/{repository.FullName}/issues?state=all&per_page=5";
        HttpResponseInfo issuesResp = await _httpClient.GetAsync(issuesUrl, cancellationToken);
        LastResponses.Add(issuesResp);
        ApplyResponseMeta(repository, issuesResp);
        if (!issuesResp.IsNotModified && issuesResp.StatusCode == HttpStatusCode.OK && !string.IsNullOrEmpty(issuesResp.Body))
        {
            ParseIssues(issuesResp.Body, repository, result);
        }

        return result;
    }

    private static void ParseCommits(string json, TrackedRepository repo, List<ActivityItem> items)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (JsonElement el in doc.RootElement.EnumerateArray())
        {
            string sha = el.TryGetProperty("sha", out JsonElement shaEl) ? shaEl.GetString() ?? string.Empty : string.Empty;
            string shortSha = sha.Length > 7 ? sha.Substring(0, 7) : sha;
            string url = el.TryGetProperty("html_url", out JsonElement urlEl) ? urlEl.GetString() ?? string.Empty : string.Empty;

            string author = "unknown";
            string title = string.Empty;
            DateTimeOffset timestamp = DateTimeOffset.Now;

            if (el.TryGetProperty("commit", out JsonElement commitEl))
            {
                if (commitEl.TryGetProperty("message", out JsonElement msgEl))
                {
                    title = GetFirstLine(msgEl.GetString() ?? string.Empty);
                }

                if (commitEl.TryGetProperty("author", out JsonElement commitAuthorEl))
                {
                    if (commitAuthorEl.TryGetProperty("name", out JsonElement nameEl))
                    {
                        author = nameEl.GetString() ?? author;
                    }

                    if (commitAuthorEl.TryGetProperty("date", out JsonElement dateEl) &&
                        DateTimeOffset.TryParse(dateEl.GetString(), out DateTimeOffset parsedDate))
                    {
                        timestamp = parsedDate.ToLocalTime();
                    }
                }
            }

            if (el.TryGetProperty("author", out JsonElement userEl) &&
                userEl.ValueKind == JsonValueKind.Object &&
                userEl.TryGetProperty("login", out JsonElement loginEl))
            {
                author = loginEl.GetString() ?? author;
            }

            items.Add(new ActivityItem
            {
                Platform = GitPlatform.GitHub,
                RepositoryFullName = repo.FullName,
                RepositoryShortName = repo.ShortName,
                Type = ActivityType.Commit,
                ExternalId = shortSha,
                Author = author,
                Title = title,
                Timestamp = timestamp,
                Url = url
            });
        }
    }

    private static void ParsePullRequests(string json, TrackedRepository repo, List<ActivityItem> items)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (JsonElement el in doc.RootElement.EnumerateArray())
        {
            string number = el.TryGetProperty("number", out JsonElement numEl) ? numEl.GetInt32().ToString() : string.Empty;
            string title = el.TryGetProperty("title", out JsonElement titleEl) ? GetFirstLine(titleEl.GetString() ?? string.Empty) : string.Empty;
            string url = el.TryGetProperty("html_url", out JsonElement urlEl) ? urlEl.GetString() ?? string.Empty : string.Empty;

            string author = "unknown";
            if (el.TryGetProperty("user", out JsonElement userEl) &&
                userEl.ValueKind == JsonValueKind.Object &&
                userEl.TryGetProperty("login", out JsonElement loginEl))
            {
                author = loginEl.GetString() ?? author;
            }

            DateTimeOffset timestamp = DateTimeOffset.Now;
            if (el.TryGetProperty("updated_at", out JsonElement dateEl) &&
                DateTimeOffset.TryParse(dateEl.GetString(), out DateTimeOffset parsedDate))
            {
                timestamp = parsedDate.ToLocalTime();
            }

            items.Add(new ActivityItem
            {
                Platform = GitPlatform.GitHub,
                RepositoryFullName = repo.FullName,
                RepositoryShortName = repo.ShortName,
                Type = ActivityType.PullRequest,
                ExternalId = number,
                Author = author,
                Title = title,
                Timestamp = timestamp,
                Url = url
            });
        }
    }

    private static void ParseIssues(string json, TrackedRepository repo, List<ActivityItem> items)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (JsonElement el in doc.RootElement.EnumerateArray())
        {
            if (el.TryGetProperty("pull_request", out _))
            {
                continue;
            }

            string number = el.TryGetProperty("number", out JsonElement numEl) ? numEl.GetInt32().ToString() : string.Empty;
            string title = el.TryGetProperty("title", out JsonElement titleEl) ? GetFirstLine(titleEl.GetString() ?? string.Empty) : string.Empty;
            string url = el.TryGetProperty("html_url", out JsonElement urlEl) ? urlEl.GetString() ?? string.Empty : string.Empty;

            string author = "unknown";
            if (el.TryGetProperty("user", out JsonElement userEl) &&
                userEl.ValueKind == JsonValueKind.Object &&
                userEl.TryGetProperty("login", out JsonElement loginEl))
            {
                author = loginEl.GetString() ?? author;
            }

            DateTimeOffset timestamp = DateTimeOffset.Now;
            if (el.TryGetProperty("updated_at", out JsonElement dateEl) &&
                DateTimeOffset.TryParse(dateEl.GetString(), out DateTimeOffset parsedDate))
            {
                timestamp = parsedDate.ToLocalTime();
            }

            items.Add(new ActivityItem
            {
                Platform = GitPlatform.GitHub,
                RepositoryFullName = repo.FullName,
                RepositoryShortName = repo.ShortName,
                Type = ActivityType.Issue,
                ExternalId = number,
                Author = author,
                Title = title,
                Timestamp = timestamp,
                Url = url
            });
        }
    }

    private static void ApplyResponseMeta(TrackedRepository repo, HttpResponseInfo response)
    {
        repo.LastSyncedAt = DateTimeOffset.Now;
        if (response.RateLimitRemaining.HasValue)
        {
            repo.RateLimitRemaining = response.RateLimitRemaining;
        }
        if (response.RateLimitReset.HasValue)
        {
            repo.RateLimitResetAt = response.RateLimitReset;
        }

        if (response.StatusCode == HttpStatusCode.OK || response.IsNotModified)
        {
            repo.Status = RepositoryStatus.Active;
            repo.StatusMessage = null;
        }
        else if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            repo.Status = RepositoryStatus.Unauthorized;
            repo.StatusMessage = "HTTP 401 Unauthorized";
        }
        else if (response.StatusCode == HttpStatusCode.NotFound)
        {
            repo.Status = RepositoryStatus.NotFound;
            repo.StatusMessage = "HTTP 404 Not Found";
        }
        else if (response.StatusCode == HttpStatusCode.TooManyRequests ||
                 (response.StatusCode == HttpStatusCode.Forbidden && response.RateLimitRemaining == 0))
        {
            repo.Status = RepositoryStatus.RateLimited;
            repo.StatusMessage = "Rate limit exceeded";
        }
        else
        {
            repo.Status = RepositoryStatus.NetworkError;
            repo.StatusMessage = $"HTTP {(int)response.StatusCode}";
        }
    }

    private static string GetBaseUrl(string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return "https://api.github.com";
        }

        return baseUrl.Trim().TrimEnd('/');
    }

    private static string NormalizeRepoPath(string path)
    {
        string text = path.Trim();
        int schemeIndex = text.IndexOf("://", StringComparison.Ordinal);
        if (schemeIndex >= 0)
        {
            text = text.Substring(schemeIndex + 3);
            int firstSlash = text.IndexOf('/');
            if (firstSlash >= 0)
            {
                text = text.Substring(firstSlash + 1);
            }
        }

        text = text.Trim('/');
        if (text.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            text = text.Substring(0, text.Length - 4);
        }

        return text.Trim('/');
    }

    private static string GetFirstLine(string text)
    {
        int index = text.IndexOfAny(new[] { '\r', '\n' });
        return index >= 0 ? text.Substring(0, index) : text;
    }
}
