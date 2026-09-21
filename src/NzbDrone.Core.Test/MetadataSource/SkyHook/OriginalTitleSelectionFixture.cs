using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MetadataSource.SkyHook;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MetadataSource.SkyHook
{
    [TestFixture]
    public class OriginalTitleSelectionFixture : TestBase
    {
        [TestCase("Russian")]
        [TestCase("russian")]
        [TestCase("ru")]
        [TestCase("rus")]
        public void should_parse_languages_by_name_or_iso_code(string configured)
        {
            OriginalTitleSelection.ParseLanguages(configured)
                                  .Should().Equal(Language.Russian);
        }

        [Test]
        public void should_parse_multiple_languages_and_ignore_blanks()
        {
            OriginalTitleSelection.ParseLanguages(" Russian , , ukrainian ")
                                  .Should().Equal(Language.Russian, Language.Ukrainian);
        }

        [Test]
        public void should_ignore_unrecognised_languages()
        {
            OriginalTitleSelection.ParseLanguages("Klingon").Should().BeEmpty();
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void should_return_no_languages_when_unconfigured(string configured)
        {
            OriginalTitleSelection.ParseLanguages(configured).Should().BeEmpty();
        }

        [Test]
        public void should_not_swap_duplicate_languages()
        {
            OriginalTitleSelection.ParseLanguages("Russian,ru,rus")
                                  .Should().Equal(Language.Russian);
        }

        [Test]
        public void should_swap_when_original_language_is_configured()
        {
            var result = OriginalTitleSelection.Select("Silver Spoon (2014)", "Мажор", Language.Russian, new[] { Language.Russian });

            result.Title.Should().Be("Мажор");
            result.AlternateTitle.Should().Be("Silver Spoon (2014)");
        }

        [Test]
        public void should_not_swap_when_original_language_is_not_configured()
        {
            var result = OriginalTitleSelection.Select("SPY x FAMILY", "SPY × FAMILY", Language.Japanese, new[] { Language.Russian });

            result.Title.Should().Be("SPY x FAMILY");
            result.AlternateTitle.Should().Be("SPY × FAMILY");
        }

        [Test]
        public void should_not_swap_when_no_languages_configured()
        {
            var result = OriginalTitleSelection.Select("Silver Spoon (2014)", "Мажор", Language.Russian, Enumerable.Empty<Language>().ToList());

            result.Title.Should().Be("Silver Spoon (2014)");
            result.AlternateTitle.Should().Be("Мажор");
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void should_not_swap_when_original_title_is_missing(string originalTitle)
        {
            var result = OriginalTitleSelection.Select("Silver Spoon (2014)", originalTitle, Language.Russian, new[] { Language.Russian });

            result.Title.Should().Be("Silver Spoon (2014)");
            result.AlternateTitle.Should().Be(originalTitle);
        }

        [Test]
        public void should_not_swap_when_titles_only_differ_by_case()
        {
            var result = OriginalTitleSelection.Select("Mashle", "mashle", Language.Russian, new[] { Language.Russian });

            result.Title.Should().Be("Mashle");
            result.AlternateTitle.Should().Be("mashle");
        }

        [Test]
        public void should_not_swap_when_original_language_is_unknown()
        {
            var result = OriginalTitleSelection.Select("Silver Spoon (2014)", "Мажор", null, new[] { Language.Russian });

            result.Title.Should().Be("Silver Spoon (2014)");
            result.AlternateTitle.Should().Be("Мажор");
        }
    }
}
