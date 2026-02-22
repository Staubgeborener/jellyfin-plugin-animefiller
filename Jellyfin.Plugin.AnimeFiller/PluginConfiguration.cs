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
        FillerSuffix = "[F]";
        MarkMixedEpisodes = true;
        MixedSuffix = "[C/F]";
    }

    /// <summary>
    /// When true, pure filler episodes are marked with FillerSuffix. Default: true
    /// </summary>
    public bool MarkFiller { get; set; }

    /// <summary>
    /// Suffix prepended to pure filler episode names. Default: [F]
    /// </summary>
    public string FillerSuffix { get; set; }

    /// <summary>
    /// When true, mixed canon/filler episodes are also marked with MixedSuffix. Default: true
    /// </summary>
    public bool MarkMixedEpisodes { get; set; }

    /// <summary>
    /// Suffix prepended to mixed canon/filler episode names. Default: [C/F]
    /// </summary>
    public string MixedSuffix { get; set; }
}
