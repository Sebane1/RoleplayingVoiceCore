using LibVLCSharp.Shared;
using NAudio.Vorbis;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using RoleplayingVoiceCore;
using SixLabors.ImageSharp.Advanced;
using System;
using System.Collections.Concurrent;
using System.IO.MemoryMappedFiles;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;

namespace RoleplayingMediaCore {
    public class MediaObject {
        private IGameObject _playerObject;
        private VolumeSampleProvider _volumeSampleProvider;
        private PanningSampleProvider _panningSampleProvider;
        private WaveOutEvent _waveOutEvent;
        private string _soundPath;
        private IGameObject _camera;
        private string _libVLCPath;
        private MediaManager _parent;
        private SoundType _soundType;
        private bool stopPlaybackOnMovement;
        private Vector3 lastPosition;
        private WaveStream _player;
        LibVLC libVLC;
        MediaPlayer _vlcPlayer;
        private static MemoryMappedFile _currentMappedFile;
        private static MemoryMappedViewAccessor _currentMappedViewAccessor;

        private const uint _width = 640;
        private const uint _height = 360;

        /// <summary>
        /// RGBA is used, so 4 byte per pixel, or 32 bits.
        /// </summary>
        private const uint _bytePerPixel = 4;

        /// <summary>
        /// the number of bytes per "line"
        /// For performance reasons inside the core of VLC, it must be aligned to multiples of 32.
        /// </summary>
        private uint _pitch;

        /// <summary>
        /// The number of lines in the buffer.
        /// For performance reasons inside the core of VLC, it must be aligned to multiples of 32.
        /// </summary>
        private uint _lines;
        private WasapiOut _wasapiOut;
        private float offsetVolume = 1;
        private LoopStream _loopStream;

        public MediaObject(MediaManager parent, IGameObject playerObject, IGameObject camera,
            SoundType soundType, string soundPath, string libVLCPath) {
            _playerObject = playerObject;
            _soundPath = soundPath;
            _camera = camera;
            _libVLCPath = libVLCPath;
            _parent = parent;
            this._soundType = soundType;
            _pitch = Align(_width * _bytePerPixel);
            _lines = Align(_height);
        }

        private static uint Align(uint size) {
            if (size % 32 == 0) {
                return size;
            }
            return ((size / 32) + 1) * 32; // Align on the next multiple of 32
        }

        private void SoundLoopCheck(WaveOutEvent waveOutEvent) {
            Task.Run(async () => {
                try {
                    Thread.Sleep(500);
                    lastPosition = _playerObject.Position;
                    Thread.Sleep(500);
                    while (true) {
                        if (_playerObject != null && waveOutEvent != null && _volumeSampleProvider != null) {
                            float distance = Vector3.Distance(lastPosition, _playerObject.Position);
                            if ((distance > 0.01f && _soundType == SoundType.Loop) ||
                          (distance < 0.1f && _soundType == SoundType.LoopWhileMoving)) {
                                waveOutEvent.Stop();
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
                    _volumeSampleProvider.Volume = value * offsetVolume;
                }
                if (_vlcPlayer != null) {
                    try {
                        int newValue = (int)(value * 100f);
                        if (newValue != _vlcPlayer.Volume) {
                            _vlcPlayer.Volume = newValue;
                        }
                    } catch {

                    }
                }
            }
        }
        public PlaybackState PlaybackState {
            get {
                if (_waveOutEvent != null) {
                    try {
                        return _waveOutEvent.PlaybackState;
                    } catch {
                        return PlaybackState.Stopped;
                    }
                } else if (_vlcPlayer != null) {
                    try {
                        return _vlcPlayer.IsPlaying ? PlaybackState.Playing : PlaybackState.Stopped;
                    } catch {
                        return PlaybackState.Stopped;
                    }
                } else if (_wasapiOut != null) {
                    try {
                        return _wasapiOut.PlaybackState;
                    } catch {
                        return PlaybackState.Stopped;
                    }
                } else {
                    return PlaybackState.Stopped;
                }
            }
        }
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

        public IGameObject Camera { get => _camera; set => _camera = value; }

        public void Stop() {
            if (_waveOutEvent != null) {
                try {
                    _waveOutEvent?.Stop();
                } catch { }
            }
            if (_vlcPlayer != null) {
                try {
                    _vlcPlayer?.Stop();
                } catch { }
            }
        }
        public void LoopEarly() {
            _loopStream?.LoopEarly();
        }
        public async void Play(WaveStream soundPath, float volume, int delay) {
            if (PlaybackState == PlaybackState.Stopped) {
                _player = soundPath;
                WaveStream desiredStream = _player;
                if (_soundType != SoundType.MainPlayerTts &&
                    _soundType != SoundType.OtherPlayerTts &&
                    _soundType != SoundType.LoopWhileMoving &&
                    _soundType != SoundType.Livestream &&
                    _soundType != SoundType.MainPlayerCombat &&
                    _soundType != SoundType.OtherPlayerCombat &&
                    _player.TotalTime.TotalSeconds > 13) {
                    _soundType = SoundType.Loop;
                }
                offsetVolume = 0.7f;
                float distance = Vector3.Distance(_camera.Position, PlayerObject.Position);
                float newVolume = volume * ((20 - distance) / 20) * offsetVolume;
                if (delay > 0) {
                    Thread.Sleep(delay);
                }
                if (_soundType == SoundType.Loop || _soundType == SoundType.LoopWhileMoving) {
                    SoundLoopCheck(_waveOutEvent);
                    _loopStream = new LoopStream(_player) { EnableLooping = true };
                    desiredStream = _loopStream;
                }
                _volumeSampleProvider = new VolumeSampleProvider(desiredStream.ToSampleProvider());
                _volumeSampleProvider.Volume = newVolume;
                _panningSampleProvider =
                new PanningSampleProvider(_volumeSampleProvider.ToMono());
                Vector3 dir = PlayerObject.Position - _camera.Position;
                float direction = AngleDir(_camera.Forward, dir, _camera.Top);
                _panningSampleProvider.Pan = Math.Clamp(direction / 3, -1, 1);
                try {
                    _waveOutEvent = new WaveOutEvent();
                    _waveOutEvent?.Init(_panningSampleProvider);
                    _waveOutEvent?.Play();
                } catch (Exception e) {

                }
            }
        }
        public async void Play(string soundPath, float volume, int delay) {
            if (!string.IsNullOrEmpty(soundPath) && PlaybackState == PlaybackState.Stopped) {
                if (!soundPath.StartsWith("http")) {
                    _player = soundPath.EndsWith(".ogg") ?
                    new VorbisWaveReader(soundPath) : new AudioFileReader(soundPath);
                    WaveStream desiredStream = _player;
                    if (_soundType != SoundType.MainPlayerTts &&
                        _soundType != SoundType.OtherPlayerTts &&
                        _soundType != SoundType.LoopWhileMoving &&
                        _soundType != SoundType.Livestream &&
                        _soundType != SoundType.MainPlayerCombat &&
                        _soundType != SoundType.OtherPlayerCombat &&
                        _player.TotalTime.TotalSeconds > 13) {
                        _soundType = SoundType.Loop;
                    }
                    float distance = Vector3.Distance(_camera.Position, PlayerObject.Position);
                    float newVolume = volume * ((20 - distance) / 20);
                    _waveOutEvent ??= new WaveOutEvent();
                    if (delay > 0) {
                        Thread.Sleep(delay);
                    }
                    if (_soundType == SoundType.Loop || _soundType == SoundType.LoopWhileMoving) {
                        SoundLoopCheck(_waveOutEvent);
                        desiredStream = new LoopStream(_player) { EnableLooping = true };
                    }
                    _volumeSampleProvider = new VolumeSampleProvider(desiredStream.ToSampleProvider());
                    _volumeSampleProvider.Volume = newVolume;
                    _panningSampleProvider =
                    new PanningSampleProvider(_volumeSampleProvider.ToMono());
                    Vector3 dir = PlayerObject.Position - _camera.Position;
                    float direction = AngleDir(_camera.Forward, dir, _camera.Top);
                    _panningSampleProvider.Pan = Math.Clamp(direction / 3, -1, 1);
                    if (_waveOutEvent != null) {
                        try {
                            _waveOutEvent?.Init(_panningSampleProvider);
                            _waveOutEvent?.Play();
                            _waveOutEvent?.Play();
                        } catch (Exception e) {

                        }
                    }
                } else {
                    try {
                        _parent.LastFrame = Array.Empty<byte>();
                        string location = _libVLCPath + @"\libvlc\win-x64";
                        //VideoView videoView = new VideoView();
                        Core.Initialize(location);
                        libVLC = new LibVLC("--vout", "none");
                        var media = new Media(libVLC, soundPath, FromType.FromLocation);
                        await media.Parse(MediaParseOptions.ParseNetwork);
                        _vlcPlayer = new MediaPlayer(media);
                        var processingCancellationTokenSource = new CancellationTokenSource();
                        _vlcPlayer.Stopped += (s, e) => processingCancellationTokenSource.CancelAfter(1);
                        _vlcPlayer.Stopped += delegate { _parent.LastFrame = null; };
                        _vlcPlayer.SetVideoFormat("RV32", _width, _height, _pitch);
                        _vlcPlayer.SetVideoCallbacks(Lock, null, Display);
                        _vlcPlayer.Play();
                    } catch {

                    }
                }
            }
        }

        public static float AngleDir(Vector3 fwd, Vector3 targetDir, Vector3 up) {
            Vector3 perp = Vector3.Cross(fwd, targetDir);
            float dir = Vector3.Dot(perp, up);
            return dir;
        }

        private IntPtr Lock(IntPtr opaque, IntPtr planes) {
            try {
                _currentMappedFile = MemoryMappedFile.CreateNew(null, _pitch * _lines);
                _currentMappedViewAccessor = _currentMappedFile.CreateViewAccessor();
                Marshal.WriteIntPtr(planes, _currentMappedViewAccessor.SafeMemoryMappedViewHandle.DangerousGetHandle());
                return IntPtr.Zero;
            } catch {
                return IntPtr.Zero;
            }
        }

        private void Display(IntPtr opaque, IntPtr picture) {
            try {
                using (var image = new Image<Bgra32>((int)(_pitch / _bytePerPixel), (int)_lines))
                using (var sourceStream = _currentMappedFile.CreateViewStream()) {
                    var mg = image.GetPixelMemoryGroup();
                    for (int i = 0; i < mg.Count; i++) {
                        sourceStream.Read(MemoryMarshal.AsBytes(mg[i].Span));
                    }
                    lock (_parent.LastFrame) {
                        MemoryStream stream = new MemoryStream();
                        image.SaveAsJpeg(stream);
                        stream.Flush();
                        _parent.LastFrame = stream.ToArray();
                    }
                }
                _currentMappedViewAccessor.Dispose();
                _currentMappedFile.Dispose();
                _currentMappedFile = null;
                _currentMappedViewAccessor = null;
            } catch {

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
        LoopWhileMoving,
        Livestream,
        MainPlayerCombat,
        OtherPlayerCombat
    }
}
