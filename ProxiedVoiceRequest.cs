using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RoleplayingVoiceCore {
    public class ProxiedVoiceRequest {
        string _voice;
        string _text;
        string _model;
        bool _disableCache;

        public string Voice { get => _voice; set => _voice = value; }
        public string Text { get => _text; set => _text = value; }

        public bool DisableCache { get => _disableCache; set => _disableCache = value; }
        public string Model { get => _model; set => _model = value; }
    }
}
