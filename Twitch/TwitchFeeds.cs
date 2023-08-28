using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace RoleplayingVoiceCore.Twitch {
    public class TwitchFeeds {
        public bool success { get; set; }
        public StreamUrls urls { get; set; }
    }

    public class StreamUrls {
        public string audio_only { get; set; }

        [JsonProperty("160p")]
        public string _160p { get; set; }

        [JsonProperty("360p")]
        public string _360p { get; set; }

        [JsonProperty("480p")]
        public string _480p { get; set; }

        [JsonProperty("720p60")]
        public string _720p60 { get; set; }

        [JsonProperty("1080p60")]
        public string _1080p60 { get; set; }
    }

    public static class TwitchFeedManager {
        public static string GetServerResponse(string url) {
            var httpWebRequest = (HttpWebRequest)WebRequest.Create(@"https://pwn.sh/tools/streamapi.py?url=" + url);
            httpWebRequest.ContentType = "application/json";
            httpWebRequest.Method = "POST";

            var httpResponse = (HttpWebResponse)httpWebRequest.GetResponse();
            using (var streamReader = new StreamReader(httpResponse.GetResponseStream())) {
                var result = streamReader.ReadToEnd();
                var response = JsonConvert.DeserializeObject<TwitchFeeds>(result);
                return response.urls.audio_only;
            }
        }
    }
}
