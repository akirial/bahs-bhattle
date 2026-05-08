using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;

public enum BossVoiceCategory
{
    Intro,
    Slam,
    DelayedFakeout,
    Roll,
    Laser,
    BigLaser,
    Fakeout,
    BossHurt,
    PlayerHit,
    PhaseTwo,
    Death
}

/// <summary>
/// Plays boss voice lines by category with cooldown, shuffle-bag variety,
/// and interrupt logic. Attach to the Boss prefab alongside
/// NetworkBossAttack / NetworkBossHealth.
/// </summary>
public class BossVoiceManager : MonoBehaviourPunCallbacks
{
    [Header("Audio")]
    public AudioSource audioSource;

    [Header("Voice Clips")]
    public AudioClip[] introClips;
    public AudioClip[] slamClips;
    public AudioClip[] delayedFakeoutClips;
    public AudioClip[] rollClips;
    public AudioClip[] laserClips;
    public AudioClip[] bigLaserClips;
    public AudioClip[] fakeoutClips;
    public AudioClip[] bossHurtClips;
    public AudioClip[] playerHitClips;
    public AudioClip[] phaseTwoClips;
    public AudioClip[] deathClips;

    [Header("Cooldown")]
    [Tooltip("Minimum seconds between voice lines (ignored by priority categories).")]
    public float cooldownDuration = 1.0f;

    [Tooltip("If a voice line has been playing for this long, a new attack line can interrupt it.")]
    public float interruptAfter = 0.6f;

    /// <summary>Global voice volume (0-1). Adjusted from the pause menu settings.</summary>
    public static float VoiceVolume { get; set; } = 0.8f;

    private float _lastPlayTime = -999f;

    private readonly Dictionary<int, List<int>> _shuffleBags = new();

    private void Awake()
    {
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
                audioSource = gameObject.AddComponent<AudioSource>();
        }
        // Force "pure" 2D playback with no effects that can alter perceived pitch.
        audioSource.outputAudioMixerGroup = null;
        audioSource.bypassEffects = true;
        audioSource.bypassListenerEffects = true;
        audioSource.bypassReverbZones = true;
        audioSource.dopplerLevel = 0f;
        audioSource.spatialBlend = 0f;
        audioSource.playOnAwake = false;
        audioSource.pitch = 1f;
    }

    private void LateUpdate()
    {
        if (audioSource != null && audioSource.pitch != 1f)
            audioSource.pitch = 1f;
    }

    public void PlayVoice(BossVoiceCategory category)
    {
        if (!PhotonNetwork.IsMasterClient) return;
        photonView.RPC(nameof(PlayVoiceRpc), RpcTarget.All, (int)category);
    }

    [PunRPC]
    private void PlayVoiceRpc(int categoryInt)
    {
        BossVoiceCategory category = (BossVoiceCategory)categoryInt;
        bool bypassCooldown = category == BossVoiceCategory.Intro
                           || category == BossVoiceCategory.PhaseTwo
                           || category == BossVoiceCategory.Death;

        if (!bypassCooldown && Time.time - _lastPlayTime < cooldownDuration)
            return;

        AudioClip[] clips = GetClips(category);
        if (clips == null || clips.Length == 0) return;

        int catIdx = (int)category;
        int chosen = PickFromShuffleBag(catIdx, clips.Length);

        AudioClip clip = clips[chosen];
        if (clip == null) return;

        if (audioSource.isPlaying && !bypassCooldown)
        {
            float elapsed = Time.time - _lastPlayTime;
            if (elapsed < interruptAfter) return;
        }

        audioSource.Stop();
        audioSource.pitch = 1f;
        audioSource.volume = VoiceVolume;
        audioSource.clip = clip;
        audioSource.Play();
        _lastPlayTime = Time.time;
    }

    private AudioClip[] GetClips(BossVoiceCategory category)
    {
        return category switch
        {
            BossVoiceCategory.Intro          => introClips,
            BossVoiceCategory.Slam           => slamClips,
            BossVoiceCategory.DelayedFakeout => delayedFakeoutClips,
            BossVoiceCategory.Roll           => rollClips,
            BossVoiceCategory.Laser          => laserClips,
            BossVoiceCategory.BigLaser       => bigLaserClips,
            BossVoiceCategory.Fakeout        => fakeoutClips,
            BossVoiceCategory.BossHurt       => bossHurtClips,
            BossVoiceCategory.PlayerHit      => playerHitClips,
            BossVoiceCategory.PhaseTwo       => phaseTwoClips,
            BossVoiceCategory.Death          => deathClips,
            _ => null
        };
    }

    /// <summary>
    /// Detaches the AudioSource onto a standalone GameObject so the voice line
    /// survives the boss being destroyed. Used for the death line.
    /// </summary>
    public void DetachAndPlay(BossVoiceCategory category)
    {
        AudioClip[] clips = GetClips(category);
        if (clips == null || clips.Length == 0) return;

        int catIdx = (int)category;
        int chosen = PickFromShuffleBag(catIdx, clips.Length);

        AudioClip clip = clips[chosen];
        if (clip == null) return;

        audioSource.Stop();

        GameObject go = new GameObject("BossDeathAudio");
        go.transform.position = transform.position;
        AudioSource src = go.AddComponent<AudioSource>();
        src.outputAudioMixerGroup = null;
        src.bypassEffects = true;
        src.bypassListenerEffects = true;
        src.bypassReverbZones = true;
        src.dopplerLevel = 0f;
        src.spatialBlend = 0f;
        src.pitch = 1f;
        src.volume = VoiceVolume;
        src.clip = clip;
        src.Play();
        Object.Destroy(go, clip.length + 0.5f);
    }

    /// <summary>
    /// Shuffle bag: plays every clip in the category once in random order
    /// before any clip can repeat. Guarantees maximum variety.
    /// </summary>
    private int PickFromShuffleBag(int categoryIndex, int clipCount)
    {
        if (clipCount <= 0) return 0;
        if (clipCount == 1) return 0;

        if (!_shuffleBags.TryGetValue(categoryIndex, out List<int> bag) || bag.Count == 0)
        {
            bag = new List<int>(clipCount);
            for (int i = 0; i < clipCount; i++)
                bag.Add(i);

            for (int i = bag.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (bag[i], bag[j]) = (bag[j], bag[i]);
            }

            _shuffleBags[categoryIndex] = bag;
        }

        int pick = bag[bag.Count - 1];
        bag.RemoveAt(bag.Count - 1);
        return pick;
    }
}
