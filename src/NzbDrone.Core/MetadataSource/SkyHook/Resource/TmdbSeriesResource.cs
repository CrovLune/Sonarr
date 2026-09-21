using Newtonsoft.Json;

namespace NzbDrone.Core.MetadataSource.SkyHook.Resource
{
    public class TmdbSeriesResource
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("original_name")]
        public string OriginalName { get; set; }

        [JsonProperty("original_language")]
        public string OriginalLanguage { get; set; }
    }
}
