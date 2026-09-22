using System.Collections.Generic;
using Newtonsoft.Json;

namespace NzbDrone.Core.MetadataSource.SkyHook.Resource
{
    public class TmdbSeasonResource
    {
        [JsonProperty("season_number")]
        public int SeasonNumber { get; set; }

        [JsonProperty("episodes")]
        public List<TmdbEpisodeResource> Episodes { get; set; }
    }

    public class TmdbEpisodeResource
    {
        [JsonProperty("season_number")]
        public int SeasonNumber { get; set; }

        [JsonProperty("episode_number")]
        public int EpisodeNumber { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("overview")]
        public string Overview { get; set; }
    }
}
