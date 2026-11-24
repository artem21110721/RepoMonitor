namespace Sandbox.Memory;

public static class RepoAddressParser
{
    public static bool TryParse(
        ReadOnlySpan<char> input,
        out string host,
        out string owner,
        out string repo)
    {
        host = string.Empty;
        owner = string.Empty;
        repo = string.Empty;

        ReadOnlySpan<char> text = input.Trim();
        if (text.IsEmpty)
        {
            return false;
        }

        int schemeIndex = text.IndexOf("://", StringComparison.Ordinal);
        if (schemeIndex >= 0)
        {
            text = text.Slice(schemeIndex + 3);
        }

        text = text.TrimEnd('/');
        if (text.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            text = text.Slice(0, text.Length - 4);
            text = text.TrimEnd('/');
        }

        int firstSlash = text.IndexOf('/');
        if (firstSlash <= 0 || firstSlash == text.Length - 1)
        {
            return false;
        }

        ReadOnlySpan<char> hostSpan = text.Slice(0, firstSlash);
        ReadOnlySpan<char> pathSpan = text.Slice(firstSlash + 1).Trim('/');

        int lastSlash = pathSpan.LastIndexOf('/');
        if (lastSlash <= 0 || lastSlash == pathSpan.Length - 1)
        {
            return false;
        }

        ReadOnlySpan<char> ownerSpan = pathSpan.Slice(0, lastSlash);
        ReadOnlySpan<char> repoSpan = pathSpan.Slice(lastSlash + 1);

        if (hostSpan.IsEmpty || ownerSpan.IsEmpty || repoSpan.IsEmpty)
        {
            return false;
        }

        host = hostSpan.ToString();
        owner = ownerSpan.ToString();
        repo = repoSpan.ToString();
        return true;
    }

    public static bool TryParseWithSplit(
        string input,
        out string host,
        out string owner,
        out string repo)
    {
        host = string.Empty;
        owner = string.Empty;
        repo = string.Empty;

        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        string text = input.Trim();

        string[] schemeParts = text.Split("://");
        if (schemeParts.Length > 1)
        {
            text = schemeParts[1];
        }

        text = text.TrimEnd('/');
        if (text.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            text = text.Substring(0, text.Length - 4).TrimEnd('/');
        }

        string[] parts = text.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3)
        {
            return false;
        }

        host = parts[0];
        repo = parts[parts.Length - 1];
        owner = string.Join("/", parts, 1, parts.Length - 2);
        return true;
    }
}
