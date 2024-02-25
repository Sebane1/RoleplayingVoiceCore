using Newtonsoft.Json;
using System.Net;
using System.Net.Http.Headers;

namespace RoleplayingVoiceCore {
    public class NPCVoiceManager {
        Dictionary<string, string> _characterToVoicePairing = new Dictionary<string, string>();

        public NPCVoiceManager(Dictionary<string, string> characterToVoicePairing) {
            _characterToVoicePairing = characterToVoicePairing;
        }

        public async Task<Stream> GetCharacterAudio(string text, string character, bool gender) {
            try {
                string selectedVoice = "none";
                foreach (var pair in _characterToVoicePairing) {
                    if (character.StartsWith(pair.Key) || character.EndsWith(pair.Key)) {
                        selectedVoice = pair.Key;
                        break;
                    }
                }
                if (_characterToVoicePairing.ContainsKey(selectedVoice)) {
                    ProxiedVoiceRequest proxiedVoiceRequest = new ProxiedVoiceRequest() { Voice = _characterToVoicePairing[character], Text = text, Model = "quality" };
                    using (HttpClient httpClient = new HttpClient()) {
                        httpClient.BaseAddress = new Uri("https://ai.hubujubu.com:5697");
                        //httpClient.DefaultRequestHeaders.Accept.Clear();
                        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                        var post = await httpClient.PostAsync(httpClient.BaseAddress, new StringContent(JsonConvert.SerializeObject(proxiedVoiceRequest)));
                        if (post.StatusCode != HttpStatusCode.OK) {
                            return null;
                        }
                        var result = await post.Content.ReadAsStreamAsync();
                        return result;
                    }
                } else {
                    ProxiedVoiceRequest elevenLabsRequest = new ProxiedVoiceRequest() { Voice = !gender ? "Mciv" : "Maiden", Text = text, Model = "quality" };
                    using (HttpClient httpClient = new HttpClient()) {
                        httpClient.BaseAddress = new Uri("https://ai.hubujubu.com:5697");
                        //httpClient.DefaultRequestHeaders.Accept.Clear();
                        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                        var post = await httpClient.PostAsync(httpClient.BaseAddress, new StringContent(JsonConvert.SerializeObject(elevenLabsRequest)));
                        if (post.StatusCode != HttpStatusCode.OK) {
                            return null;
                        }
                        var result = await post.Content.ReadAsStreamAsync();
                        return result;
                    }
                }
            } catch {
                return null;
            }
        }
    }
}
