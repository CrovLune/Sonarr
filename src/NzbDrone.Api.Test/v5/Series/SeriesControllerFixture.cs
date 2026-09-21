using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.SeriesStats;
using NzbDrone.Core.Tv;
using NzbDrone.Core.Tv.Events;
using NzbDrone.SignalR;
using NzbDrone.Test.Common;
using Sonarr.Api.V5.Series;
using Sonarr.Http;
using CoreSeries = NzbDrone.Core.Tv.Series;

namespace NzbDrone.Api.Test.v5.Series
{
    [TestFixture]
    public class SeriesControllerFixture : TestBase<SeriesController>
    {
        private CoreSeries _series;
        private List<SignalRMessage> _broadcasts;

        [SetUp]
        public void Setup()
        {
            _broadcasts = new List<SignalRMessage>();

            _series = new CoreSeries
            {
                Id = 12,
                TvdbId = 34,
                Title = "Test Series",
                Path = @"C:\Test\Series".AsOsAgnostic(),
                Images = new List<MediaCover>
                {
                    new MediaCover(MediaCoverTypes.Poster, "https://example.com/poster.jpg")
                },
                Seasons = new List<Season>
                {
                    new Season { SeasonNumber = 1, Monitored = true }
                }
            };

            Mocker.GetMock<ISeriesService>()
                .Setup(s => s.GetSeries(_series.Id))
                .Returns(_series);

            // Persisting a series publishes SeriesEditedEvent synchronously, and handling that
            // event is what broadcasts the change to connected clients.
            Mocker.GetMock<ISeriesService>()
                .Setup(s => s.UpdateSeries(It.IsAny<CoreSeries>(), It.IsAny<bool>(), It.IsAny<bool>()))
                .Callback((CoreSeries series, bool updateEpisodes, bool publishEvent) =>
                    Subject.Handle(new SeriesEditedEvent(series, _series)))
                .Returns((CoreSeries series, bool updateEpisodes, bool publishEvent) => series);

            Mocker.GetMock<ISeriesStatisticsService>()
                .Setup(s => s.SeriesStatistics(_series.Id))
                .Returns(new SeriesStatistics());

            Mocker.GetMock<IMapCoversToLocal>()
                .Setup(s => s.ConvertToLocalUrls(It.IsAny<int>(), It.IsAny<IEnumerable<MediaCover>>()))
                .Callback<int, IEnumerable<MediaCover>>((seriesId, covers) =>
                {
                    foreach (var cover in covers)
                    {
                        cover.Url = $"/MediaCover/{seriesId}/{cover.CoverType.ToString().ToLowerInvariant()}.jpg";
                    }
                });

            Mocker.GetMock<IBroadcastSignalRMessage>()
                .Setup(s => s.IsConnected)
                .Returns(true);

            Mocker.GetMock<IBroadcastSignalRMessage>()
                .Setup(s => s.BroadcastMessage(It.IsAny<SignalRMessage>()))
                .Callback<SignalRMessage>(message => _broadcasts.Add(message));
        }

        private SeriesResource SingleBroadcastResource()
        {
            _broadcasts.Should().HaveCount(1);

            return ((ResourceChangeMessage<SeriesResource>)_broadcasts[0].Body).Resource;
        }

        [Test]
        public void should_broadcast_covers_mapped_to_local_urls_when_season_monitoring_is_toggled()
        {
            Subject.UpdateSeasonMonitored(_series.Id, new SeasonResource { SeasonNumber = 1, Monitored = false });

            SingleBroadcastResource().Images.Should().OnlyContain(i => i.Url != null);
        }

        [Test]
        public void should_not_broadcast_values_that_were_not_persisted_when_updating()
        {
            var submitted = new SeriesResource
            {
                Id = _series.Id,
                Title = "Never Persisted",
                Path = _series.Path,
                Images = new List<MediaCover>
                {
                    new MediaCover(MediaCoverTypes.Poster, "https://example.com/poster.jpg")
                },
                Seasons = new List<SeasonResource>
                {
                    new SeasonResource { SeasonNumber = 1, Monitored = false }
                }
            };

            Subject.UpdateSeries(submitted);

            var resource = SingleBroadcastResource();

            // Title isn't applied by Series.ApplyChanges, so it must not reach other clients.
            resource.Title.Should().Be(_series.Title);
            resource.Images.Should().OnlyContain(i => i.Url != null);
        }
    }
}
