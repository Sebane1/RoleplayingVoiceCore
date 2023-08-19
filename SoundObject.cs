using NAudio.Vorbis;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.Numerics;

namespace RoleplayingVoiceCore {
    public class SoundObject {
        private IGameObject _playerObject;
        private VolumeSampleProvider _volumeSampleProvider;
        private PanningSampleProvider _panningSampleProvider;
        private WaveOutEvent _waveOutEvent;
        private string _soundPath;
        private SoundType soundType;
        private bool stopPlaybackOnMovement;
        private Vector3 lastPosition;
        private bool stopForReal;

        public SoundObject(IGameObject playerObject, WaveOutEvent waveOutEvent, SoundType soundType, string soundPath) {
            _playerObject = playerObject;
            _waveOutEvent = waveOutEvent;
            _soundPath = soundPath;
            this.soundType = soundType;
            if (soundType == SoundType.Song) {
                waveOutEvent.PlaybackStopped += delegate {
                    try {
                        if (File.Exists(soundPath)) {
                            using (WaveStream player = soundPath.EndsWith(".ogg") ?
                            new VorbisWaveReader(soundPath) : new AudioFileReader(soundPath)) {
                                if (!stopForReal) {
                                    VolumeSampleProvider = new VolumeSampleProvider(player.ToSampleProvider());
                                    VolumeSampleProvider.Volume = 1;
                                    PanningSampleProvider = new PanningSampleProvider(VolumeSampleProvider.ToMono());
                                    WaveOutEvent?.Init(PanningSampleProvider);
                                    WaveOutEvent?.Play();
                                }
                            }
                        }
                    } catch {

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

        public IGameObject PlayerObject { get => _playerObject; set => _playerObject = value; }
        public VolumeSampleProvider VolumeSampleProvider { get => _volumeSampleProvider; set => _volumeSampleProvider = value; }
        public WaveOutEvent WaveOutEvent { get => _waveOutEvent; set => _waveOutEvent = value; }
        public SoundType SoundType { get => soundType; set => soundType = value; }
        public bool StopPlaybackOnMovement { get => stopPlaybackOnMovement; set => stopPlaybackOnMovement = value; }
        public string SoundPath { get => _soundPath; set => _soundPath = value; }
        public PanningSampleProvider PanningSampleProvider { get => _panningSampleProvider; set => _panningSampleProvider = value; }

        public void Stop() {
            if (WaveOutEvent != null) {
                stopForReal = true;
                WaveOutEvent.Stop();
            }
        }
    }

    public enum SoundType {
        MainPlayerTts,
        MainPlayerVoice,
        OtherPlayerTts,
        OtherPlayer,
        Emote,
        Song
    }
}
