using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RoleplayingMediaCore.AudioRecycler {
    public class CharacterVoices {
        Dictionary<string, Dictionary<string, string>> voiceCatalogue = new Dictionary<string, Dictionary<string, string>>();
        public Dictionary<string, Dictionary<string, string>> VoiceCatalogue { get => voiceCatalogue; set => voiceCatalogue = value; }
    }
}
