using System.Net;
using System.Text.Json;
using RepoMonitor.Core.Abstractions;
using RepoMonitor.Core.Http;
using RepoMonitor.Core.Models;

namespace RepoMonitor.Core.Clients;

public class ForgejoPlatformClient : IGitPlatformClient
{
    private readonly ConditionalHttpClient _httpClient;

    public GitPlatform Platform => GitPlatform.Forgejo;
    public List<HttpResponseInfo> LastResponses { get; } = new();

    public ForgejoPlatformClient(ConditionalHttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<PollResult<IReadOnlyList<TrackedRepository>>> GetRepositoriesAsync(
        SourceConfig source,
        CancellationToken cancellationToken = default)
    {
        string baseUrl = GetBaseUrl(source.BaseUrl);
        Dictionary<string, string> headers = BuildHeaders(source);

        if (source.Mode == MonitoringMode.AccountRepositories)
        {
            if (string.IsNullOrWhiteSpace(source.AccessToken))
            {
                return PollResult<IReadOnlyList<TrackedRepository>>.Fail(
                    RepositoryStatus.Unauthorized,
                    "Access token is required");
            }

            string listUrl = $"{baseUrl}/api/v1/user/repos?limit=50";
            HttpResponseInfo listResp = await _httpClient.GetAsync(listUrl, headers, cancellationToken);
            if (listResp.IsError)
            {
                return PollResult<IReadOnlyList<TrackedRepository>>.Fail(
                    listResp.GetStatus(),
                    listResp.GetErrorMessage(),
                    listResp.RateLimitRemaining,
                    listResp.RateLimitReset);
            }

            List<TrackedRepository> repos = new();
            if (!listResp.IsNotModified && !string.IsNullOrEmpty(listResp.Body))
            {
                try
                {
                    using JsonDocument doc = JsonDocument.Parse(listResp.Body);
                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (JsonElement el in doc.RootElement.EnumerateArray())
                        {
                            string fullName = el.TryGetProperty("full_name", out JsonElement fnEl)
                                ? fnEl.GetString() ?? string.Empty
                                : string.Empty;
                            string shortName = el.TryGetProperty("name", out JsonElement nEl)
                                ? nEl.GetString() ?? fullName
                                : fullName;

                            if (string.IsNullOrEmpty(fullName))
                            {
                                continue;
                            }

                            TrackedRepository item = new TrackedRepository
                            {
                                Id = $"forgejo:{baseUrl}:{fullName}",
                                SourceId = source.Id,
                                Platform = GitPlatform.Forgejo,
                                BaseUrl = baseUrl,
                                FullName = fullName,
                                ShortName = shortName,
                                Status = RepositoryStatus.Active
                            };
                            ApplyMeta(item, listResp);
                            repos.Add(item);
                        }
                    }
                }
                catch (JsonException ex)
                {
                    return PollResult<IReadOnlyList<TrackedRepository>>.Fail(RepositoryStatus.NetworkError, ex.Message);
                }
            }

            return PollResult<IReadOnlyList<TrackedRepository>>.Ok(
                repos,
                listResp.IsNotModified,
                listResp.RateLimitRemaining,
                listResp.RateLimitReset);
        }

        string repoPath = NormalizeRepoPath(source.RepositoryPath);
        string repoShort = repoPath.Contains('/') ? repoPath.Split('/').Last() : repoPath;

        TrackedRepository repo = new TrackedRepository
        {
            Id = $"forgejo:{baseUrl}:{repoPath}",
            SourceId = source.Id,
            Platform = GitPlatform.Forgejo,
            BaseUrl = baseUrl,
            FullName = repoPath,
            ShortName = repoShort,
            Status = RepositoryStatus.Active
        };

        string url = $"{baseUrl}/api/v1/repos/{repoPath}";
        HttpResponseInfo response = await _httpClient.GetAsync(url, headers, cancellationToken);
        ApplyMeta(repo, response);

        if (response.IsError)
        {
            return PollResult<IReadOnlyList<TrackedRepository>>.Fail(
                repo.Status,
                repo.StatusMessage ?? "Request failed",
                response.RateLimitRemaining,
                response.RateLimitReset);
        }

        if (!response.IsNotModified && !string.IsNullOrEmpty(response.Body))
        {
            try
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
            catch (JsonException ex)
            {
                return PollResult<IReadOnlyList<TrackedRepository>>.Fail(RepositoryStatus.NetworkError, ex.Message);
            }
        }

        return PollResult<IReadOnlyList<TrackedRepository>>.Ok(
            new List<TrackedRepository> { repo },
            response.IsNotModified,
            response.RateLimitRemaining,
            response.RateLimitReset);
    }

    public async Task<PollResult<IReadOnlyList<ActivityItem>>> GetActivitiesAsync(
        SourceConfig source,
        TrackedRepository repository,
        CancellationToken cancellationToken = default)
    {
        LastResponses.Clear();
        List<ActivityItem> result = new();
        string baseUrl = GetBaseUrl(string.IsNullOrEmpty(repository.BaseUrl) ? source.BaseUrl : repository.BaseUrl);
        Dictionary<string, string> headers = BuildHeaders(source);

        string commitsUrl = $"{baseUrl}/api/v1/repos/{repository.FullName}/commits?limit=5";
        HttpResponseInfo commitsResp = await _httpClient.GetAsync(commitsUrl, headers, cancellationToken);
        LastResponses.Add(commitsResp);

        if (commitsResp.StatusCode == HttpStatusCode.Conflict)
        {
            repository.LastSyncedAt = DateTimeOffset.Now;
            repository.Status = RepositoryStatus.Active;
            repository.StatusMessage = null;
        }
        else
        {
            ApplyMeta(repository, commitsResp);
            if (commitsResp.IsError)
            {
                return PollResult<IReadOnlyList<ActivityItem>>.Fail(
                    repository.Status,
                    repository.StatusMessage ?? "Commits error",
                    commitsResp.RateLimitRemaining,
                    commitsResp.RateLimitReset);
            }

            if (!commitsResp.IsNotModified && !string.IsNullOrEmpty(commitsResp.Body))
            {
                try
                {
                    ParseCommits(commitsResp.Body, repository, result);
                }
                catch (JsonException ex)
                {
                    return PollResult<IReadOnlyList<ActivityItem>>.Fail(RepositoryStatus.NetworkError, ex.Message);
                }
            }
        }

        string pullsUrl = $"{baseUrl}/api/v1/repos/{repository.FullName}/pulls?state=all&limit=5";
        HttpResponseInfo pullsResp = await _httpClient.GetAsync(pullsUrl, headers, cancellationToken);
        LastResponses.Add(pullsResp);
        ApplyMeta(repository, pullsResp);
        if (pullsResp.IsError)
        {
            return PollResult<IReadOnlyList<ActivityItem>>.Fail(
                repository.Status,
                repository.StatusMessage ?? "Pulls error",
                pullsResp.RateLimitRemaining,
                pullsResp.RateLimitReset);
        }

        if (!pullsResp.IsNotModified && !string.IsNullOrEmpty(pullsResp.Body))
        {
            try
            {
                ParsePullRequests(pullsResp.Body, repository, result);
            }
            catch (JsonException ex)
            {
                return PollResult<IReadOnlyList<ActivityItem>>.Fail(RepositoryStatus.NetworkError, ex.Message);
            }
        }

        string issuesUrl = $"{baseUrl}/api/v1/repos/{repository.FullName}/issues?state=all&type=issues&limit=5";
        HttpResponseInfo issuesResp = await _httpClient.GetAsync(issuesUrl, headers, cancellationToken);
        LastResponses.Add(issuesResp);
        ApplyMeta(repository, issuesResp);
        if (issuesResp.IsError)
        {
            return PollResult<IReadOnlyList<ActivityItem>>.Fail(
                repository.Status,
                repository.StatusMessage ?? "Issues error",
                issuesResp.RateLimitRemaining,
                issuesResp.RateLimitReset);
        }

        if (!issuesResp.IsNotModified && !string.IsNullOrEmpty(issuesResp.Body))
        {
            try
            {
                ParseIssues(issuesResp.Body, repository, result);
            }
            catch (JsonException ex)
            {
                return PollResult<IReadOnlyList<ActivityItem>>.Fail(RepositoryStatus.NetworkError, ex.Message);
            }
        }

        bool allNotModified = commitsResp.IsNotModified && pullsResp.IsNotModified && issuesResp.IsNotModified;
        return PollResult<IReadOnlyList<ActivityItem>>.Ok(
            result,
            allNotModified,
            repository.RateLimitRemaining,
            repository.RateLimitResetAt);
    }

    private static Dictionary<string, string> BuildHeaders(SourceConfig source)
    {
        Dictionary<string, string> headers = new();
        if (!string.IsNullOrWhiteSpace(source.AccessToken))
        {
            headers["Authorization"] = $"token {source.AccessToken.Trim()}";
        }

        return headers;
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

            if (el.TryGetProperty("created", out JsonElement createdEl) &&
                DateTimeOffset.TryParse(createdEl.GetString(), out DateTimeOffset createdDate))
            {
                timestamp = createdDate.ToLocalTime();
            }

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
                Platform = GitPlatform.Forgejo,
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
            string number = el.TryGetProperty("number", out JsonElement numEl) && numEl.TryGetInt32(out int num)
                ? num.ToString()
                : string.Empty;
            string title = el.TryGetProperty("title", out JsonElement titleEl)
                ? GetFirstLine(titleEl.GetString() ?? string.Empty)
                : string.Empty;
            string url = el.TryGetProperty("html_url", out JsonElement urlEl)
                ? urlEl.GetString() ?? string.Empty
                : string.Empty;
            string author = ReadUserLogin(el);

            DateTimeOffset timestamp = DateTimeOffset.Now;
            if (el.TryGetProperty("updated_at", out JsonElement dateEl) &&
                DateTimeOffset.TryParse(dateEl.GetString(), out DateTimeOffset parsedDate))
            {
                timestamp = parsedDate.ToLocalTime();
            }

            items.Add(new ActivityItem
            {
                Platform = GitPlatform.Forgejo,
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
            if (el.TryGetProperty("pull_request", out JsonElement prEl) && prEl.ValueKind != JsonValueKind.Null)
            {
                continue;
            }

            string number = el.TryGetProperty("number", out JsonElement numEl) && numEl.TryGetInt32(out int num)
                ? num.ToString()
                : string.Empty;
            string title = el.TryGetProperty("title", out JsonElement titleEl)
                ? GetFirstLine(titleEl.GetString() ?? string.Empty)
                : string.Empty;
            string url = el.TryGetProperty("html_url", out JsonElement urlEl)
                ? urlEl.GetString() ?? string.Empty
                : string.Empty;
            string author = ReadUserLogin(el);

            DateTimeOffset timestamp = DateTimeOffset.Now;
            if (el.TryGetProperty("updated_at", out JsonElement dateEl) &&
                DateTimeOffset.TryParse(dateEl.GetString(), out DateTimeOffset parsedDate))
            {
                timestamp = parsedDate.ToLocalTime();
            }

            items.Add(new ActivityItem
            {
                Platform = GitPlatform.Forgejo,
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

    private static string ReadUserLogin(JsonElement el)
    {
        if (el.TryGetProperty("user", out JsonElement userEl) &&
            userEl.ValueKind == JsonValueKind.Object)
        {
            if (userEl.TryGetProperty("login", out JsonElement loginEl))
            {
                return loginEl.GetString() ?? "unknown";
            }
            if (userEl.TryGetProperty("username", out JsonElement userNameEl))
            {
                return userNameEl.GetString() ?? "unknown";
            }
        }

        return "unknown";
    }

    private static void ApplyMeta(TrackedRepository repo, HttpResponseInfo response)
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

        repo.Status = response.GetStatus();
        repo.StatusMessage = repo.Status == RepositoryStatus.Active ? null : response.GetErrorMessage();
    }

    private static string GetBaseUrl(string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return "https://codeberg.org";
        }

        string text = baseUrl.Trim().TrimEnd('/');
        if (text.EndsWith("/api/v1", StringComparison.OrdinalIgnoreCase))
        {
            text = text.Substring(0, text.Length - 7);
        }

        return text.TrimEnd('/');
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
