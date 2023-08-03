using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;

namespace RoleplayingVoiceCore {
    public class AudioManager : IDisposable {
        ConcurrentDictionary<string, SoundObject> playbackSounds = new ConcurrentDictionary<string, SoundObject>();
        private IPlayerObject _mainPlayer = null;
        float _mainPlayerVolume = 1.0f;
        float _otherPlayerVolume = 1.0f;
        float _unfocusedPlayerVolume = 1.0f;
        float _songVolume = 1.0f;
        private bool notDisposed = true;

        public float MainPlayerVolume { get => _mainPlayerVolume; set => _mainPlayerVolume = value; }
        public float OtherPlayerVolume { get => _otherPlayerVolume; set => _otherPlayerVolume = value; }
        public float UnfocusedPlayerVolume { get => _unfocusedPlayerVolume; set => _unfocusedPlayerVolume = value; }
        public float SongVolume { get => _songVolume; set => _songVolume = value; }
        public event EventHandler OnNewAudioTriggered;
        public AudioManager(IPlayerObject playerObject) {
            _mainPlayer = playerObject;
            Task.Run(() => Update());
        }

        public async void PlayAudio(IPlayerObject playerObject, string audioPath, SoundType soundType, int delay = 0) {
            Task.Run(() => {
                OnNewAudioTriggered?.Invoke(this, EventArgs.Empty);
                bool cancelOperation = false;
                if (!string.IsNullOrEmpty(audioPath)) {
                    if (File.Exists(audioPath)) {
                        using (var player = new AudioFileReader(audioPath)) {
                            if (playbackSounds.ContainsKey(playerObject.Name)) {
                                if (playbackSounds[playerObject.Name].WaveOutEvent != null) {
                                    if (playbackSounds[playerObject.Name].VolumeSampleProvider != null) {
                                        if (soundType == SoundType.MainPlayerTts || soundType == SoundType.MainPlayerVoice) {
                                            Stopwatch waitTimer = new Stopwatch();
                                            waitTimer.Start();
                                            while (playbackSounds[playerObject.Name].WaveOutEvent.PlaybackState == PlaybackState.Playing
                                            && waitTimer.ElapsedMilliseconds < 10000) {
                                                Thread.Sleep(100);
                                            }
                                        } else if (soundType == SoundType.Song ||
                                        soundType == SoundType.Emote ||
                                        soundType == SoundType.OtherPlayer) {
                                            playbackSounds[playerObject.Name].WaveOutEvent.Stop();
                                        }
                                    }
                                }
                            }
                            playbackSounds[playerObject.Name] = new SoundObject() {
                                PlayerObject = playerObject,
                                WaveOutEvent = new WaveOutEvent(),
                                SoundType = soundType,
                                SoundPath = audioPath,
                            };
                            try {
                                lock (playbackSounds[playerObject.Name]) {
                                    float volume = GetVolume(playbackSounds[playerObject.Name].SoundType, playbackSounds[playerObject.Name].PlayerObject);
                                    float distance = _mainPlayer.Name != playbackSounds[playerObject.Name].PlayerObject.Name ?
                                    Vector3.Distance(_mainPlayer.Position, playbackSounds[playerObject.Name].PlayerObject.Position) : 1;
                                    float newVolume = volume * ((20 - distance) / 20);
                                    if (playbackSounds[playerObject.Name].WaveOutEvent == null) {
                                        playbackSounds[playerObject.Name].WaveOutEvent = new WaveOutEvent();
                                    }
                                    if (delay > 0) {
                                        Thread.Sleep(delay);
                                    }
                                    playbackSounds[playerObject.Name].VolumeSampleProvider = new VolumeSampleProvider(player.ToSampleProvider());
                                    playbackSounds[playerObject.Name].VolumeSampleProvider.Volume = newVolume;
                                    playbackSounds[playerObject.Name].WaveOutEvent?.Init(playbackSounds[playerObject.Name].VolumeSampleProvider);
                                    playbackSounds[playerObject.Name].WaveOutEvent?.Play();
                                }
                            } catch {

                            }
                        }
                    }
                }
            });
        }
        public void StopAudio(IPlayerObject playerObject) {
            playbackSounds[playerObject.Name].WaveOutEvent.Stop();
        }

        private async void Update() {
            while (notDisposed) {
                for (int i = 0; i < playbackSounds.Count; i++) {
                    string playerName = playbackSounds.Keys.ElementAt<string>(i);
                    lock (playbackSounds[playerName]) {
                        if (playbackSounds[playerName].PlayerObject != null) {
                            try {
                                float volume = GetVolume(playbackSounds[playerName].SoundType, playbackSounds[playerName].PlayerObject);
                                float distance = Vector3.Distance(_mainPlayer.Position, playbackSounds[playerName].PlayerObject.Position);
                                float newVolume = volume * ((20 - distance) / 20);
                                if (playbackSounds[playerName].VolumeSampleProvider != null) {
                                    playbackSounds[playerName].VolumeSampleProvider.Volume = newVolume;
                                    if (playbackSounds[playerName].WaveOutEvent != null) {
                                        if (playbackSounds[playerName].WaveOutEvent.PlaybackState == PlaybackState.Stopped) {
                                            if (playbackSounds[playerName].SoundType == SoundType.Song) {
                                                PlayAudio(
                                                    playbackSounds[playerName].PlayerObject,
                                                    playbackSounds[playerName].SoundPath,
                                                    playbackSounds[playerName].SoundType);
                                            }
                                        }
                                    }
                                }
                            } catch {
                                //SoundObject deadObject;
                                //playbackSounds.TryRemove(playerName, out deadObject);
                            }
                        }
                    }
                }
                Thread.Sleep(100);
            }
        }

        public float GetVolume(SoundType soundType, IPlayerObject playerObject) {
            if (playerObject != null) {
                if (_mainPlayer.FocusedPlayerObject == null ||
                    playerObject.Name == _mainPlayer.Name ||
                    _mainPlayer.FocusedPlayerObject == playerObject.Name) {
                    switch (soundType) {
                        case SoundType.MainPlayerTts:
                            return _mainPlayerVolume;
                        case SoundType.Emote:
                        case SoundType.MainPlayerVoice:
                            return _mainPlayerVolume * 0.7f;
                        case SoundType.OtherPlayer:
                            return _otherPlayerVolume;
                        case SoundType.Song:
                            return _songVolume;
                    }
                } else {
                    return _unfocusedPlayerVolume;
                }
            }
            return 1;
        }

        public void Dispose() {
            notDisposed = false;
            playbackSounds.Clear();
        }
    }
}
