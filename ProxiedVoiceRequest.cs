namespace RoleplayingVoiceCore {
    public class ProxiedVoiceRequest {
        private bool _useMuteList;
        private string _versionIdentifier;
        private string _character;
        private string _voice;
        private string _text;
        private string _unfilteredtext;
        private string _model;
        private string extraJsonData;
        private VoiceLinePriority _voiceLinePriority;

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
        public string RawText { get; internal set; }
        public string VersionIdentifier { get => _versionIdentifier; set => _versionIdentifier = value; }
        internal bool UseMuteList { get => _useMuteList; set => _useMuteList = value; }
    }
    public enum VoiceLinePriority {
        Elevenlabs = 0,
        Alternative = 1,
        XTTS = 2,
        None = 3,
        Datamining = 4,
        Ignore = 5,
        SendNote = 6,
    }
}

