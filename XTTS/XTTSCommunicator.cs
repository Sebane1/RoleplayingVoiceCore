using Newtonsoft.Json;
using System.Net.Http.Headers;
using System.Net;
using NAudio.Wave;
using NAudio.Lame;
using System.Diagnostics;
using RoleplayingVoiceCore;

namespace AIDataProxy.XTTS {
    public static class XTTSCommunicator {
        private static void Dummy() {
            Action<Type> noop = _ => { };
            var dummy = typeof(NAudio.Lame.LameDLL);
            noop(dummy);
        }
        static bool _requestAlreadyProcessing = false;
        static bool _lastSuccess;
        public static async Task<byte[]> GetVoiceData(string voice, string text, string folder, string language) {
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();
            while (true && stopwatch.ElapsedMilliseconds < 120000) {
                try {
                    var httpWebRequest = (HttpWebRequest)WebRequest.Create("http://localhost:8020/tts_to_audio/");
                    httpWebRequest.ContentType = "application/json";
                    httpWebRequest.Headers.Add("Accept: application/json");
                    httpWebRequest.Method = "POST";
                    httpWebRequest.Timeout = int.MaxValue;
                    string json = JsonConvert.SerializeObject(new XTTSRequest(text, voice, language));
                    using (var streamWriter = new StreamWriter(httpWebRequest.GetRequestStream())) {
                        streamWriter.Write(json);
                    }
                    var httpResponse = (HttpWebResponse)httpWebRequest.GetResponse();
                    MemoryStream wavStream = new MemoryStream();
                    if (httpResponse.StatusCode == HttpStatusCode.OK) {
                        var responseStream = httpResponse.GetResponseStream();
                        await responseStream.CopyToAsync(wavStream);
                        await responseStream.FlushAsync();
                        wavStream.Position = 0;
                        return await AudioConversionHelper.WaveStreamToMp3Bytes(wavStream);
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
            if (!_requestAlreadyProcessing) {
                _requestAlreadyProcessing = true;
                Task.Run(() => {
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
                        _lastSuccess = httpResponse.StatusCode == HttpStatusCode.OK;
                    } catch { }
                    _requestAlreadyProcessing = false;
                });
            }
            return _lastSuccess;
        }
    }
}
