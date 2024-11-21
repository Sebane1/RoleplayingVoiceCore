using CachedTTSRelay;
using ElevenLabs.Voices;
using NAudio.Wave;
using Newtonsoft.Json;
using RoleplayingMediaCore.AudioRecycler;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;

namespace RoleplayingVoiceCore {
    public class NPCVoiceManager {
        private bool _useCustomRelayServer = false;
        private Dictionary<string, string> _characterToVoicePairing = new Dictionary<string, string>();
        private Dictionary<string, VoiceLinePriority> _characterToCacheType = new Dictionary<string, VoiceLinePriority>();
        private string _cacheLocation;
        private CharacterVoices _characterVoices = new CharacterVoices();

        private CharacterVoices _characterVoicesMasterList = new CharacterVoices();
        private string _cachePath = "";
        private string _editorCacheLocation = "";
        private string _versionIdentifier;
        private string _customRelayServer;
        private string _port = "5670";
        private string _currentServerAlias;
        Stopwatch cacheTimer = new Stopwatch();
        Stopwatch cacheSaveTimer = new Stopwatch();
        private bool _cacheLoaded;
        private bool alreadySaving;
        public event EventHandler OnMasterListAcquired;
        public bool UseCustomRelayServer { get => _useCustomRelayServer; set => _useCustomRelayServer = value; }
        public string CustomRelayServer { get => _customRelayServer; set => _customRelayServer = value; }
        public string Port { get => _port; set => _port = value; }
        public string CurrentServerAlias { get => _currentServerAlias; set => _currentServerAlias = value; }
        public CharacterVoices CharacterVoices { get => _characterVoices; }
        public CharacterVoices CharacterVoicesMasterList { get => _characterVoicesMasterList; set => _characterVoicesMasterList = value; }

        public NPCVoiceManager(Dictionary<string, string> characterToVoicePairing, Dictionary<string, VoiceLinePriority> characterToCacheType,
            string cacheLocation, string version, bool isAServer) {
            _characterToVoicePairing = characterToVoicePairing;
            _characterToCacheType = characterToCacheType;
            _cacheLocation = cacheLocation;
            _versionIdentifier = version;
            RefreshCache(cacheLocation);
            cacheTimer.Start();
            cacheSaveTimer.Start();
            if (!isAServer) {
                GetCloserServerHost();
                GetVoiceLineMasterList();
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
            if (!string.IsNullOrEmpty(cacheLocation)) {
                _cachePath = Path.Combine(cacheLocation, "NPC Dialogue Cache\\");
                _editorCacheLocation = Path.Combine(cacheLocation, "NPC Dialogue Editor\\");
                Directory.CreateDirectory(_cachePath);
                Directory.CreateDirectory(_editorCacheLocation);
                string cacheFile = Path.Combine(_cachePath, "cacheIndex.json");
                string cacheFileBackup = Path.Combine(_cachePath, "cacheIndex_backup.json");
                if (File.Exists(cacheFile)) {
                    try {
                        _characterVoices = JsonConvert.DeserializeObject<CharacterVoices>(File.ReadAllText(cacheFile));
                    } catch (Exception e) {
                        Console.WriteLine(e);
                        if (File.Exists(cacheFileBackup)) {
                            try {
                                _characterVoices = JsonConvert.DeserializeObject<CharacterVoices>(File.ReadAllText(cacheFileBackup));
                            } catch {

                            }
                        }
                    }
                }
            }
            _cacheLoaded = true;
        }
        private void GetVoiceLineMasterList() {
            Task.Run(async () => {
                try {
                    string currentRelayServer = "http://ai.hubujubu.com:5684";
                    MemoryStream memoryStream = new MemoryStream();
                    InformationRequest informationRequest = new InformationRequest();
                    informationRequest.InformationRequestType = InformationRequestType.GetVoiceLineList;
                    using (HttpClient httpClient = new HttpClient()) {
                        httpClient.BaseAddress = new Uri(currentRelayServer);
                        //httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                        httpClient.Timeout = new TimeSpan(0, 6, 0);
                        string requestJson = JsonConvert.SerializeObject(informationRequest);
                        using (BinaryWriter streamWriter = new BinaryWriter(memoryStream)) {
                            streamWriter.Write(requestJson);
                            memoryStream.Position = 0;
                            var post = await httpClient.PostAsync(httpClient.BaseAddress, new StreamContent(memoryStream));
                            if (post.StatusCode == HttpStatusCode.OK) {
                                var result = await post.Content.ReadAsStringAsync();
                                _characterVoicesMasterList = JsonConvert.DeserializeObject<CharacterVoices>(result);
                                OnMasterListAcquired?.Invoke(this, EventArgs.Empty);
                            }
                        }
                    }
                } catch {
                }
            });
        }

        public int GetFileCountForCharacter(string characterName) {
            string path = Path.Combine(_editorCacheLocation, characterName);
            return Directory.EnumerateFiles(path).Count();
        }

        public async Task<bool> UploadCharacterVoicePack(string characterName) {
            string currentRelayServer = "http://ai.hubujubu.com:5684";
            string path = Path.Combine(_editorCacheLocation, characterName);
            string zipPath = path + ".zip";
            if (File.Exists(zipPath)) {
                File.Delete(zipPath);
            }
            ZipFile.CreateFromDirectory(path, path + ".zip");
            InformationRequest informationRequest = new InformationRequest();
            informationRequest.Name = characterName + "_" + NPCVoiceManager.CreateMD5(Environment.MachineName);
            informationRequest.InformationRequestType = InformationRequestType.UploadVoiceLines;
            string json = JsonConvert.SerializeObject(informationRequest);
            MemoryStream stream = new MemoryStream();
            BinaryWriter binaryWriter = new BinaryWriter(stream);
            binaryWriter.Write(json);
            stream.Write(await File.ReadAllBytesAsync(zipPath));
            using (HttpClient httpClient = new HttpClient()) {
                httpClient.BaseAddress = new Uri(currentRelayServer);
                //httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                httpClient.Timeout = new TimeSpan(0, 6, 0);
                stream.Position = 0;
                var post = await httpClient.PostAsync(httpClient.BaseAddress, new StreamContent(stream));
                if (post.StatusCode == HttpStatusCode.OK) {
                    return true;
                }
            }
            return false;
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

        public async void AddCharacterAudio(Stream inputStream, string text, string character) {
            string voiceEngine = "Real";
            bool succeeded = false;
            string characterGendered = character;
            try {
                if (_cacheLoaded) {
                    if (!string.IsNullOrEmpty(_editorCacheLocation)) {
                        if (!_characterVoices.VoiceCatalogue.ContainsKey(characterGendered)) {
                            _characterVoices.VoiceCatalogue[characterGendered] = new Dictionary<string, string>();
                        }
                        if (!_characterVoices.VoiceEngine.ContainsKey(characterGendered)) {
                            _characterVoices.VoiceEngine[characterGendered] = new Dictionary<string, string>();
                        }
                        if (inputStream.Length > 0) {
                            string relativeFolderPath = characterGendered + "\\";
                            string filePath = relativeFolderPath + CreateMD5(characterGendered + text) + ".mp3";
                            _characterVoices.VoiceEngine[characterGendered][text] = voiceEngine;
                            if (_characterVoices.VoiceCatalogue[characterGendered].ContainsKey(text)) {
                                File.Delete(Path.Combine(_editorCacheLocation, _characterVoices.VoiceCatalogue[characterGendered][text]));
                            }
                            _characterVoices.VoiceCatalogue[characterGendered][text] = filePath;
                            Directory.CreateDirectory(Path.Combine(_editorCacheLocation, relativeFolderPath));
                            using (FileStream stream = new FileStream(Path.Combine(_editorCacheLocation, filePath), FileMode.Create, FileAccess.Write, FileShare.Write)) {
                                await inputStream.CopyToAsync(stream);
                                await inputStream.FlushAsync();
                            }
                        }
                    }
                }
            } catch {

            }
        }
        public string VoicelinePath(string text, string character) {
            string characterGendered = character;
            string relativeFolderPath = characterGendered + "\\";
            string filePath = Path.Combine(_editorCacheLocation, relativeFolderPath + CreateMD5(characterGendered + text) + ".mp3");
            return filePath;
        }
        public async Task<Tuple<bool, string>> GetCharacterAudio(Stream outputStream, string text, string originalValue, string rawText, string character,
            bool gender, string backupVoice = "", bool aggressiveCache = false, VoiceModel voiceModel = VoiceModel.Speed, string extraJson = "",
            bool redoLine = false, bool overrideGeneration = false, bool useMuteList = false, VoiceLinePriority overrideVoiceLinePriority = VoiceLinePriority.None, bool ignoreRefreshCache = false, HttpListenerResponse resp = null) {
            string currentRelayServer = Environment.MachineName == "ARTEMISDIALOGUE" ? "https://ai.hubujubu.com:5697" : "http://ai.hubujubu.com:5670";
            bool recoverLineType = false;
            bool isServerRequest = resp != null;
            string characterGendered = character + (gender ? "_0" : "_1");
            if (_useCustomRelayServer) {
                currentRelayServer = "http://" + _customRelayServer + ":" + _port;
            }
            string voiceEngine = "";
            bool succeeded = false;
            if (_cacheLoaded) {
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

                    var task = async () => {
                        string relativeFolderPath = characterGendered + "\\";
                        string filePath = Path.Combine(_cachePath, relativeFolderPath + CreateMD5(characterGendered + text) + ".mp3");
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
                                recoverLineType = true;
                                if (resp != null) {
                                    resp.Close();
                                }
                            } catch {
                            }
                        }
                    };

                    if (!string.IsNullOrEmpty(_cachePath)) {
                        if (_characterVoices.VoiceCatalogue.ContainsKey(characterGendered) && !redoLine) {
                            if (_characterVoices.VoiceCatalogue[characterGendered].ContainsKey(text)) {
                                string relativePath = _characterVoices.VoiceCatalogue[characterGendered][text];
                                bool needsRefreshing = false;
                                if (!ignoreRefreshCache) {
                                    if (voiceLinePriority != VoiceLinePriority.None) {
                                        needsRefreshing = _characterVoices.VoiceEngine[characterGendered][text] != voiceLinePriority.ToString();
                                    }
                                    if (overrideVoiceLinePriority != VoiceLinePriority.None) {
                                        needsRefreshing = _characterVoices.VoiceEngine[characterGendered][text] != overrideVoiceLinePriority.ToString();
                                    }
                                    if (voiceLinePriority == VoiceLinePriority.Ignore) {
                                        needsRefreshing = true;
                                    }
                                }
                                string fullPath = Path.Combine(_cachePath, relativePath);
                                if (File.Exists(fullPath) && !needsRefreshing) {
                                    voiceEngine = _characterVoices.VoiceEngine[characterGendered][text];
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
                            } else {
                                await task.Invoke();
                            }
                        } else {
                            await task.Invoke();
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
                                Text = text,
                                Model = voiceModel.ToString().ToLower(),
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
                                if (voiceEngine != "" || characterGendered.ToLower().Contains("narrator")) {
                                    if (!_characterVoices.VoiceCatalogue.ContainsKey(characterGendered)) {
                                        _characterVoices.VoiceCatalogue[characterGendered] = new Dictionary<string, string>();
                                    }
                                    if (!_characterVoices.VoiceEngine.ContainsKey(characterGendered)) {
                                        _characterVoices.VoiceEngine[characterGendered] = new Dictionary<string, string>();
                                    }
                                    if (memoryStream.Length > 0) {
                                        string relativeFolderPath = characterGendered + "\\";
                                        string filePath = relativeFolderPath + CreateMD5(characterGendered + text) + ".mp3";
                                        _characterVoices.VoiceEngine[characterGendered][text] = voiceEngine;
                                        if (_characterVoices.VoiceCatalogue[characterGendered].ContainsKey(text) && !recoverLineType) {
                                            File.Delete(Path.Combine(_cachePath, _characterVoices.VoiceCatalogue[characterGendered][text]));
                                        }
                                        _characterVoices.VoiceCatalogue[characterGendered][text] = filePath;
                                        Directory.CreateDirectory(Path.Combine(_cachePath, relativeFolderPath));
                                        if (!recoverLineType) {
                                            using (FileStream stream = new FileStream(Path.Combine(_cachePath, filePath), FileMode.Create, FileAccess.Write, FileShare.Write)) {
                                                await memoryStream.CopyToAsync(stream);
                                                await memoryStream.FlushAsync();
                                            }
                                        }
                                        if (_cacheLoaded && !alreadySaving) {
                                            if (cacheSaveTimer.ElapsedMilliseconds > 30000 || !isServerRequest) {
                                                if (_characterVoices.VoiceCatalogue.Count > 0) {
                                                    alreadySaving = true;
                                                    string primaryCache = Path.Combine(_cachePath, "cacheIndex.json");
                                                    if (File.Exists(primaryCache)) {
                                                        File.Copy(primaryCache, Path.Combine(_cachePath, "cacheIndex_backup.json"), true);
                                                    }
                                                    bool isLocked = true;
                                                    string json = JsonConvert.SerializeObject(_characterVoices, Formatting.Indented);
                                                    while (isLocked) {
                                                        try {
                                                            await File.WriteAllTextAsync(Path.Combine(_cachePath, "cacheIndex.json"), json);
                                                            isLocked = false;
                                                        } catch {
                                                            Thread.Sleep(500);
                                                        }
                                                    }
                                                    alreadySaving = false;
                                                }
                                                cacheSaveTimer.Restart();
                                                //if (cacheTimer.ElapsedMilliseconds > 600000) {
                                                //    RefreshCache(_cacheLocation);
                                                //    cacheTimer.Restart();
                                                //}
                                            }
                                        }
                                    }
                                }
                                memoryStream.Position = 0;
                            }
                        }
                        if (isServerRequest) {
                            memoryStream.DisposeAsync();
                        }
                    }
                } catch {
                    return new Tuple<bool, string>(false, "Error");
                }
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
