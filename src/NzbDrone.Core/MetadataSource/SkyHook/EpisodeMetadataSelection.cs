using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Languages;

namespace NzbDrone.Core.MetadataSource.SkyHook
{
    /// <summary>
    /// Chooses an episode's title and overview from the metadata source and the supplementary
    /// TMDB lookup. Kept free of HTTP and configuration lookups so it can be tested directly.
    /// </summary>
    public static class EpisodeMetadataSelection
    {
        // TheTVDB stores non-English series without episode names, or with a numbered stand-in.
        // Both mean "this episode has no title", so neither should win over a real title from TMDB.
        private static readonly Regex NumberedTitleRegex = new Regex(@"^(?:episode|эпизод|серия|серія)\s*\d+$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        // Russian and Ukrainian series are numbered rather than titled, so a synthesised stand-in is
        // the real title rather than a guess. Other languages keep the English wording.
        private static readonly Dictionary<Language, string> EpisodeWords = new Dictionary<Language, string>
        {
            { Language.Russian, "Серия" },
            { Language.Ukrainian, "Серія" }
        };

        /// <summary>
        /// True when a title carries no information: blank, "TBA", or a bare "Episode 5"/"Серия 5".
        /// </summary>
        public static bool IsPlaceholderTitle(string title)
        {
            if (title.IsNullOrWhiteSpace())
            {
                return true;
            }

            var trimmed = title.Trim();

            return trimmed.Equals("TBA", StringComparison.InvariantCultureIgnoreCase) ||
                   NumberedTitleRegex.IsMatch(trimmed);
        }

        /// <summary>
        /// Returns the episode title and overview to store.
        /// Only series whose original language is one of <paramref name="languages"/> are touched,
        /// so English shows keep Sonarr's stock behaviour of holding a TBA until TheTVDB fills it in.
        /// For the configured languages the metadata title wins when it is a real title, otherwise the
        /// TMDB title is used, and an aired episode that neither source names falls back to a numbered
        /// stand-in. That stand-in matters beyond display: a stored "TBA" makes
        /// <see cref="Tv.ShouldRefreshSeries"/> force a full refresh on every cycle and blocks imports
        /// for 48 hours, neither of which ever resolves for a series that will never be titled.
        /// </summary>
        public static EpisodeMetadata Select(
            string metadataTitle,
            string metadataOverview,
            string supplementaryTitle,
            string supplementaryOverview,
            int seasonNumber,
            int episodeNumber,
            Language originalLanguage,
            IReadOnlyCollection<Language> languages,
            DateTime? airDateUtc,
            DateTime utcNow)
        {
            if (originalLanguage == null ||
                languages == null ||
                languages.Count == 0 ||
                !languages.Contains(originalLanguage))
            {
                return new EpisodeMetadata(metadataTitle, metadataOverview);
            }

            // Only ever replaced with something better. Clearing a numbered title would store it as
            // "TBA", and EpisodeTitleSpecification only exempts an episode from the TBA rejection
            // once it aired more than 48 hours ago - which an episode with no air date never does,
            // so the import would be blocked for good.
            var title = metadataTitle;

            if (IsPlaceholderTitle(title))
            {
                if (!IsPlaceholderTitle(supplementaryTitle))
                {
                    title = supplementaryTitle;
                }
                else if (seasonNumber > 0 && airDateUtc.HasValue && airDateUtc.Value <= utcNow)
                {
                    // Specials are excluded because they are genuinely titled when they exist, and an
                    // unaired episode keeps its TBA because its title really is still to be announced.
                    title = $"{EpisodeWord(originalLanguage)} {episodeNumber}";
                }
            }

            var overview = metadataOverview.IsNullOrWhiteSpace() ? supplementaryOverview : metadataOverview;

            return new EpisodeMetadata(title, overview);
        }

        private static string EpisodeWord(Language language)
        {
            return EpisodeWords.TryGetValue(language, out var word) ? word : "Episode";
        }
    }

    public sealed class EpisodeMetadata
    {
        public EpisodeMetadata(string title, string overview)
        {
            Title = title;
            Overview = overview;
        }

        public string Title { get; }
        public string Overview { get; }
    }
}
