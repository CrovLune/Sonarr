using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using NLog;
using NzbDrone.Common.Cloud;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.DataAugmentation.DailySeries;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MetadataSource.SkyHook.Resource;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.MetadataSource.SkyHook
{
    public class SkyHookProxy : IProvideSeriesInfo, ISearchForNewSeries
    {
        private readonly IHttpClient _httpClient;
        private readonly IConfigService _configService;
        private readonly Logger _logger;
        private readonly ISeriesService _seriesService;
        private readonly IDailySeriesService _dailySeriesService;
        private readonly IHttpRequestBuilderFactory _requestBuilder;
        private readonly Dictionary<int, string> _tmdbOriginalTitleCache = new();

        public SkyHookProxy(IHttpClient httpClient,
                            ISonarrCloudRequestBuilder requestBuilder,
                            ISeriesService seriesService,
                            IDailySeriesService dailySeriesService,
                            IConfigService configService,
                            Logger logger)
        {
            _httpClient = httpClient;
            _configService = configService;
            _requestBuilder = requestBuilder.SkyHookTvdb;
            _logger = logger;
            _seriesService = seriesService;
            _dailySeriesService = dailySeriesService;
            _requestBuilder = requestBuilder.SkyHookTvdb;
        }

        public Tuple<Series, List<Episode>> GetSeriesInfo(int tvdbSeriesId)
        {
            var httpRequest = _requestBuilder.Create()
                                             .SetSegment("route", "shows")
                                             .Resource(tvdbSeriesId.ToString())
                                             .Build();

            httpRequest.AllowAutoRedirect = true;
            httpRequest.SuppressHttpError = true;

            var httpResponse = _httpClient.Get<ShowResource>(httpRequest);

            if (httpResponse.HasHttpError)
            {
                if (httpResponse.StatusCode == HttpStatusCode.NotFound)
                {
                    throw new SeriesNotFoundException(tvdbSeriesId);
                }
                else
                {
                    throw new HttpException(httpRequest, httpResponse);
                }
            }

            var series = MapSeries(httpResponse.Resource);

            var configuredLanguages = OriginalTitleSelection.ParseLanguages(_configService.OriginalTitleLanguages);
            var supplementaryEpisodes = FetchEpisodesFromTmdb(httpResponse.Resource, series.OriginalLanguage, configuredLanguages);
            var utcNow = DateTime.UtcNow;

            var episodes = httpResponse.Resource.Episodes
                                                .Select(episode => MapEpisode(episode, series.OriginalLanguage, configuredLanguages, supplementaryEpisodes, utcNow))
                                                .ToList();

            return new Tuple<Series, List<Episode>>(series, episodes);
        }

        public List<Series> SearchForNewSeriesByImdbId(string imdbId)
        {
            imdbId = Parser.Parser.NormalizeImdbId(imdbId);

            if (imdbId == null)
            {
                return new List<Series>();
            }

            var results = SearchForNewSeries($"imdb:{imdbId}");

            return results;
        }

        public List<Series> SearchForNewSeriesByAniListId(int aniListId)
        {
            var results = SearchForNewSeries($"anilist:{aniListId}");

            return results;
        }

        public List<Series> SearchForNewSeriesByMyAnimeListId(int malId)
        {
            var results = SearchForNewSeries($"mal:{malId}");

            return results;
        }

        public List<Series> SearchForNewSeriesByTmdbId(int tmdbId)
        {
            var results = SearchForNewSeries($"tmdb:{tmdbId}");

            return results;
        }

        public List<Series> SearchForNewSeries(string title)
        {
            if (title.IsPathValid(PathValidationType.AnyOs))
            {
                throw new InvalidSearchTermException("Invalid search term '{0}'", title);
            }

            try
            {
                var lowerTitle = title.ToLowerInvariant();

                if (lowerTitle.StartsWith("tvdb:") || lowerTitle.StartsWith("tvdbid:"))
                {
                    var slug = lowerTitle.Split(':')[1].Trim();

                    if (slug.IsNullOrWhiteSpace() || slug.Any(char.IsWhiteSpace) || !int.TryParse(slug, out var tvdbId) || tvdbId <= 0)
                    {
                        return new List<Series>();
                    }

                    try
                    {
                        var existingSeries = _seriesService.FindByTvdbId(tvdbId);
                        if (existingSeries != null)
                        {
                            return new List<Series> { existingSeries };
                        }

                        return new List<Series> { GetSeriesInfo(tvdbId).Item1 };
                    }
                    catch (SeriesNotFoundException)
                    {
                        return new List<Series>();
                    }
                }

                var httpRequest = _requestBuilder.Create()
                                                 .SetSegment("route", "search")
                                                 .AddQueryParam("term", title.ToLower().Trim())
                                                 .Build();

                var httpResponse = _httpClient.Get<List<ShowResource>>(httpRequest);

                return httpResponse.Resource.SelectList(MapSearchResult);
            }
            catch (HttpException ex)
            {
                _logger.Warn(ex);
                throw new SkyHookException("Search for '{0}' failed. Unable to communicate with SkyHook. {1}", ex, title, ex.Message);
            }
            catch (WebException ex)
            {
                _logger.Warn(ex);
                throw new SkyHookException("Search for '{0}' failed. Unable to communicate with SkyHook. {1}", ex, title, ex.Message);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex);
                throw new SkyHookException("Search for '{0}' failed. Invalid response received from SkyHook. {1}", ex, title, ex.Message);
            }
        }

        private Series MapSearchResult(ShowResource show)
        {
            var series = _seriesService.FindByTvdbId(show.TvdbId);

            if (series == null)
            {
                series = MapSeries(show);
            }

            return series;
        }

        private Series MapSeries(ShowResource show)
        {
            var series = new Series();
            series.TvdbId = show.TvdbId;

            if (show.TvRageId.HasValue)
            {
                series.TvRageId = show.TvRageId.Value;
            }

            if (show.TvMazeId.HasValue)
            {
                series.TvMazeId = show.TvMazeId.Value;
            }

            if (show.TmdbId.HasValue)
            {
                series.TmdbId = show.TmdbId.Value;
            }

            series.ImdbId = show.ImdbId;
            series.MalIds = show.MalIds;
            series.AniListIds = show.AniListIds;

            // Resolved before the titles because the title choice depends on it.
            series.OriginalLanguage = show.OriginalLanguage.IsNotNullOrWhiteSpace() ?
                IsoLanguages.Find(show.OriginalLanguage.ToLower())?.Language ?? Language.English :
                Language.English;

            var titles = OriginalTitleSelection.Select(
                show.Title,
                show.OriginalTitle ?? FetchOriginalTitleFromTmdb(show),
                series.OriginalLanguage,
                OriginalTitleSelection.ParseLanguages(_configService.OriginalTitleLanguages));

            series.Title = titles.Title;
            series.OriginalTitle = titles.AlternateTitle;
            series.CleanOriginalTitle = titles.AlternateTitle.IsNotNullOrWhiteSpace()
                ? Parser.Parser.CleanSeriesTitle(titles.AlternateTitle)
                : null;
            series.CleanTitle = Parser.Parser.CleanSeriesTitle(titles.Title);
            series.SortTitle = SeriesTitleNormalizer.Normalize(titles.Title, show.TvdbId);

            if (show.FirstAired != null)
            {
                series.FirstAired = DateTime.ParseExact(show.FirstAired, "yyyy-MM-dd", DateTimeFormatInfo.InvariantInfo, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
                series.Year = series.FirstAired.Value.Year;
            }

            if (show.LastAired != null)
            {
                series.LastAired = DateTime.ParseExact(show.LastAired, "yyyy-MM-dd", DateTimeFormatInfo.InvariantInfo, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
            }

            series.Overview = show.Overview;

            if (show.Runtime != null)
            {
                series.Runtime = show.Runtime.Value;
            }

            series.Network = show.Network;

            if (show.TimeOfDay != null)
            {
                series.AirTime = string.Format("{0:00}:{1:00}", show.TimeOfDay.Hours, show.TimeOfDay.Minutes);
            }

            series.TitleSlug = show.Slug;
            series.Status = MapSeriesStatus(show.Status);
            series.Ratings = MapRatings(show.Rating);
            series.Genres = show.Genres;
            series.OriginalCountry = show.OriginalCountry;

            if (show.ContentRating.IsNotNullOrWhiteSpace())
            {
                series.Certification = show.ContentRating.ToUpper();
            }

            if (_dailySeriesService.IsDailySeries(series.TvdbId))
            {
                series.SeriesType = SeriesTypes.Daily;
            }

            series.Actors = show.Actors.Select(MapActors).ToList();
            series.Seasons = show.Seasons.Select(MapSeason).ToList();
            series.Images = show.Images.Select(MapImage).ToList();
            series.Monitored = true;

            return series;
        }

        private static Actor MapActors(ActorResource arg)
        {
            var newActor = new Actor
            {
                Name = arg.Name,
                Character = arg.Character
            };

            if (arg.Image != null)
            {
                newActor.Images = new List<MediaCover.MediaCover>
                {
                    new MediaCover.MediaCover(MediaCoverTypes.Headshot, arg.Image)
                };
            }

            return newActor;
        }

        private static Episode MapEpisode(EpisodeResource oracleEpisode,
                                          Language originalLanguage,
                                          IReadOnlyCollection<Language> languages,
                                          IReadOnlyDictionary<(int Season, int Episode), TmdbEpisodeResource> supplementaryEpisodes,
                                          DateTime utcNow)
        {
            supplementaryEpisodes.TryGetValue((oracleEpisode.SeasonNumber, oracleEpisode.EpisodeNumber), out var supplementary);

            var metadata = EpisodeMetadataSelection.Select(
                oracleEpisode.Title,
                oracleEpisode.Overview,
                supplementary?.Name,
                supplementary?.Overview,
                oracleEpisode.SeasonNumber,
                oracleEpisode.EpisodeNumber,
                originalLanguage,
                languages,
                oracleEpisode.AirDateUtc,
                utcNow);

            var episode = new Episode();
            episode.TvdbId = oracleEpisode.TvdbId;
            episode.Overview = metadata.Overview;
            episode.SeasonNumber = oracleEpisode.SeasonNumber;
            episode.EpisodeNumber = oracleEpisode.EpisodeNumber;
            episode.AbsoluteEpisodeNumber = oracleEpisode.AbsoluteEpisodeNumber;
            episode.Title = metadata.Title;
            episode.AiredAfterSeasonNumber = oracleEpisode.AiredAfterSeasonNumber;
            episode.AiredBeforeSeasonNumber = oracleEpisode.AiredBeforeSeasonNumber;
            episode.AiredBeforeEpisodeNumber = oracleEpisode.AiredBeforeEpisodeNumber;

            episode.AirDate = oracleEpisode.AirDate;
            episode.AirDateUtc = oracleEpisode.AirDateUtc;
            episode.Runtime = oracleEpisode.Runtime;
            episode.FinaleType = oracleEpisode.FinaleType;

            episode.Ratings = MapRatings(oracleEpisode.Rating);

            // Don't include series fanart images as episode screenshot
            if (oracleEpisode.Image != null)
            {
                episode.Images.Add(new MediaCover.MediaCover(MediaCoverTypes.Screenshot, oracleEpisode.Image));
            }

            return episode;
        }

        private static Season MapSeason(SeasonResource seasonResource)
        {
            return new Season
            {
                SeasonNumber = seasonResource.SeasonNumber,
                Images = seasonResource.Images.Select(MapImage).ToList(),
                Monitored = seasonResource.SeasonNumber > 0
            };
        }

        private static SeriesStatusType MapSeriesStatus(string status)
        {
            if (status.Equals("ended", StringComparison.InvariantCultureIgnoreCase))
            {
                return SeriesStatusType.Ended;
            }

            if (status.Equals("upcoming", StringComparison.InvariantCultureIgnoreCase))
            {
                return SeriesStatusType.Upcoming;
            }

            return SeriesStatusType.Continuing;
        }

        private static Ratings MapRatings(RatingResource rating)
        {
            if (rating == null)
            {
                return new Ratings();
            }

            return new Ratings
            {
                Votes = rating.Count,
                Value = rating.Value
            };
        }

        private static MediaCover.MediaCover MapImage(ImageResource arg)
        {
            return new MediaCover.MediaCover
            {
                RemoteUrl = arg.Url,
                CoverType = MapCoverType(arg.CoverType)
            };
        }

        /// <summary>
        /// TheTVDB only serves English, and its English records for series in the configured
        /// languages routinely carry no episode names or overviews at all. TMDB holds that data in
        /// the show's own language, so it is pulled in per season to fill the gaps.
        /// </summary>
        private IReadOnlyDictionary<(int Season, int Episode), TmdbEpisodeResource> FetchEpisodesFromTmdb(ShowResource show, Language originalLanguage, IReadOnlyCollection<Language> languages)
        {
            var supplementary = new Dictionary<(int Season, int Episode), TmdbEpisodeResource>();

            if (originalLanguage == null || languages == null || languages.Count == 0 || !languages.Contains(originalLanguage))
            {
                return supplementary;
            }

            var tmdbApiKey = _configService.TmdbApiKey;

            if (tmdbApiKey.IsNullOrWhiteSpace() || !show.TmdbId.HasValue || show.TmdbId.Value <= 0)
            {
                _logger.Debug("FetchEpisodesFromTmdb: Skipping - ApiKey empty: {0} TmdbId: {1}", tmdbApiKey.IsNullOrWhiteSpace(), show.TmdbId);
                return supplementary;
            }

            var isoLanguage = IsoLanguages.Get(originalLanguage);

            if (isoLanguage == null)
            {
                _logger.Debug("FetchEpisodesFromTmdb: No ISO code for '{0}', skipping TMDB lookup", originalLanguage);
                return supplementary;
            }

            var tmdbId = show.TmdbId.Value;

            var seasonNumbers = show.Episodes.Select(episode => episode.SeasonNumber)
                                             .Where(seasonNumber => seasonNumber > 0)
                                             .Distinct()
                                             .OrderBy(seasonNumber => seasonNumber)
                                             .ToList();

            foreach (var seasonNumber in seasonNumbers)
            {
                try
                {
                    var httpRequest = new HttpRequest($"https://api.themoviedb.org/3/tv/{tmdbId}/season/{seasonNumber}?language={isoLanguage.TwoLetterCode}")
                    {
                        AllowAutoRedirect = true,
                        SuppressHttpError = true,

                        // Seasons are fetched one after another, and AddSeriesService discards the
                        // episodes entirely, so a stalled TMDB must not hold up adding a series for
                        // the dispatcher's 100 second default per season.
                        RequestTimeout = TimeSpan.FromSeconds(15)
                    };

                    httpRequest.Headers.Add("Authorization", $"Bearer {tmdbApiKey}");

                    var response = _httpClient.Get<TmdbSeasonResource>(httpRequest);

                    if (response.HasHttpError)
                    {
                        _logger.Warn("TMDB season request failed with status {0} for TmdbId {1} season {2}", response.StatusCode, tmdbId, seasonNumber);
                        continue;
                    }

                    foreach (var episode in response.Resource?.Episodes ?? new List<TmdbEpisodeResource>())
                    {
                        supplementary[(seasonNumber, episode.EpisodeNumber)] = episode;
                    }
                }
                catch (Exception ex)
                {
                    // Supplementary data only - a TMDB outage must not fail the series refresh.
                    _logger.Warn(ex, "Failed to fetch episodes from TMDB for TmdbId {0} season {1}", tmdbId, seasonNumber);
                }
            }

            _logger.Debug("FetchEpisodesFromTmdb: Found {0} episodes across {1} seasons for TmdbId {2}", supplementary.Count, seasonNumbers.Count, tmdbId);

            return supplementary;
        }

        private string FetchOriginalTitleFromTmdb(ShowResource show)
        {
            var tmdbApiKey = _configService.TmdbApiKey;

            _logger.Debug("FetchOriginalTitleFromTmdb: TmdbId={0} OriginalLanguage={1} HasApiKey={2}", show.TmdbId, show.OriginalLanguage, tmdbApiKey.IsNotNullOrWhiteSpace());

            if (tmdbApiKey.IsNullOrWhiteSpace() || !show.TmdbId.HasValue || show.TmdbId.Value <= 0)
            {
                _logger.Debug("FetchOriginalTitleFromTmdb: Skipping - ApiKey empty: {0} TmdbId: {1}", tmdbApiKey.IsNullOrWhiteSpace(), show.TmdbId);
                return null;
            }

            if (show.OriginalLanguage.IsNullOrWhiteSpace() || show.OriginalLanguage == "eng")
            {
                _logger.Debug("FetchOriginalTitleFromTmdb: Skipping - English or no original language");
                return null;
            }

            var isoLanguage = IsoLanguages.Find(show.OriginalLanguage.ToLower());

            if (isoLanguage == null)
            {
                _logger.Debug("Could not find ISO language for '{0}', skipping TMDB lookup", show.OriginalLanguage);
                return null;
            }

            var tmdbId = show.TmdbId.Value;

            if (_tmdbOriginalTitleCache.TryGetValue(tmdbId, out var cachedTitle))
            {
                _logger.Debug("Using cached original title '{0}' for TmdbId {1}", cachedTitle, tmdbId);
                return cachedTitle;
            }

            try
            {
                var httpRequest = new HttpRequest($"https://api.themoviedb.org/3/tv/{tmdbId}")
                {
                    AllowAutoRedirect = true,
                    SuppressHttpError = true
                };

                httpRequest.Headers.Add("Authorization", $"Bearer {tmdbApiKey}");

                var response = _httpClient.Get<TmdbSeriesResource>(httpRequest);

                if (response.HasHttpError)
                {
                    _logger.Warn("TMDB API request failed with status {0} for TmdbId {1}", response.StatusCode, tmdbId);
                    return null;
                }

                var originalName = response.Resource?.OriginalName;

                if (originalName.IsNotNullOrWhiteSpace() &&
                    !string.Equals(originalName, show.Title, StringComparison.InvariantCultureIgnoreCase))
                {
                    _logger.Debug("Found original title '{0}' from TMDB for '{1}'", originalName, show.Title);
                    _tmdbOriginalTitleCache[tmdbId] = originalName;
                    return originalName;
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to fetch original title from TMDB for TmdbId {0}", tmdbId);
            }

            // No language-tagged original title available. Alternative titles carry no
            // language information, so guessing one risks writing an unrelated show's title
            // (and poisoning search with it).
            _tmdbOriginalTitleCache[tmdbId] = null;
            return null;
        }

        private static MediaCoverTypes MapCoverType(string coverType)
        {
            switch (coverType.ToLower())
            {
                case "poster":
                    return MediaCoverTypes.Poster;
                case "banner":
                    return MediaCoverTypes.Banner;
                case "fanart":
                    return MediaCoverTypes.Fanart;
                case "clearlogo":
                    return MediaCoverTypes.Clearlogo;
                default:
                    return MediaCoverTypes.Unknown;
            }
        }
    }
}
