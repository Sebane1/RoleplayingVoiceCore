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
        private IGameObject _camera;
        private SoundType _soundType;
        private bool stopPlaybackOnMovement;
        private Vector3 lastPosition;
        private bool stopForReal;
        private WaveStream _player;

        public SoundObject(IGameObject playerObject, IGameObject camera,
            SoundType soundType, string soundPath) {
            _playerObject = playerObject;
            _waveOutEvent = new WaveOutEvent();
            _soundPath = soundPath;
            _camera = camera;
            this._soundType = soundType;
        }

        private void SoundLoopCheck(WaveOutEvent waveOutEvent) {
            Task.Run(async () => {
                try {
                    Thread.Sleep(500);
                    lastPosition = _playerObject.Position;
                    Thread.Sleep(500);
                    while (true) {
                        if (_playerObject != null && _waveOutEvent != null && _volumeSampleProvider != null) {
                            float distance = Vector3.Distance(lastPosition, _playerObject.Position);
                            if ((distance > 0.01f && _soundType == SoundType.Loop) ||
                          (distance < 0.1f && _soundType == SoundType.LoopWhileMoving)) {
                                stopForReal = true;
                                _waveOutEvent.Stop();
                                break;
                            }
                        }
                        if (_soundType == SoundType.LoopWhileMoving) {
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
        public SoundType SoundType { get => _soundType; set => _soundType = value; }
        public bool StopPlaybackOnMovement { get => stopPlaybackOnMovement; set => stopPlaybackOnMovement = value; }
        public string SoundPath { get => _soundPath; set => _soundPath = value; }
        public PanningSampleProvider PanningSampleProvider { get => _panningSampleProvider; set => _panningSampleProvider = value; }

        public void Stop() {
            if (WaveOutEvent != null) {
                stopForReal = true;
                WaveOutEvent.Stop();
                _player.Dispose();
            }
        }
        public void Play(string soundPath, float volume, int delay) {
            _player = soundPath.EndsWith(".ogg") ?
            new VorbisWaveReader(soundPath) : new AudioFileReader(soundPath);
            WaveStream desiredStream = _player;
            if (_soundType != SoundType.MainPlayerTts &&
                _soundType != SoundType.OtherPlayerTts &&
                _soundType != SoundType.LoopWhileMoving &&
                _player.TotalTime.TotalSeconds > 13) {
                _soundType = SoundType.Loop;
            }
            float distance = Vector3.Distance(_camera.Position, PlayerObject.Position);
            float newVolume = volume * ((20 - distance) / 20);
            if (WaveOutEvent == null) {
                WaveOutEvent = new WaveOutEvent();
            }
            if (delay > 0) {
                Thread.Sleep(delay);
            }
            if (_soundType == SoundType.Loop || _soundType == SoundType.LoopWhileMoving) {
                SoundLoopCheck(_waveOutEvent);
                desiredStream = new LoopStream(_player) { EnableLooping = true };
            }
            _volumeSampleProvider = new VolumeSampleProvider(desiredStream.ToSampleProvider());
            _volumeSampleProvider.Volume = volume;
            _panningSampleProvider =
            new PanningSampleProvider(_volumeSampleProvider.ToMono());
            Vector3 dir = PlayerObject.Position - _camera.Position;
            float direction = AngleDir(_camera.Forward, dir, _camera.Top);
            _panningSampleProvider.Pan = Math.Clamp(direction / 3, -1, 1);
            WaveOutEvent?.Init(PanningSampleProvider);
            WaveOutEvent?.Play();
        }
        public static float AngleDir(Vector3 fwd, Vector3 targetDir, Vector3 up) {
            Vector3 perp = Vector3.Cross(fwd, targetDir);
            float dir = Vector3.Dot(perp, up);
            return dir;
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
