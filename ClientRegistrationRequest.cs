using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace CachedTTSRelay {
    public class ClientRegistrationRequest {
        private bool _getNearestIp = true;
        public bool GetNearestIp { get => _getNearestIp; set => _getNearestIp = value; }
    }
}
