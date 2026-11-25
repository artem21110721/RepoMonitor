using System.Net;

namespace RepoMonitor.Core.Http;

public class HttpResponseInfo
{
    public string Url { get; set; } = string.Empty;
    public HttpStatusCode StatusCode { get; set; }
    public bool IsNotModified { get; set; }
    public string Body { get; set; } = string.Empty;
    public string? SentETag { get; set; }
    public string? ReceivedETag { get; set; }
    public int? RateLimitRemaining { get; set; }
    public DateTimeOffset? RateLimitReset { get; set; }
}
