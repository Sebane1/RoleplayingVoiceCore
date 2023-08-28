using LibVLCSharp.Shared;
using NAudio.Vorbis;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System;
using System.Numerics;
using System.Reflection;

namespace RoleplayingVoiceCore {
    public class SoundObject {
        private IGameObject _playerObject;
        private VolumeSampleProvider _volumeSampleProvider;
        private PanningSampleProvider _panningSampleProvider;
        private WaveOutEvent _waveOutEvent;
        private string _soundPath;
        private IGameObject _camera;
        private string _libVLCPath;
        private SoundType _soundType;
        private bool stopPlaybackOnMovement;
        private Vector3 lastPosition;
        private bool stopForReal;
        private WaveStream _player;
        LibVLC libVLC;
        MediaPlayer _vlcPlayer;
        private MediaPlayer mediaPlayer;

        public SoundObject(IGameObject playerObject, IGameObject camera,
            SoundType soundType, string soundPath, string libVLCPath) {
            _playerObject = playerObject;
            _waveOutEvent = new WaveOutEvent();
            _soundPath = soundPath;
            _camera = camera;
            _libVLCPath = libVLCPath;
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
        public float Volume {
            get {
                if (_volumeSampleProvider == null) {
                    return 0;
                } else {
                    return _volumeSampleProvider.Volume;
                }
            }
            set {
                if (_volumeSampleProvider != null) {
                    _volumeSampleProvider.Volume = value;
                }
                if (_vlcPlayer != null) {
                    try {
                        _vlcPlayer.Volume = (int)(value * 100f);
                    } catch {

                    }
                }
            }
        }
        public WaveOutEvent WaveOutEvent { get => _waveOutEvent; set => _waveOutEvent = value; }
        public SoundType SoundType { get => _soundType; set => _soundType = value; }
        public bool StopPlaybackOnMovement { get => stopPlaybackOnMovement; set => stopPlaybackOnMovement = value; }
        public string SoundPath { get => _soundPath; set => _soundPath = value; }
        public float Pan {
            get {
                if (_panningSampleProvider == null) {
                    return 0;
                } else {
                    return _panningSampleProvider.Pan;
                }
            }

            set {
                if (_panningSampleProvider != null) {
                    _panningSampleProvider.Pan = value;
                }
            }
        }

        public void Stop() {
            try {
                if (WaveOutEvent != null) {
                    stopForReal = true;
                    WaveOutEvent?.Stop();
                    _waveOutEvent?.Dispose();
                    _player?.Dispose();
                }
                if (mediaPlayer != null) {
                    mediaPlayer?.Stop();
                    mediaPlayer?.Dispose();
                }
            } catch { }
        }
        public async void Play(string soundPath, float volume, int delay) {
            if (!string.IsNullOrEmpty(soundPath)) {
                if (!soundPath.StartsWith("http")) {
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
                    if (_waveOutEvent == null) {
                        _waveOutEvent = new WaveOutEvent();
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
                    if (_waveOutEvent != null) {
                        try {
                            _waveOutEvent?.Init(_panningSampleProvider);
                            _waveOutEvent?.Play();
                        } catch (Exception e) {

                        }
                    }
                } else {
                    try {
                        string location = _libVLCPath + @"\libvlc\win-x64";
                        Core.Initialize(location);
                        libVLC = new LibVLC(enableDebugLogs: true);
                        var media = new Media(libVLC, @"https://video-weaver.fra06.hls.ttvnw.net/v1/playlist/CswEnwrziuA8T6S00LHqoYfMVltwn6EuPP54B8r__wzTyb4HW4mxYuzOuX_07xRURNuyUJqpOG6JHSWI5j1mpG6iTVfIo4zqSMv8wUWzZHQbV8OWhqE3pq4mJH3yyIU8q7Q7CKWX2637JBIQaHdC51ySvEwD4WiwhILhy4bqd5wfDD6WzaSp_PYWt9Ang4h3439SepISKw3gFKZZTp0639OV2yZ2Rbplem_lBlwu0XxtSEYTRAXX7DNoIzRAPXpqVj1bJlRSNy2GWar_KSly3NThVizitg99Ws-GNycZ2LtSfbEOF0rcqbgpV6qbuDVjof8nSCh18dWh1Q_i7wfxQbyLORhi4CB9dyVozRRw-ZhtKNbswACg8aNcgaC6BKB_eLTs_RXDWjhRV6VyTSjObfpekdLlFwu3HUcLzli4Kb4QFXDSjhmNUXlUvfYva8yWCLVm4S8hi_4t59ojKsuwAOL5sLKJPNMdqQk_tt-Cnfg5D9n-XdvKBcC7oed1CHreFB4XB3TajJnvIJ-zXEAguO_Y_zn-kb4VcTLm5eElr9f-wL6p5b1G3Edv-E9IOZcBA7CJOBOvh0iWpfHAX3IkA9xQIdrsGpCTRy3z08R7ZZV18yKi_Pt208rwkj35tSEEQDHlYQdwJVBSIc3BEgHFcyeI7n7z-91Hibm1HyJYq1URadiN6a-Htn0E8vCVB1R4cNqWO7jsnkebrr0Cmo18IX8pQ5WubS3otORa1XsGOe1ugCr-i3HX_BpArUid-Wxl6Ayw3IEvIS7Dd7W-E0d6Ggzm8UA8aUrCJNrZqt0gASoJdXMtZWFzdC0yMLAH.m3u8", FromType.FromLocation);
                        await media.Parse(MediaParseOptions.ParseNetwork);
                        mediaPlayer = new MediaPlayer(media);
                        var value = mediaPlayer.Play();
                    } catch {

                    }
                }
            }
        }
        private static string AssemblyDirectory {
            get {
                string codeBase = Assembly.GetExecutingAssembly().CodeBase;
                UriBuilder uri = new UriBuilder(codeBase);
                string path = Uri.UnescapeDataString(uri.Path);
                return Path.GetDirectoryName(path);
            }
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
