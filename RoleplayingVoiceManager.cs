using ElevenLabs;
using ElevenLabs.History;
using ElevenLabs.User;
using ElevenLabs.Voices;
using FFXIVLooseTextureCompiler.Networking;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using RoleplayingVoiceCore.AudioRecycler;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using static System.Net.Mime.MediaTypeNames;

namespace RoleplayingVoiceCore {
    public class RoleplayingVoiceManager {
        private string _apiKey;
        private ElevenLabsClient? _api;
        private NetworkedClient _networkedClient;
        private CharacterVoices _characterVoices = new CharacterVoices();
        SubscriptionInfo _info = new SubscriptionInfo();

        private string clipPath = "";
        public event EventHandler? VoicesUpdated;
        public event EventHandler<ValidationResult>? OnApiValidationComplete;

        private IReadOnlyList<HistoryItem> _history;
        private bool apiValid;
        private string rpVoiceCache;

        public RoleplayingVoiceManager(string apiKey, string cache, NetworkedClient client, CharacterVoices? characterVoices = null) {
            rpVoiceCache = cache;
            // Spin a new thread for this
            Task.Run(() => {
                _apiKey = apiKey;
                try {
                    _api = new ElevenLabsClient(apiKey);
                    var test = _api.UserEndpoint.GetUserInfoAsync().Result;
                    apiValid = true;
                } catch (Exception e) {
                    var errorMain = e.Message.ToString();
                    if (errorMain.Contains("invalid_api_key")) {
                        apiValid = false;
                    }
                }
                _networkedClient = client;
                if (characterVoices != null) {
                    _characterVoices = characterVoices;
                }
            });
        }

        public string ClipPath { get => clipPath; set => clipPath = value; }
        public CharacterVoices CharacterVoices { get => _characterVoices; set => _characterVoices = value; }
        public string ApiKey { get => _apiKey; set => _apiKey = value; }
        public SubscriptionInfo Info { get => _info; set => _info = value; }
        public NetworkedClient NetworkedClient { get => _networkedClient; set => _networkedClient = value; }

        public async Task<bool> ApiValidation(string key) {
            if (!string.IsNullOrEmpty(key) && key.All(c => char.IsAsciiLetterOrDigit(c))) {
                try {
                    var api = new ElevenLabsClient(key);
                    await api.UserEndpoint.GetUserInfoAsync();
                    apiValid = true;
                } catch (Exception e) {
                    var errorMain = e.Message.ToString();
                    if (errorMain.Contains("invalid_api_key")) {
                        apiValid = false;
                    }
                }
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
            IReadOnlyList<Voice>? voices = null;
            if (_api != null) {
                try {
                    voices = await _api.VoicesEndpoint.GetAllVoicesAsync();
                } catch (Exception e) {
                    var errorVoiceList = e.Message.ToString();
                    if (errorVoiceList.Contains("invalid_api_key")) {
                        apiValid = false;
                        state.ValidationState = 3;
                        OnApiValidationComplete?.Invoke(this, state);
                    }
                }
            }
            voicesNames.Add("None");
            if (voices != null) {
                foreach (var voice in voices) {
                    voicesNames.Add(voice.Name);
                }
            }
            return voicesNames.ToArray();
        }

        public async void RefreshElevenlabsSubscriptionInfo() {
            ValidationResult state = new ValidationResult();
            _info = null;
            SubscriptionInfo? value = null;
            if (_api != null) {
                try {
                    value = await _api.UserEndpoint.GetSubscriptionInfoAsync();
                } catch (Exception e) {
                    var errorSubInfo = e.Message.ToString();
                    if (errorSubInfo.Contains("invalid_api_key")) {
                        apiValid = false;
                        state.ValidationState = 3;
                        OnApiValidationComplete?.Invoke(this, state);
                    }
                }
            }
            _info = value;
        }
        public async Task<string> DoVoice(string sender, string text, string voiceType,
            bool isEmote, float volume, Vector3 position, bool aggressiveSplicing) {
            string hash = Shai1Hash(sender + text);
            ValidationResult state = new ValidationResult();
            IReadOnlyList<Voice>? voices = null;
            if (_api != null) {
                try {
                    voices = await _api.VoicesEndpoint.GetAllVoicesAsync();
                } catch (Exception e) {
                    var errorVoiceGen = e.Message.ToString();
                    if (errorVoiceGen.Contains("invalid_api_key")) {
                        apiValid = false;
                        state.ValidationState = 3;
                        OnApiValidationComplete?.Invoke(this, state);
                    }
                }
            }
            Voice? characterVoice = null;
            if (voices != null) {
                foreach (var voice in voices) {
                    if (voice.Name.ToLower().Contains(voiceType.ToLower())) {
                        characterVoice = voice;
                        break;
                    }
                }
            }
            if (characterVoice != null) {
                try {
                    if (!text.StartsWith("(") && !text.EndsWith(")") && !(isEmote && (!text.Contains(@"""") || text.Contains(@"“")))) {
                        string stitchedPath = Path.Combine(rpVoiceCache, hash + ".mp3");
                        if (!File.Exists(stitchedPath)) {
                            string trimmedText = TrimText(text);
                            string[] audioClips = (trimmedText.Contains(@"""") || trimmedText.Contains(@"“"))
                                ? ExtractQuotationsToList(trimmedText, aggressiveSplicing) : AggressiveWordSplicing(trimmedText);
                            List<string> audioPaths = new List<string>();
                            foreach (string audioClip in audioClips) {
                                audioPaths.Add(await GetVoicePath(voiceType, audioClip, characterVoice));
                            }
                            VoicesUpdated?.Invoke(this, EventArgs.Empty);
                            MemoryStream playbackStream = ConcatenateAudio(audioPaths.ToArray());
                            try {
                                using (Stream stitchedStream = File.OpenWrite(stitchedPath)) {
                                    playbackStream.Position = 0;
                                    playbackStream.CopyTo(stitchedStream);
                                    stitchedStream.Flush();
                                    stitchedStream.Close();
                                }
                            } catch (Exception e) {

                                var error = e.Message;

                            }
                        }
                        Task.Run(() => _networkedClient.SendFile(hash, stitchedPath, position));
                        clipPath = stitchedPath;
                    }
                } catch {

                }
            }
            return clipPath;
        }

        public async Task<bool> SendSound(string sender, string identifier, string soundOnDisk, float volume, Vector3 position) {
            string hash = Shai1Hash(sender + identifier);
            bool sendState = false;
            await Task.Run(async () => { sendState = await _networkedClient.SendFile(hash, soundOnDisk, position); });
            return sendState;
        }
        public async Task<bool> SendZip(string sender, string soundOnDisk) {
            string hash = Shai1Hash(sender);
            bool sendState = false;
            await Task.Run(async () => { sendState = await _networkedClient.SendZip(hash, soundOnDisk); });
            return sendState;
        }
        private async Task<string> GetVoicePath(string voiceType, string trimmedText, Voice characterVoice) {
            string audioPath = "";
            var defaultVoiceSettings = new VoiceSettings(0.3f, 1);
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
            return audioPath;
        }

        private async Task<string> GetVoiceFromElevenLabs(string trimmedText, string voiceType,
            VoiceSettings defaultVoiceSettings, Voice characterVoice) {
            string unquotedText = trimmedText.Replace(@"""", null);
            string numberAdjusted = char.IsDigit(unquotedText.Last()) ? unquotedText + "." : unquotedText;
            string finalText = @"""" + numberAdjusted + @"""";
            string audioPath = "";
            bool foundInHistory = false;
            var history = await _api.HistoryEndpoint.GetHistoryAsync();
            foreach (var item in history) {
                if (item.VoiceName.ToLower().Contains(voiceType.ToLower())) {
                    if (item.Text.ToLower().Replace(@"""", null).Replace(".", null).Trim()
                        == finalText.ToLower().Replace(@"""", null).Replace(".", null).Trim()) {
                        audioPath = await _api.HistoryEndpoint.GetHistoryAudioAsync(item, rpVoiceCache);
                        foundInHistory = true;
                        break;
                    }
                }
            }
            if (!foundInHistory) {
                audioPath = await _api.TextToSpeechEndpoint
                    .TextToSpeechAsync(finalText, characterVoice,
                    defaultVoiceSettings, null, rpVoiceCache);
            }
            CharacterVoices.VoiceCatalogue[(voiceType)].Add(trimmedText.ToLower(), audioPath);
            return audioPath;
        }

        private string TrimText(string text) {
            string newText = text.Replace("XD", "ahahaha")
            .Replace("lmao", "ahahaha")
            .Replace("lol", "ahahaha")
            .Replace("lmfao", "ahahaha")
            .Replace("kek", "ahahaha")
            .Replace("rotflmao", "ahahaha")
            .Replace("rotflmfao", "ahahaha")
            .Replace("lmao", "ahahaha")
            .Replace(":D", ".")
            .Replace(":3", ".")
            .Replace(":P", ".")
            .Replace("<3", "love");
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
            Vector3 centerPosition, bool isShoutYell, string subDirectory = null, bool ignoreCache = false) {
            string path = "";
            if (_networkedClient != null) {
                KeyValuePair<Vector3, string> data = new KeyValuePair<Vector3, string>();
                Vector3 position = new Vector3();
                Guid id = Guid.NewGuid();
                string hash = Shai1Hash(sender + identifier);
                string localPath = Path.Combine(rpVoiceCache + subDirectory, (!ignoreCache ? hash : id) + ".mp3");
                if (!File.Exists(localPath) || ignoreCache) {
                    data = await _networkedClient.GetFile(hash, rpVoiceCache + subDirectory, id.ToString());
                    path = data.Value;
                    position = data.Key;
                } else {
                    path = localPath;
                    position = await _networkedClient.GetPosition(hash);
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
}