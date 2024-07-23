using LibVLCSharp.Shared;
using NAudio.Vorbis;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using RoleplayingVoiceCore;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Advanced;
using SixLabors.ImageSharp.PixelFormats;
using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Numerics;
using System.Runtime.InteropServices;
using VarispeedDemo.SoundTouch;

namespace RoleplayingMediaCore {
    public class MediaObject {
        private IMediaGameObject _playerObject;
        private IMediaGameObject _camera;
        private SoundType _soundType;

        private VolumeSampleProvider _volumeSampleProvider;
        private PanningSampleProvider _panningSampleProvider;
        private IWavePlayer _wavePlayer;
        private LibVLC libVLC;
        private MediaPlayer _vlcPlayer;
        private MediaManager _parent;
        private WaveStream _player;

        private static MemoryMappedFile _currentMappedFile;
        private static MemoryMappedViewAccessor _currentMappedViewAccessor;
        public event EventHandler<MediaError> OnErrorReceived;
        public event EventHandler<string> PlaybackStopped;
        public event EventHandler<StreamVolumeEventArgs> StreamVolumeChanged;

        private string _soundPath;
        private string _libVLCPath;

        private bool stopPlaybackOnMovement;
        private Vector3 lastPosition;

        private const uint _width = 1280;
        private const uint _height = 720;

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
        private float volumePercentage = 1;

        private LoopStream _loopStream;
        private float _baseVolume = 1;
        private MeteringSampleProvider _meteringSampleProvider;
        private bool _vlcWasAbleToStart;
        private float _volumeOffset;

        public MediaObject(MediaManager parent, IMediaGameObject playerObject, IMediaGameObject camera,
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
                        if (_playerObject != null && _wavePlayer != null && _volumeSampleProvider != null) {
                            float distance = Vector3.Distance(lastPosition, _playerObject.Position);
                            if ((distance > 0.01f && _soundType == SoundType.Loop) ||
                          (distance < 0.1f && _soundType == SoundType.LoopWhileMoving)) {
                                _wavePlayer?.Stop();
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
                    while (_playerObject != null && _wavePlayer != null && _volumeSampleProvider != null) {
                        if (_playerObject != null && _wavePlayer != null && _volumeSampleProvider != null) {
                            float distance = Vector3.Distance(lastPosition, _playerObject.Position);
                            if ((distance > 0.01f && _soundType == SoundType.LoopUntilStopped)) {
                                volumePercentage = Math.Clamp(volumePercentage + 0.1f, 0, 0.8f);
                            } else {
                                volumePercentage = Math.Clamp(volumePercentage - 0.1f, 0.2f, 0.8f);
                            }
                        }
                        if (_volumeSampleProvider != null) {
                            _volumeSampleProvider.Volume = (_volumeOffset * _baseVolume) * volumePercentage;
                        }
                        lastPosition = _playerObject.Position;
                        Thread.Sleep(200);
                    }
                } catch (Exception e) { OnErrorReceived?.Invoke(this, new MediaError() { Exception = e }); }
            });
        }
        private void DonePlayingCheck() {
            Stopwatch stopwatch = new Stopwatch();
            Task.Run(async () => {
                long lastPosition = _player.Position;
                try {
                    Thread.Sleep(1200);
                    while (true) {
                        if (_player.Position == lastPosition) {
                            if (!stopwatch.IsRunning) {
                                stopwatch.Start();
                            }
                            if (stopwatch.ElapsedMilliseconds > 500) {
                                Thread.Sleep(500);
                                _wavePlayer.Stop();
                                break;
                            }
                        } else {
                            stopwatch.Reset();
                        }
                        lastPosition = _player.Position;
                        Thread.Sleep(250);
                    }
                } catch (Exception e) { OnErrorReceived?.Invoke(this, new MediaError() { Exception = e }); }
            });
        }

        public IMediaGameObject CharacterObject { get => _playerObject; set => _playerObject = value; }
        public float Volume {
            get {
                if (_volumeSampleProvider != null) {
                    return _volumeSampleProvider.Volume;
                }
                try {
                    if (_vlcPlayer != null) {
                        return _vlcPlayer.Volume;
                    }
                } catch { }
                return 0;
            }
            set {
                if (_volumeSampleProvider != null) {
                    _baseVolume = value;
                    _volumeSampleProvider.Volume = (_volumeOffset * value) * volumePercentage;
                }
                if (_vlcPlayer != null) {
                    try {
                        int newValue = (int)(value * 100f);
                        if (newValue != _vlcPlayer.Volume) {
                            _baseVolume = newValue;
                            _vlcPlayer.Volume = (int)((float)newValue * volumePercentage);
                        }
                    } catch (Exception e) { OnErrorReceived?.Invoke(this, new MediaError() { Exception = e }); }
                }
            }
        }
        public PlaybackState PlaybackState {
            get {
                if (_wavePlayer != null) {
                    try {
                        return _wavePlayer.PlaybackState;
                    } catch {
                        return PlaybackState.Stopped;
                    }
                } else if (_vlcPlayer != null) {
                    try {
                        return _vlcPlayer.IsPlaying ? PlaybackState.Playing : PlaybackState.Stopped;
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

        public IMediaGameObject Camera { get => _camera; set => _camera = value; }
        public bool Invalidated { get; internal set; }

        public void Stop() {
            Volume = 0;
            if (_wavePlayer != null) {
                try {
                    if (_wavePlayer != null) {
                        try {
                            _wavePlayer?.Stop();
                            _wavePlayer?.Dispose();
                        } catch {
                            PlaybackStopped.Invoke(this, "OK");
                        }
                    }
                    if (_loopStream != null) {
                        try {
                            _loopStream.EnableLooping = false;
                            _loopStream?.Dispose();
                        } catch {

                        }
                    }
                } catch (Exception e) {
                    OnErrorReceived?.Invoke(this, new MediaError() { Exception = e });
                    PlaybackStopped.Invoke(this, "OK");
                }
            } else {
                PlaybackStopped.Invoke(this, "OK");
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

        public async void Play(WaveStream soundPath, float volume, int delay, bool useSmbPitch,
            AudioOutputType audioPlayerType, float pitch = 0, bool lowPerformanceMode = false, float speed = 1, float volumeOffset = 1) {
            _volumeOffset = volumeOffset;
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
                        volumePercentage = 0.7f;
                        if (delay > 0) {
                            Thread.Sleep(delay);
                        }
                        switch (audioPlayerType) {
                            case AudioOutputType.WaveOut:
                                _wavePlayer = new WaveOutEvent();
                                break;
                            case AudioOutputType.DirectSound:
                                _wavePlayer = new DirectSoundOut();
                                break;
                            case AudioOutputType.Wasapi:
                                _wavePlayer = new WasapiOut();
                                break;
                        }
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
                        float distance = Vector3.Distance(_camera.Position, CharacterObject.Position);
                        float newVolume = _parent.CalculateObjectVolume(_playerObject.Name, this);
                        ISampleProvider sampleProvider = null;
                        if (!lowPerformanceMode || _soundType != SoundType.MainPlayerCombat && _soundType
                            != SoundType.MainPlayerTts && _soundType != SoundType.NPC) {
                            if (desiredStream != null) {
                                _meteringSampleProvider = new MeteringSampleProvider(desiredStream.ToSampleProvider());
                                _meteringSampleProvider.StreamVolume += _meteringSampleProvider_StreamVolume;
                                _volumeSampleProvider = new VolumeSampleProvider(_meteringSampleProvider);
                                _volumeSampleProvider.Volume = _volumeOffset * volume;
                                _panningSampleProvider = new PanningSampleProvider(
                                _player.WaveFormat.Channels == 1 ? _volumeSampleProvider : _volumeSampleProvider.ToMono());
                                Vector3 dir = CharacterObject.Position - _camera.Position;
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
                            }
                        } else {
                            _meteringSampleProvider = new MeteringSampleProvider(desiredStream.ToSampleProvider());
                            _meteringSampleProvider.StreamVolume += _meteringSampleProvider_StreamVolume;
                            if (pitch != 1) {
                                ISampleProvider newSampleProvider = null;
                                if (!useSmbPitch) {
                                    var pitchSample = new VarispeedSampleProvider(_meteringSampleProvider, 100, new SoundTouchProfile(false, true));
                                    pitchSample.PlaybackRate = pitch;
                                    newSampleProvider = pitchSample;
                                } else {
                                    var pitchSample = new SmbPitchShiftingSampleProvider(_meteringSampleProvider);
                                    pitchSample.PitchFactor = pitch;
                                    newSampleProvider = pitchSample;
                                }
                                _volumeSampleProvider = new VolumeSampleProvider(newSampleProvider);
                                _volumeSampleProvider.Volume = _volumeOffset * volume;
                                sampleProvider = _volumeSampleProvider;
                            } else {
                                _volumeSampleProvider = new VolumeSampleProvider(_meteringSampleProvider);
                                _volumeSampleProvider.Volume = _volumeOffset * volume;
                                sampleProvider = _volumeSampleProvider;
                            }
                        }
                        if (Math.Abs(speed) > 0.0001f) {
                            if (sampleProvider != null) {
                                var playbackSpeed = new VarispeedSampleProvider(sampleProvider, 100, new SoundTouchProfile(true, true));
                                playbackSpeed.PlaybackRate = speed;
                                sampleProvider = playbackSpeed;
                            }
                        }
                        if (_wavePlayer != null && sampleProvider != null) {
                            try {
                                _wavePlayer?.Init(sampleProvider);
                                if (_soundType == SoundType.Loop ||
                                    _soundType == SoundType.MainPlayerVoice ||
                                    _soundType == SoundType.OtherPlayer) {
                                } else {
                                    _player.Position = 0;
                                }
                                if (_soundType == SoundType.MainPlayerCombat ||
                                    _soundType == SoundType.OtherPlayerCombat) {
                                    if (_wavePlayer != null) {
                                        try {
                                            var waveOutEvent = _wavePlayer as WaveOutEvent;
                                            if (waveOutEvent != null) {
                                                waveOutEvent.DesiredLatency = 50;
                                            }
                                        } catch {

                                        }
                                    }
                                }
                                _wavePlayer.PlaybackStopped += delegate {
                                    if (PlaybackStopped != null) {
                                        try {
                                            PlaybackStopped?.Invoke(this, "OK");
                                        } catch (Exception e) { OnErrorReceived?.Invoke(this, new MediaError() { Exception = e }); }
                                    }
                                };
                                _wavePlayer?.Play();
                                DonePlayingCheck();
                            } catch (Exception e) {
                                OnErrorReceived?.Invoke(this, new MediaError() { Exception = e });
                                PlaybackStopped?.Invoke(this, "ERR");
                            }
                        }
                    }
                } catch (Exception e) { OnErrorReceived?.Invoke(this, new MediaError() { Exception = e }); }
            }
        }

        //private void NPCDoneTalking() {
        //    Task.Run(async () => {
        //        try {
        //           int lastPosition = 
        //            Thread.Sleep(500);
        //            while (true) {
        //                if (_playerObject != null && _wavePlayer != null && _volumeSampleProvider != null) {
        //                    float distance = Vector3.Distance(lastPosition, _playerObject.Position);
        //                    if () {
        //                        _wavePlayer?.Stop();
        //                        break;
        //                    }
        //                }
        //                if (_soundType == SoundType.LoopWhileMoving) {
        //                    lastPosition = _playerObject.Position;
        //                }
        //                Thread.Sleep(200);
        //            }
        //        } catch (Exception e) { OnErrorReceived?.Invoke(this, new MediaError() { Exception = e }); }
        //    });
        //}

        private void _meteringSampleProvider_StreamVolume(object? sender, StreamVolumeEventArgs e) {
            StreamVolumeChanged?.Invoke(sender, e);
        }

        public async void Play(string soundPath, float volume, int delay, TimeSpan skipAhead,
            AudioOutputType audioPlayerType, bool lowPerformanceMode = false, int volumeOffset = 0) {
            _volumeOffset = volumeOffset;
            await Task.Run(async delegate {
                try {
                    Stopwatch latencyTimer = Stopwatch.StartNew();
                    if (!string.IsNullOrEmpty(soundPath) && PlaybackState == PlaybackState.Stopped) {
                        if (!soundPath.StartsWith("http") && !soundPath.StartsWith("rtmp") &&
                            (audioPlayerType != AudioOutputType.VLCExperimental || _soundType != SoundType.NPC)) {
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
                                _soundType != SoundType.LoopUntilStopped &&
                                _player.TotalTime.TotalSeconds > 13) {
                                _soundType = SoundType.Loop;
                            }
                            switch (audioPlayerType) {
                                case AudioOutputType.VLCExperimental:
                                case AudioOutputType.WaveOut:
                                    _wavePlayer = new WaveOutEvent();
                                    break;
                                case AudioOutputType.DirectSound:
                                    _wavePlayer = new DirectSoundOut();
                                    break;
                                case AudioOutputType.Wasapi:
                                    _wavePlayer = new WasapiOut();
                                    break;
                            }
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
                            } else if (_soundType == SoundType.LoopUntilStopped) {
                                _loopStream = new LoopStream(_player) { EnableLooping = true };
                                desiredStream = _loopStream;
                            }
                            float distance = Vector3.Distance(_camera.Position, CharacterObject.Position);
                            float newVolume = _parent.CalculateObjectVolume(_playerObject.Name, this);
                            ISampleProvider sampleProvider = null;
                            if (!lowPerformanceMode || _soundType != SoundType.MainPlayerCombat && _soundType != SoundType.MainPlayerTts && _soundType != SoundType.ChatSound) {
                                _meteringSampleProvider = new MeteringSampleProvider(desiredStream.ToSampleProvider());
                                _meteringSampleProvider.StreamVolume += _meteringSampleProvider_StreamVolume;
                                _volumeSampleProvider = new VolumeSampleProvider(_meteringSampleProvider);
                                _baseVolume = volume;
                                if (_soundType != SoundType.LoopUntilStopped) {
                                    _volumeSampleProvider.Volume = _volumeOffset * volume;
                                } else {
                                    volumePercentage = 0;
                                    _volumeSampleProvider.Volume = 0;
                                }
                                _panningSampleProvider = new PanningSampleProvider(
                                _player.WaveFormat.Channels == 1 ? _volumeSampleProvider : _volumeSampleProvider.ToMono());
                                Vector3 dir = CharacterObject.Position - _camera.Position;
                                float direction = AngleDir(_camera.Forward, dir, _camera.Top);
                                _panningSampleProvider.Pan = Math.Clamp(direction / 3, -1, 1);
                                sampleProvider = _panningSampleProvider;
                            } else {
                                _meteringSampleProvider = new MeteringSampleProvider(desiredStream.ToSampleProvider());
                                _meteringSampleProvider.StreamVolume += _meteringSampleProvider_StreamVolume;
                                _volumeSampleProvider = new VolumeSampleProvider(_meteringSampleProvider);
                                _baseVolume = volume;
                                if (_soundType != SoundType.LoopUntilStopped) {
                                    _volumeSampleProvider.Volume = _volumeOffset * volume;
                                } else {
                                    volumePercentage = 0;
                                    _volumeSampleProvider.Volume = 0;
                                }
                                Pan = 0;
                                sampleProvider = _volumeSampleProvider;
                            }
                            if (_wavePlayer != null) {
                                try {
                                    try {
                                        _wavePlayer?.Init(sampleProvider);
                                    } catch {
                                    }
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
                                        try {
                                            var waveOutEvent = _wavePlayer as WaveOutEvent;
                                            if (waveOutEvent != null) {
                                                waveOutEvent.DesiredLatency = 50;
                                            }
                                        } catch {

                                        }
                                    }
                                    _wavePlayer.PlaybackStopped += delegate {
                                        PlaybackStopped?.Invoke(this, "OK");
                                    };
                                    if (_wavePlayer != null) {
                                        _wavePlayer?.Play();
                                    }
                                    if (_soundType == SoundType.LoopUntilStopped) {
                                        MountLoopCheck();
                                    }
                                } catch (Exception e) {
                                    OnErrorReceived?.Invoke(this, new MediaError() { Exception = e });
                                    PlaybackStopped?.Invoke(this, "ERR");
                                }
                            }
                        } else {
                            try {
                                _parent.LastFrame = Array.Empty<byte>();
                                string location = _libVLCPath + @"\libvlc\win-x64";
                                Core.Initialize(location);
                                libVLC = new LibVLC("--vout", "none");
                                var media = new Media(libVLC, soundPath, soundPath.StartsWith("http") || soundPath.StartsWith("rtmp")
                                    ? FromType.FromLocation : FromType.FromPath);
                                await media.Parse(soundPath.StartsWith("http") || soundPath.StartsWith("rtmp")
                                    ? MediaParseOptions.ParseNetwork : MediaParseOptions.ParseLocal);
                                _vlcPlayer = new MediaPlayer(media);
                                var processingCancellationTokenSource = new CancellationTokenSource();
                                _vlcPlayer.Stopped += (s, e) => processingCancellationTokenSource.CancelAfter(1);
                                _vlcPlayer.Stopped += delegate { _parent.LastFrame = null; };
                                if (soundPath.StartsWith("http") || soundPath.StartsWith("rtmp")) {
                                    _vlcPlayer.SetVideoFormat("RV32", _width, _height, _pitch);
                                    _vlcPlayer.SetVideoCallbacks(Lock, null, Display);
                                }
                                _baseVolume = volume;
                                Volume = volume;
                                _vlcPlayer.Play();
                                _vlcWasAbleToStart = true;
                            } catch (Exception e) {
                                OnErrorReceived?.Invoke(this, new MediaError() { Exception = e });
                                PlaybackStopped?.Invoke(this, "OK");
                            }
                        }
                    }
                } catch (Exception e) {
                    OnErrorReceived?.Invoke(this, new MediaError() { Exception = e });
                    PlaybackStopped?.Invoke(this, "ERR");
                }
            });
        }
        public async void ChangeVideoStream(string soundPath, float width) {
            try {
                if (_vlcWasAbleToStart) {
                    var media = new Media(libVLC, soundPath, soundPath.StartsWith("http") || soundPath.StartsWith("rtmp")
                                     ? FromType.FromLocation : FromType.FromPath);
                    await media.Parse(soundPath.StartsWith("http") || soundPath.StartsWith("rtmp")
                        ? MediaParseOptions.ParseNetwork : MediaParseOptions.ParseLocal);
                    _vlcPlayer.Media = media;
                    _vlcPlayer.Play();
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
            if (_wavePlayer != null) {
                _wavePlayer.Volume = 1;
            }
        }
        private void Display(IntPtr opaque, IntPtr picture) {
            if (!Invalidated) {
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
            } else {
                try {
                    _vlcPlayer.Stop();
                } catch (Exception e) { OnErrorReceived?.Invoke(this, new MediaError() { Exception = e }); }
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
        OtherPlayerCombat,
        NPC,
        ChatSound,
        LoopUntilStopped
    }
}
