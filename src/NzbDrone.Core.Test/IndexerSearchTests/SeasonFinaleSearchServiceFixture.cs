using System;
using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.IndexerSearch;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.IndexerSearchTests
{
    [TestFixture]
    public class SeasonFinaleSearchServiceFixture : CoreTest<SeasonFinaleSearchService>
    {
        private DateTime _lastExecution;
        private List<Episode> _season;

        [SetUp]
        public void Setup()
        {
            _lastExecution = DateTime.UtcNow.AddHours(-1);

            _season = Builder<Episode>.CreateListOfSize(3)
                                      .All()
                                      .With(e => e.SeriesId = 1)
                                      .With(e => e.SeasonNumber = 2)
                                      .With(e => e.AirDateUtc = DateTime.UtcNow.AddDays(-14))
                                      .TheLast(1)
                                      .With(e => e.AirDateUtc = DateTime.UtcNow.AddMinutes(-30))
                                      .BuildList();

            Mocker.GetMock<IEpisodeService>()
                  .Setup(s => s.EpisodesBetweenDates(It.IsAny<DateTime>(), It.IsAny<DateTime>(), false, true))
                  .Returns(() => _season.Where(e => e.AirDateUtc >= _lastExecution).ToList());

            Mocker.GetMock<IEpisodeService>()
                  .Setup(s => s.GetEpisodesBySeason(1, 2))
                  .Returns(() => _season);
        }

        private void Execute()
        {
            Subject.Execute(new SeasonFinaleSearchCommand { LastExecutionTime = _lastExecution });
        }

        private void VerifySeasonSearches(Times times)
        {
            Mocker.GetMock<IManageCommandQueue>()
                  .Verify(v => v.Push(It.Is<SeasonSearchCommand>(c => c.SeriesId == 1 && c.SeasonNumber == 2),
                                      It.IsAny<CommandPriority>(),
                                      It.IsAny<CommandTrigger>()),
                          times);
        }

        [Test]
        public void should_search_season_whose_finale_aired_since_the_last_run()
        {
            Execute();

            VerifySeasonSearches(Times.Once());
        }

        [Test]
        public void should_not_search_when_a_later_episode_has_not_aired()
        {
            _season.Add(Builder<Episode>.CreateNew()
                                        .With(e => e.SeriesId = 1)
                                        .With(e => e.SeasonNumber = 2)
                                        .With(e => e.AirDateUtc = DateTime.UtcNow.AddDays(7))
                                        .Build());

            Execute();

            VerifySeasonSearches(Times.Never());
        }

        [Test]
        public void should_not_search_when_a_later_episode_has_no_air_date()
        {
            _season.Add(Builder<Episode>.CreateNew()
                                        .With(e => e.SeriesId = 1)
                                        .With(e => e.SeasonNumber = 2)
                                        .With(e => e.AirDateUtc = null)
                                        .Build());

            Execute();

            VerifySeasonSearches(Times.Never());
        }

        [Test]
        public void should_search_a_season_once_when_several_episodes_aired_together()
        {
            _season.ForEach(e => e.AirDateUtc = DateTime.UtcNow.AddMinutes(-30));

            Execute();

            VerifySeasonSearches(Times.Once());
        }

        [Test]
        public void should_not_search_when_nothing_aired_since_the_last_run()
        {
            _season.Last().AirDateUtc = DateTime.UtcNow.AddDays(-2);

            Execute();

            VerifySeasonSearches(Times.Never());
        }
    }
}
