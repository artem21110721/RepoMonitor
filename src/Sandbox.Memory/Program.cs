using Sandbox.Memory;

MemoryDemo.Run();

Console.WriteLine();
Console.WriteLine("--- Проверка парсера ---");

string[] urls =
{
    "https://github.com/dotnet/runtime",
    "https://github.com/dotnet/runtime.git/",
    "https://gitlab.com/group/subgroup/project.git/",
    "https://codeberg.org/forgejo/forgejo/",
    "not-a-valid-url"
};

foreach (string url in urls)
{
    bool ok = RepoAddressParser.TryParse(url, out string host, out string owner, out string repo);
    if (ok)
    {
        Console.WriteLine($"OK:   {url} -> host={host}, owner={owner}, repo={repo}");
    }
    else
    {
        Console.WriteLine($"FAIL: {url}");
    }
}

Console.WriteLine();
Console.WriteLine("--- Замер аллокаций (10 000 итераций) ---");

string testUrl = "https://gitlab.com/group/subgroup/project.git/";
int iterations = 10_000;

RepoAddressParser.TryParseWithSplit(testUrl, out _, out _, out _);
RepoAddressParser.TryParse(testUrl, out _, out _, out _);

long beforeSplit = GC.GetAllocatedBytesForCurrentThread();
for (int i = 0; i < iterations; i++)
{
    RepoAddressParser.TryParseWithSplit(testUrl, out _, out _, out _);
}
long splitBytes = GC.GetAllocatedBytesForCurrentThread() - beforeSplit;

long beforeSpan = GC.GetAllocatedBytesForCurrentThread();
for (int i = 0; i < iterations; i++)
{
    RepoAddressParser.TryParse(testUrl, out _, out _, out _);
}
long spanBytes = GC.GetAllocatedBytesForCurrentThread() - beforeSpan;

Console.WriteLine($"string.Split:       {splitBytes} байт");
Console.WriteLine($"ReadOnlySpan<char>: {spanBytes} байт");
Console.WriteLine($"Разница:            {splitBytes - spanBytes} байт");
