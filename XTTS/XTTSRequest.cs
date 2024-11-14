using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AIDataProxy.XTTS {
    public class XTTSRequest {

        public XTTSRequest(string text, string speaker_wav, string language = "en") {
            this.text = text;
            this.speaker_wav = speaker_wav;
            this.language = language;
        }

        public string text { get; set; }
        public string speaker_wav { get; set; }
        public string language { get; set; }
    }
}
