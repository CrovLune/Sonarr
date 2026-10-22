using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.MetadataSource.SkyHook.Resource;

namespace NzbDrone.Core.MetadataSource.SkyHook
{
    /// <summary>
    /// Fork: chooses the title stored as OriginalTitle alongside the series title.
    /// Kept free of HTTP lookups so it can be tested directly.
    /// </summary>
    public static class OriginalTitleSelection
    {
        /// <summary>
        /// Upstream stores the original-language title. When the series' metadata language
        /// is its original language that title is the series title itself, so keep the
        /// English title instead: OriginalTitle feeds CleanOriginalTitle lookups and release
        /// search, so both names keep matching.
        /// </summary>
        public static string Select(string title, string originalLanguage, IEnumerable<TranslationResource> translations)
        {
            var titles = translations.Where(t => t.Title.IsNotNullOrWhiteSpace()).ToList();

            foreach (var language in new[] { originalLanguage, "eng" })
            {
                var candidate = titles.FirstOrDefault(t => t.Language == language)?.Title;

                if (candidate != null && !candidate.Equals(title, StringComparison.InvariantCultureIgnoreCase))
                {
                    return candidate;
                }
            }

            return null;
        }
    }
}
