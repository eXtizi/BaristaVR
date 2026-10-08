using UnityEngine;

namespace Barista
{
    /// <summary>Quiet café murmur from the customer side of the room, behind the trainee.</summary>
    public class AmbientCafe : MonoBehaviour
    {
        [Range(0f, 1f)] public float volume = 0.25f;

        void Start()
        {
            var src = AudioKit.Loop(transform, Sfx.Cafe, volume);
            src.minDistance = 1.5f;
            src.spread = 120f;
            src.Play();
        }
    }
}
