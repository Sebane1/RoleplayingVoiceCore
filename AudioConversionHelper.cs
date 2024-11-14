using NAudio.Lame;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RoleplayingVoiceCore {
    public static class AudioConversionHelper {
        public static int GetSimpleHash(string s) {
            return s.Select(a => (int)a).Sum();
        }
        public static async Task<byte[]> WaveStreamToMp3Bytes(Stream wavStream) {
            wavStream.Position = 0;
            MemoryStream mp3Stream = new MemoryStream();
            using (WaveFileReader waveFileReader = new WaveFileReader(wavStream)) {
                using (var resampler = new MediaFoundationResampler(waveFileReader, 44100)) {
                    using (var mp3Writer = new LameMP3FileWriter(mp3Stream, resampler.WaveFormat, 64)) {
                        resampler.ResamplerQuality = 60;
                        var arr = new byte[128];
                        while (resampler.Read(arr, 0, arr.Length) > 0) {
                            // Send stream to the provider
                            await mp3Writer.WriteAsync(arr, 0, arr.Length);
                        }
                        await mp3Writer.FlushAsync();
                    }
                }
            }
            mp3Stream.Position = 0;
            var bytes = mp3Stream.ToArray();
            if (bytes.Length > 0) {
                return bytes;
            } else {
                return null;
            }
        }
    }
}
