namespace RoleplayingVoiceCore {
    public class ProxiedVoiceRequest {
        string _character;
        string _voice;
        string _text;
        string _unfilteredtext;
        string _model;
        string extraJsonData;
        VoiceLinePriority _voiceLinePriority;

        bool _aggressiveCache;
        bool _redoLine;
        private bool _override;

        public string Voice { get => _voice; set => _voice = value; }
        public string Text { get => _text; set => _text = value; }

        public bool AggressiveCache { get => _aggressiveCache; set => _aggressiveCache = value; }
        public string Model { get => _model; set => _model = value; }
        public string Character { get => _character; set => _character = value; }
        public string ExtraJsonData { get => extraJsonData; set => extraJsonData = value; }
        public string UnfilteredText { get => _unfilteredtext; set => _unfilteredtext = value; }
        public bool RedoLine { get => _redoLine; set => _redoLine = value; }
        public bool Override { get => _override; set => _override = value; }
        public VoiceLinePriority VoiceLinePriority { get => _voiceLinePriority; set => _voiceLinePriority = value; }
    }
    public enum VoiceLinePriority {
        Elevenlabs = 0,
        AlternativeCache = 1,
        XTTS = 2,
        None = 3,
        Blacklist = 4
    }
}

