using System.Net;
using RepoMonitor.Core.Models;

namespace RepoMonitor.Core.Http;

public class HttpResponseInfo
{
    public string Url { get; set; } = string.Empty;
    public HttpStatusCode StatusCode { get; set; }
    public bool IsNotModified { get; set; }
    public bool IsNetworkError { get; set; }
    public string? ErrorMessage { get; set; }
    public string Body { get; set; } = string.Empty;
    public string? SentETag { get; set; }
    public string? ReceivedETag { get; set; }
    public int? RateLimitRemaining { get; set; }
    public DateTimeOffset? RateLimitReset { get; set; }

    public bool IsError => IsNetworkError || (StatusCode != HttpStatusCode.OK && !IsNotModified);

    public RepositoryStatus GetStatus()
    {
        if (IsNetworkError) return RepositoryStatus.NetworkError;
        if (StatusCode == HttpStatusCode.OK || IsNotModified) return RepositoryStatus.Active;
        if (StatusCode == HttpStatusCode.Unauthorized) return RepositoryStatus.Unauthorized;
        if (StatusCode == HttpStatusCode.Forbidden) return RateLimitRemaining == 0 ? RepositoryStatus.RateLimited : RepositoryStatus.Unauthorized;
        if (StatusCode == HttpStatusCode.NotFound) return RepositoryStatus.NotFound;
        if (StatusCode == HttpStatusCode.TooManyRequests) return RepositoryStatus.RateLimited;
        return RepositoryStatus.NetworkError;
    }

    public string GetErrorMessage()
    {
        if (IsNetworkError) return string.IsNullOrWhiteSpace(ErrorMessage) ? "Network error" : ErrorMessage;
        return $"HTTP {(int)StatusCode} {StatusCode}";
    }
}
