using ElevenLabs.Voices;
using Newtonsoft.Json;
using System.Net;
using System.Net.Http.Headers;

namespace RoleplayingVoiceCore {
    public class NPCVoiceManager {
        Dictionary<string, string> _characterToVoicePairing = new Dictionary<string, string>();
        private Dictionary<string, VoiceLinePriority> _characterToCacheType = new Dictionary<string, VoiceLinePriority>();

        public NPCVoiceManager(Dictionary<string, string> characterToVoicePairing, Dictionary<string, VoiceLinePriority> characterToCacheType) {
            _characterToVoicePairing = characterToVoicePairing;
            _characterToCacheType = characterToCacheType;
        }
        public enum VoiceModel {
            Quality,
            Speed,
            Cheap,
        }
        public async Task<KeyValuePair<Stream, bool>> GetCharacterAudio(string text, string originalValue, string character,
            bool gender, string backupVoice = "", bool aggressiveCache = false, VoiceModel voiceModel = VoiceModel.Speed, string extraJson = "", bool redoLine = false, bool overrideGeneration = false) {
            try {
                string currentCharacter = "none";
                foreach (var pair in _characterToVoicePairing) {
                    if (character.StartsWith(pair.Key) || character.EndsWith(pair.Key)) {
                        currentCharacter = pair.Key;
                        break;
                    }
                }
                if (_characterToVoicePairing.ContainsKey(currentCharacter)) {
                    VoiceLinePriority voiceLinePriority = VoiceLinePriority.Elevenlabs;
                    if (_characterToCacheType.ContainsKey(currentCharacter)) {
                        voiceLinePriority = _characterToCacheType[currentCharacter];
                    }
                    ProxiedVoiceRequest proxiedVoiceRequest = new ProxiedVoiceRequest() {
                        Voice = _characterToVoicePairing[currentCharacter],
                        Text = text,
                        UnfilteredText = originalValue,
                        Model = "quality",
                        Character = character,
                        AggressiveCache = aggressiveCache,
                        RedoLine = redoLine,
                        ExtraJsonData = extraJson,
                        Override = overrideGeneration,
                        VoiceLinePriority = voiceLinePriority,
                    };
                    using (HttpClient httpClient = new HttpClient()) {
                        httpClient.BaseAddress = new Uri("https://ai.hubujubu.com:5697");
                        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                        httpClient.Timeout = new TimeSpan(0, 6, 0);
                        var post = await httpClient.PostAsync(httpClient.BaseAddress, new StringContent(JsonConvert.SerializeObject(proxiedVoiceRequest)));
                        if (post.StatusCode != HttpStatusCode.OK) {
                            return new KeyValuePair<Stream, bool>(null, false);
                        }
                        var result = await post.Content.ReadAsStreamAsync();
                        MemoryStream memoryStream = new MemoryStream();
                        await result.CopyToAsync(memoryStream);
                        await result.FlushAsync();
                        memoryStream.Position = 0;
                        return new KeyValuePair<Stream, bool>(memoryStream, true);
                    }
                } else {
                    ProxiedVoiceRequest elevenLabsRequest = new ProxiedVoiceRequest() {
                        Voice = !string.IsNullOrEmpty(backupVoice) ? backupVoice : PickVoiceBasedOnNameAndGender(character, gender),
                        Text = text, Model = voiceModel.ToString().ToLower(),
                        UnfilteredText = originalValue,
                        Character = character,
                        AggressiveCache = aggressiveCache,
                        RedoLine = redoLine,
                        ExtraJsonData = extraJson,
                    };
                    using (HttpClient httpClient = new HttpClient()) {
                        httpClient.BaseAddress = new Uri("https://ai.hubujubu.com:5697");
                        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                        httpClient.Timeout = new TimeSpan(0, 6, 0);
                        var post = await httpClient.PostAsync(httpClient.BaseAddress, new StringContent(JsonConvert.SerializeObject(elevenLabsRequest)));
                        if (post.StatusCode != HttpStatusCode.OK) {
                            return new KeyValuePair<Stream, bool>(null, false);
                        }
                        var result = await post.Content.ReadAsStreamAsync();
                        MemoryStream memoryStream = new MemoryStream();
                        await result.CopyToAsync(memoryStream);
                        await result.FlushAsync();
                        memoryStream.Position = 0;
                        return new KeyValuePair<Stream, bool>(memoryStream, false);
                    }
                }
            } catch {
                return new KeyValuePair<Stream, bool>(null, false);
            }
        }

        private string PickVoiceBasedOnNameAndGender(string character, bool gender) {
            if (!string.IsNullOrEmpty(character)) {
                Random random = new Random(GetSimpleHash(character));
                return !gender ? PickMaleVoice(random.Next(0, 2)) : PickFemaleVoice(random.Next(0, 2));
            } else {
                return "Bella";
            }
        }
        private static int GetSimpleHash(string s) {
            return s.Select(a => (int)a).Sum();
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
