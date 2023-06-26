using ElevenLabs;
using ElevenLabs.Voices;
using FFXIVLooseTextureCompiler.Networking;
using NAudio.Wave;

namespace RoleplayingVoiceCore {
    public class RoleplayingVoiceManager {
        private ElevenLabsClient _api;
        private NetworkedClient _networkedClient;
        private string clipPath = "";
        public RoleplayingVoiceManager(string apiKey, NetworkedClient client) {
            _api = new ElevenLabsClient(apiKey);
            _networkedClient = client;
        }

        public string ClipPath { get => clipPath; set => clipPath = value; }

        public async Task<string> DoVoice(string sender, string text, string voiceType) {
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
                clipPath = await _api.TextToSpeechEndpoint.TextToSpeechAsync(TrimText(text), characterVoice,
                    defaultVoiceSettings, null, Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
                using (var player = new AudioFileReader(clipPath)) {
                    output.Init(player);
                    output.Play();
                }
                _networkedClient.SendFile(CreateMD5(sender + text), clipPath);
            }
            return clipPath;
        }
        private string TrimText(string text) {
            string newText = text;
            foreach (char character in @"@#$%^&*()_+{}:;\/<>|`~".ToCharArray()) {
                newText = newText.Replace(character + "", null);
            }
            return newText;
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
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
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