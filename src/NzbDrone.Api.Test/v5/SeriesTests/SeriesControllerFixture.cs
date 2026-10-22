using System;
using System.Collections.Generic;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
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

namespace NzbDrone.Api.Test.v5.SeriesTests
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

            Mocker.GetMock<ISeriesService>()
                .Setup(s => s.UpdateSeries(It.IsAny<CoreSeries>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
                .Callback((CoreSeries series, string sourcePath, string destinationPath, bool moveFiles) =>
                    Subject.Handle(new SeriesEditedEvent(series, _series)))
                .Returns((CoreSeries series, string sourcePath, string destinationPath, bool moveFiles) => series);

            Mocker.GetMock<ISeriesStatisticsService>()
                .Setup(s => s.SeriesStatistics(_series.Id, It.IsAny<int>()))
                .Returns(new SeriesStatistics());

            Mocker.GetMock<ISeriesTranslationService>()
                .Setup(s => s.GetTranslations(_series.Id))
                .Returns(new List<SeriesTranslation>());

            Mocker.GetMock<IMapCoversToLocal>()
                .Setup(s => s.ConvertToLocalUrls(It.IsAny<int>(), It.IsAny<IEnumerable<MediaCover>>(), It.IsAny<DateTime?>()))
                .Callback<int, IEnumerable<MediaCover>, DateTime?>((seriesId, covers, lastInfoSync) =>
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

        private List<SeriesResource> BroadcastResources()
        {
            _broadcasts.Should().NotBeEmpty();

            return _broadcasts.ConvertAll(b => ((ResourceChangeMessage<SeriesResource>)b.Body).Resource);
        }

        [Test]
        public void should_broadcast_covers_mapped_to_local_urls_when_season_monitoring_is_toggled()
        {
            Subject.UpdateSeasonMonitored(_series.Id, new SeasonResource { SeasonNumber = 1, Monitored = false });

            BroadcastResources().Should().OnlyContain(r => r.Images.TrueForAll(i => i.Url != null));
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

            // UpdateSeries builds the Accepted location with the controller's url helper.
            Subject.Url = Mock.Of<IUrlHelper>();

            Subject.UpdateSeries(submitted);

            // Title isn't applied by Series.ApplyChanges, so it must not reach other clients.
            BroadcastResources().Should().OnlyContain(r => r.Title == _series.Title && r.Images.TrueForAll(i => i.Url != null));
        }
    }
}
