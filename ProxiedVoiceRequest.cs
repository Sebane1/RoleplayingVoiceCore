namespace RoleplayingVoiceCore {
    public class ProxiedVoiceRequest {
        string _character;
        string _voice;
        string _text;
        string _unfilteredtext;
        string _model;
        string extraJsonData;
        bool _aggressiveCache;

        public string Voice { get => _voice; set => _voice = value; }
        public string Text { get => _text; set => _text = value; }

        public bool AggressiveCache { get => _aggressiveCache; set => _aggressiveCache = value; }
        public string Model { get => _model; set => _model = value; }
        public string Character { get => _character; set => _character = value; }
        public string ExtraJsonData { get => extraJsonData; set => extraJsonData = value; }
        public string UnfilteredText { get => _unfilteredtext; set => _unfilteredtext = value; }
    }
}
