using System;
using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.DecisionEngineTests
{
    [TestFixture]
    public class SeasonFullyAiredSpecificationFixture : CoreTest<SeasonFullyAiredSpecification>
    {
        private Series _series;
        private List<Episode> _season;
        private RemoteEpisode _remoteEpisode;

        [SetUp]
        public void Setup()
        {
            _series = Builder<Series>.CreateNew().With(s => s.Id = 1234).Build();

            _season = Builder<Episode>.CreateListOfSize(3)
                                      .All()
                                      .With(e => e.SeriesId = _series.Id)
                                      .With(e => e.SeasonNumber = 1)
                                      .With(e => e.AirDateUtc = DateTime.UtcNow.AddDays(-8))
                                      .BuildList();

            _remoteEpisode = new RemoteEpisode
            {
                Series = _series,
                ParsedEpisodeInfo = new ParsedEpisodeInfo(),
                Episodes = new List<Episode> { _season.First() },
                Release = new ReleaseInfo { Title = "Series.Title.S01E01.720p.WEB-DL-RlsGrp" }
            };

            Mocker.GetMock<IEpisodeService>()
                  .Setup(s => s.GetEpisodesBySeason(_series.Id, 1))
                  .Returns(() => _season);
        }

        private ReleaseDecisionInformation SearchInfo(SearchCriteriaBase criteria, bool userInvoked, bool interactive)
        {
            criteria.Episodes = _remoteEpisode.Episodes;
            criteria.UserInvokedSearch = userInvoked;
            criteria.InteractiveSearch = interactive;

            return new ReleaseDecisionInformation(false, criteria);
        }

        [Test]
        public void should_accept_when_every_episode_in_the_season_has_aired()
        {
            Subject.IsSatisfiedBy(_remoteEpisode, new()).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_reject_when_a_later_episode_in_the_season_has_not_aired()
        {
            _season.Last().AirDateUtc = DateTime.UtcNow.AddDays(6);

            var decision = Subject.IsSatisfiedBy(_remoteEpisode, new());

            decision.Accepted.Should().BeFalse();
            decision.Reason.Should().Be(DownloadRejectionReason.SeasonNotFullyAired);
        }

        [Test]
        public void should_reject_when_an_episode_in_the_season_has_no_air_date()
        {
            _season.Last().AirDateUtc = null;

            Subject.IsSatisfiedBy(_remoteEpisode, new()).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_reject_a_season_pack_when_the_season_has_not_finished()
        {
            _season.Last().AirDateUtc = DateTime.UtcNow.AddDays(6);
            _remoteEpisode.ParsedEpisodeInfo.FullSeason = true;
            _remoteEpisode.Episodes = _season.ToList();

            Subject.IsSatisfiedBy(_remoteEpisode, new()).Accepted.Should().BeFalse();
        }

        [TestCase(SeriesTypes.Anime)]
        [TestCase(SeriesTypes.Daily)]
        public void should_apply_to_every_series_type(SeriesTypes seriesType)
        {
            _series.SeriesType = seriesType;
            _season.Last().AirDateUtc = DateTime.UtcNow.AddDays(6);

            Subject.IsSatisfiedBy(_remoteEpisode, new()).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_apply_to_specials()
        {
            _season.ForEach(e => e.SeasonNumber = 0);
            _season.Last().AirDateUtc = DateTime.UtcNow.AddDays(6);

            Mocker.GetMock<IEpisodeService>()
                  .Setup(s => s.GetEpisodesBySeason(_series.Id, 0))
                  .Returns(_season);

            Subject.IsSatisfiedBy(_remoteEpisode, new()).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_reject_a_multi_season_release_when_any_season_has_not_finished()
        {
            var secondSeason = Builder<Episode>.CreateListOfSize(2)
                                               .All()
                                               .With(e => e.SeriesId = _series.Id)
                                               .With(e => e.SeasonNumber = 2)
                                               .With(e => e.AirDateUtc = DateTime.UtcNow.AddDays(-1))
                                               .TheLast(1)
                                               .With(e => e.AirDateUtc = DateTime.UtcNow.AddDays(6))
                                               .BuildList();

            Mocker.GetMock<IEpisodeService>()
                  .Setup(s => s.GetEpisodesBySeason(_series.Id, 2))
                  .Returns(secondSeason);

            _remoteEpisode.Episodes = _season.Concat(secondSeason.Take(1)).ToList();

            Subject.IsSatisfiedBy(_remoteEpisode, new()).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_reject_during_an_automatic_season_search()
        {
            _season.Last().AirDateUtc = DateTime.UtcNow.AddDays(6);

            var info = SearchInfo(new SeasonSearchCriteria { SeasonNumber = 1 }, false, false);

            Subject.IsSatisfiedBy(_remoteEpisode, info).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_reject_during_a_user_invoked_season_search()
        {
            _season.Last().AirDateUtc = DateTime.UtcNow.AddDays(6);
            _remoteEpisode.Episodes = _season.Take(2).ToList();

            var info = SearchInfo(new SeasonSearchCriteria { SeasonNumber = 1 }, true, false);

            Subject.IsSatisfiedBy(_remoteEpisode, info).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_accept_during_a_user_invoked_single_episode_search()
        {
            _season.Last().AirDateUtc = DateTime.UtcNow.AddDays(6);

            var info = SearchInfo(new SingleEpisodeSearchCriteria(), true, false);

            Subject.IsSatisfiedBy(_remoteEpisode, info).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_accept_during_an_interactive_search()
        {
            _season.Last().AirDateUtc = DateTime.UtcNow.AddDays(6);
            _remoteEpisode.Episodes = _season.Take(2).ToList();

            var info = SearchInfo(new SeasonSearchCriteria { SeasonNumber = 1 }, true, true);

            Subject.IsSatisfiedBy(_remoteEpisode, info).Accepted.Should().BeTrue();
        }
    }
}
