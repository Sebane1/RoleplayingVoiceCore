﻿using ElevenLabs;
using ElevenLabs.History;
using ElevenLabs.Models;
using ElevenLabs.User;
using ElevenLabs.Voices;
using FFXIVLooseTextureCompiler.Networking;
using RoleplayingMediaCore.AudioRecycler;
using System.ComponentModel.DataAnnotations;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace RoleplayingMediaCore {
    public class RoleplayingMediaManager {
        private string _apiKey;
        private ElevenLabsClient? _api;
        private NetworkedClient _networkedClient;
        private CharacterVoices _characterVoices = new CharacterVoices();
        SubscriptionInfo _info = new SubscriptionInfo();
        public event EventHandler? VoicesUpdated;
        public event EventHandler<ValidationResult>? OnApiValidationComplete;
        public event EventHandler<VoiceFailure>? OnVoiceFailed;

        private IReadOnlyList<HistoryItem> _history;
        private bool apiValid;
        private string rpVoiceCache;
        private IReadOnlyList<Voice> _voices;
        private Voice _characterVoice;
        private string _voiceType;

        public RoleplayingMediaManager(string apiKey, string cache, NetworkedClient client, CharacterVoices? characterVoices = null) {
            rpVoiceCache = cache;
            _networkedClient = client;
            if (string.IsNullOrWhiteSpace(apiKey)) {
                apiValid = false;
            } else {
                _apiKey = apiKey;
                if (!string.IsNullOrEmpty(apiKey)) {
                    apiValid = true;
                }
                // Spin a new thread for this
                Task.Run(() => {
                    SetAPI(apiKey);
                });
                if (characterVoices != null) {
                    _characterVoices = characterVoices;
                }
            }
            RefreshElevenlabsSubscriptionInfo();
            GetVoiceList();
        }
        public CharacterVoices CharacterVoices { get => _characterVoices; set => _characterVoices = value; }
        public string ApiKey { get => _apiKey; set => _apiKey = value; }
        public SubscriptionInfo Info { get => _info; set => _info = value; }
        public NetworkedClient NetworkedClient { get => _networkedClient; set => _networkedClient = value; }

        public async Task<bool> ApiValidation(string key) {
            if (!string.IsNullOrWhiteSpace(key) && key.All(c => char.IsAsciiLetterOrDigit(c))) {
                var api = new ElevenLabsClient(key);
                apiValid = true;
                try {
                    await api.UserEndpoint.GetUserInfoAsync();
                } catch (Exception e) {
                    var errorMain = e.Message.ToString();
                    if (errorMain.Contains("invalid_api_key")) {
                        apiValid = false;
                    }
                }
            }
            if (string.IsNullOrWhiteSpace(key)) {
                apiValid = false;
            }
            ValidationResult validationResult = new ValidationResult();
            validationResult.ValidationSuceeded = apiValid;
            OnApiValidationComplete?.Invoke(this, validationResult);
            if (apiValid) {
                return true;
            }
            return false;
        }

        public async Task<string[]> GetVoiceList() {
            ValidationResult state = new ValidationResult();
            List<string> voicesNames = new List<string>();
            if (_api != null) {
                int failure = 0;
                while (failure < 10) {
                    try {
                        _voices = await _api.VoicesEndpoint.GetAllVoicesAsync();
                        break;
                    } catch (Exception e) {
                        var errorVoiceList = e.Message.ToString();
                        if (errorVoiceList.Contains("invalid_api_key")) {
                            apiValid = false;
                            state.ValidationState = 3;
                            OnApiValidationComplete?.Invoke(this, state);
                        } else {
                            failure++;
                        }
                    }
                }
            }
            voicesNames.Add("None");
            if (_voices != null) {
                foreach (var voice in _voices) {
                    voicesNames.Add(voice.Name);
                }
            }
            return voicesNames.ToArray();
        }
        public void SetAPI(string apiKey) {
            _api = new ElevenLabsClient(apiKey);
            apiValid = true;
            ValidationResult validationResult = new ValidationResult();
            validationResult.ValidationSuceeded = apiValid;
            OnApiValidationComplete?.Invoke(this, validationResult);
        }
        public void RefreshElevenlabsSubscriptionInfo() {
            Task.Run(async delegate {
                ValidationResult state = new ValidationResult();
                SubscriptionInfo? value = null;
                if (_api != null) {
                    int failure = 0;
                    while (failure < 10) {
                        try {
                            value = await _api.UserEndpoint.GetSubscriptionInfoAsync();
                            break;
                        } catch (Exception e) {
                            var errorSubInfo = e.Message.ToString();
                            if (errorSubInfo.Contains("invalid_api_key")) {
                                apiValid = false;
                                state.ValidationState = 3;
                                OnApiValidationComplete?.Invoke(this, state);
                            } else {
                                failure++;
                            }
                        }
                    }
                }
                _info = value;
            }
            );
        }

        public async void SetVoice(string voiceType) {
            _voiceType = voiceType.ToLower();
            ValidationResult state = new ValidationResult();
            if (_api != null) {
                try {
                    _voices = await _api.VoicesEndpoint.GetAllVoicesAsync();
                } catch (Exception e) {
                    var errorVoiceGen = e.Message.ToString();
                    if (errorVoiceGen.Contains("invalid_api_key")) {
                        apiValid = false;
                        state.ValidationState = 3;
                        OnApiValidationComplete?.Invoke(this, state);
                    }
                }
                if (_voices != null) {
                    foreach (var voice in _voices) {
                        if (voice.Name.ToLower().Contains(_voiceType)) {
                            if (voice != null) {
                                _characterVoice = voice;
                            }
                            break;
                        }
                    }
                }
            }
        }

        public async Task<string> DoVoice(string sender, string text,
            bool isEmote, float volume, Vector3 position, bool aggressiveSplicing, bool useSync) {
            string clipPath = "";
            string hash = Shai1Hash(sender + text);
            if (_characterVoice == null) {
                if (_voices == null) {
                    await GetVoiceList();
                }
                if (_voices != null) {
                    foreach (var voice in _voices) {
                        if (voice.Name.ToLower().Contains(_voiceType)) {
                            if (voice != null) {
                                _characterVoice = voice;
                            }
                            break;
                        }
                    }
                }
            }

            if (_characterVoice != null) {
                try {
                    if (!text.StartsWith("(") && !text.EndsWith(")") && !(isEmote && (!text.Contains(@"""") || text.Contains(@"“")))) {
                        Directory.CreateDirectory(rpVoiceCache + @"\Outgoing");
                        string stitchedPath = Path.Combine(rpVoiceCache + @"\Outgoing", _characterVoice + "-" + hash + ".mp3");
                        if (!File.Exists(stitchedPath)) {
                            string trimmedText = TrimText(text);
                            string[] audioClips = (trimmedText.Contains(@"""") || trimmedText.Contains(@"“"))
                                ? ExtractQuotationsToList(trimmedText, aggressiveSplicing) : (aggressiveSplicing ? AggressiveWordSplicing(trimmedText) : new string[] { trimmedText });
                            List<string> audioPaths = new List<string>();
                            foreach (string audioClip in audioClips) {
                                audioPaths.Add(await GetVoicePath(_characterVoice.Name, audioClip, _characterVoice));
                            }
                            MemoryStream playbackStream = ConcatenateAudio(audioPaths.ToArray());
                            try {
                                using (Stream stitchedStream = File.OpenWrite(stitchedPath)) {
                                    playbackStream.Position = 0;
                                    playbackStream.CopyTo(stitchedStream);
                                    stitchedStream.Flush();
                                    stitchedStream.Close();
                                }
                            } catch (Exception e) {
                                OnVoiceFailed?.Invoke(this, new VoiceFailure() { FailureMessage = "Failed", Exception = e });
                            }
                        }
                        if (useSync) {
                            Task.Run(() => _networkedClient.SendFile(hash, stitchedPath));
                        }
                        clipPath = stitchedPath;
                        VoicesUpdated?.Invoke(this, EventArgs.Empty);
                    } else {
                        return "";
                    }

                } catch {

                }
            }
            return clipPath;
        }

        public async Task<bool> SendSound(string sender, string identifier, string soundOnDisk, float volume, Vector3 position) {
            string hash = Shai1Hash(sender + identifier);
            bool sendState = false;
            await Task.Run(async () => { sendState = await _networkedClient.SendFile(hash, soundOnDisk); });
            return sendState;
        }

        public async Task<bool> SendZip(string sender, string soundOnDisk) {
            string hash = Shai1Hash(sender);
            bool sendState = false;
            await Task.Run(async () => { sendState = await _networkedClient.SendZip(hash, soundOnDisk); });
            return sendState;
        }

        public async Task<bool> SendShort(string sender, ushort shortvalue) {
            string hash = Shai1Hash(sender);
            bool sendState = false;
            await Task.Run(async () => { sendState = await _networkedClient.SendShort(hash, shortvalue); });
            return sendState;
        }
        public async Task<ushort> GetShort(string sender) {
            if (_networkedClient != null) {
                string hash = Shai1Hash(sender);
                return await _networkedClient.GetShort(hash);
            }
            return 0;
        }

        private async Task<string> GetVoicePath(string voiceType, string trimmedText, Voice characterVoice) {
            string audioPath = "";
            var defaultVoiceSettings = new VoiceSettings(0.3f, 1);
            try {
                if (!CharacterVoices.VoiceCatalogue.ContainsKey(voiceType)) {
                    CharacterVoices.VoiceCatalogue[voiceType] = new Dictionary<string, string>();
                }
                if (!CharacterVoices.VoiceCatalogue[(voiceType)].ContainsKey(trimmedText.ToLower())) {
                    audioPath = await GetVoiceFromElevenLabs(trimmedText, voiceType, defaultVoiceSettings, characterVoice);
                } else if (File.Exists(CharacterVoices.VoiceCatalogue[(voiceType)][trimmedText.ToLower()])) {
                    audioPath = CharacterVoices.VoiceCatalogue[(voiceType)][trimmedText.ToLower()];
                } else {
                    CharacterVoices.VoiceCatalogue[(voiceType)].Remove(trimmedText.ToLower());
                    audioPath = await GetVoiceFromElevenLabs(trimmedText, voiceType, defaultVoiceSettings, characterVoice);
                }
            } catch (Exception e) {
                OnVoiceFailed?.Invoke(this, new VoiceFailure() { FailureMessage = "Failed", Exception = e });
            }
            return audioPath;
        }

        private async Task<string> GetVoiceFromElevenLabs(string trimmedText, string voiceType,
            VoiceSettings defaultVoiceSettings, Voice characterVoice) {
            string unquotedText = trimmedText.Replace(@"""", null);
            string numberAdjusted = char.IsDigit(unquotedText.Last()) ? unquotedText + "." : unquotedText;
            string finalText = @"""" + numberAdjusted + @"""";
            string audioPath = "";
            bool foundInHistory = false;
            try {
                if (!foundInHistory) {
                    audioPath = await _api.TextToSpeechEndpoint
                        .TextToSpeechAsync(finalText, characterVoice,
                        defaultVoiceSettings, new Model("eleven_turbo_v2"), rpVoiceCache);
                }
                CharacterVoices.VoiceCatalogue[(voiceType)].Add(trimmedText.ToLower(), audioPath);
            } catch {

            }
            return audioPath;
        }

        private string TrimText(string text) {
            string newText = text;
            var wordReplacements = new Dictionary<string, string>
            {
                {"XD", "ahahaha" },
                {"lmao", "ahahaha" },
                {"lol", "ahahaha" },
                {"lmfao", "ahahaha" },
                {"kek", "ahahaha" },
                {"kekw", "ahahaha" },
                {"rotflmao", "ahahaha" },
                {"rotflmfao", "ahahaha" },
                {":D", "." },
                {":P", "." },
                {":3", "." },
                {"<3", "love" }
            };
            foreach (var word in wordReplacements) {
                newText = Regex.Replace(newText, $@"(?<=^|\s){word.Key}(?=\s|$)", word.Value, RegexOptions.IgnoreCase);
            }
            foreach (char character in @"@#$%^&*()_+{}\/<>|`~".ToCharArray()) {
                newText = newText.Replace(character + "", null);
            }
            return newText;
        }

        public MemoryStream ConcatenateAudio(params string[] mp3filenames) {
            MemoryStream output = new MemoryStream();
            foreach (string filename in mp3filenames) {
                if (File.Exists(filename)) {
                    using (Stream stream = File.OpenRead(filename)) {
                        stream.CopyTo(output);
                    }
                }
            }
            return output;
        }

        private string ExtractQuotations(string text) {
            string newText = "";
            string[] strings = null;
            if (text.Contains(@"""")) {
                strings = text.Split('"');
            } else {
                strings = text.Split('“');
            }
            if (strings.Length > 1) {
                for (int i = 0; i < strings.Length; i++) {
                    if ((i + 1) % 2 == 0) {
                        newText += strings[i] + (strings.Length % 2 == 0 ? " -- " : "");
                    }
                }
                return newText;
            } else {
                return text;
            }
        }

        private string[] ExtractQuotationsToList(string text, bool aggressiveSplicing) {
            string newText = "";
            string[] strings = null;
            List<string> quotes = new List<string>();
            if (text.Contains(@"""")) {
                strings = text.Split('"');
            } else {
                strings = text.Split('“');
            }
            if (strings.Length > 1) {
                for (int i = 0; i < strings.Length; i++) {
                    if ((i + 1) % 2 == 0) {
                        if (aggressiveSplicing) {
                            quotes.AddRange(AggressiveWordSplicing(strings[i].Replace("\"", null).Replace("“", null).Replace(",", " - ")));
                        } else {
                            quotes.Add(strings[i].Replace("\"", null).Replace("“", null).Replace(",", " - "));
                        }
                    }
                }
            } else {
                quotes.Add(text);
            }
            return quotes.ToArray();
        }

        private string[] AggressiveWordSplicing(string text) {
            string[] strings = null;
            List<string> quotes = new List<string>();
            strings = text.Split(' ');
            string temp = "";
            for (int i = 0; i < strings.Length; i++) {
                temp += strings[i] + " ";
                if (strings[i].Contains(",") || (strings[i].Contains(".") && !strings[i].Contains("..."))
                    || strings[i].Contains("!") || strings[i].Contains("?") || strings[i].Contains(";")
                    || strings[i].Contains(":") || strings[i].Contains("·")) {
                    quotes.Add(temp.Replace("\"", null).Replace("“", null).Replace(",", "").Trim());
                    temp = "";
                }
            }
            if (!string.IsNullOrEmpty(temp)) {
                quotes.Add(temp.Replace("\"", null).Replace("“", null).Replace(",", "").Trim());
            }
            return quotes.ToArray();
        }

        public static string CreateMD5(string input) {
            // Use input string to calculate MD5 hash
            using (System.Security.Cryptography.MD5 md5 = System.Security.Cryptography.MD5.Create()) {
                byte[] inputBytes = System.Text.Encoding.ASCII.GetBytes(input);
                byte[] hashBytes = md5.ComputeHash(inputBytes);
                return Convert.ToHexString(hashBytes); // .NET 5 +
            }
        }
        public static string Shai1Hash(string input) {
            using var sha1 = SHA1.Create();
            return Convert.ToHexString(sha1.ComputeHash(Encoding.UTF8.GetBytes(input)));
        }

        public async Task<string> GetSound(string sender, string identifier, float volume,
            Vector3 centerPosition, bool isShoutYell, string? subDirectory = null, bool ignoreCache = false) {
            string path = "";
            if (_networkedClient != null) {
                KeyValuePair<Vector3, string> data = new KeyValuePair<Vector3, string>();
                Guid id = Guid.NewGuid();
                string hash = Shai1Hash(sender + identifier);
                string localPath = Path.Combine(rpVoiceCache + subDirectory, (!ignoreCache ? hash : id) + ".mp3");
                if (!File.Exists(localPath) || ignoreCache) {
                    data = await _networkedClient.GetFile(hash, rpVoiceCache + subDirectory, id.ToString());
                    path = data.Value;
                } else {
                    path = localPath;
                }
            }
            return path;
        }
        public async Task<string> GetZip(string sender, string path) {
            if (_networkedClient != null) {
                string hash = Shai1Hash(sender);
                return await _networkedClient.GetZip(hash, path);
            }
            return "";
        }
    }

    public class ValidationResult : EventArgs {
        public bool ValidationSuceeded { get; set; }
        // ValidationState 3 is meant for api calls failed when they shouldn't have
        // Meaning somehow an invalid key slipped by the validation, or it got invalidated by outside sources
        // Right now the plugin isn't set up to actually make use of it, and this needs to be thought through
        public int ValidationState { get; set; }
    }
    public class VoiceFailure : EventArgs {
        string _failureMessage;
        Exception _exception;

        public string FailureMessage { get => _failureMessage; set => _failureMessage = value; }
        public Exception Exception { get => _exception; set => _exception = value; }
    }
}