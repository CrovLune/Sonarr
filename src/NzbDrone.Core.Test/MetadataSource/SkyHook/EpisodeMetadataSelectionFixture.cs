using System;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MetadataSource.SkyHook;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MetadataSource.SkyHook
{
    [TestFixture]
    public class EpisodeMetadataSelectionFixture : TestBase
    {
        private static readonly DateTime Now = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime Aired = new DateTime(2024, 8, 3, 0, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime NotAired = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private static readonly Language[] Configured = { Language.Russian };

        private static EpisodeMetadata Select(
            string metadataTitle,
            string supplementaryTitle,
            DateTime? airDateUtc = null,
            int seasonNumber = 1,
            int episodeNumber = 5,
            Language originalLanguage = null,
            string metadataOverview = null,
            string supplementaryOverview = null)
        {
            return EpisodeMetadataSelection.Select(
                metadataTitle,
                metadataOverview,
                supplementaryTitle,
                supplementaryOverview,
                seasonNumber,
                episodeNumber,
                originalLanguage ?? Language.Russian,
                Configured,
                airDateUtc ?? Aired,
                Now);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("TBA")]
        [TestCase("tba")]
        [TestCase("Episode 1")]
        [TestCase("episode 12")]
        [TestCase("Эпизод 3")]
        [TestCase("Серия 4")]
        [TestCase("Серія 4")]
        public void should_recognise_placeholder_titles(string title)
        {
            EpisodeMetadataSelection.IsPlaceholderTitle(title).Should().BeTrue();
        }

        [TestCase("Зверь")]
        [TestCase("The One Where It Happens")]
        [TestCase("Episode of Doom")]
        [TestCase("Episode")]
        [TestCase("1")]
        public void should_not_treat_real_titles_as_placeholders(string title)
        {
            EpisodeMetadataSelection.IsPlaceholderTitle(title).Should().BeFalse();
        }

        [Test]
        public void should_keep_metadata_title_when_it_is_a_real_title()
        {
            Select("Зверь", "Ignored").Title.Should().Be("Зверь");
        }

        [Test]
        public void should_use_supplementary_title_when_metadata_has_none()
        {
            Select(null, "Зверь").Title.Should().Be("Зверь");
        }

        [Test]
        public void should_use_supplementary_title_when_metadata_title_is_generic()
        {
            Select("Episode 5", "Зверь").Title.Should().Be("Зверь");
        }

        [Test]
        public void should_synthesise_native_placeholder_when_no_source_has_a_title()
        {
            Select(null, null).Title.Should().Be("Серия 5");
        }

        [Test]
        public void should_synthesise_native_placeholder_when_both_sources_are_generic()
        {
            Select("Episode 5", "Эпизод 5").Title.Should().Be("Серия 5");
        }

        [Test]
        public void should_use_language_specific_placeholder()
        {
            var result = EpisodeMetadataSelection.Select(
                null,
                null,
                null,
                null,
                1,
                5,
                Language.Ukrainian,
                new[] { Language.Ukrainian },
                Aired,
                Now);

            result.Title.Should().Be("Серія 5");
        }

        [Test]
        public void should_fall_back_to_english_placeholder_for_other_languages()
        {
            var result = EpisodeMetadataSelection.Select(
                null,
                null,
                null,
                null,
                1,
                5,
                Language.Korean,
                new[] { Language.Korean },
                Aired,
                Now);

            result.Title.Should().Be("Episode 5");
        }

        [Test]
        public void should_not_synthesise_a_title_for_an_episode_that_has_not_aired()
        {
            Select(null, null, NotAired).Title.Should().BeNull();
        }

        [Test]
        public void should_not_synthesise_a_title_when_the_air_date_is_unknown()
        {
            EpisodeMetadataSelection.Select(
                null,
                null,
                null,
                null,
                1,
                5,
                Language.Russian,
                Configured,
                null,
                Now)
                .Title.Should().BeNull();
        }

        [Test]
        public void should_not_synthesise_a_title_for_specials()
        {
            Select(null, null, seasonNumber: 0).Title.Should().BeNull();
        }

        // A numbered metadata title carries no more information than TBA, but it is still better
        // than the TBA that clearing it would produce: RefreshEpisodeService stores a null title as
        // "TBA", and EpisodeTitleSpecification only exempts an episode from the TBA rejection once
        // it has aired more than 48 hours ago, which an episode with no air date never does.
        [Test]
        public void should_keep_a_numbered_metadata_title_when_the_air_date_is_unknown()
        {
            EpisodeMetadataSelection.Select(
                "Серия 5",
                null,
                null,
                null,
                1,
                5,
                Language.Russian,
                Configured,
                null,
                Now)
                .Title.Should().Be("Серия 5");
        }

        [Test]
        public void should_keep_a_numbered_metadata_title_for_specials()
        {
            Select("Episode 3", null, seasonNumber: 0).Title.Should().Be("Episode 3");
        }

        [Test]
        public void should_keep_a_numbered_metadata_title_when_the_episode_has_not_aired()
        {
            Select("Episode 5", null, NotAired).Title.Should().Be("Episode 5");
        }

        [Test]
        public void should_leave_titles_alone_when_original_language_is_not_configured()
        {
            var result = EpisodeMetadataSelection.Select(
                null,
                null,
                "Зверь",
                "Overview",
                1,
                5,
                Language.English,
                Configured,
                Aired,
                Now);

            result.Title.Should().BeNull();
            result.Overview.Should().BeNull();
        }

        [Test]
        public void should_prefer_metadata_overview()
        {
            Select("Зверь", null, metadataOverview: "From TheTVDB", supplementaryOverview: "From TMDB")
                .Overview.Should().Be("From TheTVDB");
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void should_use_supplementary_overview_when_metadata_has_none(string metadataOverview)
        {
            Select("Зверь", null, metadataOverview: metadataOverview, supplementaryOverview: "From TMDB")
                .Overview.Should().Be("From TMDB");
        }

        [Test]
        public void should_keep_overview_null_when_neither_source_has_one()
        {
            Select("Зверь", null).Overview.Should().BeNull();
        }
    }
}
