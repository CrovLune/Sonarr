using System;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.DecisionEngine.Specifications
{
    // Fork: don't grab anything from a season until every episode in it has aired,
    // so a season is downloaded all at once instead of episode by episode.
    public class SeasonFullyAiredSpecification : IDownloadDecisionEngineSpecification
    {
        private readonly IEpisodeService _episodeService;
        private readonly Logger _logger;

        public SeasonFullyAiredSpecification(IEpisodeService episodeService, Logger logger)
        {
            _episodeService = episodeService;
            _logger = logger;
        }

        public SpecificationPriority Priority => SpecificationPriority.Database;
        public RejectionType Type => RejectionType.Permanent;

        public virtual DownloadSpecDecision IsSatisfiedBy(RemoteEpisode subject, ReleaseDecisionInformation information)
        {
            var searchCriteria = information.SearchCriteria;

            if (searchCriteria != null)
            {
                if (searchCriteria.InteractiveSearch)
                {
                    _logger.Debug("Skipping season aired check during interactive search");
                    return DownloadSpecDecision.Accept();
                }

                if (searchCriteria.UserInvokedSearch && searchCriteria.Episodes.Count == 1)
                {
                    _logger.Debug("Skipping season aired check during user invoked episode search");
                    return DownloadSpecDecision.Accept();
                }
            }

            foreach (var seasonNumber in subject.Episodes.Select(e => e.SeasonNumber).Distinct())
            {
                var episodes = _episodeService.GetEpisodesBySeason(subject.Series.Id, seasonNumber);

                if (episodes.Any(e => !e.AirDateUtc.HasValue || e.AirDateUtc.Value.After(DateTime.UtcNow)))
                {
                    _logger.Debug("Release {0} rejected, season {1} has not fully aired", subject.Release.Title, seasonNumber);
                    return DownloadSpecDecision.Reject(DownloadRejectionReason.SeasonNotFullyAired, "Season {0} has not fully aired", seasonNumber);
                }
            }

            return DownloadSpecDecision.Accept();
        }
    }
}
