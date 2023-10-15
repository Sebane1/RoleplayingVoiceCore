﻿using LibVLCSharp.Shared;
using NAudio.Vorbis;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;

namespace RoleplayingMediaCore {
    public class MediaManager : IDisposable {
        byte[] _lastFrame;
        ConcurrentDictionary<string, MediaObject> _textToSpeechSounds = new ConcurrentDictionary<string, MediaObject>();
        ConcurrentDictionary<string, MediaObject> _voicePackSounds = new ConcurrentDictionary<string, MediaObject>();
        ConcurrentDictionary<string, MediaObject> _nativeGameAudio = new ConcurrentDictionary<string, MediaObject>();
        ConcurrentDictionary<string, MediaObject> _playbackStreams = new ConcurrentDictionary<string, MediaObject>();
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
        private float _liveStreamVolume = 1;
        private bool alreadyConfiguringSound;

        public float MainPlayerVolume { get => _mainPlayerVolume; set => _mainPlayerVolume = value; }
        public float OtherPlayerVolume { get => _otherPlayerVolume; set => _otherPlayerVolume = value; }
        public float UnfocusedPlayerVolume { get => _unfocusedPlayerVolume; set => _unfocusedPlayerVolume = value; }
        public float SFXVolume { get => _sfxVolume; set => _sfxVolume = value; }
        public float LiveStreamVolume { get => _liveStreamVolume; set => _liveStreamVolume = value; }
        public byte[] LastFrame { get => _lastFrame; set => _lastFrame = value; }

        public event EventHandler OnNewMediaTriggered;
        public MediaManager(IGameObject playerObject, IGameObject camera, string libVLCPath) {
            _mainPlayer = playerObject;
            _camera = camera;
            _libVLCPath = libVLCPath;
            _updateLoop = Task.Run(() => Update());
        }

        public async void PlayAudio(IGameObject playerObject, string audioPath, SoundType soundType, int delay = 0, TimeSpan skipAhead = new TimeSpan()) {
            _ = Task.Run(() => {
                OnNewMediaTriggered?.Invoke(this, EventArgs.Empty);
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
                            case SoundType.MainPlayerCombat:
                            case SoundType.OtherPlayerCombat:
                            case SoundType.Loop:
                            case SoundType.LoopWhileMoving:
                                ConfigureAudio(playerObject, audioPath, soundType, _voicePackSounds, delay);
                                break;
                        }
                    }
                }
            });
        }

        public async void PlayAudioStream(IGameObject playerObject, WaveStream audioStream, SoundType soundType, int delay = 0) {
            try {
                if (playerObject != null) {
                    if (_nativeGameAudio.ContainsKey(playerObject.Name)) {
                        _nativeGameAudio[playerObject.Name].Stop();
                    }
                    _nativeGameAudio[playerObject.Name] = new MediaObject(
                        this, playerObject, _camera,
                        soundType, "", _libVLCPath);
                    lock (_nativeGameAudio[playerObject.Name]) {
                        float volume = GetVolume(_nativeGameAudio[playerObject.Name].SoundType, _nativeGameAudio[playerObject.Name].PlayerObject);
                        _nativeGameAudio[playerObject.Name].OnErrorReceived += MediaManager_OnErrorReceived;
                        _nativeGameAudio[playerObject.Name].Play(audioStream, volume, delay);
                    }
                }
            } catch (Exception e) {
                OnErrorReceived?.Invoke(this, new MediaError() { Exception = e });
            }
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
                    if (audioPath.StartsWith("http")) {
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
                if (_voicePackSounds.ContainsKey(playerObject.Name)) {
                    _voicePackSounds[playerObject.Name].Stop();
                }
                if (_nativeGameAudio.ContainsKey(playerObject.Name)) {
                    _nativeGameAudio[playerObject.Name].Stop();
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
            if (!alreadyConfiguringSound) {
                alreadyConfiguringSound = true;
                bool soundIsPlayingAlready = false;
                if (sounds.ContainsKey(playerObject.Name)) {
                    if (soundType == SoundType.MainPlayerVoice || soundType == SoundType.MainPlayerCombat) {
                        soundIsPlayingAlready = sounds[playerObject.Name].PlaybackState == PlaybackState.Playing;
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
                            sounds[playerObject.Name].Play(audioPath, volume, delay, skipAhead);
                            sounds[playerObject.Name].OnErrorReceived += MediaManager_OnErrorReceived;
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
                Thread.Sleep(100);
            }
        }
        public void UpdateVolumes(ConcurrentDictionary<string, MediaObject> sounds) {
            for (int i = 0; i < sounds.Count; i++) {
                string playerName = sounds.Keys.ElementAt<string>(i);
                if (sounds.ContainsKey(playerName)) {
                    try {
                        lock (sounds[playerName]) {
                            if (sounds[playerName].PlayerObject != null) {
                                float maxDistance = (playerName == _mainPlayer.Name ||
                                sounds[playerName].SoundType == SoundType.Livestream) ? 100 : 20;
                                float volume = GetVolume(sounds[playerName].SoundType, sounds[playerName].PlayerObject);
                                float distance = Vector3.Distance(_camera.Position, sounds[playerName].PlayerObject.Position);
                                float newVolume = Math.Clamp(volume * ((maxDistance - distance) / maxDistance), 0f, 1f);
                                Vector3 dir = sounds[playerName].PlayerObject.Position - _camera.Position;
                                float direction = AngleDir(_camera.Forward, dir, _camera.Top);
                                sounds[playerName].Volume = newVolume;
                                sounds[playerName].Pan = Math.Clamp(direction / 3, -1, 1);
                            }
                        }
                    } catch (Exception e) { OnErrorReceived?.Invoke(this, new MediaError() { Exception = e }); }
                }
            }
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
                            return _mainPlayerVolume * 1f;
                        case SoundType.OtherPlayerTts:
                        case SoundType.OtherPlayer:
                            return _otherPlayerVolume;
                        case SoundType.Loop:
                            return _sfxVolume;
                        case SoundType.LoopWhileMoving:
                            return _sfxVolume;
                        case SoundType.Livestream:
                            return _liveStreamVolume;
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
                            return _liveStreamVolume;
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
            } catch (Exception e) { OnErrorReceived?.Invoke(this, new MediaError() { Exception = e }); }
        }
    }
}
