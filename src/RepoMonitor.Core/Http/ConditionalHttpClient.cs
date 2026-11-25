using System.Net;

namespace RepoMonitor.Core.Http;

public class ConditionalHttpClient
{
    private readonly HttpClient _httpClient;
    private readonly ETagCache _cache;

    public ConditionalHttpClient(HttpClient httpClient, ETagCache cache)
    {
        _httpClient = httpClient;
        _cache = cache;
    }

    public async Task<HttpResponseInfo> GetAsync(string url, CancellationToken cancellationToken = default)
    {
        using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("RepoMonitor");

        string? savedETag = _cache.Get(url);
        if (!string.IsNullOrEmpty(savedETag))
        {
            request.Headers.TryAddWithoutValidation("If-None-Match", savedETag);
        }

        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);

        string? receivedETag = null;
        if (response.Headers.TryGetValues("ETag", out IEnumerable<string>? etagValues))
        {
            receivedETag = etagValues.FirstOrDefault();
        }

        int? remaining = null;
        if (response.Headers.TryGetValues("X-RateLimit-Remaining", out IEnumerable<string>? remValues))
        {
            if (int.TryParse(remValues.FirstOrDefault(), out int parsedRem))
            {
                remaining = parsedRem;
            }
        }

        DateTimeOffset? reset = null;
        if (response.Headers.TryGetValues("X-RateLimit-Reset", out IEnumerable<string>? resetValues))
        {
            if (long.TryParse(resetValues.FirstOrDefault(), out long unixSeconds))
            {
                reset = DateTimeOffset.FromUnixTimeSeconds(unixSeconds).ToLocalTime();
            }
        }

        bool isNotModified = response.StatusCode == HttpStatusCode.NotModified;
        string body = string.Empty;

        if (!isNotModified)
        {
            body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                _cache.Save(url, receivedETag);
            }
        }

        return new HttpResponseInfo
        {
            Url = url,
            StatusCode = response.StatusCode,
            IsNotModified = isNotModified,
            Body = body,
            SentETag = savedETag,
            ReceivedETag = receivedETag,
            RateLimitRemaining = remaining,
            RateLimitReset = reset
        };
    }
}
