using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.AnimeFiller;

/// <summary>
/// Configuration for the Anime Filler Marker plugin.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    public PluginConfiguration()
    {
        MarkFiller = true;
        MarkMixedEpisodes = true;
    }

    /// <summary>
    /// When true, pure filler episodes are marked with [F]. Default: true
    /// </summary>
    public bool MarkFiller { get; set; }

    /// <summary>
    /// When true, mixed canon/filler episodes are marked with [C/F]. Default: true
    /// </summary>
    public bool MarkMixedEpisodes { get; set; }
}
