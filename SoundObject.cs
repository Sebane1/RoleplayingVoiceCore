using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.Numerics;

namespace RoleplayingVoiceCore {
    public class SoundObject {
        private IPlayerObject _playerObject;
        private VolumeSampleProvider _volumeSampleProvider;
        private WaveOutEvent _waveOutEvent;
        private string _soundPath;
        private SoundType soundType;
        private bool stopPlaybackOnMovement;
        private Vector3 lastPosition;
        private bool stopForReal;

        public SoundObject(IPlayerObject playerObject, WaveOutEvent waveOutEvent, SoundType soundType, string soundPath) {
            _playerObject = playerObject;
            _waveOutEvent = waveOutEvent;
            _soundPath = soundPath;
            this.soundType = soundType;
            if (soundType == SoundType.Song) {
                waveOutEvent.PlaybackStopped += delegate {
                    using (var player = new AudioFileReader(soundPath)) {
                        if (!stopForReal) {
                            VolumeSampleProvider = new VolumeSampleProvider(player.ToSampleProvider());
                            VolumeSampleProvider.Volume = 1;
                            WaveOutEvent?.Init(VolumeSampleProvider);
                            WaveOutEvent?.Play();
                        }
                    }
                };
                Task.Run(async () => {
                    try {
                        Thread.Sleep(500);
                        lastPosition = _playerObject.Position;
                        while (true) {
                            if (_playerObject != null && _waveOutEvent != null && _volumeSampleProvider != null) {
                                if (Vector3.Distance(lastPosition, _playerObject.Position) > 0.01f) {
                                    stopForReal = true;
                                    _waveOutEvent.Stop();
                                    break;
                                }
                            }
                            Thread.Sleep(200);
                        }
                    } catch {

                    }
                });
            }
        }

        public IPlayerObject PlayerObject { get => _playerObject; set => _playerObject = value; }
        public VolumeSampleProvider VolumeSampleProvider { get => _volumeSampleProvider; set => _volumeSampleProvider = value; }
        public WaveOutEvent WaveOutEvent { get => _waveOutEvent; set => _waveOutEvent = value; }
        public SoundType SoundType { get => soundType; set => soundType = value; }
        public bool StopPlaybackOnMovement { get => stopPlaybackOnMovement; set => stopPlaybackOnMovement = value; }
        public string SoundPath { get => _soundPath; set => _soundPath = value; }
    }

    public enum SoundType {
        MainPlayerTts,
        MainPlayerVoice,
        OtherPlayer,
        Emote,
        Song
    }
}
