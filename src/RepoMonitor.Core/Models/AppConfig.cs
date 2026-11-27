namespace RepoMonitor.Core.Models;

public class AppConfig
{
    public int PollIntervalSeconds { get; set; } = 60;
    public bool AlwaysOnTop { get; set; }
    public bool IsPinned { get; set; }
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public double WindowWidth { get; set; } = 380;
    public double WindowHeight { get; set; } = 520;
    public List<SourceConfig> Sources { get; set; } = new();
}
