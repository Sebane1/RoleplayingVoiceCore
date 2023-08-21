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
            if (soundType == SoundType.Loop || soundType == SoundType.LoopWhileMoving) {
                SoundLoopCheck(waveOutEvent);
            }
        }

        private void SoundLoopCheck(WaveOutEvent waveOutEvent) {
            waveOutEvent.PlaybackStopped += delegate {
                try {
                    if (File.Exists(_soundPath)) {
                        using (WaveStream player = _soundPath.EndsWith(".ogg") ?
                        new VorbisWaveReader(_soundPath) : new AudioFileReader(_soundPath)) {
                            if (!stopForReal) {
                                float volume = VolumeSampleProvider.Volume = 1;
                                VolumeSampleProvider = new VolumeSampleProvider(player.ToSampleProvider());
                                VolumeSampleProvider.Volume = volume;
                                float panning = PanningSampleProvider.Pan;
                                PanningSampleProvider = new PanningSampleProvider(VolumeSampleProvider.ToMono());
                                PanningSampleProvider.Pan = panning;
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
                    Thread.Sleep(500);
                    while (true) {
                        if (_playerObject != null && _waveOutEvent != null && _volumeSampleProvider != null) {
                            float distance = Vector3.Distance(lastPosition, _playerObject.Position);
                            if ((distance > 0.01f && soundType == SoundType.Loop) ||
                          (distance < 0.1f && soundType == SoundType.LoopWhileMoving)) {
                                stopForReal = true;
                                _waveOutEvent.Stop();
                                break;
                            }
                        }
                        if (soundType == SoundType.LoopWhileMoving) {
                            lastPosition = _playerObject.Position;
                        }
                        Thread.Sleep(200);
                    }
                } catch {

                }
            });
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
        Loop,
        LoopWhileMoving
    }
}
