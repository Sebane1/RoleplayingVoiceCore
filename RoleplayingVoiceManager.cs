using ElevenLabs;
using System.Diagnostics.Contracts;

namespace RoleplayingVoiceCore {
    public class RoleplayingVoiceManager {
        private ElevenLabsClient _api;

        public RoleplayingVoiceManager(string apiKey) {
            _api = new ElevenLabsClient(apiKey);
        }

        public async Task<string> GetVoice(string text, string apikey, string voiceType) {
            var voice = (await _api.VoicesEndpoint.GetAllVoicesAsync()).FirstOrDefault();
            var defaultVoiceSettings = await _api.VoicesEndpoint.GetDefaultVoiceSettingsAsync();
            var clipPath = await _api.TextToSpeechEndpoint.TextToSpeechAsync(text, voice, defaultVoiceSettings);
            return clipPath;
        }
    }
}