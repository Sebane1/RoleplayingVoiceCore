using System.Numerics;

namespace RoleplayingVoiceCore {
    public interface IPlayerObject {
        public string Name { get; }
        public Vector3 Position { get; }
        public float Rotation { get; }
        public string FocusedPlayerObject { get; }
    }
}
