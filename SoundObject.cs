using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace RoleplayingVoiceCore {
    public class SoundObject {
        private IPlayerObject _playerObject;
        private VolumeSampleProvider _volumeSampleProvider;
        private WaveOutEvent _waveOutEvent;
        private string _soundPath;
        private SoundType soundType;
        private bool stopPlaybackOnMovement;

        public IPlayerObject PlayerObject { get => _playerObject; set => _playerObject = value; }
        public VolumeSampleProvider VolumeSampleProvider { get => _volumeSampleProvider; set => _volumeSampleProvider = value; }
        public WaveOutEvent WaveOutEvent { get => _waveOutEvent; set => _waveOutEvent = value; }
        public SoundType SoundType { get => soundType; set => soundType = value; }
        public bool StopPlaybackOnMovement { get => stopPlaybackOnMovement; set => stopPlaybackOnMovement = value; }
        public string SoundPath { get => _soundPath; set => _soundPath = value; }
    }

    public enum SoundType {
        MainPlayerTts,
        MainPlayerVoice,
        OtherPlayer,        
        Emote,
        Song
    }
}
