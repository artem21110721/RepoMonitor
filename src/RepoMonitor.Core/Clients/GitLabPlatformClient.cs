using System.Text.Json;
using RepoMonitor.Core.Abstractions;
using RepoMonitor.Core.Http;
using RepoMonitor.Core.Models;

namespace RepoMonitor.Core.Clients;

public class GitLabPlatformClient : IGitPlatformClient
{
    private readonly ConditionalHttpClient _httpClient;

    public GitPlatform Platform => GitPlatform.GitLab;
    public List<HttpResponseInfo> LastResponses { get; } = new();

    public GitLabPlatformClient(ConditionalHttpClient httpClient)
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

            string listUrl = $"{baseUrl}/api/v4/projects?membership=true&order_by=last_activity_at&per_page=50";
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
                            string fullName = el.TryGetProperty("path_with_namespace", out JsonElement fnEl)
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
                                Id = $"gitlab:{baseUrl}:{fullName}",
                                SourceId = source.Id,
                                Platform = GitPlatform.GitLab,
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
        string encodedPath = Uri.EscapeDataString(repoPath);
        string repoShort = repoPath.Contains('/') ? repoPath.Split('/').Last() : repoPath;

        TrackedRepository repo = new TrackedRepository
        {
            Id = $"gitlab:{baseUrl}:{repoPath}",
            SourceId = source.Id,
            Platform = GitPlatform.GitLab,
            BaseUrl = baseUrl,
            FullName = repoPath,
            ShortName = repoShort,
            Status = RepositoryStatus.Active
        };

        string url = $"{baseUrl}/api/v4/projects/{encodedPath}";
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
                if (doc.RootElement.TryGetProperty("path_with_namespace", out JsonElement fullNameEl))
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
        string encodedPath = Uri.EscapeDataString(repository.FullName);
        Dictionary<string, string> headers = BuildHeaders(source);

        string commitsUrl = $"{baseUrl}/api/v4/projects/{encodedPath}/repository/commits?per_page=5";
        HttpResponseInfo commitsResp = await _httpClient.GetAsync(commitsUrl, headers, cancellationToken);
        LastResponses.Add(commitsResp);
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

        string mrUrl = $"{baseUrl}/api/v4/projects/{encodedPath}/merge_requests?state=all&per_page=5";
        HttpResponseInfo mrResp = await _httpClient.GetAsync(mrUrl, headers, cancellationToken);
        LastResponses.Add(mrResp);
        ApplyMeta(repository, mrResp);
        if (mrResp.IsError)
        {
            return PollResult<IReadOnlyList<ActivityItem>>.Fail(
                repository.Status,
                repository.StatusMessage ?? "Merge requests error",
                mrResp.RateLimitRemaining,
                mrResp.RateLimitReset);
        }

        if (!mrResp.IsNotModified && !string.IsNullOrEmpty(mrResp.Body))
        {
            try
            {
                ParseMergeRequests(mrResp.Body, repository, result);
            }
            catch (JsonException ex)
            {
                return PollResult<IReadOnlyList<ActivityItem>>.Fail(RepositoryStatus.NetworkError, ex.Message);
            }
        }

        string issuesUrl = $"{baseUrl}/api/v4/projects/{encodedPath}/issues?state=all&per_page=5";
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

        bool allNotModified = commitsResp.IsNotModified && mrResp.IsNotModified && issuesResp.IsNotModified;
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
            headers["PRIVATE-TOKEN"] = source.AccessToken.Trim();
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
            string id = el.TryGetProperty("short_id", out JsonElement shortEl)
                ? shortEl.GetString() ?? string.Empty
                : string.Empty;
            if (string.IsNullOrEmpty(id) && el.TryGetProperty("id", out JsonElement fullIdEl))
            {
                string fullId = fullIdEl.GetString() ?? string.Empty;
                id = fullId.Length > 7 ? fullId.Substring(0, 7) : fullId;
            }

            string title = el.TryGetProperty("title", out JsonElement titleEl)
                ? GetFirstLine(titleEl.GetString() ?? string.Empty)
                : string.Empty;
            string author = el.TryGetProperty("author_name", out JsonElement authorEl)
                ? authorEl.GetString() ?? "unknown"
                : "unknown";
            string url = el.TryGetProperty("web_url", out JsonElement urlEl)
                ? urlEl.GetString() ?? string.Empty
                : string.Empty;

            DateTimeOffset timestamp = DateTimeOffset.Now;
            if (el.TryGetProperty("committed_date", out JsonElement dateEl) &&
                DateTimeOffset.TryParse(dateEl.GetString(), out DateTimeOffset parsedDate))
            {
                timestamp = parsedDate.ToLocalTime();
            }

            items.Add(new ActivityItem
            {
                Platform = GitPlatform.GitLab,
                RepositoryFullName = repo.FullName,
                RepositoryShortName = repo.ShortName,
                Type = ActivityType.Commit,
                ExternalId = id,
                Author = author,
                Title = title,
                Timestamp = timestamp,
                Url = url
            });
        }
    }

    private static void ParseMergeRequests(string json, TrackedRepository repo, List<ActivityItem> items)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (JsonElement el in doc.RootElement.EnumerateArray())
        {
            string number = el.TryGetProperty("iid", out JsonElement numEl) && numEl.TryGetInt32(out int iid)
                ? iid.ToString()
                : string.Empty;
            string title = el.TryGetProperty("title", out JsonElement titleEl)
                ? GetFirstLine(titleEl.GetString() ?? string.Empty)
                : string.Empty;
            string url = el.TryGetProperty("web_url", out JsonElement urlEl)
                ? urlEl.GetString() ?? string.Empty
                : string.Empty;
            string author = ReadAuthor(el);

            DateTimeOffset timestamp = DateTimeOffset.Now;
            if (el.TryGetProperty("updated_at", out JsonElement dateEl) &&
                DateTimeOffset.TryParse(dateEl.GetString(), out DateTimeOffset parsedDate))
            {
                timestamp = parsedDate.ToLocalTime();
            }

            items.Add(new ActivityItem
            {
                Platform = GitPlatform.GitLab,
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
            string number = el.TryGetProperty("iid", out JsonElement numEl) && numEl.TryGetInt32(out int iid)
                ? iid.ToString()
                : string.Empty;
            string title = el.TryGetProperty("title", out JsonElement titleEl)
                ? GetFirstLine(titleEl.GetString() ?? string.Empty)
                : string.Empty;
            string url = el.TryGetProperty("web_url", out JsonElement urlEl)
                ? urlEl.GetString() ?? string.Empty
                : string.Empty;
            string author = ReadAuthor(el);

            DateTimeOffset timestamp = DateTimeOffset.Now;
            if (el.TryGetProperty("updated_at", out JsonElement dateEl) &&
                DateTimeOffset.TryParse(dateEl.GetString(), out DateTimeOffset parsedDate))
            {
                timestamp = parsedDate.ToLocalTime();
            }

            items.Add(new ActivityItem
            {
                Platform = GitPlatform.GitLab,
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

    private static string ReadAuthor(JsonElement el)
    {
        if (el.TryGetProperty("author", out JsonElement authorEl) &&
            authorEl.ValueKind == JsonValueKind.Object)
        {
            if (authorEl.TryGetProperty("username", out JsonElement userEl))
            {
                return userEl.GetString() ?? "unknown";
            }
            if (authorEl.TryGetProperty("name", out JsonElement nameEl))
            {
                return nameEl.GetString() ?? "unknown";
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
            return "https://gitlab.com";
        }

        string text = baseUrl.Trim().TrimEnd('/');
        if (text.EndsWith("/api/v4", StringComparison.OrdinalIgnoreCase))
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

        int dashIndex = text.IndexOf("/-/", StringComparison.Ordinal);
        if (dashIndex >= 0)
        {
            text = text.Substring(0, dashIndex);
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
