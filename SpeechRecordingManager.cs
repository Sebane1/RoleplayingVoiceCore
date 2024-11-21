using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.Diagnostics;

namespace RoleplayingVoiceCore {
    public class SpeechRecordingManager {
        private Stopwatch _timer = new Stopwatch();
        private string _outputPath;
        private WaveInEvent _waveSource;
        private MemoryStream _recordedAudioStream;
        private WaveFileWriter _waveWriter;
        private string _basePath;
        private string _modelName;
        private string _finalText;
        int _retry;
        private bool _isRecording;
        private bool _rpMode;

        public string FinalText { get => _finalText; set => _finalText = value; }
        public bool IsRecording { get => _isRecording; set => _isRecording = value; }
        public bool RpMode { get => _rpMode; set => _rpMode = value; }

        public SpeechRecordingManager() {

        }
        public event EventHandler RecordingFinished;

        /// <summary>
        /// Record from the mic
        /// </summary>
        /// <param name="seconds">Duration in seconds</param>
        /// <param name="filename">Output file name</param>
        public void RecordAudio(string outputPath) {
            _outputPath = outputPath;
            _waveSource = new WaveInEvent {
                WaveFormat = new WaveFormat(16000, 1),
            };
            if (File.Exists(outputPath)) {
                File.Delete(outputPath);
            }
            _waveSource.DataAvailable += DataAvailable;
            _waveSource.RecordingStopped += RecordingStopped;
            _recordedAudioStream = new MemoryStream();
            _waveWriter = new WaveFileWriter(_recordedAudioStream, _waveSource.WaveFormat);
            _waveSource.StartRecording();
            _isRecording = true;
        }
        /// <summary>
        /// Callback executed when new data is available
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void DataAvailable(object sender, WaveInEventArgs e) {
            if (_waveWriter != null) {
                _waveWriter.Write(e.Buffer, 0, e.BytesRecorded);
                int threshold = 500;
                int value = Math.Abs(BitConverter.ToInt16(e.Buffer, (e.BytesRecorded - 2)));
            }
        }
        /// <summary>
        /// Callback that will be executed once the recording duration has elapsed
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        public async Task<MemoryStream> StopRecording() {
            _waveSource?.StopRecording();
            _waveWriter.Flush();
            _waveSource.DataAvailable -= DataAvailable;
            _waveSource.RecordingStopped -= RecordingStopped;
            /*Stop the audio recording*/
            MemoryStream memoryStream = new MemoryStream();
            try {
                _recordedAudioStream.Position = 0;
                float max = 0;

                using (var reader = new WaveFileReader(_recordedAudioStream)) {
                    ISampleProvider floats = reader.ToSampleProvider();
                    // find the max peak
                    float[] buffer = new float[reader.WaveFormat.SampleRate];
                    int read;
                    do {
                        read = floats.Read(buffer, 0, buffer.Length);
                        for (int n = 0; n < read; n++) {
                            var abs = Math.Abs(buffer[n]);
                            if (abs > max) { max = abs; }
                        }
                    } while (read > 0);
                    if (max == 0 || max > 1.0f) {
                        reader.Position = 0;
                        MediaFoundationEncoder.EncodeToMp3(reader, memoryStream);
                    } else {
                        Console.WriteLine($"Max sample value: {max}");
                        // rewind and amplify
                        reader.Position = 0;
                        VolumeSampleProvider volumeSampleProvider = new VolumeSampleProvider(floats);
                        volumeSampleProvider.Volume = 1.0f / max;
                        // write out to a new WAV file
                        MediaFoundationEncoder.EncodeToMp3(volumeSampleProvider.ToWaveProvider16(), memoryStream);
                    }
                }
            } catch {

            }
            try {
                _waveSource?.Dispose();
                _waveWriter?.Dispose();
            } catch { }
            _timer.Reset();
            _isRecording = false;
            return memoryStream;
        }
        /// <summary>
        /// Callback executed when the recording is stopped
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void RecordingStopped(object sender, StoppedEventArgs e) {

        }
    }
}
