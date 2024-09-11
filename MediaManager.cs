using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using RoleplayingVoiceCore;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;

namespace RoleplayingMediaCore {
    public class MediaManager : IDisposable {
        AudioOutputType audioPlayerType = AudioOutputType.WaveOut;
        byte[] _lastFrame;
        private bool _invalidated = false;
        ConcurrentDictionary<string, MediaObject> _textToSpeechSounds = new ConcurrentDictionary<string, MediaObject>();
        ConcurrentDictionary<string, MediaObject> _voicePackSounds = new ConcurrentDictionary<string, MediaObject>();
        ConcurrentDictionary<string, MediaObject> _combatVoicePackSounds = new ConcurrentDictionary<string, MediaObject>();
        ConcurrentDictionary<string, MediaObject> _chatSounds = new ConcurrentDictionary<string, MediaObject>();
        ConcurrentDictionary<string, MediaObject> _nativeGameAudio = new ConcurrentDictionary<string, MediaObject>();
        ConcurrentDictionary<string, MediaObject> _playbackStreams = new ConcurrentDictionary<string, MediaObject>();
        ConcurrentDictionary<string, Queue<WaveStream>> _nativeAudioQueue = new ConcurrentDictionary<string, Queue<WaveStream>>();
        List<KeyValuePair<string, MediaObject>> _cleanupList = new List<KeyValuePair<string, MediaObject>>();
        private ConcurrentDictionary<string, MediaObject> _mountLoopSounds = new ConcurrentDictionary<string, MediaObject>();
        MediaObject _npcSound = null;

        public event EventHandler<MediaError> OnErrorReceived;
        public event EventHandler OnCleanupTime;
        private IMediaGameObject _mainPlayer = null;
        private IMediaGameObject _camera;
        private string _libVLCPath;
        private Task _updateLoop;
        float _mainPlayerVolume = 1.0f;
        float _otherPlayerVolume = 1.0f;
        float _unfocusedPlayerVolume = 1.0f;
        float _sfxVolume = 1.0f;
        private bool notDisposed = true;
        private float _livestreamVolume = 1;
        private bool alreadyConfiguringSound;
        Stopwatch mainPlayerCombatCooldownTimer = new Stopwatch();
        private bool _lowPerformanceMode;
        private float _npcVolume = 1;
        private float _cameraAndPlayerPositionSlider;
        private int _spatialAudioAccuracy = 100;
        private bool _ignoreSpatialAudioForTTS;

        public float MainPlayerVolume { get => _mainPlayerVolume; set => _mainPlayerVolume = value; }
        public float OtherPlayerVolume { get => _otherPlayerVolume; set => _otherPlayerVolume = value; }
        public float UnfocusedPlayerVolume { get => _unfocusedPlayerVolume; set => _unfocusedPlayerVolume = value; }
        public float SFXVolume { get => _sfxVolume; set => _sfxVolume = value; }
        public float LiveStreamVolume { get => _livestreamVolume; set => _livestreamVolume = value; }
        public byte[] LastFrame { get => _lastFrame; set => _lastFrame = value; }
        public bool LowPerformanceMode { get => _lowPerformanceMode; set => _lowPerformanceMode = value; }
        public float NpcVolume { get => _npcVolume; set => _npcVolume = value; }
        public AudioOutputType AudioPlayerType { get => audioPlayerType; set => audioPlayerType = value; }
        public float CameraAndPlayerPositionSlider { get => _cameraAndPlayerPositionSlider; set => _cameraAndPlayerPositionSlider = value; }
        public int SpatialAudioAccuracy { get => _spatialAudioAccuracy; set => _spatialAudioAccuracy = value; }
        public bool IgnoreSpatialAudioForTTS { get => _ignoreSpatialAudioForTTS; set => _ignoreSpatialAudioForTTS = value; }
        public bool Invalidated { get => _invalidated; set => _invalidated = value; }

        public event EventHandler OnNewMediaTriggered;
        public MediaManager(IMediaGameObject playerObject, IMediaGameObject camera, string libVLCPath) {
            _mainPlayer = playerObject;
            _camera = camera;
            _libVLCPath = libVLCPath;
            _updateLoop = Task.Run(() => Update());
        }

        public async void PlayAudio(IMediaGameObject playerObject, string audioPath, SoundType soundType, bool noSpatial,
            int delay = 0, TimeSpan skipAhead = new TimeSpan(),
            EventHandler<string> onPlaybackStopped = null, EventHandler<StreamVolumeEventArgs> streamVolumeEvent = null, int volumeOffset = 1) {
            await Task.Run(() => {
                if (!string.IsNullOrEmpty(audioPath)) {
                    if ((File.Exists(audioPath) && Directory.Exists(Path.GetDirectoryName(audioPath)))) {
                        switch (soundType) {
                            case SoundType.MainPlayerTts:
                            case SoundType.OtherPlayerTts:
                                ConfigureAudio(playerObject, audioPath, soundType, noSpatial, _textToSpeechSounds, delay, default, onPlaybackStopped, streamVolumeEvent, volumeOffset);
                                break;
                            case SoundType.MainPlayerVoice:
                            case SoundType.OtherPlayer:
                            case SoundType.Emote:
                            case SoundType.Loop:
                            case SoundType.LoopWhileMoving:
                            case SoundType.PlayWhileMoving:
                                ConfigureAudio(playerObject, audioPath, soundType, noSpatial, _voicePackSounds, delay, default, onPlaybackStopped, streamVolumeEvent, volumeOffset);
                                break;
                            case SoundType.LoopUntilStopped:
                                ConfigureAudio(playerObject, audioPath, soundType, noSpatial, _mountLoopSounds, delay, default, onPlaybackStopped, streamVolumeEvent, volumeOffset);
                                break;
                            case SoundType.ChatSound:
                                ConfigureAudio(playerObject, audioPath, soundType, noSpatial, _chatSounds, 0, default, onPlaybackStopped, streamVolumeEvent);
                                break;
                            case SoundType.MainPlayerCombat:
                            case SoundType.OtherPlayerCombat:
                                ConfigureAudio(playerObject, audioPath, soundType, noSpatial, _combatVoicePackSounds, delay, default, onPlaybackStopped, streamVolumeEvent, volumeOffset);
                                break;
                        }
                    }
                }
                OnNewMediaTriggered?.Invoke(this, EventArgs.Empty);
            });
        }

        public async void PlayAudioStream(IMediaGameObject playerObject, WaveStream audioStream, SoundType soundType,
            bool queuePlayback, bool useSmbPitch, float pitch, int delay = 0, bool forceLowLatency = false,
            EventHandler<string> onStopped = null, EventHandler<StreamVolumeEventArgs> streamVolumeChanged = null, float speed = 1, float volumeOffset = 1) {
            try {
                if (playerObject != null) {
                    bool playbackQueued = false;
                    if (_nativeGameAudio.ContainsKey(playerObject.Name)) {
                        var mediaObject = _nativeGameAudio[playerObject.Name];
                        if (!queuePlayback) {
                            mediaObject.Stop();
                        } else if (mediaObject.PlaybackState == PlaybackState.Playing) {
                            if (!_nativeAudioQueue.ContainsKey(playerObject.Name)) {
                                _nativeAudioQueue.TryAdd(playerObject.Name, new Queue<WaveStream>());
                            }
                            _nativeAudioQueue[playerObject.Name].Enqueue(audioStream);
                            string name = playerObject.Name;
                            var queue = _nativeAudioQueue[name];
                            EventHandler<string> function = delegate {
                                try {
                                    if (queue.Count > 0) {
                                        PlayAudioStream(playerObject, queue.Dequeue(), soundType,
                                        false, useSmbPitch, pitch, delay, forceLowLatency, onStopped, streamVolumeChanged, volumeOffset);
                                    }
                                } catch { }
                            };
                            EventHandler<string> removalFunction = delegate {
                                mediaObject.PlaybackStopped -= function;
                                mediaObject.Invalidated = true;
                            };
                            mediaObject.PlaybackStopped += function;
                            mediaObject.PlaybackStopped += removalFunction;
                            playbackQueued = true;
                        }
                    }
                    if (!playbackQueued) {
                        _nativeGameAudio[playerObject.Name] = new MediaObject(
                            this, playerObject, _camera,
                            soundType, "", _libVLCPath, true);
                        var mediaObject = _nativeGameAudio[playerObject.Name];
                        lock (_nativeGameAudio[playerObject.Name]) {
                            float volume = GetVolume(mediaObject.SoundType, mediaObject.CharacterObject);
                            mediaObject.OnErrorReceived += MediaManager_OnErrorReceived;
                            mediaObject.PlaybackStopped += onStopped;
                            if (streamVolumeChanged != null) {
                                mediaObject.StreamVolumeChanged += streamVolumeChanged;
                            }
                            mediaObject.PlaybackStopped += delegate {
                                string name = playerObject.Name;
                                try {
                                    if (_nativeGameAudio.ContainsKey(name)) {
                                        mediaObject.PlaybackStopped -= onStopped;
                                        mediaObject.StreamVolumeChanged -= streamVolumeChanged;
                                    }
                                } catch { }
                            };
                            mediaObject.Play(audioStream, volume, delay, useSmbPitch, audioPlayerType, pitch, forceLowLatency, speed, volumeOffset);
                        }
                    }
                }
            } catch (Exception e) {
                OnErrorReceived?.Invoke(this, new MediaError() { Exception = e });
            }
        }

        public async Task<bool> CheckAudioStreamIsPlaying(IMediaGameObject playerObject) {
            try {
                bool value = _nativeGameAudio[playerObject.Name].PlaybackState == PlaybackState.Playing; ;
                return value;
            } catch (Exception e) {
                OnErrorReceived?.Invoke(this, new MediaError() { Exception = e });
            }
            return false;
        }

        private void MediaManager_OnErrorReceived(object? sender, MediaError e) {
            OnErrorReceived?.Invoke(this, new MediaError() { Exception = e.Exception });
        }

        public async void PlayStream(IMediaGameObject playerObject, string audioPath, int delay = 0) {
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            Task.Run(() => {
                try {
                    OnNewMediaTriggered?.Invoke(this, EventArgs.Empty);
                    bool cancelOperation = false;
                    if (!string.IsNullOrEmpty(audioPath)) {
                        if (audioPath.StartsWith("http") || audioPath.StartsWith("rtmp")) {
                            foreach (var sound in _playbackStreams) {
                                sound.Value.Invalidated = true;
                                sound.Value?.Stop();
                            }
                            _playbackStreams.Clear();
                            ConfigureAudio(playerObject, audioPath, SoundType.Livestream, false, _playbackStreams, delay);
                        }
                    }
                } catch (Exception e) {
                    OnErrorReceived?.Invoke(this, new MediaError() { Exception = e });
                }
            });
        }
        public async void ChangeStream(IMediaGameObject playerObject, string audioPath, float width) {
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            Task.Run(() => {
                try {
                    OnNewMediaTriggered?.Invoke(this, EventArgs.Empty);
                    bool cancelOperation = false;
                    if (!string.IsNullOrEmpty(audioPath)) {
                        if (audioPath.StartsWith("http") || audioPath.StartsWith("rtmp")) {
                            if (_playbackStreams.ContainsKey(playerObject.Name)) {
                                _playbackStreams[playerObject.Name].ChangeVideoStream(audioPath, width);
                            }
                        }
                    }
                } catch (Exception e) {
                    OnErrorReceived?.Invoke(this, new MediaError() { Exception = e });
                }
            });
        }
        public async void StopStream() {
            foreach (var sound in _playbackStreams) {
                sound.Value?.Stop();
            }
            _playbackStreams.Clear();
        }

        public bool IsAllowedToStartStream(IMediaGameObject playerObject) {
            if (_playbackStreams.ContainsKey(playerObject.Name)) {
                return true;
            } else {
                if (_playbackStreams.Count == 0) {
                    return true;
                } else {
                    foreach (string key in _playbackStreams.Keys) {
                        bool noStream = _playbackStreams[key].PlaybackState == PlaybackState.Stopped;
                        return noStream;
                    }
                }
            }
            return false;
        }

        public void StopAudio(IMediaGameObject playerObject) {
            if (playerObject != null) {
                try {
                    if (_voicePackSounds.ContainsKey(playerObject.Name)) {
                        _voicePackSounds[playerObject.Name].Invalidated = true;
                        _voicePackSounds[playerObject.Name].Stop();
                    }
                } catch {

                }
                try {
                    if (_chatSounds.ContainsKey(playerObject.Name)) {
                        _chatSounds[playerObject.Name].Invalidated = true;
                        _chatSounds[playerObject.Name].Stop();
                    }
                } catch {

                }
                try {
                    if (_mountLoopSounds.ContainsKey(playerObject.Name)) {
                        _mountLoopSounds[playerObject.Name].Invalidated = true;
                        _mountLoopSounds[playerObject.Name].Stop();
                    }
                } catch {

                }
                try {
                    if (_nativeGameAudio.ContainsKey(playerObject.Name)) {
                        _nativeAudioQueue.Clear();
                        _nativeGameAudio[playerObject.Name].Invalidated = true;
                        _nativeGameAudio[playerObject.Name].Stop();
                    }
                } catch {

                }
            }
        }

        public void LoopEarly(IMediaGameObject playerObject) {
            if (playerObject != null) {
                if (_voicePackSounds.ContainsKey(playerObject.Name)) {
                    _voicePackSounds[playerObject.Name].LoopEarly();
                }
                if (_nativeGameAudio.ContainsKey(playerObject.Name)) {
                    _nativeGameAudio[playerObject.Name].LoopEarly();
                }
            }
        }
        public async void ConfigureAudio(IMediaGameObject playerObject, string audioPath,
            SoundType soundType, bool noSpatial, ConcurrentDictionary<string, MediaObject> sounds,
            int delay = 0, TimeSpan skipAhead = new TimeSpan(), EventHandler<string> value = null,
            EventHandler<StreamVolumeEventArgs> streamVolumeEvent = null, int volumeOffset = 0) {
            await Task.Run(delegate {
                if (playerObject != null) {
                    if (!alreadyConfiguringSound && ((soundType != SoundType.MainPlayerCombat && soundType != SoundType.LoopWhileMoving)
                        || ((soundType == SoundType.MainPlayerCombat || soundType == SoundType.LoopWhileMoving) && mainPlayerCombatCooldownTimer.ElapsedMilliseconds > 400 || !mainPlayerCombatCooldownTimer.IsRunning))) {
                        alreadyConfiguringSound = true;
                        bool soundIsPlayingAlready = false;
                        if (sounds.ContainsKey(playerObject.Name)) {
                            if (soundType == SoundType.MainPlayerVoice || soundType == SoundType.MainPlayerCombat || soundType == SoundType.PlayWhileMoving) {
                                soundIsPlayingAlready = sounds[playerObject.Name].PlaybackState == PlaybackState.Playing;
                                if (soundType == SoundType.MainPlayerCombat || soundType == SoundType.LoopWhileMoving) {
                                    mainPlayerCombatCooldownTimer.Restart();
                                }
                            } else if (soundType == SoundType.MainPlayerTts) {
                                Stopwatch waitTimer = new Stopwatch();
                                waitTimer.Start();
                                while (sounds[playerObject.Name].PlaybackState == PlaybackState.Playing
                                && waitTimer.ElapsedMilliseconds < 20000) {
                                    Thread.Sleep(100);
                                }
                                try {
                                    sounds[playerObject.Name]?.Stop();
                                } catch (Exception e) {
                                    OnErrorReceived?.Invoke(this, new MediaError() { Exception = e });
                                }
                            } else {
                                try {
                                    sounds[playerObject.Name]?.Stop();
                                } catch (Exception e) {
                                    OnErrorReceived?.Invoke(this, new MediaError() { Exception = e });
                                }
                            }
                        }
                        if (!soundIsPlayingAlready) {
                            try {
                                sounds[playerObject.Name] = new MediaObject(
                                    this, playerObject, _camera,
                                    soundType, audioPath, _libVLCPath, (soundType == SoundType.OtherPlayerTts) ? (!IgnoreSpatialAudioForTTS || !noSpatial) : !noSpatial);
                                lock (sounds[playerObject.Name]) {
                                    float volume = GetVolume(sounds[playerObject.Name].SoundType, sounds[playerObject.Name].CharacterObject);
                                    if (volume < 0.001f || (soundType == SoundType.OtherPlayerTts) ? IgnoreSpatialAudioForTTS || noSpatial : noSpatial) {
                                        volume = 1;
                                    }
                                    sounds[playerObject.Name].OnErrorReceived += MediaManager_OnErrorReceived;
                                    if (streamVolumeEvent != null) {
                                        sounds[playerObject.Name].StreamVolumeChanged += streamVolumeEvent;
                                    }
                                    if (value != null) {
                                        sounds[playerObject.Name].PlaybackStopped += value;
                                    }
                                    Stopwatch soundPlaybackTimer = Stopwatch.StartNew();
                                    sounds[playerObject.Name].Play(audioPath, volume, delay, skipAhead, audioPlayerType, _lowPerformanceMode, volumeOffset);
                                    _cleanupList.Add(new KeyValuePair<string, MediaObject>(playerObject.Name, sounds[playerObject.Name]));
                                }
                            } catch (Exception e) {
                                OnErrorReceived?.Invoke(this, new MediaError() { Exception = e });
                            }
                        }
                        alreadyConfiguringSound = false;
                    }
                }
            });
        }
        private void Update() {
            while (notDisposed) {
                Task.Run(() => {
                    UpdateVolumes(_textToSpeechSounds, IgnoreSpatialAudioForTTS);
                });
                Task.Run(() => {
                    UpdateVolumes(_voicePackSounds);
                });
                Task.Run(() => {
                    UpdateVolumes(_playbackStreams);
                });
                Task.Run(() => {
                    UpdateVolumes(_nativeGameAudio);
                });
                Task.Run(() => {
                    UpdateVolumes(_mountLoopSounds);
                });
                if (!_lowPerformanceMode) {
                    Task.Run(() => {
                        UpdateVolumes(_combatVoicePackSounds);
                    });
                }
                Thread.Sleep(_spatialAudioAccuracy);
            }
        }
        public void UpdateVolumes(ConcurrentDictionary<string, MediaObject> sounds, bool noSpatial = false) {
            for (int i = 0; i < sounds.Count; i++) {
                lock (sounds) {
                    try {
                        string characterObjectName = sounds.Keys.ElementAt<string>(i);
                        if (sounds.ContainsKey(characterObjectName)) {
                            try {
                                lock (sounds[characterObjectName]) {
                                    if (!noSpatial && sounds[characterObjectName].SpatialAllowed) {
                                        if (sounds[characterObjectName].CharacterObject != null) {
                                            Vector3 dir = new Vector3();
                                            if (sounds[characterObjectName].CharacterObject.Position.Length() > 0) {
                                                dir = sounds[characterObjectName].CharacterObject.Position - GetListeningPosition();
                                            } else {
                                                dir = _mainPlayer.Position - GetListeningPosition();
                                            }
                                            float direction = AngleDir(_camera.Forward, dir, _camera.Top);
                                            float pan = Math.Clamp(direction / 3, -1, 1);
                                            try {
                                                sounds[characterObjectName].Pan = pan;
                                                sounds[characterObjectName].Volume = CalculateObjectVolume(characterObjectName, sounds[characterObjectName]);

                                            } catch (Exception e) { OnErrorReceived?.Invoke(this, new MediaError() { Exception = e }); }
                                        }
                                    } else {
                                        sounds[characterObjectName].Pan = 0.5f;
                                        sounds[characterObjectName].Volume = 1;
                                    }
                                }
                            } catch (Exception e) { OnErrorReceived?.Invoke(this, new MediaError() { Exception = e }); }
                        }
                    } catch (Exception e) { OnErrorReceived?.Invoke(this, new MediaError() { Exception = e }); }
                }
            }
        }
        Vector3 GetListeningPosition() {
            return Vector3.Lerp(new Vector3(_camera.Position.X, _mainPlayer.Position.Y, _camera.Position.Z), _mainPlayer.Position, _cameraAndPlayerPositionSlider);
        }
        public void VolumeFix() {
            List<KeyValuePair<string, MediaObject>> fixList = new List<KeyValuePair<string, MediaObject>>();
            fixList.AddRange(_textToSpeechSounds);
            fixList.AddRange(_voicePackSounds);
            fixList.AddRange(_playbackStreams);
            fixList.AddRange(_nativeGameAudio);
            fixList.AddRange(_combatVoicePackSounds);
            fixList.AddRange(_mountLoopSounds);
            fixList.AddRange(_chatSounds);
            foreach (var sound in fixList) {
                if (sound.Value != null) {
                    sound.Value.ResetVolume();
                }
            }
            new WaveOutEvent().Volume = 1;
        }

        public float CalculateObjectVolume(string playerName, MediaObject mediaObject) {
            float maxDistance = (playerName == _mainPlayer.Name ||
            mediaObject.SoundType == SoundType.Livestream) ? 100 : 20;
            float volume = GetVolume(mediaObject.SoundType, mediaObject.CharacterObject);
            float distance = Vector3.Distance(GetListeningPosition(), mediaObject.CharacterObject.Position);
            return mediaObject.SoundType != SoundType.NPC ?
            Math.Clamp(volume * ((maxDistance - distance) / maxDistance), 0f, 1f) : volume;
        }
        public float AngleDir(Vector3 fwd, Vector3 targetDir, Vector3 up) {
            Vector3 perp = Vector3.Cross(fwd, targetDir);
            float dir = Vector3.Dot(perp, up);
            return dir;
        }
        public float GetVolume(SoundType soundType, IMediaGameObject characterObject) {
            if (characterObject != null) {
                if (_mainPlayer.FocusedPlayerObject == null ||
                    characterObject.Name == _mainPlayer.Name ||
                    _mainPlayer.FocusedPlayerObject == characterObject.Name) {
                    switch (soundType) {
                        case SoundType.MainPlayerTts:
                            return _mainPlayerVolume;
                        case SoundType.Emote:
                        case SoundType.MainPlayerVoice:
                        case SoundType.MainPlayerCombat:
                            return _mainPlayerVolume * 1f;
                        case SoundType.OtherPlayerTts:
                        case SoundType.OtherPlayer:
                        case SoundType.OtherPlayerCombat:
                            return _otherPlayerVolume;
                        case SoundType.Loop:
                        case SoundType.LoopWhileMoving:
                        case SoundType.LoopUntilStopped:
                        case SoundType.PlayWhileMoving:
                            return _sfxVolume;
                        case SoundType.Livestream:
                            return _livestreamVolume;
                        case SoundType.NPC:
                            return _npcVolume;
                        case SoundType.ChatSound:
                            return _mainPlayerVolume;
                    }
                } else {
                    switch (soundType) {
                        case SoundType.MainPlayerTts:
                            return _mainPlayerVolume;
                        case SoundType.Emote:
                        case SoundType.MainPlayerVoice:
                        case SoundType.MainPlayerCombat:
                            return _mainPlayerVolume * 1f;
                        case SoundType.OtherPlayerTts:
                        case SoundType.OtherPlayer:
                        case SoundType.OtherPlayerCombat:
                            return _unfocusedPlayerVolume;
                        case SoundType.Loop:
                        case SoundType.LoopUntilStopped:
                        case SoundType.LoopWhileMoving:
                        case SoundType.PlayWhileMoving:
                            return _sfxVolume;
                        case SoundType.Livestream:
                            return _livestreamVolume;
                        case SoundType.NPC:
                            return _npcVolume;
                        case SoundType.ChatSound:
                            return _otherPlayerVolume;
                    }
                }
            }
            return 1;
        }
        public void Dispose() {
            Task.Run(async () => {
                notDisposed = false;
                CleanSounds();
                try {
                    if (_updateLoop != null) {
                        _updateLoop?.Dispose();
                    }
                } catch (Exception e) { OnErrorReceived?.Invoke(this, new MediaError() { Exception = e }); }
            });
        }
        public void CleanNonStreamingSounds() {
            try {
                _cleanupList.AddRange(_textToSpeechSounds);
                _cleanupList.AddRange(_voicePackSounds);
                _cleanupList.AddRange(_nativeGameAudio);
                _cleanupList.AddRange(_chatSounds);
                _cleanupList.AddRange(_combatVoicePackSounds);
                foreach (var sound in _cleanupList) {
                    if (sound.Value != null) {
                        sound.Value.Invalidated = true;
                        sound.Value?.Stop();
                        sound.Value.OnErrorReceived -= MediaManager_OnErrorReceived;
                    }
                }
                _lastFrame = null;
                _textToSpeechSounds?.Clear();
                _voicePackSounds?.Clear();
                _nativeGameAudio?.Clear();
                _chatSounds?.Clear();
                _combatVoicePackSounds?.Clear();
                _cleanupList.Clear();
                OnCleanupTime?.Invoke(this, EventArgs.Empty);
            } catch (Exception e) { OnErrorReceived?.Invoke(this, new MediaError() { Exception = e }); }
        }
        public void CleanSounds() {
            try {
                _cleanupList.AddRange(_textToSpeechSounds);
                _cleanupList.AddRange(_voicePackSounds);
                _cleanupList.AddRange(_playbackStreams);
                _cleanupList.AddRange(_nativeGameAudio);
                _cleanupList.AddRange(_chatSounds);
                _cleanupList.AddRange(_combatVoicePackSounds);
                _cleanupList.AddRange(_mountLoopSounds);
                for (int i = 0; i < _cleanupList.Count; i++) {
                    var sound = _cleanupList[i];
                    if (sound.Value != null) {
                        sound.Value.Invalidated = true;
                        sound.Value?.Stop();
                        sound.Value.OnErrorReceived -= MediaManager_OnErrorReceived;
                    }
                }
                _lastFrame = null;
                _textToSpeechSounds?.Clear();
                _voicePackSounds?.Clear();
                _playbackStreams?.Clear();
                _nativeGameAudio?.Clear();
                _chatSounds?.Clear();
                _combatVoicePackSounds?.Clear();
                _mountLoopSounds?.Clear();
                _npcSound = null;
                _cleanupList.Clear();
                OnCleanupTime?.Invoke(this, EventArgs.Empty);
            } catch (Exception e) { OnErrorReceived?.Invoke(this, new MediaError() { Exception = e }); }
        }
    }
}
