namespace RepoMonitor.Core.Http;

public class ETagCache
{
    private readonly Dictionary<string, string> _etags = new();

    public string? Get(string url)
    {
        if (_etags.TryGetValue(url, out string? etag))
        {
            return etag;
        }

        return null;
    }

    public void Save(string url, string? etag)
    {
        if (!string.IsNullOrEmpty(etag))
        {
            _etags[url] = etag;
        }
    }
}
