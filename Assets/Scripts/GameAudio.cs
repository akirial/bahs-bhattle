using UnityEngine;

/// <summary>
/// Procedural sound effects. Generates short AudioClips at runtime so we
/// don't need to ship audio assets. Call GameAudio.Play(SfxId, position).
/// </summary>
public enum SfxId
{
    BossSlam,
    BossHop,
    BossCharge,
    Shockwave,
    LaserCharge,
    LaserFire,
    BossHit,
    PlayerHurt,
    PhaseTransition,
    Gunshot,
    BossDeath,
}

public static class GameAudio
{
    private const int SampleRate = 44100;
    private static readonly System.Collections.Generic.Dictionary<SfxId, AudioClip> Clips = new();
    private static bool _initialized;

    private static void EnsureInitialized()
    {
        if (_initialized) return;
        _initialized = true;
        Clips[SfxId.BossSlam] = MakeSlam();
        Clips[SfxId.BossHop] = MakeHop();
        Clips[SfxId.BossCharge] = MakeCharge();
        Clips[SfxId.Shockwave] = MakeShockwave();
        Clips[SfxId.LaserCharge] = MakeLaserCharge();
        Clips[SfxId.LaserFire] = MakeLaserFire();
        Clips[SfxId.BossHit] = MakeBossHit();
        Clips[SfxId.PlayerHurt] = MakePlayerHurt();
        Clips[SfxId.PhaseTransition] = MakePhaseTransition();
        Clips[SfxId.Gunshot] = MakeGunshot();
        Clips[SfxId.BossDeath] = MakeBossDeath();
    }

    public static void Play(SfxId id, Vector3 position, float volume = 0.6f)
    {
        EnsureInitialized();
        if (!Clips.TryGetValue(id, out AudioClip clip) || clip == null) return;
        AudioSource.PlayClipAtPoint(clip, position, volume);
    }

    public static void PlayUI(SfxId id, float volume = 0.6f)
    {
        EnsureInitialized();
        if (!Clips.TryGetValue(id, out AudioClip clip) || clip == null) return;
        Camera cam = Camera.main;
        Vector3 pos = cam != null ? cam.transform.position : Vector3.zero;
        AudioSource.PlayClipAtPoint(clip, pos, volume);
    }

    // ============== Procedural generation helpers ==============

    private static AudioClip BuildClip(string name, float duration, System.Func<float, float> sampleFn)
    {
        int n = Mathf.CeilToInt(SampleRate * duration);
        float[] data = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / SampleRate;
            data[i] = Mathf.Clamp(sampleFn(t), -1f, 1f);
        }
        AudioClip clip = AudioClip.Create(name, n, 1, SampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    private static float Noise() => Random.Range(-1f, 1f);

    // Sharp low boom: sub sine + noise transient with quick decay.
    private static AudioClip MakeSlam()
    {
        return BuildClip("BossSlam", 0.55f, t =>
        {
            float env = Mathf.Exp(-t * 6f);
            float sub = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(80f, 40f, t / 0.55f) * t);
            float crack = Mathf.Exp(-t * 50f) * Noise();
            return (sub * 0.7f + crack * 0.5f) * env;
        });
    }

    // Quick "boing" -- rising sine sweep with light vibrato.
    private static AudioClip MakeHop()
    {
        return BuildClip("BossHop", 0.18f, t =>
        {
            float env = Mathf.Exp(-t * 14f);
            float freq = Mathf.Lerp(220f, 480f, t / 0.18f);
            return Mathf.Sin(2f * Mathf.PI * freq * t) * env * 0.6f;
        });
    }

    // Building rumble for the cube roll charge.
    private static AudioClip MakeCharge()
    {
        return BuildClip("BossCharge", 0.45f, t =>
        {
            float env = Mathf.SmoothStep(0f, 1f, t / 0.45f) * 0.7f;
            float lf = Mathf.Sin(2f * Mathf.PI * 60f * t) * 0.5f;
            float n = Noise() * 0.5f;
            return (lf + n) * env;
        });
    }

    // Whoosh that follows the slam.
    private static AudioClip MakeShockwave()
    {
        return BuildClip("Shockwave", 0.7f, t =>
        {
            float env = Mathf.Exp(-t * 3f);
            // Filtered-ish noise via simple low-pass: sum two random taps.
            float n = (Noise() + Noise()) * 0.5f;
            float whoosh = Mathf.Sin(2f * Mathf.PI * 90f * t);
            return (n * 0.65f + whoosh * 0.25f) * env;
        });
    }

    // Rising charging tone.
    private static AudioClip MakeLaserCharge()
    {
        return BuildClip("LaserCharge", 0.55f, t =>
        {
            float k = t / 0.55f;
            float env = Mathf.Pow(k, 1.8f);
            float freq = Mathf.Lerp(140f, 1100f, k);
            float saw = Mathf.Repeat(freq * t, 1f) * 2f - 1f;
            return saw * 0.4f * env;
        });
    }

    // Sustained sci-fi beam tone.
    private static AudioClip MakeLaserFire()
    {
        return BuildClip("LaserFire", 0.45f, t =>
        {
            float env = (t < 0.04f ? t / 0.04f : 1f) * Mathf.Exp(-t * 1.2f);
            float a = Mathf.Sin(2f * Mathf.PI * 880f * t);
            float b = Mathf.Sin(2f * Mathf.PI * 1320f * t + Mathf.Sin(t * 18f) * 0.5f);
            return (a * 0.4f + b * 0.4f) * env;
        });
    }

    // Metallic clang when the boss takes damage.
    private static AudioClip MakeBossHit()
    {
        return BuildClip("BossHit", 0.18f, t =>
        {
            float env = Mathf.Exp(-t * 22f);
            float a = Mathf.Sin(2f * Mathf.PI * 660f * t);
            float b = Mathf.Sin(2f * Mathf.PI * 1320f * t);
            float c = Mathf.Sin(2f * Mathf.PI * 1980f * t);
            return (a + b * 0.6f + c * 0.4f) * 0.4f * env;
        });
    }

    // Quick low pain thud when the player gets hit.
    private static AudioClip MakePlayerHurt()
    {
        return BuildClip("PlayerHurt", 0.22f, t =>
        {
            float env = Mathf.Exp(-t * 14f);
            float low = Mathf.Sin(2f * Mathf.PI * 110f * t);
            float n = Noise() * 0.5f;
            return (low * 0.7f + n * 0.4f) * env;
        });
    }

    // Long rising rumble + impact at the end for phase transitions.
    private static AudioClip MakePhaseTransition()
    {
        return BuildClip("PhaseTransition", 2.5f, t =>
        {
            float k = t / 2.5f;
            float build = Mathf.SmoothStep(0f, 1f, k);
            float freq = Mathf.Lerp(60f, 220f, k);
            float lf = Mathf.Sin(2f * Mathf.PI * freq * t);
            float n = Noise() * 0.4f;
            float impact = 0f;
            if (k > 0.85f)
            {
                float ki = (k - 0.85f) / 0.15f;
                impact = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(200f, 60f, ki) * t) * (1f - ki);
            }
            return (lf * 0.4f + n * 0.4f) * build + impact * 0.5f;
        });
    }

    // Sharp pop for the player gun.
    private static AudioClip MakeGunshot()
    {
        return BuildClip("Gunshot", 0.18f, t =>
        {
            float env = Mathf.Exp(-t * 28f);
            float crack = Noise();
            float bass = Mathf.Sin(2f * Mathf.PI * 120f * t) * Mathf.Exp(-t * 12f);
            return (crack * 0.7f + bass * 0.5f) * env;
        });
    }

    // Long descending rumble for boss death.
    private static AudioClip MakeBossDeath()
    {
        return BuildClip("BossDeath", 1.6f, t =>
        {
            float env = Mathf.Exp(-t * 1.5f);
            float freq = Mathf.Lerp(220f, 40f, t / 1.6f);
            float main = Mathf.Sin(2f * Mathf.PI * freq * t);
            float n = Noise() * 0.4f;
            return (main * 0.7f + n * 0.3f) * env;
        });
    }
}
