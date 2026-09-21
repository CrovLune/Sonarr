using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Languages;
using NzbDrone.Core.Parser;

namespace NzbDrone.Core.MetadataSource.SkyHook
{
    /// <summary>
    /// Chooses which of a show's two known titles is used as the series title.
    /// Kept free of HTTP and configuration lookups so it can be tested directly.
    /// </summary>
    public static class OriginalTitleSelection
    {
        /// <summary>
        /// Parses a comma separated list of languages. Entries may be language names
        /// ("Russian") or ISO codes ("ru", "rus"); unrecognised entries are ignored.
        /// </summary>
        public static List<Language> ParseLanguages(string configuredLanguages)
        {
            var languages = new List<Language>();

            if (configuredLanguages.IsNullOrWhiteSpace())
            {
                return languages;
            }

            foreach (var entry in configuredLanguages.Split(','))
            {
                var trimmed = entry.Trim();

                if (trimmed.IsNullOrWhiteSpace())
                {
                    continue;
                }

                var language = IsoLanguages.FindByName(trimmed)?.Language ??
                               IsoLanguages.Find(trimmed.ToLowerInvariant())?.Language;

                if (language != null && !languages.Contains(language))
                {
                    languages.Add(language);
                }
            }

            return languages;
        }

        /// <summary>
        /// Returns the title to display and the alternate title to keep alongside it.
        /// When the show's original language is one of <paramref name="languages"/> the two
        /// are swapped, so the native title becomes the series title and the metadata title
        /// is retained as the alternate. The alternate still feeds CleanOriginalTitle lookups
        /// and release search, so both names keep matching.
        /// </summary>
        public static SeriesTitles Select(string metadataTitle, string originalTitle, Language originalLanguage, IReadOnlyCollection<Language> languages)
        {
            if (originalTitle.IsNullOrWhiteSpace() ||
                originalLanguage == null ||
                languages == null ||
                languages.Count == 0 ||
                !languages.Contains(originalLanguage) ||
                originalTitle.Equals(metadataTitle, StringComparison.InvariantCultureIgnoreCase))
            {
                return new SeriesTitles(metadataTitle, originalTitle);
            }

            return new SeriesTitles(originalTitle, metadataTitle);
        }
    }

    public sealed class SeriesTitles
    {
        public SeriesTitles(string title, string alternateTitle)
        {
            Title = title;
            AlternateTitle = alternateTitle;
        }

        public string Title { get; }
        public string AlternateTitle { get; }
    }
}
