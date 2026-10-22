using System;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.IndexerSearch
{
    // Fork: SeasonFullyAiredSpecification holds back every release until a season has
    // fully aired, and RSS only sees new posts, so episodes posted while the season was
    // airing would never be grabbed. Search each season once, when its finale airs.
    public class SeasonFinaleSearchService : IExecute<SeasonFinaleSearchCommand>
    {
        public const int IntervalMinutes = 60;

        private readonly IEpisodeService _episodeService;
        private readonly IManageCommandQueue _commandQueueManager;
        private readonly Logger _logger;

        public SeasonFinaleSearchService(IEpisodeService episodeService, IManageCommandQueue commandQueueManager, Logger logger)
        {
            _episodeService = episodeService;
            _commandQueueManager = commandQueueManager;
            _logger = logger;
        }

        public void Execute(SeasonFinaleSearchCommand message)
        {
            var now = DateTime.UtcNow;
            var since = message.LastExecutionTime ?? now.AddMinutes(-IntervalMinutes);

            var seasons = _episodeService.EpisodesBetweenDates(since, now, false, true)
                                         .Select(e => new { e.SeriesId, e.SeasonNumber })
                                         .Distinct();

            foreach (var season in seasons)
            {
                var episodes = _episodeService.GetEpisodesBySeason(season.SeriesId, season.SeasonNumber);

                if (episodes.Any(e => !e.AirDateUtc.HasValue || e.AirDateUtc.Value.After(now)))
                {
                    continue;
                }

                _logger.Info("Season {0} of series {1} has finished airing, searching for it", season.SeasonNumber, season.SeriesId);

                _commandQueueManager.Push(new SeasonSearchCommand
                {
                    SeriesId = season.SeriesId,
                    SeasonNumber = season.SeasonNumber
                });
            }
        }
    }
}
