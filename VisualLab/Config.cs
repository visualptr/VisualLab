namespace VisualLab;

/// <summary>The plugin configuration.</summary>
public sealed class Config
{
    /// <summary>Gets or sets the relay HTTP address.</summary>
    public string RelayUrl { get; set; } = "https://visuallab-sl.pages.dev/relay";

    /// <summary>Gets or sets the dashboard website address.</summary>
    public string WebsiteUrl { get; set; } = "https://visuallab-sl.pages.dev";

    /// <summary>Gets or sets the maximum dashboard session lifetime in minutes.</summary>
    public int SessionTtlMinutes { get; set; } = 720;

    /// <summary>Gets or sets the graph storage folder.</summary>
    public string PluginsFolder { get; set; } = "plugins";
}
