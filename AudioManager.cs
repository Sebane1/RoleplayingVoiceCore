using NAudio.Vorbis;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;

namespace RoleplayingVoiceCore {
    public class AudioManager : IDisposable {
        ConcurrentDictionary<string, SoundObject> playbackSounds = new ConcurrentDictionary<string, SoundObject>();
        private IGameObject _mainPlayer = null;
        private IGameObject _camera;
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
        public AudioManager(IGameObject playerObject, IGameObject camera) {
            _mainPlayer = playerObject;
            _camera = camera;
            Task.Run(() => Update());
        }

        public async void PlayAudio(IGameObject playerObject, string audioPath, SoundType soundType, int delay = 0) {
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            Task.Run(() => {
                OnNewAudioTriggered?.Invoke(this, EventArgs.Empty);
                bool cancelOperation = false;
                if (!string.IsNullOrEmpty(audioPath)) {
                    if (audioPath.EndsWith(".ogg")) {
                        if (File.Exists(audioPath) && Directory.Exists(Path.GetDirectoryName(audioPath))) {
                            using (var player = new VorbisWaveReader(audioPath)) {
                                ConfigureAudio(playerObject, audioPath, soundType, player, delay);
                            }
                        }
                    } else if (File.Exists(audioPath) && Directory.Exists(Path.GetDirectoryName(audioPath))) {
                        using (var player = new AudioFileReader(audioPath)) {
                            ConfigureAudio(playerObject, audioPath, soundType, player, delay);
                        }
                    }
                }
            });
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
        }
        public void StopAudio(IGameObject playerObject) {
            playbackSounds[playerObject.Name].Stop();
        }
        public async void ConfigureAudio(IGameObject playerObject, string audioPath,
            SoundType soundType, WaveStream player, int delay = 0) {
            if (playbackSounds.ContainsKey(playerObject.Name)) {
                if (playbackSounds[playerObject.Name].WaveOutEvent != null) {
                    if (playbackSounds[playerObject.Name].VolumeSampleProvider != null) {
                        if (soundType == SoundType.MainPlayerTts || soundType == SoundType.MainPlayerVoice) {
                            Stopwatch waitTimer = new Stopwatch();
                            waitTimer.Start();
                            while (playbackSounds[playerObject.Name].WaveOutEvent.PlaybackState == PlaybackState.Playing
                            && waitTimer.ElapsedMilliseconds < 20000) {
                                Thread.Sleep(100);
                            }
                        } else {
                            playbackSounds[playerObject.Name].Stop();
                        }
                    }
                }
            }
            if (soundType != SoundType.MainPlayerTts &&
                soundType != SoundType.OtherPlayerTts &&
                player.TotalTime.TotalSeconds > 20) {
                soundType = SoundType.Song;
            }
            playbackSounds[playerObject.Name] = new SoundObject(playerObject,
              new WaveOutEvent(),
               soundType,
               audioPath);

            try {
                lock (playbackSounds[playerObject.Name]) {
                    float volume = GetVolume(playbackSounds[playerObject.Name].SoundType, playbackSounds[playerObject.Name].PlayerObject);
                    float distance =
                    Vector3.Distance(_camera.Position, playbackSounds[playerObject.Name].PlayerObject.Position);
                    float newVolume = volume * ((20 - distance) / 20);
                    if (playbackSounds[playerObject.Name].WaveOutEvent == null) {
                        playbackSounds[playerObject.Name].WaveOutEvent = new WaveOutEvent();
                    }
                    if (delay > 0) {
                        Thread.Sleep(delay);
                    }
                    playbackSounds[playerObject.Name].VolumeSampleProvider = new VolumeSampleProvider(player.ToSampleProvider());
                    playbackSounds[playerObject.Name].VolumeSampleProvider.Volume = newVolume;
                    playbackSounds[playerObject.Name].PanningSampleProvider =
                    new PanningSampleProvider(playbackSounds[playerObject.Name].VolumeSampleProvider.ToMono());
                    Vector3 dir = playbackSounds[playerObject.Name].PlayerObject.Position - _camera.Position;
                    float direction = AngleDir(_camera.Forward, dir, _camera.Top);
                    playbackSounds[playerObject.Name].PanningSampleProvider.Pan = Math.Clamp(direction / 3, -1, 1);
                    playbackSounds[playerObject.Name].WaveOutEvent?.Init(playbackSounds[playerObject.Name].PanningSampleProvider);
                    playbackSounds[playerObject.Name].WaveOutEvent?.Play();
                }
            } catch {

            }
        }
        private async void Update() {
            while (notDisposed) {
                for (int i = 0; i < playbackSounds.Count; i++) {
                    string playerName = playbackSounds.Keys.ElementAt<string>(i);
                    lock (playbackSounds[playerName]) {
                        if (playbackSounds[playerName].PlayerObject != null) {
                            try {
                                float maxDistance = playerName == _mainPlayer.Name ? 100 : 20;
                                float volume = GetVolume(playbackSounds[playerName].SoundType, playbackSounds[playerName].PlayerObject);
                                float distance = Vector3.Distance(_camera.Position, playbackSounds[playerName].PlayerObject.Position);
                                float newVolume = Math.Clamp(volume * ((maxDistance - distance) / maxDistance), 0f, 1f);
                                Vector3 dir = playbackSounds[playerName].PlayerObject.Position - _camera.Position;
                                float direction = AngleDir(_camera.Forward, dir, _camera.Top);
                                if (playbackSounds[playerName].VolumeSampleProvider != null) {
                                    playbackSounds[playerName].VolumeSampleProvider.Volume = newVolume;
                                    if (playbackSounds[playerName].PanningSampleProvider != null) {
                                        playbackSounds[playerName].PanningSampleProvider.Pan = Math.Clamp(direction / 3, -1, 1);
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
            foreach (var sound in playbackSounds) {
                sound.Value.Stop();
            }
            playbackSounds.Clear();
        }
    }
}
