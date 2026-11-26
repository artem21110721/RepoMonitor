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

    public Task<HttpResponseInfo> GetAsync(string url, CancellationToken cancellationToken = default)
    {
        return GetAsync(url, null, cancellationToken);
    }

    public async Task<HttpResponseInfo> GetAsync(
        string url,
        Dictionary<string, string>? headers,
        CancellationToken cancellationToken = default)
    {
        using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("RepoMonitor");

        if (headers != null)
        {
            foreach (KeyValuePair<string, string> pair in headers)
            {
                request.Headers.TryAddWithoutValidation(pair.Key, pair.Value);
            }
        }

        string? savedETag = _cache.Get(url);
        if (!string.IsNullOrEmpty(savedETag))
        {
            request.Headers.TryAddWithoutValidation("If-None-Match", savedETag);
        }

        try
        {
            using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);

            string? receivedETag = GetHeader(response, "ETag");

            int? remaining = null;
            string? remText = GetHeader(response, "X-RateLimit-Remaining") ?? GetHeader(response, "RateLimit-Remaining");
            if (int.TryParse(remText, out int parsedRem))
            {
                remaining = parsedRem;
            }

            DateTimeOffset? reset = null;
            string? resetText = GetHeader(response, "X-RateLimit-Reset") ?? GetHeader(response, "RateLimit-Reset");
            if (long.TryParse(resetText, out long unixSeconds))
            {
                reset = DateTimeOffset.FromUnixTimeSeconds(unixSeconds).ToLocalTime();
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
        catch (HttpRequestException ex)
        {
            return new HttpResponseInfo
            {
                Url = url,
                IsNetworkError = true,
                ErrorMessage = ex.Message,
                SentETag = savedETag
            };
        }
        catch (TaskCanceledException)
        {
            return new HttpResponseInfo
            {
                Url = url,
                IsNetworkError = true,
                ErrorMessage = "Request timeout or cancelled",
                SentETag = savedETag
            };
        }
    }

    private static string? GetHeader(HttpResponseMessage response, string name)
    {
        if (response.Headers.TryGetValues(name, out IEnumerable<string>? values))
        {
            return values.FirstOrDefault();
        }

        return null;
    }
}
