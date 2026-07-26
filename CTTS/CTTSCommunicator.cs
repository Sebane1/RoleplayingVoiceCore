using Newtonsoft.Json;
using System.Net.Http.Headers;
using System.Net;
using NAudio.Wave;
using NAudio.Lame;
using System.Diagnostics;
using RoleplayingVoiceCore;

namespace AIDataProxy.CTTS {
    public static class CTTSCommunicator {
        public static async Task<byte[]> GetVoiceData(string text, string speakerWavPath, string language, string serverAddress) {
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();
            while (true && stopwatch.ElapsedMilliseconds < 120000) {
                try {
                    using (HttpClient client = new HttpClient()) {
                        client.Timeout = TimeSpan.FromMinutes(2);
                        
                        using (var multipartFormContent = new MultipartFormDataContent()) {
                            // "request" field
                            string jsonRequest = JsonConvert.SerializeObject(new { input = text, language = language });
                            multipartFormContent.Add(new StringContent(jsonRequest), "request");

                            // "audio_prompt" field
                            if (File.Exists(speakerWavPath)) {
                                var fileStreamContent = new StreamContent(File.OpenRead(speakerWavPath));
                                fileStreamContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
                                multipartFormContent.Add(fileStreamContent, "audio_prompt", Path.GetFileName(speakerWavPath));
                            } else {
                                return null;
                            }

                            string url = serverAddress.TrimEnd('/') + "/v1/audio/speech/clone";
                            var response = await client.PostAsync(url, multipartFormContent);
                            if (response.IsSuccessStatusCode) {
                                using (var wavStream = await response.Content.ReadAsStreamAsync()) {
                                    return await AudioConversionHelper.WaveStreamToMp3Bytes(wavStream);
                                }
                            } else {
                                Console.WriteLine(response.StatusCode.ToString());
                                return null;
                            }
                        }
                    }
                } catch (Exception e) {
                    string error = e.Message;
                    Thread.Sleep(5000);
                }
            }
            return null;
        }
    }
}
