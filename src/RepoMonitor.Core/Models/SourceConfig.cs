namespace RepoMonitor.Core.Models;

public class SourceConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public GitPlatform Platform { get; set; }
    public MonitoringMode Mode { get; set; }
    public string BaseUrl { get; set; } = string.Empty;
    public string RepositoryPath { get; set; } = string.Empty;
    public string? AccessToken { get; set; }
    public string DisplayName { get; set; } = string.Empty;
}
