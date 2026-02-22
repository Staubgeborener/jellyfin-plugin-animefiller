using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using System.Collections.Generic;

namespace Jellyfin.Plugin.AnimeFiller;

/// <summary>
/// Main plugin class for the Anime Filler Marker.
/// Marks filler episodes with a configurable suffix (default: [F]).
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    public static Plugin? Instance { get; private set; }

    public override string Name => "Anime Filler Marker";

    public override Guid Id => Guid.Parse("d4c3b2a1-f5e6-7890-abcd-ef0987654321");

    public override string Description =>
        "Marks anime filler episodes based on animefillerlist.com with [F] in the episode title.";

    public IEnumerable<PluginPageInfo> GetPages()
    {
        yield return new PluginPageInfo
        {
            Name = "AnimeFillerMarkerConfig",
            EmbeddedResourcePath = $"{GetType().Namespace}.configPage.html"
        };
    }
}
