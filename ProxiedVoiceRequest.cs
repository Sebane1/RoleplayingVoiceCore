namespace RoleplayingVoiceCore {
    public class ProxiedVoiceRequest {
        string _voice;
        string _text;
        string _model;
        bool _aggressiveCache;

        public string Voice { get => _voice; set => _voice = value; }
        public string Text { get => _text; set => _text = value; }

        public bool AggressiveCache { get => _aggressiveCache; set => _aggressiveCache = value; }
        public string Model { get => _model; set => _model = value; }
    }
}
