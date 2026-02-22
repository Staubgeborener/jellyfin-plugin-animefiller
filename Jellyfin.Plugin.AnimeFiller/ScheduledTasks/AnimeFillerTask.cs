using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AnimeFiller.ScheduledTasks;

/// <summary>
/// Scheduled task: scans all series in the library, matches episodes against
/// animefillerlist.com and marks filler episodes with the configured suffix (default: [F]).
/// </summary>
public class AnimeFillerTask : IScheduledTask
{
    private readonly ILibraryManager _libraryManager;
    private readonly AnimeFillerListClient _client;
    private readonly ILogger<AnimeFillerTask> _logger;

    public AnimeFillerTask(
        ILibraryManager libraryManager,
        IHttpClientFactory httpClientFactory,
        ILoggerFactory loggerFactory)
    {
        _libraryManager = libraryManager;
        _logger = loggerFactory.CreateLogger<AnimeFillerTask>();
        _client = new AnimeFillerListClient(
            httpClientFactory,
            loggerFactory.CreateLogger<AnimeFillerListClient>());
    }

    // ── IScheduledTask ────────────────────────────────────────────────────────

    public string Name => "Anime Filler Marker";
    public string Key => "AnimeFillerMarker";
    public string Description => "Marks anime filler episodes with [F] and mixed canon/filler episodes with [C/F] in the episode title.";
    public string Category => "Library";

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        // Default: daily at 03:00
        yield return new TaskTriggerInfo
        {
            Type = TaskTriggerInfoType.DailyTrigger,
            TimeOfDayTicks = TimeSpan.FromHours(3).Ticks
        };
    }

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var config       = Plugin.Instance!.Configuration;
        var fillerSuffix = config.FillerSuffix;        // e.g. "[F]"
        var mixedSuffix  = config.MixedSuffix;         // e.g. "[C/F]"
        var markFiller   = config.MarkFiller;
        var markMixed    = config.MarkMixedEpisodes;
        var nothingMode  = !markFiller && !markMixed;  // strip all markings

        _logger.LogInformation(
            "Config: MarkFiller={MarkFiller}, MarkMixed={MarkMixed}, NothingMode={NothingMode}",
            markFiller, markMixed, nothingMode);

        // Load all series from the library
        var allSeries = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.Series],
            IsVirtualItem = false,
            Recursive = true
        });

        if (allSeries.Count == 0)
        {
            _logger.LogInformation("No series found in the library.");
            return;
        }

        _logger.LogInformation("Anime Filler task started – checking {Count} series.", allSeries.Count);

        for (var i = 0; i < allSeries.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var series = allSeries[i];
            progress.Report((double)i / allSeries.Count * 100);

            // In nothing mode we skip the AFL lookup and use empty sets so all
            // existing markings are stripped without adding new ones.
            EpisodeTypeResult types;
            if (nothingMode)
            {
                types = new EpisodeTypeResult(new HashSet<int>(), new HashSet<int>(), new Dictionary<string, int>());
            }
            else
            {
                var slug = await _client.FindShowSlugAsync(series.Name, cancellationToken).ConfigureAwait(false);
                if (slug is null)
                {
                    _logger.LogDebug("No AFL entry for '{Name}' – skipping.", series.Name);
                    continue;
                }

                _logger.LogInformation("Processing '{Name}' (slug: {Slug})", series.Name, slug);
                types = await _client.GetEpisodeTypesAsync(slug, cancellationToken).ConfigureAwait(false);
            }

            if (!nothingMode && types.Filler.Count == 0 && types.Mixed.Count == 0)
            {
                _logger.LogInformation("No filler/mixed episodes found for '{Name}'.", series.Name);
                continue;
            }

            // Load only real (downloaded) episodes for processing.
            var episodes = _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = [BaseItemKind.Episode],
                AncestorIds = [series.Id],
                IsVirtualItem = false
            });

            // Build the absolute-number map robust to missing episodes.
            //
            // Old approach (idx+1): a positional counter that breaks when episodes
            // are missing – e.g. if S01E01-E07 have no entry, S01E08 gets absolute
            // number 1 instead of 8, shifting everything that follows.
            //
            // New approach:
            //   absolute# = seasonOffset[season] + episode.IndexNumber
            //
            // The season offset is the cumulative sum of MAX episode numbers for all
            // prior seasons (not the count of present episodes), so gaps do not shift
            // subsequent numbers. Season 0 (specials/movies) is excluded because AFL
            // only counts regular broadcast episodes.
            var allEpisodesForMap = _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = [BaseItemKind.Episode],
                AncestorIds = [series.Id]
                // IsVirtualItem not set → returns both real and virtual episodes
            });

            // Step 1: highest episode number seen per season (season 0 excluded)
            var seasonMaxEp = allEpisodesForMap
                .OfType<Episode>()
                .Where(e => e.IndexNumber.HasValue && (e.ParentIndexNumber ?? 0) > 0)
                .GroupBy(e => e.ParentIndexNumber!.Value)
                .ToDictionary(g => g.Key, g => g.Max(e => e.IndexNumber!.Value));

            // Step 2: cumulative offset per season
            var seasonOffset = new Dictionary<int, int>();
            int cumOffset = 0;
            foreach (var s in seasonMaxEp.Keys.OrderBy(s => s))
            {
                seasonOffset[s] = cumOffset;
                cumOffset += seasonMaxEp[s];
            }

            // Step 3: absolute number = season offset + episode number within season
            var absoluteMap = allEpisodesForMap
                .OfType<Episode>()
                .Where(e => e.IndexNumber.HasValue
                         && e.ParentIndexNumber.HasValue
                         && e.ParentIndexNumber.Value > 0
                         && seasonOffset.ContainsKey(e.ParentIndexNumber.Value))
                .ToDictionary(
                    e => e.Id,
                    e => seasonOffset[e.ParentIndexNumber!.Value] + e.IndexNumber!.Value);

            var changed = 0;
            foreach (var item in episodes)
            {
                if (item is not Episode episode) continue;
                if (episode.IndexNumber is null) continue;

                // Strip any existing known markings first (needed for title matching too)
                var cleanName = StripSuffixes(episode.Name, fillerSuffix, mixedSuffix);

                // Resolve absolute episode number – two fallback levels:
                //   1. absoluteMap built from virtual + real episodes
                //      → language-independent; correct as long as Jellyfin has
                //        fetched series metadata so virtual placeholders exist
                //   2. Title match against AFL's scraped episode list
                //      → last resort, only works with English episode titles
                int epNum;
                if (absoluteMap.TryGetValue(episode.Id, out var absNum))
                {
                    epNum = absNum;
                }
                else if (types.TitleToAbsolute.TryGetValue(
                             AnimeFillerListClient.NormalizeTitle(cleanName), out var titleMatch))
                {
                    epNum = titleMatch;
                    _logger.LogDebug("Title match: '{Title}' -> absolute #{Num}", cleanName, titleMatch);
                }
                else
                {
                    epNum = episode.IndexNumber.Value;
                }

                // Determine the desired prefix
                string? desiredSuffix = null;
                if (markFiller && types.Filler.Contains(epNum))
                    desiredSuffix = fillerSuffix;
                else if (markMixed && types.Mixed.Contains(epNum))
                    desiredSuffix = mixedSuffix;

                // Build the new name (prefix goes at the beginning)
                var newName = desiredSuffix is not null
                    ? $"{desiredSuffix} {cleanName}"
                    : cleanName;

                if (newName == episode.Name)
                    continue; // nothing to do

                episode.Name = newName;
                await _libraryManager.UpdateItemAsync(
                    episode,
                    episode.GetParent(),
                    ItemUpdateType.MetadataEdit,
                    cancellationToken).ConfigureAwait(false);

                changed++;
                _logger.LogDebug(
                    "{Action}: S{Season}E{Ep} -> '{Name}'",
                    desiredSuffix is not null ? "Marked" : "Unmarked",
                    episode.ParentIndexNumber, epNum, newName);
            }

            if (changed > 0)
                _logger.LogInformation("'{Name}': {Count} episode(s) updated.", series.Name, changed);

            // Brief pause to avoid hammering animefillerlist.com
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
        }

        progress.Report(100);
        _logger.LogInformation("Anime Filler task completed.");
    }

    /// <summary>
    /// Removes known prefixes/suffixes from an episode title.
    /// Strips from the beginning (current format) and from the end (legacy format).
    /// </summary>
    private static string StripSuffixes(string name, string fillerSuffix, string mixedSuffix)
    {
        // Strip from beginning (current format: "[F] Episode Title")
        if (name.StartsWith($"{fillerSuffix} ", StringComparison.Ordinal))
            return name[(fillerSuffix.Length + 1)..];

        if (name.StartsWith($"{mixedSuffix} ", StringComparison.Ordinal))
            return name[(mixedSuffix.Length + 1)..];

        // Strip from end (legacy format: "Episode Title [F]")
        if (name.EndsWith($" {fillerSuffix}", StringComparison.Ordinal))
            return name[..^(fillerSuffix.Length + 1)];

        if (name.EndsWith($" {mixedSuffix}", StringComparison.Ordinal))
            return name[..^(mixedSuffix.Length + 1)];

        return name;
    }
}
