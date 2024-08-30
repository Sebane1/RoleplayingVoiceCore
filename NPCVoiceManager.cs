using ElevenLabs.Voices;
using NAudio.Wave;
using Newtonsoft.Json;
using RoleplayingMediaCore.AudioRecycler;
using System.IO;
using System.Net;
using System.Net.Http.Headers;

namespace RoleplayingVoiceCore {
    public class NPCVoiceManager {
        private Dictionary<string, string> _characterToVoicePairing = new Dictionary<string, string>();
        private Dictionary<string, VoiceLinePriority> _characterToCacheType = new Dictionary<string, VoiceLinePriority>();
        private CharacterVoices _characterVoices = new CharacterVoices();
        private string _cachePath = "";
        private string _versionIdentifier;

        public NPCVoiceManager(Dictionary<string, string> characterToVoicePairing, Dictionary<string, VoiceLinePriority> characterToCacheType,
            string cacheLocation, string version) {
            _characterToVoicePairing = characterToVoicePairing;
            _characterToCacheType = characterToCacheType;
            if (cacheLocation != null) {
                _cachePath = Path.Combine(cacheLocation, "NPC Dialogue Cache\\");
                Directory.CreateDirectory(_cachePath);
                string cacheFile = Path.Combine(_cachePath, "cacheIndex.json");
                if (File.Exists(cacheFile)) {
                    try {
                        _characterVoices = JsonConvert.DeserializeObject<CharacterVoices>(cacheFile);
                    } catch {

                    }
                }
            }
            _versionIdentifier = version;
        }
        public enum VoiceModel {
            Quality,
            Speed,
            Cheap,
        }
        public async Task<Tuple<Stream, bool, string>> GetCharacterAudio(string text, string originalValue, string rawText, string character,
            bool gender, string backupVoice = "", bool aggressiveCache = false, VoiceModel voiceModel = VoiceModel.Speed, string extraJson = "", bool redoLine = false, bool overrideGeneration = false, bool useMuteList = false, VoiceLinePriority overrideVoiceLinePriority = VoiceLinePriority.None) {
            MemoryStream memoryStream = new MemoryStream();
            string voiceEngine = "";
            bool succeeded = false;
            try {
                string characterVoice = "none";
                foreach (var pair in _characterToVoicePairing) {
                    if (character.StartsWith(pair.Key) || character.EndsWith(pair.Key)) {
                        characterVoice = pair.Key;
                        break;
                    }
                }
                VoiceLinePriority voiceLinePriority = VoiceLinePriority.None;
                if (_characterToCacheType.ContainsKey(characterVoice)) {
                    voiceLinePriority = _characterToCacheType[characterVoice];
                }
                if (!string.IsNullOrEmpty(_cachePath)) {
                    if (_characterVoices.VoiceCatalogue.ContainsKey(character) && !redoLine) {
                        if (_characterVoices.VoiceCatalogue[character].ContainsKey(text)) {
                            string relativePath = _characterVoices.VoiceCatalogue[character][text];
                            bool needsRefreshing = false;
                            if (voiceLinePriority != VoiceLinePriority.None) {
                                needsRefreshing = _characterVoices.VoiceEngine[character][text] != voiceLinePriority.ToString();
                            }
                            if (overrideVoiceLinePriority != VoiceLinePriority.None) {
                                needsRefreshing = _characterVoices.VoiceEngine[character][text] != overrideVoiceLinePriority.ToString();
                            }
                            if (voiceLinePriority == VoiceLinePriority.Ignore) {
                                needsRefreshing = true;
                            }
                            string fullPath = Path.Combine(_cachePath, relativePath);
                            if (File.Exists(fullPath) && !needsRefreshing) {
                                voiceEngine = _characterVoices.VoiceEngine[character][text];
                                try {
                                    using (FileStream file = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read)) {
                                        await file.CopyToAsync(memoryStream);
                                        memoryStream.Position = 0;
                                        succeeded = true;
                                    }
                                } catch {
                                    needsRefreshing = true;
                                    File.Delete(fullPath);
                                }
                            }
                        }
                    }
                }
                if (!succeeded) {
                    if (_characterToVoicePairing.ContainsKey(characterVoice)) {
                        if (voiceLinePriority == VoiceLinePriority.None) {
                            voiceLinePriority = VoiceLinePriority.ETTS;
                        }
                        ProxiedVoiceRequest proxiedVoiceRequest = new ProxiedVoiceRequest() {
                            Voice = _characterToVoicePairing[characterVoice],
                            Text = text,
                            RawText = rawText,
                            UnfilteredText = originalValue,
                            Model = "quality",
                            Character = character,
                            AggressiveCache = aggressiveCache,
                            RedoLine = redoLine,
                            ExtraJsonData = extraJson,
                            Override = overrideGeneration,
                            VersionIdentifier = _versionIdentifier,
                            UseMuteList = useMuteList,
                            VoiceLinePriority = overrideVoiceLinePriority == VoiceLinePriority.None ? voiceLinePriority : overrideVoiceLinePriority
                        };
                        using (HttpClient httpClient = new HttpClient()) {
                            httpClient.BaseAddress = new Uri("https://ai.hubujubu.com:5697");
                            httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                            httpClient.Timeout = new TimeSpan(0, 6, 0);
                            var post = await httpClient.PostAsync(httpClient.BaseAddress, new StringContent(JsonConvert.SerializeObject(proxiedVoiceRequest)));
                            if (post.StatusCode == HttpStatusCode.OK) {
                                var result = await post.Content.ReadAsStreamAsync();
                                await result.CopyToAsync(memoryStream);
                                await result.FlushAsync();
                                memoryStream.Position = 0;
                                succeeded = true;
                                voiceEngine = post.ReasonPhrase;
                            }
                        }
                    } else {
                        if (voiceLinePriority == VoiceLinePriority.None) {
                            voiceLinePriority = VoiceLinePriority.Alternative;
                        }
                        ProxiedVoiceRequest elevenLabsRequest = new ProxiedVoiceRequest() {
                            Voice = !string.IsNullOrEmpty(backupVoice) ? backupVoice : PickVoiceBasedOnNameAndGender(character, gender),
                            Text = text, Model = voiceModel.ToString().ToLower(),
                            RawText = rawText,
                            UnfilteredText = originalValue,
                            Character = character,
                            AggressiveCache = aggressiveCache,
                            RedoLine = redoLine,
                            ExtraJsonData = extraJson,
                            Override = overrideGeneration,
                            VersionIdentifier = _versionIdentifier,
                            UseMuteList = useMuteList,
                            VoiceLinePriority = overrideVoiceLinePriority == VoiceLinePriority.None ? voiceLinePriority : overrideVoiceLinePriority,
                        };
                        using (HttpClient httpClient = new HttpClient()) {
                            httpClient.BaseAddress = new Uri("https://ai.hubujubu.com:5697");
                            httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                            httpClient.Timeout = new TimeSpan(0, 6, 0);
                            var post = await httpClient.PostAsync(httpClient.BaseAddress, new StringContent(JsonConvert.SerializeObject(elevenLabsRequest)));
                            if (post.StatusCode == HttpStatusCode.OK) {
                                var result = await post.Content.ReadAsStreamAsync();
                                await result.CopyToAsync(memoryStream);
                                await result.FlushAsync();
                                memoryStream.Position = 0;
                                succeeded = true;
                                voiceEngine = post.ReasonPhrase;
                            }
                        }
                    }
                    if (!string.IsNullOrEmpty(_cachePath)) {
                        if (succeeded) {
                            if (voiceEngine != "OK" && voiceEngine != "" || character.ToLower().Contains("narrator")) {
                                if (!_characterVoices.VoiceCatalogue.ContainsKey(character)) {
                                    _characterVoices.VoiceCatalogue[character] = new Dictionary<string, string>();
                                }
                                if (!_characterVoices.VoiceEngine.ContainsKey(character)) {
                                    _characterVoices.VoiceEngine[character] = new Dictionary<string, string>();
                                }
                                if (memoryStream.Length > 0) {
                                    string relativeFolderPath = character + "\\";
                                    string filePath = relativeFolderPath + Guid.NewGuid() + ".mp3";
                                    _characterVoices.VoiceEngine[character][text] = voiceEngine;
                                    if (_characterVoices.VoiceCatalogue[character].ContainsKey(text)) {
                                        File.Delete(Path.Combine(_cachePath, _characterVoices.VoiceCatalogue[character][text]));
                                    }
                                    _characterVoices.VoiceCatalogue[character][text] = filePath;
                                    Directory.CreateDirectory(Path.Combine(_cachePath, relativeFolderPath));
                                    using (FileStream stream = new FileStream(Path.Combine(_cachePath, filePath), FileMode.Create, FileAccess.Write, FileShare.Write)) {
                                        await memoryStream.CopyToAsync(stream);
                                        await memoryStream.FlushAsync();
                                    }
                                    await File.WriteAllTextAsync(Path.Combine(_cachePath, "cacheIndex.json"), JsonConvert.SerializeObject(_characterVoices, Formatting.Indented));
                                }
                            }
                            memoryStream.Position = 0;
                        }
                    }
                }
            } catch {
                return new Tuple<Stream, bool, string>(null, false, "Error");
            }
            return new Tuple<Stream, bool, string>(memoryStream, succeeded, voiceEngine.Replace("Elevenlabs", "ETTS"));
        }

        public WaveStream StreamToFoundationReader(Stream stream) {
            return new StreamMediaFoundationReader(stream);
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
