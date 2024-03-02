using NAudio.Wave;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;

namespace RoleplayingMediaCore {
    public class MediaManager : IDisposable {
        byte[] _lastFrame;
        ConcurrentDictionary<string, MediaObject> _textToSpeechSounds = new ConcurrentDictionary<string, MediaObject>();
        ConcurrentDictionary<string, MediaObject> _voicePackSounds = new ConcurrentDictionary<string, MediaObject>();
        ConcurrentDictionary<string, MediaObject> _combatVoicePackSounds = new ConcurrentDictionary<string, MediaObject>();
        ConcurrentDictionary<string, MediaObject> _nativeGameAudio = new ConcurrentDictionary<string, MediaObject>();
        ConcurrentDictionary<string, MediaObject> _playbackStreams = new ConcurrentDictionary<string, MediaObject>();
        ConcurrentDictionary<string, Queue<WaveStream>> _nativeAudioQueue = new ConcurrentDictionary<string, Queue<WaveStream>>();
        MediaObject _npcSound = null;

        public event EventHandler<MediaError> OnErrorReceived;
        private IGameObject _mainPlayer = null;
        private IGameObject _camera;
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

        public float MainPlayerVolume { get => _mainPlayerVolume; set => _mainPlayerVolume = value; }
        public float OtherPlayerVolume { get => _otherPlayerVolume; set => _otherPlayerVolume = value; }
        public float UnfocusedPlayerVolume { get => _unfocusedPlayerVolume; set => _unfocusedPlayerVolume = value; }
        public float SFXVolume { get => _sfxVolume; set => _sfxVolume = value; }
        public float LiveStreamVolume { get => _livestreamVolume; set => _livestreamVolume = value; }
        public byte[] LastFrame { get => _lastFrame; set => _lastFrame = value; }
        public bool LowPerformanceMode { get => _lowPerformanceMode; set => _lowPerformanceMode = value; }
        public float NpcVolume { get => _npcVolume; set => _npcVolume = value; }

        public event EventHandler OnNewMediaTriggered;
        public MediaManager(IGameObject playerObject, IGameObject camera, string libVLCPath) {
            _mainPlayer = playerObject;
            _camera = camera;
            _libVLCPath = libVLCPath;
            _updateLoop = Task.Run(() => Update());
        }

        public async void PlayAudio(IGameObject playerObject, string audioPath, SoundType soundType, int delay = 0, TimeSpan skipAhead = new TimeSpan()) {
            _ = Task.Run(() => {
                if (!string.IsNullOrEmpty(audioPath)) {
                    if ((File.Exists(audioPath) && Directory.Exists(Path.GetDirectoryName(audioPath)))) {
                        switch (soundType) {
                            case SoundType.MainPlayerTts:
                            case SoundType.OtherPlayerTts:
                                ConfigureAudio(playerObject, audioPath, soundType, _textToSpeechSounds, delay);
                                break;
                            case SoundType.MainPlayerVoice:
                            case SoundType.OtherPlayer:
                            case SoundType.Emote:
                            case SoundType.Loop:
                            case SoundType.LoopWhileMoving:
                                ConfigureAudio(playerObject, audioPath, soundType, _voicePackSounds, delay);
                                break;
                            case SoundType.MainPlayerCombat:
                            case SoundType.OtherPlayerCombat:
                                ConfigureAudio(playerObject, audioPath, soundType, _combatVoicePackSounds, delay);
                                break;
                        }
                    }
                }
                OnNewMediaTriggered?.Invoke(this, EventArgs.Empty);
            });
        }

        public async void PlayAudioStream(IGameObject playerObject, WaveStream audioStream, SoundType soundType,
            bool queuePlayback, bool useSmbPitch, float pitch, int delay = 0, EventHandler value = null) {
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
                            EventHandler function = delegate {
                                try {
                                    if (queue.Count > 0) {
                                        PlayAudioStream(playerObject, queue.Dequeue(), soundType, false, useSmbPitch, pitch, delay, value);
                                    }
                                } catch { }
                            };
                            EventHandler removalFunction = delegate {
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
                            soundType, "", _libVLCPath);
                        var mediaObject = _nativeGameAudio[playerObject.Name];
                        lock (_nativeGameAudio[playerObject.Name]) {
                            float volume = GetVolume(mediaObject.SoundType, mediaObject.PlayerObject);
                            mediaObject.OnErrorReceived += MediaManager_OnErrorReceived;
                            mediaObject.PlaybackStopped += value;
                            mediaObject.PlaybackStopped += delegate {
                                string name = playerObject.Name;
                                try {
                                    if (_nativeGameAudio.ContainsKey(name)) {
                                        mediaObject.PlaybackStopped -= value;
                                    }
                                } catch { }
                            };
                            mediaObject.Play(audioStream, volume, delay, useSmbPitch, pitch, mediaObject.SoundType == SoundType.NPC);
                        }
                    }
                }
            } catch (Exception e) {
                OnErrorReceived?.Invoke(this, new MediaError() { Exception = e });
            }
        }

        public async Task<bool> CheckAudioStreamIsPlaying(IGameObject playerObject) {
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

        public async void PlayStream(IGameObject playerObject, string audioPath, int delay = 0) {
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            Task.Run(() => {
                OnNewMediaTriggered?.Invoke(this, EventArgs.Empty);
                bool cancelOperation = false;
                if (!string.IsNullOrEmpty(audioPath)) {
                    if (audioPath.StartsWith("http") || audioPath.StartsWith("rtmp")) {
                        foreach (var sound in _playbackStreams) {
                            sound.Value?.Stop();
                        }
                        _playbackStreams.Clear();
                        ConfigureAudio(playerObject, audioPath, SoundType.Livestream, _playbackStreams, delay);
                    }
                }
            });
        }
        public async void StopStream() {
            foreach (var sound in _playbackStreams) {
                sound.Value?.Stop();
            }
            _playbackStreams.Clear();
        }

        public bool IsAllowedToStartStream(IGameObject playerObject) {
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

        public void StopAudio(IGameObject playerObject) {
            if (playerObject != null) {
                try {
                    if (_voicePackSounds.ContainsKey(playerObject.Name)) {
                        _voicePackSounds[playerObject.Name].Invalidated = true;
                        _voicePackSounds[playerObject.Name].Stop();
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

        public void LoopEarly(IGameObject playerObject) {
            if (playerObject != null) {
                if (_voicePackSounds.ContainsKey(playerObject.Name)) {
                    _voicePackSounds[playerObject.Name].LoopEarly();
                }
                if (_nativeGameAudio.ContainsKey(playerObject.Name)) {
                    _nativeGameAudio[playerObject.Name].LoopEarly();
                }
            }
        }
        public async void ConfigureAudio(IGameObject playerObject, string audioPath,
            SoundType soundType, ConcurrentDictionary<string, MediaObject> sounds, int delay = 0, TimeSpan skipAhead = new TimeSpan()) {
            if (!alreadyConfiguringSound && (soundType != SoundType.MainPlayerCombat
                || (soundType == SoundType.MainPlayerCombat && mainPlayerCombatCooldownTimer.ElapsedMilliseconds > 400 || !mainPlayerCombatCooldownTimer.IsRunning))) {
                alreadyConfiguringSound = true;
                bool soundIsPlayingAlready = false;
                if (sounds.ContainsKey(playerObject.Name)) {
                    if (soundType == SoundType.MainPlayerVoice || soundType == SoundType.MainPlayerCombat) {
                        soundIsPlayingAlready = sounds[playerObject.Name].PlaybackState == PlaybackState.Playing;
                        if (soundType == SoundType.MainPlayerCombat) {
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
                            soundType, audioPath, _libVLCPath);
                        lock (sounds[playerObject.Name]) {
                            float volume = GetVolume(sounds[playerObject.Name].SoundType, sounds[playerObject.Name].PlayerObject);
                            if (volume == 0) {
                                volume = 1;
                            }
                            sounds[playerObject.Name].OnErrorReceived += MediaManager_OnErrorReceived;
                            Stopwatch soundPlaybackTimer = Stopwatch.StartNew();
                            sounds[playerObject.Name].Play(audioPath, volume, delay, skipAhead, _lowPerformanceMode);
                            if (soundPlaybackTimer.ElapsedMilliseconds > 2000) {
                                _lowPerformanceMode = true;
                                OnErrorReceived?.Invoke(this, new MediaError() { Exception = new Exception("Low performance detected, enabling low performance mode.") });
                            }
                        }
                    } catch (Exception e) {
                        OnErrorReceived?.Invoke(this, new MediaError() { Exception = e });
                    }
                }
                alreadyConfiguringSound = false;
            }
        }
        private async void Update() {
            while (notDisposed) {
                UpdateVolumes(_textToSpeechSounds);
                UpdateVolumes(_voicePackSounds);
                UpdateVolumes(_playbackStreams);
                UpdateVolumes(_nativeGameAudio);
                if (!_lowPerformanceMode) {
                    UpdateVolumes(_combatVoicePackSounds);
                }
                Thread.Sleep(!_lowPerformanceMode ? 100 : 400);
            }
        }
        public void UpdateVolumes(ConcurrentDictionary<string, MediaObject> sounds) {
            for (int i = 0; i < sounds.Count; i++) {
                string playerName = sounds.Keys.ElementAt<string>(i);
                if (sounds.ContainsKey(playerName)) {
                    try {
                        lock (sounds[playerName]) {
                            if (sounds[playerName].PlayerObject != null) {
                                Vector3 dir = sounds[playerName].PlayerObject.Position - _camera.Position;
                                float direction = AngleDir(_camera.Forward, dir, _camera.Top);
                                sounds[playerName].Volume = CalculateObjectVolume(playerName, sounds[playerName]);
                                sounds[playerName].Pan = Math.Clamp(direction / 3, -1, 1);
                            }
                        }
                    } catch (Exception e) { OnErrorReceived?.Invoke(this, new MediaError() { Exception = e }); }
                }
            }
        }

        public void VolumeFix() {
            List<KeyValuePair<string, MediaObject>> fixList = new List<KeyValuePair<string, MediaObject>>();
            fixList.AddRange(_textToSpeechSounds);
            fixList.AddRange(_voicePackSounds);
            fixList.AddRange(_playbackStreams);
            fixList.AddRange(_nativeGameAudio);
            fixList.AddRange(_combatVoicePackSounds);
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
            float volume = GetVolume(mediaObject.SoundType, mediaObject.PlayerObject);
            float distance = Vector3.Distance(_camera.Position, mediaObject.PlayerObject.Position);
            return mediaObject.SoundType != SoundType.NPC ?
            Math.Clamp(volume * ((maxDistance - distance) / maxDistance), 0f, 1f) : volume;
        }
        public float AngleDir(Vector3 fwd, Vector3 targetDir, Vector3 up) {
            Vector3 perp = Vector3.Cross(fwd, targetDir);
            float dir = Vector3.Dot(perp, up);
            return dir;
        }
        public float GetVolume(SoundType soundType, IGameObject playerObject) {
            if (playerObject != null) {
                if (_mainPlayer.FocusedPlayerObject == null ||
                    playerObject.Name == _mainPlayer.Name ||
                    _mainPlayer.FocusedPlayerObject == playerObject.Name) {
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
                            return _sfxVolume;
                        case SoundType.LoopWhileMoving:
                            return _sfxVolume;
                        case SoundType.Livestream:
                            return _livestreamVolume;
                        case SoundType.NPC:
                            return _npcVolume;
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
                            return _sfxVolume;
                        case SoundType.LoopWhileMoving:
                            return _sfxVolume;
                        case SoundType.Livestream:
                            return _livestreamVolume;
                        case SoundType.NPC:
                            return _npcVolume;
                    }
                }
            }
            return 1;
        }
        public void Dispose() {
            notDisposed = false;
            CleanSounds();
            try {
                if (_updateLoop != null) {
                    _updateLoop?.Dispose();
                }
            } catch (Exception e) { OnErrorReceived?.Invoke(this, new MediaError() { Exception = e }); }
        }
        public void CleanNonStreamingSounds() {
            try {
                List<KeyValuePair<string, MediaObject>> cleanupList = new List<KeyValuePair<string, MediaObject>>();
                cleanupList.AddRange(_textToSpeechSounds);
                cleanupList.AddRange(_voicePackSounds);
                cleanupList.AddRange(_nativeGameAudio);
                foreach (var sound in cleanupList) {
                    if (sound.Value != null) {
                        sound.Value?.Stop();
                        sound.Value.OnErrorReceived -= MediaManager_OnErrorReceived;
                    }
                }
                _lastFrame = null;
                _textToSpeechSounds?.Clear();
                _voicePackSounds?.Clear();
                _nativeGameAudio?.Clear();
            } catch (Exception e) { OnErrorReceived?.Invoke(this, new MediaError() { Exception = e }); }
        }
        public void CleanSounds() {
            try {
                List<KeyValuePair<string, MediaObject>> cleanupList = new List<KeyValuePair<string, MediaObject>>();
                cleanupList.AddRange(_textToSpeechSounds);
                cleanupList.AddRange(_voicePackSounds);
                cleanupList.AddRange(_playbackStreams);
                cleanupList.AddRange(_nativeGameAudio);
                cleanupList.AddRange(_combatVoicePackSounds);
                foreach (var sound in cleanupList) {
                    if (sound.Value != null) {
                        sound.Value?.Stop();
                        sound.Value.OnErrorReceived -= MediaManager_OnErrorReceived;
                    }
                }
                _lastFrame = null;
                _textToSpeechSounds?.Clear();
                _voicePackSounds?.Clear();
                _playbackStreams?.Clear();
                _nativeGameAudio?.Clear();
                _combatVoicePackSounds?.Clear();
                _npcSound = null;
            } catch (Exception e) { OnErrorReceived?.Invoke(this, new MediaError() { Exception = e }); }
        }
    }
}
