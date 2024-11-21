namespace CachedTTSRelay {
    public class InformationRequest {
        private string _name;
        private InformationRequestType _informationRequestType;

        public InformationRequestType InformationRequestType { get => _informationRequestType; set => _informationRequestType = value; }
        public string Name { get => _name; set => _name = value; }
    }
    public enum InformationRequestType {
        GetVoiceLineList = 0,
        UploadVoiceLines
    }
}