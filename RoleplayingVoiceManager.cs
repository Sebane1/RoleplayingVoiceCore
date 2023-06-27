using ElevenLabs;
using ElevenLabs.Voices;
using FFXIVLooseTextureCompiler.Networking;
using NAudio.Wave;
using RoleplayingVoiceCore.AudioRecycler;

namespace RoleplayingVoiceCore {
    public class RoleplayingVoiceManager {
        private ElevenLabsClient _api;
        private NetworkedClient _networkedClient;
        private CharacterVoices _characterVoices = new CharacterVoices();

        private string clipPath = "";
        public event EventHandler VoicesUpdated;
        public RoleplayingVoiceManager(string apiKey, NetworkedClient client, CharacterVoices characterVoices = null) {
            _api = new ElevenLabsClient(apiKey);
            _networkedClient = client;
            if (characterVoices != null) {
                _characterVoices = characterVoices;
            }
        }

        public string ClipPath { get => clipPath; set => clipPath = value; }
        public CharacterVoices CharacterVoices { get => _characterVoices; set => _characterVoices = value; }

        public async Task<string> DoVoice(string sender, string text, string voiceType, bool isEmote) {
            var voices = await _api.VoicesEndpoint.GetAllVoicesAsync();
            Voice characterVoice = null;
            foreach (var voice in voices) {
                if (voice.Name.ToLower().Contains(voiceType.ToLower())) {
                    characterVoice = voice;
                    break;
                }
            }
            var defaultVoiceSettings = await _api.VoicesEndpoint.GetDefaultVoiceSettingsAsync();
            if (characterVoice != null) {
                WaveOutEvent output = new WaveOutEvent();
                if (!text.StartsWith("(") && !text.EndsWith(")") && !(isEmote && !text.Contains(@""""))) {
                    string trimmedText = TrimText(text);
                    if (!CharacterVoices.VoiceCatalogue.ContainsKey(voiceType)) {
                        CharacterVoices.VoiceCatalogue[voiceType] = new Dictionary<string, string>();
                    }
                    if (!CharacterVoices.VoiceCatalogue[(voiceType)].ContainsKey(trimmedText.ToLower())) {
                        clipPath = await _api.TextToSpeechEndpoint.TextToSpeechAsync(trimmedText, characterVoice,
                            defaultVoiceSettings, null,
                            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + @"\RPVoiceCache");
                        CharacterVoices.VoiceCatalogue[(voiceType)].Add(trimmedText.ToLower(), clipPath);
                    } else if (File.Exists(CharacterVoices.VoiceCatalogue[(voiceType)][trimmedText.ToLower()])) {
                        clipPath = CharacterVoices.VoiceCatalogue[(voiceType)][trimmedText.ToLower()];
                    } else {
                        CharacterVoices.VoiceCatalogue[(voiceType)].Remove(trimmedText.ToLower());
                        clipPath = await _api.TextToSpeechEndpoint.TextToSpeechAsync(trimmedText, characterVoice,
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
            return ExtractQuotations(newText);
        }

        private string ExtractQuotations(string text) {
            string newText = "";
            string[] strings = text.Split('"');
            if (strings.Length > 1) {
                for (int i = 0; i < strings.Length; i++) {
                    if ((i + 1) % 2 == 0) {
                        newText += strings[i] + "\r\n\r\n";
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

                // Convert the byte array to hexadecimal string prior to .NET 5
                // StringBuilder sb = new System.Text.StringBuilder();
                // for (int i = 0; i < hashBytes.Length; i++)
                // {
                //     sb.Append(hashBytes[i].ToString("X2"));
                // }
                // return sb.ToString();
            }
        }
        public async Task<string> GetVoice(string sender, string text) {
            string path = await _networkedClient.GetFile(CreateMD5(sender + text),
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + @"\RPVoiceCache");
            if (!string.IsNullOrEmpty(path)) {
                WaveOutEvent output = new WaveOutEvent();
                using (var player = new AudioFileReader(path)) {
                    output.Init(player);
                    output.Play();
                }
            }
            return "";
        }
    }
}