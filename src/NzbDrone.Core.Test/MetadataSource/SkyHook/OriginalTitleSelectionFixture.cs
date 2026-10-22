using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MetadataSource.SkyHook;
using NzbDrone.Core.MetadataSource.SkyHook.Resource;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MetadataSource.SkyHook
{
    [TestFixture]
    public class OriginalTitleSelectionFixture : TestBase
    {
        private static List<TranslationResource> Translations()
        {
            return new List<TranslationResource>
            {
                new() { Language = "eng", Title = "Silver Spoon (2014)" },
                new() { Language = "rus", Title = "Мажор" }
            };
        }

        [Test]
        public void should_use_the_original_language_title_when_metadata_is_in_another_language()
        {
            OriginalTitleSelection.Select("Silver Spoon (2014)", "rus", Translations())
                                  .Should().Be("Мажор");
        }

        [Test]
        public void should_use_the_english_title_when_metadata_is_in_the_original_language()
        {
            OriginalTitleSelection.Select("Мажор", "rus", Translations())
                                  .Should().Be("Silver Spoon (2014)");
        }

        [Test]
        public void should_compare_titles_ignoring_case()
        {
            OriginalTitleSelection.Select("МАЖОР", "rus", Translations())
                                  .Should().Be("Silver Spoon (2014)");
        }

        [Test]
        public void should_return_null_for_an_english_series_in_english()
        {
            var translations = new List<TranslationResource>
            {
                new() { Language = "eng", Title = "The Office" }
            };

            OriginalTitleSelection.Select("The Office", "eng", translations).Should().BeNull();
        }

        [Test]
        public void should_return_null_without_translations()
        {
            OriginalTitleSelection.Select("Мажор", "rus", new List<TranslationResource>()).Should().BeNull();
        }

        [Test]
        public void should_ignore_blank_translation_titles()
        {
            var translations = new List<TranslationResource>
            {
                new() { Language = "rus", Title = " " },
                new() { Language = "eng", Title = "Silver Spoon (2014)" }
            };

            OriginalTitleSelection.Select("Silver Spoon (2014)", "rus", translations).Should().BeNull();
        }
    }
}
