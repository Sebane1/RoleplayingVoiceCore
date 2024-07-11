using Newtonsoft.Json;
using System.Net.Http.Headers;
using System.Net;
using NAudio.Wave;
using NAudio.Lame;
using System.Diagnostics;

namespace AIDataProxy.XTTS {
    public static class XTTSCommunicator {
        private static void Dummy() {
            Action<Type> noop = _ => { };
            var dummy = typeof(NAudio.Lame.LameDLL);
            noop(dummy);
        }
        public static async Task<byte[]> GetAudioAlternate(string voice, string text, string folder) {
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();
            while (true && stopwatch.ElapsedMilliseconds < 120000) {
                try {
                    var httpWebRequest = (HttpWebRequest)WebRequest.Create("http://localhost:8020/tts_to_audio/");
                    httpWebRequest.ContentType = "application/json";
                    httpWebRequest.Headers.Add("Accept: application/json");
                    httpWebRequest.Method = "POST";
                    httpWebRequest.Timeout = int.MaxValue;
                    string json = JsonConvert.SerializeObject(new XTTSRequest(text, voice));
                    using (var streamWriter = new StreamWriter(httpWebRequest.GetRequestStream())) {
                        streamWriter.Write(json);
                    }
                    var httpResponse = (HttpWebResponse)httpWebRequest.GetResponse();
                    MemoryStream wavStream = new MemoryStream();
                    MemoryStream mp3Stream = new MemoryStream();
                    if (httpResponse.StatusCode == HttpStatusCode.OK) {
                        var responseStream = httpResponse.GetResponseStream();
                        await responseStream.CopyToAsync(wavStream);
                        await responseStream.FlushAsync();
                        wavStream.Position = 0;
                        using (WaveFileReader waveFileReader = new WaveFileReader(wavStream)) {
                            using (var mp3Writer = new LameMP3FileWriter(mp3Stream, waveFileReader.WaveFormat, 64)) {
                                await waveFileReader.CopyToAsync(mp3Writer);
                                await waveFileReader.FlushAsync();
                            }
                        }
                        mp3Stream.Position = 0;
                        var bytes = mp3Stream.ToArray();
                        if (bytes.Length > 0) {
                            return bytes;
                        }
                    } else {
                        Console.WriteLine(httpResponse.StatusCode.ToString());
                    }
                } catch (Exception e) {
                    string error = e.Message;
                    Thread.Sleep(5000);
                    SetSpeakers(folder);
                }
            }
            return null;
        }
        public static bool SetSpeakers(string folder) {
            try {
                var httpWebRequest = (HttpWebRequest)WebRequest.Create("http://localhost:8020/set_speaker_folder/");
                httpWebRequest.ContentType = "application/json";
                httpWebRequest.Headers.Add("Accept: application/json");
                httpWebRequest.Method = "POST";
                string json = "{\"speaker_folder\": \"" + folder.Replace(@"\", "/") + "\"}";
                using (var streamWriter = new StreamWriter(httpWebRequest.GetRequestStream())) {
                    streamWriter.Write(json);
                }
                var httpResponse = (HttpWebResponse)httpWebRequest.GetResponse();
                return httpResponse.StatusCode == HttpStatusCode.OK;

            } catch { }
            return false;
        }
    }
}
