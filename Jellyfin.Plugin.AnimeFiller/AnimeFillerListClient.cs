using System.Collections.Concurrent;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AnimeFiller;

/// <summary>
/// Result of an episode type query for a series.
/// </summary>
public record EpisodeTypeResult(
    HashSet<int> Filler,                      // pure filler episodes  → [F]
    HashSet<int> Mixed,                       // mixed canon/filler    → [C/F]
    Dictionary<string, int> TitleToAbsolute   // normalised title      → absolute episode number
);

/// <summary>
/// HTTP client for animefillerlist.com.
/// Fetches episode types and caches the results for 24 hours.
/// </summary>
public class AnimeFillerListClient
{
    private const string BaseUrl = "https://www.animefillerlist.com";
    private static readonly TimeSpan CacheExpiry = TimeSpan.FromHours(24);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AnimeFillerListClient> _logger;

    // Cache: slug -> (timestamp, result)
    private readonly ConcurrentDictionary<string, (DateTime Fetched, EpisodeTypeResult Result)> _episodeCache = new();

    // Cache: normalised series name -> slug
    private Dictionary<string, string>? _showIndex;
    private DateTime _showIndexFetched = DateTime.MinValue;

    public AnimeFillerListClient(IHttpClientFactory httpClientFactory, ILogger<AnimeFillerListClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Looks up the AFL slug for a given series name.
    /// Returns null if the series was not found.
    /// </summary>
    public async Task<string?> FindShowSlugAsync(string seriesName, CancellationToken cancellationToken)
    {
        var index = await GetShowIndexAsync(cancellationToken).ConfigureAwait(false);
        var normalized = NormalizeName(seriesName);

        // 1. Exact match
        foreach (var (name, slug) in index)
        {
            if (NormalizeName(name) == normalized)
                return slug;
        }

        // 2. Substring match
        foreach (var (name, slug) in index)
        {
            var normalizedAfl = NormalizeName(name);
            if (normalizedAfl.Contains(normalized) || normalized.Contains(normalizedAfl))
            {
                _logger.LogDebug("Fuzzy match: '{SeriesName}' -> '{AflName}' ({Slug})", seriesName, name, slug);
                return slug;
            }
        }

        // 3. Space-insensitive match (e.g. "Dragonball Z" vs "Dragon Ball Z")
        var normalizedNoSpace = normalized.Replace(" ", "");
        foreach (var (name, slug) in index)
        {
            if (NormalizeName(name).Replace(" ", "") == normalizedNoSpace)
            {
                _logger.LogDebug("Space-insensitive match: '{SeriesName}' -> '{AflName}' ({Slug})", seriesName, name, slug);
                return slug;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns filler and mixed episode numbers for an AFL slug.
    /// Both types are always fetched; whether mixed episodes are marked
    /// is decided by the task based on the plugin configuration.
    /// </summary>
    public async Task<EpisodeTypeResult> GetEpisodeTypesAsync(string slug, CancellationToken cancellationToken)
    {
        if (_episodeCache.TryGetValue(slug, out var cached) && DateTime.UtcNow - cached.Fetched < CacheExpiry)
            return cached.Result;

        var filler          = new HashSet<int>();
        var mixed           = new HashSet<int>();
        var titleToAbsolute = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var client = _httpClientFactory.CreateClient("AnimeFiller");
            var url = $"{BaseUrl}/shows/{slug}";
            _logger.LogInformation("Loading episode list from {Url}", url);

            var html = await client.GetStringAsync(url, cancellationToken).ConfigureAwait(false);

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            // Episode rows: <tr class="filler">, <tr class="mixed_canon/filler">, ...
            var rows = doc.DocumentNode.SelectNodes("//tr[td]");
            if (rows is null)
            {
                _logger.LogWarning("No episode table found at {Url}.", url);
                return new EpisodeTypeResult(filler, mixed, titleToAbsolute);
            }

            foreach (var row in rows)
            {
                // Episode number(s) from the first column
                var numberCell = row.SelectSingleNode("td[1]");
                if (numberCell is null) continue;

                var epText = System.Net.WebUtility.HtmlDecode(numberCell.InnerText).Trim();

                // Build title → absolute number map for single-episode rows (not ranges).
                // This enables matching by episode title for partial libraries.
                if (!epText.Contains('-') && int.TryParse(epText, out var singleNum))
                {
                    var titleCell = row.SelectSingleNode("td[2]");
                    if (titleCell is not null)
                    {
                        var titleText = System.Net.WebUtility.HtmlDecode(titleCell.InnerText).Trim();
                        if (!string.IsNullOrEmpty(titleText))
                            titleToAbsolute.TryAdd(NormalizeTitle(titleText), singleNum);
                    }
                }

                // Determine filler classification from row CSS class
                var rowClass = row.GetAttributeValue("class", "").ToLowerInvariant();

                // Use Contains instead of exact match, as AFL often writes "filler even" / "filler odd"
                bool isPureFiller = rowClass.Contains("filler") && !rowClass.Contains("mixed");
                bool isMixed      = rowClass.Contains("mixed");

                if (!isPureFiller && !isMixed)
                    continue;

                var target = isPureFiller ? filler : mixed;
                AddEpisodeNumbers(epText, target);
            }

            _logger.LogInformation(
                "'{Slug}': {F} filler, {M} mixed, {T} titles indexed.",
                slug, filler.Count, mixed.Count, titleToAbsolute.Count);

            var result = new EpisodeTypeResult(filler, mixed, titleToAbsolute);
            _episodeCache[slug] = (DateTime.UtcNow, result);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch episode list for '{Slug}'.", slug);
            return new EpisodeTypeResult(filler, mixed, titleToAbsolute);
        }
    }

    /// <summary>
    /// Parses "57" or "57-60" and adds all numbers to the target set.
    /// </summary>
    private static void AddEpisodeNumbers(string epText, HashSet<int> target)
    {
        if (epText.Contains('-'))
        {
            var parts = epText.Split('-');
            if (parts.Length == 2
                && int.TryParse(parts[0].Trim(), out var start)
                && int.TryParse(parts[1].Trim(), out var end))
            {
                for (var i = start; i <= end; i++)
                    target.Add(i);
            }
        }
        else if (int.TryParse(epText, out var epNum))
        {
            target.Add(epNum);
        }
    }

    // ---------------------------------------------------------------------------
    // Private helpers
    // ---------------------------------------------------------------------------

    private async Task<Dictionary<string, string>> GetShowIndexAsync(CancellationToken cancellationToken)
    {
        if (_showIndex is not null && DateTime.UtcNow - _showIndexFetched < CacheExpiry)
            return _showIndex;

        var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var client = _httpClientFactory.CreateClient("AnimeFiller");
            var html = await client.GetStringAsync($"{BaseUrl}/shows", cancellationToken).ConfigureAwait(false);

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            // Links of the form /shows/{slug}
            var links = doc.DocumentNode.SelectNodes("//a[starts-with(@href,'/shows/')]");
            if (links is not null)
            {
                foreach (var link in links)
                {
                    var href = link.GetAttributeValue("href", "");
                    var name = System.Net.WebUtility.HtmlDecode(link.InnerText).Trim();

                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(href))
                        continue;

                    var slug = href.Replace("/shows/", "").Trim('/');
                    if (!string.IsNullOrEmpty(slug))
                        index.TryAdd(name, slug);
                }
            }

            _logger.LogInformation("Show index loaded: {Count} entries.", index.Count);
            _showIndex = index;
            _showIndexFetched = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load show index from animefillerlist.com.");
            _showIndex ??= index;
        }

        return _showIndex;
    }

    private static string NormalizeName(string name)
        => name.ToLowerInvariant()
               .Replace(":", " ")
               .Replace("-", " ")
               .Replace("_", " ")
               .Replace("  ", " ")
               .Trim();

    internal static string NormalizeTitle(string title)
        => title.ToLowerInvariant()
                .Replace("!", "")
                .Replace("?", "")
                .Replace(",", "")
                .Replace(".", "")
                .Replace(":", " ")
                .Replace("-", " ")
                .Replace("  ", " ")
                .Trim();
}
