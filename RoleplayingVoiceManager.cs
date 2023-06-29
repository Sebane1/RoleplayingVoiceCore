using ElevenLabs;
using ElevenLabs.Voices;
using FFXIVLooseTextureCompiler.Networking;
using NAudio.Wave;
using RoleplayingVoiceCore.AudioRecycler;

namespace RoleplayingVoiceCore {
    public class RoleplayingVoiceManager {
        private string _apiKey;
        private ElevenLabsClient? _api;
        private NetworkedClient _networkedClient;
        private CharacterVoices _characterVoices = new CharacterVoices();

        private string clipPath = "";
        public event EventHandler? VoicesUpdated;
        public event EventHandler<ValidationResult>? OnApiValidationComplete;
        private bool apiValid;
        public RoleplayingVoiceManager(string apiKey, NetworkedClient client, CharacterVoices? characterVoices = null) {
            _apiKey = apiKey;
            try
            {
                _api = new ElevenLabsClient(apiKey);
                _api.UserEndpoint.GetUserInfoAsync();
                apiValid = true;
            }
            catch (Exception e)
            {
                var errorMain = e.Message.ToString();
                if (errorMain.Contains("invalid_api_key"))
                {
                    apiValid = false;
                }
            }
            _networkedClient = client;
            if (characterVoices != null) {
                _characterVoices = characterVoices;
            }
        }

        public string ClipPath { get => clipPath; set => clipPath = value; }
        public CharacterVoices CharacterVoices { get => _characterVoices; set => _characterVoices = value; }
        public string ApiKey { get => _apiKey; set => _apiKey = value; }

        public async Task<bool> ApiValidation(string key)
        {
            if (!string.IsNullOrEmpty(key) && key.All(c => char.IsAsciiLetterOrDigit(c)))
            {
                try
                {
                    var api = new ElevenLabsClient(key);
                    await api.UserEndpoint.GetUserInfoAsync();
                    apiValid = true;
                }
                catch (Exception e)
                {
                    var errorMain = e.Message.ToString();
                    if (errorMain.Contains("invalid_api_key"))
                    {
                        apiValid = false;
                    }
                }
            }
            ValidationResult validationResult = new ValidationResult();
            validationResult.ValidationSuceeded = apiValid;
            OnApiValidationComplete?.Invoke(this, validationResult);
            if (apiValid)
            {
                return true;
            }
            return false;
        }

        public async Task<string[]> GetVoiceList() {
            List<string> voicesNames = new List<string>();
            IReadOnlyList<Voice>? voices = null;
            if (_api != null)
            {
                try
                {
                    voices = await _api.VoicesEndpoint.GetAllVoicesAsync();
                }
                catch (Exception e)
                {
                    var errorVoiceList = e.Message.ToString();
                    if (errorVoiceList.Contains("invalid_api_key"))
                    {
                        apiValid = false;
                    }
                }
            }
            voicesNames.Add("None");
            if (voices != null)
            {
                foreach (var voice in voices)
                {
                    voicesNames.Add(voice.Name);
                }
            }
            return voicesNames.ToArray();
        }
        public async Task<string> DoVoice(string sender, string text, string voiceType, bool isEmote) {
            IReadOnlyList<Voice>? voices = null;
            try
            {
                voices = await _api.VoicesEndpoint.GetAllVoicesAsync();
            }
            catch (Exception e)
            {
                var errorVoiceGen = e.Message.ToString();
                if (errorVoiceGen.Contains("invalid_api_key"))
                {
                    apiValid = false;
                }
            }
            Voice characterVoice = null;
            if (voices != null)
            {
                foreach (var voice in voices)
                {
                    if (voice.Name.ToLower().Contains(voiceType.ToLower()))
                    {
                        characterVoice = voice;
                        break;
                    }
                }
            }
            var defaultVoiceSettings = new VoiceSettings(0.3f, 1);
            if (characterVoice != null) {
                WaveOutEvent output = new WaveOutEvent();
                try {
                    if (!text.StartsWith("(") && !text.EndsWith(")") && !(isEmote && !text.Contains(@""""))) {
                        string trimmedText = TrimText(text);
                        if (!CharacterVoices.VoiceCatalogue.ContainsKey(voiceType)) {
                            CharacterVoices.VoiceCatalogue[voiceType] = new Dictionary<string, string>();
                        }
                        if (!CharacterVoices.VoiceCatalogue[(voiceType)].ContainsKey(trimmedText.ToLower())) {
                            clipPath = await _api.TextToSpeechEndpoint
                                .TextToSpeechAsync(@"""" + trimmedText.Replace(@"""", null) + @"""", characterVoice,
                                defaultVoiceSettings, null,
                                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + @"\RPVoiceCache");
                            CharacterVoices.VoiceCatalogue[(voiceType)].Add(trimmedText.ToLower(), clipPath);
                        } else if (File.Exists(CharacterVoices.VoiceCatalogue[(voiceType)][trimmedText.ToLower()])) {
                            clipPath = CharacterVoices.VoiceCatalogue[(voiceType)][trimmedText.ToLower()];
                        } else {
                            CharacterVoices.VoiceCatalogue[(voiceType)].Remove(trimmedText.ToLower());
                            clipPath = await _api.TextToSpeechEndpoint
                                .TextToSpeechAsync(@"""" + trimmedText.Replace(@"""", null) + @"""", characterVoice,
                                defaultVoiceSettings, null,
                                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + @"\RPVoiceCache");
                            CharacterVoices.VoiceCatalogue[(voiceType)].Add(trimmedText.ToLower(), clipPath);
                        }
                        VoicesUpdated.Invoke(this, EventArgs.Empty);
                        if (File.Exists(clipPath)) {
                            using (var player = new AudioFileReader(clipPath)) {
                                output.Init(player);
                                output.Play();
                            }
                            _networkedClient.SendFile(CreateMD5(sender + text), clipPath);
                        }
                    }
                } catch {

                }
            }
            return clipPath;
        }
        private string TrimText(string text) {
            string newText = text.Replace("XD", "ahahaha")
            .Replace("lmao", "ahahaha")
            .Replace("lol", "ahahaha")
            .Replace("lmfao", "ahahaha")
            .Replace("kek", "ahahaha")
            .Replace(":D", ".")
            .Replace(":3", ".")
            .Replace(":P", ".");
            foreach (char character in @"@#$%^&*()_+{}:;\/<>|`~".ToCharArray()) {
                newText = newText.Replace(character + "", null);
            }
            return ExtractQuotations(newText).Replace("\"", null);
        }

        private string ExtractQuotations(string text) {
            string newText = "";
            string[] strings = text.Split('"');
            if (strings.Length > 1) {
                for (int i = 0; i < strings.Length; i++) {
                    if ((i + 1) % 2 == 0) {
                        newText += strings[i] + (strings.Length % 2 == 0 ? "\r\n\r\n" : "");
                    }
                }
                return newText;
            } else {
                return text;
            }
        }

        public static string CreateMD5(string input) {
            // Use input string to calculate MD5 hash
            using (System.Security.Cryptography.MD5 md5 = System.Security.Cryptography.MD5.Create()) {
                byte[] inputBytes = System.Text.Encoding.ASCII.GetBytes(input);
                byte[] hashBytes = md5.ComputeHash(inputBytes);

                return Convert.ToHexString(hashBytes); // .NET 5 +
            }
        }
        public async Task<string> GetVoice(string sender, string text, float volume) {
            if (_networkedClient != null) {
                string path = "";
                string hash = CreateMD5(sender + text);
                string localPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + @"\RPVoiceCache", hash + ".mp3");
                if (!File.Exists(localPath)) {
                    path = await _networkedClient.GetFile(hash,
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + @"\RPVoiceCache");
                } else {
                    path = localPath;
                }
                if (!string.IsNullOrEmpty(path)) {
                    WaveOutEvent output = new WaveOutEvent();
                    using (var player = new AudioFileReader(path)) {
                        output.Volume = Math.Clamp(volume, 0, 1);
                        output.Init(player);
                        output.Play();
                    }
                }

            }
            return "";
        }
    }
}

public class ValidationResult : EventArgs
{
    public bool ValidationSuceeded { get; set; }
}