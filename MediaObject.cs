using LibVLCSharp.Shared;
using NAudio.Vorbis;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Newtonsoft.Json.Linq;
using SixLabors.ImageSharp.Advanced;
using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Numerics;
using System.Runtime.InteropServices;
using VarispeedDemo.SoundTouch;

namespace RoleplayingMediaCore {
    public class MediaObject {
        private IGameObject _playerObject;
        private IGameObject _camera;
        private SoundType _soundType;

        private VolumeSampleProvider _volumeSampleProvider;
        private PanningSampleProvider _panningSampleProvider;
        private WaveOutEvent _waveOutEvent;
        private LibVLC libVLC;
        private MediaPlayer _vlcPlayer;
        private MediaManager _parent;
        private WaveStream _player;

        private static MemoryMappedFile _currentMappedFile;
        private static MemoryMappedViewAccessor _currentMappedViewAccessor;
        public event EventHandler<MediaError> OnErrorReceived;
        public event EventHandler PlaybackStopped;

        private string _soundPath;
        private string _libVLCPath;

        private bool stopPlaybackOnMovement;
        private Vector3 lastPosition;

        private const uint _width = 640;
        private const uint _height = 360;

        //private int _volumeOffset = 1;

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
        private float offsetVolume = 1;

        private WasapiOut _wasapiOut;
        private LoopStream _loopStream;
        private float _baseVolume = 1;

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

        private void SoundLoopCheck() {
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
                                _waveOutEvent.Stop();
                                break;
                            }
                        }
                        if (_soundType == SoundType.LoopWhileMoving) {
                            lastPosition = _playerObject.Position;
                        }
                        Thread.Sleep(200);
                    }
                } catch (Exception e) { OnErrorReceived?.Invoke(this, new MediaError() { Exception = e }); }
            });
        }
        private void MountLoopCheck() {
            Task.Run(async () => {
                try {
                    Thread.Sleep(500);
                    lastPosition = _playerObject.Position;
                    Thread.Sleep(500);
                    while (_playerObject != null && _waveOutEvent != null && _volumeSampleProvider != null) {
                        if (_playerObject != null && _waveOutEvent != null && _volumeSampleProvider != null) {
                            float distance = Vector3.Distance(lastPosition, _playerObject.Position);
                            if ((distance > 0.01f && _soundType == SoundType.MountLoop)) {
                                offsetVolume = Math.Clamp(offsetVolume + 0.1f, 0, 0.8f);
                            } else {
                                offsetVolume = Math.Clamp(offsetVolume - 0.1f, 0.2f, 0.8f);
                            }
                        }
                        if (_volumeSampleProvider != null) {
                            _volumeSampleProvider.Volume = _baseVolume * offsetVolume;
                        }
                        lastPosition = _playerObject.Position;
                        Thread.Sleep(200);
                    }
                } catch (Exception e) { OnErrorReceived?.Invoke(this, new MediaError() { Exception = e }); }
            });
        }
        private void DonePlayingCheck() {
            Task.Run(async () => {
                try {
                    Thread.Sleep(1200);
                    while (true) {
                        if (_player.Position >= _player.Length) {
                            _waveOutEvent.Stop();
                        }
                        Thread.Sleep(500);
                    }
                } catch (Exception e) { OnErrorReceived?.Invoke(this, new MediaError() { Exception = e }); }
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
                    _baseVolume = value;
                    _volumeSampleProvider.Volume = value * offsetVolume;
                }
                if (_vlcPlayer != null) {
                    try {
                        int newValue = (int)(value * 100f);
                        if (newValue != _vlcPlayer.Volume) {
                            _vlcPlayer.Volume = newValue;
                        }
                    } catch (Exception e) { OnErrorReceived?.Invoke(this, new MediaError() { Exception = e }); }
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
        public bool Invalidated { get; internal set; }

        public void Stop() {
            Volume = 0;
            if (_waveOutEvent != null) {
                try {
                    if (_loopStream != null) {
                        _loopStream.EnableLooping = false;
                        _loopStream?.Dispose();
                    }
                    _waveOutEvent?.Stop();
                    _waveOutEvent?.Dispose();
                } catch (Exception e) { OnErrorReceived?.Invoke(this, new MediaError() { Exception = e }); }
            }
            if (_vlcPlayer != null) {
                try {
                    _vlcPlayer?.Stop();
                } catch (Exception e) { OnErrorReceived?.Invoke(this, new MediaError() { Exception = e }); }
            }
            Volume = 0;
        }
        public void LoopEarly() {
            _loopStream?.LoopEarly();
        }

        public async void Play(WaveStream soundPath, float volume, int delay, bool useSmbPitch, float pitch = 0, bool lowPerformanceMode = false) {
            if (!Invalidated) {
                try {
                    if (PlaybackState == PlaybackState.Stopped) {
                        _player = soundPath;
                        WaveStream desiredStream = _player;
                        if (_soundType != SoundType.MainPlayerTts &&
                            _soundType != SoundType.OtherPlayerTts &&
                            _soundType != SoundType.LoopWhileMoving &&
                            _soundType != SoundType.Livestream &&
                            _soundType != SoundType.MainPlayerCombat &&
                            _soundType != SoundType.OtherPlayerCombat &&
                            _soundType != SoundType.NPC &&
                            _player.TotalTime.TotalSeconds > 13) {
                            _soundType = SoundType.Loop;
                        }
                        offsetVolume = 0.7f;
                        if (delay > 0) {
                            Thread.Sleep(delay);
                        }
                        _waveOutEvent = new WaveOutEvent();
                        if (_soundType == SoundType.Loop || _soundType == SoundType.LoopWhileMoving) {
                            if (_soundType != SoundType.MainPlayerCombat && _soundType != SoundType.OtherPlayerCombat) {
                                if (delay > 0) {
                                    Thread.Sleep(delay);
                                }
                            }
                        }
                        if (_soundType == SoundType.Loop || _soundType == SoundType.LoopWhileMoving) {
                            if (_soundType != SoundType.MainPlayerCombat && _soundType != SoundType.OtherPlayerCombat) {
                                SoundLoopCheck();
                            }
                            _loopStream = new LoopStream(_player) { EnableLooping = true };
                            desiredStream = _loopStream;
                        }
                        float distance = Vector3.Distance(_camera.Position, PlayerObject.Position);
                        float newVolume = _parent.CalculateObjectVolume(_playerObject.Name, this);
                        ISampleProvider sampleProvider = null;
                        if (!lowPerformanceMode || _soundType != SoundType.MainPlayerCombat && _soundType != SoundType.MainPlayerTts && _soundType != SoundType.NPC) {
                            _volumeSampleProvider = new VolumeSampleProvider(desiredStream.ToSampleProvider());
                            _volumeSampleProvider.Volume = volume;
                            _panningSampleProvider = new PanningSampleProvider(
                            _player.WaveFormat.Channels == 1 ? _volumeSampleProvider : _volumeSampleProvider.ToMono());
                            Vector3 dir = PlayerObject.Position - _camera.Position;
                            float direction = AngleDir(_camera.Forward, dir, _camera.Top);
                            _panningSampleProvider.Pan = Math.Clamp(direction / 3, -1, 1);
                            if (pitch != 1) {
                                ISampleProvider newSampleProvider = null;
                                if (!useSmbPitch) {
                                    var pitchSample = new VarispeedSampleProvider(_panningSampleProvider, 100, new SoundTouchProfile(false, true));
                                    pitchSample.PlaybackRate = pitch;
                                    newSampleProvider = pitchSample;
                                } else {
                                    var pitchSample = new SmbPitchShiftingSampleProvider(_panningSampleProvider);
                                    pitchSample.PitchFactor = pitch;
                                    newSampleProvider = pitchSample;
                                }
                                sampleProvider = newSampleProvider;
                            } else {
                                sampleProvider = _panningSampleProvider;
                            }
                        } else {
                            if (pitch != 1) {
                                ISampleProvider newSampleProvider = null;
                                if (!useSmbPitch) {
                                    var pitchSample = new VarispeedSampleProvider(desiredStream.ToSampleProvider(), 100, new SoundTouchProfile(false, true));
                                    pitchSample.PlaybackRate = pitch;
                                    newSampleProvider = pitchSample;
                                } else {
                                    var pitchSample = new SmbPitchShiftingSampleProvider(desiredStream.ToSampleProvider());
                                    pitchSample.PitchFactor = pitch;
                                    newSampleProvider = pitchSample;
                                }
                                _volumeSampleProvider = new VolumeSampleProvider(newSampleProvider);
                                _volumeSampleProvider.Volume = volume;
                                sampleProvider = _volumeSampleProvider;
                            } else {
                                _volumeSampleProvider = new VolumeSampleProvider(desiredStream.ToSampleProvider());
                                _volumeSampleProvider.Volume = volume;
                                sampleProvider = _volumeSampleProvider;
                            }
                        }
                        if (_waveOutEvent != null) {
                            try {
                                _waveOutEvent?.Init(sampleProvider);
                                if (_soundType == SoundType.Loop ||
                                    _soundType == SoundType.MainPlayerVoice ||
                                    _soundType == SoundType.OtherPlayer) {
                                } else {
                                    _player.Position = 0;
                                }
                                if (_soundType == SoundType.MainPlayerCombat ||
                                    _soundType == SoundType.OtherPlayerCombat) {
                                    _waveOutEvent.DesiredLatency = 50;
                                }
                                _waveOutEvent.PlaybackStopped += delegate {
                                    PlaybackStopped?.Invoke(this, EventArgs.Empty);
                                };
                                _waveOutEvent?.Play();
                                DonePlayingCheck();
                            } catch (Exception e) { OnErrorReceived?.Invoke(this, new MediaError() { Exception = e }); }
                        }
                    }
                } catch (Exception e) { OnErrorReceived?.Invoke(this, new MediaError() { Exception = e }); }
            }
        }


        public async void Play(string soundPath, float volume, int delay, TimeSpan skipAhead, bool lowPerformanceMode = false) {
            try {
                Stopwatch latencyTimer = Stopwatch.StartNew();
                if (!string.IsNullOrEmpty(soundPath) && PlaybackState == PlaybackState.Stopped) {
                    if (!soundPath.StartsWith("http") && !soundPath.StartsWith("rtmp")) {
                        _player = soundPath.EndsWith(".ogg") ?
                        new VorbisWaveReader(soundPath) : new MediaFoundationReader(soundPath);
                        WaveStream desiredStream = _player;
                        if (_soundType != SoundType.MainPlayerTts &&
                            _soundType != SoundType.OtherPlayerTts &&
                            _soundType != SoundType.LoopWhileMoving &&
                            _soundType != SoundType.Livestream &&
                            _soundType != SoundType.MainPlayerCombat &&
                            _soundType != SoundType.OtherPlayerCombat &&
                            _soundType != SoundType.NPC &&
                            _soundType != SoundType.MountLoop &&
                            _player.TotalTime.TotalSeconds > 13) {
                            _soundType = SoundType.Loop;
                        }
                        _waveOutEvent ??= new WaveOutEvent();
                        if (_soundType != SoundType.MainPlayerCombat && _soundType != SoundType.OtherPlayerCombat) {
                            if (delay > 0) {
                                Thread.Sleep(delay);
                            }
                        }
                        if (_soundType == SoundType.Loop || _soundType == SoundType.LoopWhileMoving) {
                            if (_soundType != SoundType.MainPlayerCombat && _soundType != SoundType.OtherPlayerCombat && _soundType != SoundType.ChatSound) {
                                SoundLoopCheck();
                            }
                            _loopStream = new LoopStream(_player) { EnableLooping = true };
                            desiredStream = _loopStream;
                        } else if (_soundType == SoundType.MountLoop) {
                            _loopStream = new LoopStream(_player) { EnableLooping = true };
                            desiredStream = _loopStream;
                        }
                        float distance = Vector3.Distance(_camera.Position, PlayerObject.Position);
                        float newVolume = _parent.CalculateObjectVolume(_playerObject.Name, this);
                        ISampleProvider sampleProvider = null;
                        if (!lowPerformanceMode || _soundType != SoundType.MainPlayerCombat && _soundType != SoundType.MainPlayerTts && _soundType != SoundType.ChatSound) {
                            _volumeSampleProvider = new VolumeSampleProvider(desiredStream.ToSampleProvider());
                            _baseVolume = volume;
                            if (_soundType != SoundType.MountLoop) {
                                _volumeSampleProvider.Volume = volume;
                            } else {
                                offsetVolume = 0;
                                _volumeSampleProvider.Volume = 0;
                            }
                            _panningSampleProvider = new PanningSampleProvider(
                            _player.WaveFormat.Channels == 1 ? _volumeSampleProvider : _volumeSampleProvider.ToMono());
                            Vector3 dir = PlayerObject.Position - _camera.Position;
                            float direction = AngleDir(_camera.Forward, dir, _camera.Top);
                            _panningSampleProvider.Pan = Math.Clamp(direction / 3, -1, 1);
                            sampleProvider = _panningSampleProvider;
                        } else {
                            _volumeSampleProvider = new VolumeSampleProvider(desiredStream.ToSampleProvider());
                            _baseVolume = volume;
                            if (_soundType != SoundType.MountLoop) {
                                _volumeSampleProvider.Volume = volume;
                            } else {
                                offsetVolume = 0;
                                _volumeSampleProvider.Volume = 0;
                            }
                            Pan = 0;
                            sampleProvider = _volumeSampleProvider;
                        }
                        if (_waveOutEvent != null) {
                            try {
                                _waveOutEvent?.Init(sampleProvider);
                                if (_soundType == SoundType.Loop ||
                                    _soundType == SoundType.MainPlayerVoice ||
                                    _soundType == SoundType.OtherPlayer) {
                                    _player.CurrentTime = skipAhead;
                                    if (_player.TotalTime.TotalSeconds > 13) {
                                        _player.CurrentTime += latencyTimer.Elapsed;
                                    }
                                } else {
                                    _player.Position = 0;
                                }
                                if (_soundType == SoundType.MainPlayerCombat ||
                                    _soundType == SoundType.OtherPlayerCombat) {
                                    _waveOutEvent.DesiredLatency = 50;
                                }
                                _waveOutEvent.PlaybackStopped += delegate {
                                    PlaybackStopped?.Invoke(this, EventArgs.Empty);
                                };
                                _waveOutEvent?.Play();
                                if (_soundType == SoundType.MountLoop) {
                                    MountLoopCheck();
                                }
                            } catch (Exception e) { OnErrorReceived?.Invoke(this, new MediaError() { Exception = e }); }
                        }
                    } else {
                        try {
                            _parent.LastFrame = Array.Empty<byte>();
                            string location = _libVLCPath + @"\libvlc\win-x64";
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
                        } catch (Exception e) { OnErrorReceived?.Invoke(this, new MediaError() { Exception = e }); }
                    }
                }
            } catch (Exception e) { OnErrorReceived?.Invoke(this, new MediaError() { Exception = e }); }
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
        public void ResetVolume() {
            if (_waveOutEvent != null) {
                _waveOutEvent.Volume = 1;
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
            } catch (Exception e) { OnErrorReceived?.Invoke(this, new MediaError() { Exception = e }); }
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
        OtherPlayerCombat,
        NPC,
        ChatSound,
        MountLoop
    }
}
