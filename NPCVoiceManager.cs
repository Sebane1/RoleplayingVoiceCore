using CachedTTSRelay;
using ElevenLabs.Voices;
using NAudio.Wave;
using Newtonsoft.Json;
using RoleplayingMediaCore.AudioRecycler;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http.Headers;

namespace RoleplayingVoiceCore {
    public class NPCVoiceManager {
        private bool _useCustomRelayServer = false;
        private Dictionary<string, string> _characterToVoicePairing = new Dictionary<string, string>();
        private Dictionary<string, VoiceLinePriority> _characterToCacheType = new Dictionary<string, VoiceLinePriority>();
        private string _cacheLocation;
        private CharacterVoices _characterVoices = new CharacterVoices();
        private string _cachePath = "";
        private string _versionIdentifier;
        private string _customRelayServer;
        private string _port = "5670";
        private string _currentServerAlias;
        Stopwatch cacheTimer = new Stopwatch();

        public bool UseCustomRelayServer { get => _useCustomRelayServer; set => _useCustomRelayServer = value; }
        public string CustomRelayServer { get => _customRelayServer; set => _customRelayServer = value; }
        public string Port { get => _port; set => _port = value; }
        public string CurrentServerAlias { get => _currentServerAlias; set => _currentServerAlias = value; }

        public NPCVoiceManager(Dictionary<string, string> characterToVoicePairing, Dictionary<string, VoiceLinePriority> characterToCacheType,
            string cacheLocation, string version, bool isAServer) {
            _characterToVoicePairing = characterToVoicePairing;
            _characterToCacheType = characterToCacheType;
            _cacheLocation = cacheLocation;
            RefreshCache(cacheLocation);
            _versionIdentifier = version;
            cacheTimer.Start();
            if (!isAServer) {
                GetCloserServerHost();
            }
        }
        public void GetCloserServerHost() {
            _ = Task.Run(async () => {
                Console.WriteLine("Aquiring Nearest Server");
                ClientRegistrationRequest clientRegistrationRequest = new ClientRegistrationRequest();
                using (HttpClient httpClient = new HttpClient()) {
                    httpClient.BaseAddress = new Uri("http://ai.hubujubu.com:5677");
                    httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                    httpClient.Timeout = new TimeSpan(0, 6, 0);
                    var post = await httpClient.PostAsync(httpClient.BaseAddress, new StringContent(JsonConvert.SerializeObject(clientRegistrationRequest)));
                    if (post.StatusCode == HttpStatusCode.OK) {
                        string value = await post.Content.ReadAsStringAsync();
                        ServerRegistrationRequest response = JsonConvert.DeserializeObject<ServerRegistrationRequest>(value);
                        _useCustomRelayServer = true;
                        _customRelayServer = response.PublicHostAddress;
                        _port = response.Port;
                        _currentServerAlias = response.Alias;
                    }
                }
            });
        }
        private void RefreshCache(string cacheLocation) {
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
        }

        public enum VoiceModel {
            Quality,
            Speed,
            Cheap,
        }
        public async Task<bool> VerifyServer(string hostname, string port) {
            string currentRelayServer = "http://" + hostname + ":" + port;
            try {
                MemoryStream memoryStream = new MemoryStream();
                ProxiedVoiceRequest proxiedVoiceRequest = new ProxiedVoiceRequest() {
                    Voice = "Bella",
                    Text = "Test",
                    RawText = "",
                    UnfilteredText = "",
                    Model = "cheap",
                    Character = "Test",
                    AggressiveCache = false,
                    RedoLine = false,
                    ExtraJsonData = "",
                    Override = false,
                    VersionIdentifier = "",
                    UseMuteList = false,
                    VoiceLinePriority = VoiceLinePriority.None
                };
                using (HttpClient httpClient = new HttpClient()) {
                    httpClient.BaseAddress = new Uri(currentRelayServer);
                    httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                    httpClient.Timeout = new TimeSpan(0, 6, 0);
                    var post = await httpClient.PostAsync(httpClient.BaseAddress, new StringContent(JsonConvert.SerializeObject(proxiedVoiceRequest)));
                    if (post.StatusCode == HttpStatusCode.OK) {
                        var result = await post.Content.ReadAsStreamAsync();
                        await result.CopyToAsync(memoryStream);
                        await result.FlushAsync();
                        if (memoryStream.Length > 0) {
                            await memoryStream.DisposeAsync();
                            return true;
                        }
                    }
                }
                memoryStream.Position = 0;
            } catch {
                return false;
            }
            return false;
        }
        public async Task<Tuple<bool, string>> GetCharacterAudio(Stream outputStream, string text, string originalValue, string rawText, string character,
            bool gender, string backupVoice = "", bool aggressiveCache = false, VoiceModel voiceModel = VoiceModel.Speed, string extraJson = "",
            bool redoLine = false, bool overrideGeneration = false, bool useMuteList = false, VoiceLinePriority overrideVoiceLinePriority = VoiceLinePriority.None, bool ignoreRefreshCache = false, HttpListenerResponse resp = null) {
            string currentRelayServer = Environment.MachineName == "ARTEMISDIALOGUE" ? "https://ai.hubujubu.com:5697" : "http://ai.hubujubu.com:5670";
            bool recoverLineType = false;
            if (_useCustomRelayServer) {
                currentRelayServer = "http://" + _customRelayServer + ":" + _port;
            }
            if (cacheTimer.ElapsedMilliseconds > 120000) {
                RefreshCache(_cacheLocation);
                cacheTimer.Restart();
            }
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
                if (_characterToCacheType.ContainsKey(character)) {
                    voiceLinePriority = _characterToCacheType[character];
                }
                if (!string.IsNullOrEmpty(_cachePath)) {
                    if (_characterVoices.VoiceCatalogue.ContainsKey(character) && !redoLine) {
                        if (_characterVoices.VoiceCatalogue[character].ContainsKey(text)) {
                            string relativePath = _characterVoices.VoiceCatalogue[character][text];
                            bool needsRefreshing = false;
                            if (!ignoreRefreshCache) {
                                if (voiceLinePriority != VoiceLinePriority.None) {
                                    needsRefreshing = _characterVoices.VoiceEngine[character][text] != voiceLinePriority.ToString();
                                }
                                if (overrideVoiceLinePriority != VoiceLinePriority.None) {
                                    needsRefreshing = _characterVoices.VoiceEngine[character][text] != overrideVoiceLinePriority.ToString();
                                }
                                if (voiceLinePriority == VoiceLinePriority.Ignore) {
                                    needsRefreshing = true;
                                }
                            }
                            string fullPath = Path.Combine(_cachePath, relativePath);
                            if (File.Exists(fullPath) && !needsRefreshing) {
                                voiceEngine = _characterVoices.VoiceEngine[character][text];
                                try {
                                    if (resp != null && !recoverLineType) {
                                        resp.StatusCode = (int)HttpStatusCode.OK;
                                        resp.StatusDescription = voiceEngine;
                                    }
                                    FileStream file = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                                    await file.CopyToAsync(outputStream);
                                    succeeded = true;
                                    if (resp != null) {
                                        resp.Close();
                                    }
                                } catch {
                                    needsRefreshing = true;
                                    File.Delete(fullPath);
                                }
                            }
                        }
                    } else {
                        string relativeFolderPath = character + "\\";
                        string filePath = relativeFolderPath + CreateMD5(character + text) + ".mp3";
                        if (File.Exists(filePath)) {
                            try {
                                voiceEngine = "Cached";
                                if (resp != null && !recoverLineType) {
                                    resp.StatusCode = (int)HttpStatusCode.OK;
                                    resp.StatusDescription = voiceEngine;
                                }
                                FileStream file = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                                await file.CopyToAsync(outputStream);
                                succeeded = true;
                                voiceEngine = _characterVoices.VoiceEngine[character][text];
                                recoverLineType = true;
                                if (resp != null) {
                                    resp.Close();
                                }
                            } catch {
                            }
                        }
                    }
                }
                if (!succeeded || recoverLineType) {
                    MemoryStream memoryStream = new MemoryStream();
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
                            httpClient.BaseAddress = new Uri(currentRelayServer);
                            httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                            httpClient.Timeout = new TimeSpan(0, 6, 0);
                            var post = await httpClient.PostAsync(httpClient.BaseAddress, new StringContent(JsonConvert.SerializeObject(proxiedVoiceRequest)));
                            if (post.StatusCode == HttpStatusCode.OK) {
                                var result = await post.Content.ReadAsStreamAsync();
                                voiceEngine = post.ReasonPhrase;
                                if (resp != null && !recoverLineType) {
                                    resp.StatusCode = (int)HttpStatusCode.OK;
                                    resp.StatusDescription = voiceEngine;
                                }
                                if (!recoverLineType) {
                                    await result.CopyToAsync(memoryStream);
                                    await result.FlushAsync();
                                    memoryStream.Position = 0;
                                }
                                result.Close();
                                succeeded = true;
                            }
                        }
                    } else {
                        if (voiceLinePriority == VoiceLinePriority.None) {
                            voiceLinePriority = VoiceLinePriority.Alternative;
                        }
                        ProxiedVoiceRequest ttsRequest = new ProxiedVoiceRequest() {
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
                            httpClient.BaseAddress = new Uri(currentRelayServer);
                            httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                            httpClient.Timeout = new TimeSpan(0, 6, 0);
                            var post = await httpClient.PostAsync(httpClient.BaseAddress, new StringContent(JsonConvert.SerializeObject(ttsRequest)));
                            if (post.StatusCode == HttpStatusCode.OK) {
                                var result = await post.Content.ReadAsStreamAsync();
                                voiceEngine = post.ReasonPhrase;
                                if (resp != null && !recoverLineType) {
                                    resp.StatusCode = (int)HttpStatusCode.OK;
                                    resp.StatusDescription = voiceEngine;
                                }
                                if (!recoverLineType) {
                                    await result.CopyToAsync(memoryStream);
                                    await result.FlushAsync();
                                    memoryStream.Position = 0;
                                }
                                result.Close();
                                succeeded = true;
                            }
                        }
                    }
                    if (!recoverLineType) {
                        await memoryStream.CopyToAsync(outputStream);
                        memoryStream.Position = 0;
                    }
                    if (resp != null && !recoverLineType) {
                        resp.Close();
                    }
                    if (!string.IsNullOrEmpty(_cachePath)) {
                        if (succeeded) {
                            if (voiceEngine != "" || character.ToLower().Contains("narrator")) {
                                if (!_characterVoices.VoiceCatalogue.ContainsKey(character)) {
                                    _characterVoices.VoiceCatalogue[character] = new Dictionary<string, string>();
                                }
                                if (!_characterVoices.VoiceEngine.ContainsKey(character)) {
                                    _characterVoices.VoiceEngine[character] = new Dictionary<string, string>();
                                }
                                if (memoryStream.Length > 0) {
                                    string relativeFolderPath = character + "\\";
                                    string filePath = relativeFolderPath + CreateMD5(character + text) + ".mp3";
                                    _characterVoices.VoiceEngine[character][text] = voiceEngine;
                                    if (_characterVoices.VoiceCatalogue[character].ContainsKey(text) && !recoverLineType) {
                                        File.Delete(Path.Combine(_cachePath, _characterVoices.VoiceCatalogue[character][text]));
                                    }
                                    _characterVoices.VoiceCatalogue[character][text] = filePath;
                                    Directory.CreateDirectory(Path.Combine(_cachePath, relativeFolderPath));
                                    if (!recoverLineType) {
                                        using (FileStream stream = new FileStream(Path.Combine(_cachePath, filePath), FileMode.Create, FileAccess.Write, FileShare.Write)) {
                                            await memoryStream.CopyToAsync(stream);
                                            await memoryStream.FlushAsync();
                                        }
                                    }
                                    await File.WriteAllTextAsync(Path.Combine(_cachePath, "cacheIndex.json"), JsonConvert.SerializeObject(_characterVoices, Formatting.Indented));
                                }
                            }
                            memoryStream.Position = 0;
                        }
                    }
                    memoryStream.DisposeAsync();
                }
            } catch {
                return new Tuple<bool, string>(false, "Error");
            }
            return new Tuple<bool, string>(succeeded, voiceEngine.Replace("Elevenlabs", "ETTS").Replace("OK", "XTTS"));
        }

        public WaveStream StreamToFoundationReader(Stream stream) {
            return new StreamMediaFoundationReader(stream);
        }
        public static string CreateMD5(string input) {
            // Use input string to calculate MD5 hash
            using (System.Security.Cryptography.MD5 md5 = System.Security.Cryptography.MD5.Create()) {
                byte[] inputBytes = System.Text.Encoding.ASCII.GetBytes(input);
                byte[] hashBytes = md5.ComputeHash(inputBytes);

                return Convert.ToHexString(hashBytes); // .NET 5 +

                // Convert the byte array to hexadecimal string prior to .NET 5
                // StringBuilder sb = new System.Text.StringBuilder();
                // for (int i = 0; i < hashBytes.Length; i++)
                // {
                //     sb.Append(hashBytes[i].ToString("X2"));
                // }
                // return sb.ToString();
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
