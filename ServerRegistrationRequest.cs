using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace CachedTTSRelay {
    public class ServerRegistrationRequest {
        private string _publicHostAddress = "";
        private string _port = "5670";
        private string _region = "";
        private string _alias = "";
        private string _uniqueIdentifier = "";
        private bool _getNearestIp;
        private long _lastResponse;
        private Vector2 _hardwareRegionLocation = new Vector2(); 
        public string PublicHostAddress { get => _publicHostAddress; set => _publicHostAddress = value; }
        public string Port { get => _port; set => _port = value; }
        public string Region { get => _region; set => _region = value; }
        public string Alias { get => _alias; set => _alias = value; }
        public string UniqueIdentifier { get => _uniqueIdentifier; set => _uniqueIdentifier = value; }
        public long LastResponse { get => _lastResponse; set => _lastResponse = value; }
        public bool GetNearestIp { get => _getNearestIp; set => _getNearestIp = value; }
        public Vector2 HardwareRegionLocation { get => _hardwareRegionLocation; set => _hardwareRegionLocation = value; }
    }
}
