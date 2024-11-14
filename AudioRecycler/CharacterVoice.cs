using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RoleplayingMediaCore.AudioRecycler {
    public class CharacterVoices {
        Dictionary<string, Dictionary<string, string>> _voiceCatalogue = new Dictionary<string, Dictionary<string, string>>();
        Dictionary<string, Dictionary<string, string>> _voiceEngine = new Dictionary<string, Dictionary<string, string>>();
        public Dictionary<string, Dictionary<string, string>> VoiceCatalogue { get => _voiceCatalogue; set => _voiceCatalogue = value; }
        public Dictionary<string, Dictionary<string, string>> VoiceEngine { get => _voiceEngine; set => _voiceEngine = value; }
    }
}
