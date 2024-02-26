using ElevenLabs.Voices;
using Newtonsoft.Json;
using System.Net;
using System.Net.Http.Headers;

namespace RoleplayingVoiceCore {
    public class NPCVoiceManager {
        Dictionary<string, string> _characterToVoicePairing = new Dictionary<string, string>();

        public NPCVoiceManager(Dictionary<string, string> characterToVoicePairing) {
            _characterToVoicePairing = characterToVoicePairing;
        }

        public async Task<KeyValuePair<Stream, bool>> GetCharacterAudio(string text, string character, bool gender, string backupVoice = "") {
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
                            return new KeyValuePair<Stream, bool>(null, false);
                        }
                        var result = await post.Content.ReadAsStreamAsync();
                        return new KeyValuePair<Stream, bool>(result, true);
                    }
                } else {
                    ProxiedVoiceRequest elevenLabsRequest = new ProxiedVoiceRequest() { Voice = !string.IsNullOrEmpty(backupVoice) ? backupVoice : PickVoiceBasedOnNameAndGender(character, gender), Text = text, Model = "quality" };
                    using (HttpClient httpClient = new HttpClient()) {
                        httpClient.BaseAddress = new Uri("https://ai.hubujubu.com:5697");
                        //httpClient.DefaultRequestHeaders.Accept.Clear();
                        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                        var post = await httpClient.PostAsync(httpClient.BaseAddress, new StringContent(JsonConvert.SerializeObject(elevenLabsRequest)));
                        if (post.StatusCode != HttpStatusCode.OK) {
                            return new KeyValuePair<Stream, bool>(null, false);
                        }
                        var result = await post.Content.ReadAsStreamAsync();
                        return new KeyValuePair<Stream, bool>(result, false);
                    }
                }
            } catch {
                return new KeyValuePair<Stream, bool>(null, false);
            }
        }

        private string PickVoiceBasedOnNameAndGender(string character, bool gender) {
            if (!string.IsNullOrEmpty(character)) {
                Random random = new Random(character.GetHashCode());
                return !gender ? PickMaleVoice(random.Next(0, 2)) : PickFemaleVoice(random.Next(0, 2));
            } else {
                return "Sys";
            }
        }
        public string PickMaleVoice(int voice) {
            string[] voices = new string[] {
                "Mciv",
                "Zin"
            };
            return voices[voice];
        }
        public string PickFemaleVoice(int voice) {
            string[] voices = new string[] {
                "Maiden",
                "Dla"
            };
            return voices[voice];
        }
    }
}
