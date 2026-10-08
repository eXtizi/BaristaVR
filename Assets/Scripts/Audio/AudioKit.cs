using System.Collections.Generic;
using UnityEngine;

namespace Barista
{
    public enum Sfx
    {
        Grinder,
        Steam,
        Pump,
        Pour,
        Knock,
        Click,
        Chime,
        Cafe,
    }

    /// <summary>
    /// All sound in the experience is synthesised here at runtime, so there are no third-party audio
    /// files to license. Every source is fully 3D (spatialBlend = 1) with logarithmic distance roll-off.
    /// </summary>
    public static class AudioKit
    {
        const int k_Rate = 44100;
        static readonly Dictionary<Sfx, AudioClip> s_Cache = new Dictionary<Sfx, AudioClip>();

        public static AudioClip Clip(Sfx sfx)
        {
            if (!s_Cache.TryGetValue(sfx, out var clip) || clip == null)
            {
                clip = Build(sfx);
                s_Cache[sfx] = clip;
            }
            return clip;
        }

        public static void Configure(AudioSource src)
        {
            src.spatialBlend = 1f;
            src.rolloffMode = AudioRolloffMode.Logarithmic;
            src.minDistance = 0.35f;
            src.maxDistance = 12f;
            src.dopplerLevel = 0f;
            src.spread = 20f;
            src.playOnAwake = false;
        }

        public static AudioSource Loop(Transform parent, Sfx sfx, float volume)
        {
            var go = new GameObject(sfx + " Audio");
            go.transform.SetParent(parent, false);
            var src = go.AddComponent<AudioSource>();
            Configure(src);
            src.clip = Clip(sfx);
            src.loop = true;
            src.volume = volume;
            return src;
        }

        public static void PlayAt(Sfx sfx, Vector3 position, float volume = 1f)
        {
            var go = new GameObject("OneShot " + sfx);
            go.transform.position = position;
            var src = go.AddComponent<AudioSource>();
            Configure(src);
            src.clip = Clip(sfx);
            src.volume = volume;
            src.Play();
            Object.Destroy(go, src.clip.length + 0.1f);
        }

        static AudioClip Build(Sfx sfx)
        {
            var rng = new System.Random((int)sfx * 7919 + 17);
            float White() => (float)(rng.NextDouble() * 2.0 - 1.0);

            float seconds;
            switch (sfx)
            {
                case Sfx.Grinder: seconds = 2f; break;
                case Sfx.Steam: seconds = 2f; break;
                case Sfx.Pump: seconds = 1f; break;
                case Sfx.Pour: seconds = 2f; break;
                case Sfx.Knock: seconds = 0.25f; break;
                case Sfx.Click: seconds = 0.06f; break;
                case Sfx.Chime: seconds = 0.9f; break;
                default: seconds = 6f; break;
            }

            var n = Mathf.CeilToInt(seconds * k_Rate);
            var data = new float[n];
            float brown = 0f, low = 0f, prev = 0f;

            for (var i = 0; i < n; i++)
            {
                var t = i / (float)k_Rate;
                var w = White();
                float s;
                switch (sfx)
                {
                    case Sfx.Grinder:
                        brown = brown * 0.97f + w * 0.3f;
                        var motor = Mathf.Sin(2f * Mathf.PI * 95f * t) * 0.25f + Mathf.Sin(2f * Mathf.PI * 190f * t) * 0.15f;
                        var crackle = rng.NextDouble() < 0.002 ? White() * 0.8f : 0f;
                        s = (motor + brown * 0.6f + crackle) * (1f + 0.15f * Mathf.Sin(2f * Mathf.PI * 7f * t)) * 0.55f;
                        break;
                    case Sfx.Steam:
                        s = ((w - prev) * 0.45f + w * 0.15f) * (0.9f + 0.1f * Mathf.Sin(2f * Mathf.PI * 3f * t));
                        prev = w;
                        break;
                    case Sfx.Pump:
                        s = Mathf.Sin(2f * Mathf.PI * 50f * t) * 0.3f + Mathf.Sin(2f * Mathf.PI * 100f * t) * 0.15f
                            + Mathf.Sin(2f * Mathf.PI * 150f * t) * 0.08f + w * 0.04f;
                        break;
                    case Sfx.Pour:
                        low += 0.08f * (w - low);
                        s = low * 1.6f * (0.6f + 0.4f * Mathf.Sin(2f * Mathf.PI * 5f * t + Mathf.Sin(2f * Mathf.PI * 1.3f * t)));
                        break;
                    case Sfx.Knock:
                        s = Mathf.Exp(-t * 30f) * Mathf.Sin(2f * Mathf.PI * 110f * t) + 0.4f * w * Mathf.Exp(-t * 80f);
                        break;
                    case Sfx.Click:
                        s = Mathf.Exp(-t * 120f) * (Mathf.Sin(2f * Mathf.PI * 1800f * t) * 0.5f + w * 0.5f);
                        break;
                    case Sfx.Chime:
                        s = Mathf.Exp(-t * 4f) * (Mathf.Sin(2f * Mathf.PI * 880f * t) * 0.45f + Mathf.Sin(2f * Mathf.PI * 1320f * t) * 0.25f);
                        break;
                    default:
                        brown = brown * 0.995f + w * 0.05f;
                        low += 0.02f * (w - low);
                        s = brown * 0.5f + low * (0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * 0.4f * t)) * 1.5f;
                        break;
                }
                data[i] = Mathf.Clamp(s, -1f, 1f);
            }

            if (sfx == Sfx.Cafe)
                AddClinks(data, rng);

            var clip = AudioClip.Create(sfx.ToString(), n, 1, k_Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static void AddClinks(float[] data, System.Random rng)
        {
            for (var c = 0; c < 5; c++)
            {
                var start = rng.Next(0, data.Length - k_Rate / 2);
                var freq = 2200f + (float)rng.NextDouble() * 1800f;
                for (var i = 0; i < k_Rate / 4; i++)
                {
                    var t = i / (float)k_Rate;
                    data[start + i] = Mathf.Clamp(data[start + i] + Mathf.Exp(-t * 25f) * Mathf.Sin(2f * Mathf.PI * freq * t) * 0.15f, -1f, 1f);
                }
            }
        }
    }
}
