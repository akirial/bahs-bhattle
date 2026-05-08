using System.Collections;
using Photon.Pun;
using UnityEngine;
using Hashtable = ExitGames.Client.Photon.Hashtable;

/// <summary>
/// MasterClient-authoritative boss health. HP lives in room custom properties
/// so every client can read it; visual flash/death are broadcast via PunRPC.
/// Tracks the boss's phase (1 or 2). When HP first drops to 50% the boss
/// runs a phase transition cinematic and flips to Phase 2.
/// </summary>
[RequireComponent(typeof(PhotonView))]
public class NetworkBossHealth : MonoBehaviourPunCallbacks
{
    [Header("Health")]
    public int maxHealth = 1000;

    [Header("Visuals")]
    public Renderer bossRenderer;
    public Color hitFlashColor = new Color(1f, 0.2f, 0.2f);
    public float hitFlashDuration = 0.1f;

    [Header("Hit Distortion")]
    public float distortScale = 1.15f;
    public float distortSpeed = 22f;

    [Header("Phase Transition")]
    public string transitionShockwavePrefab = "BossShockwaveRing";
    public float transitionFloatHeight = 3f;
    public float transitionFloatDuration = 0.8f;
    public float transitionSpinDuration = 2.0f;
    public float transitionSpinSpeed = 720f;
    public float transitionDropDuration = 0.5f;
    public float transitionPostPause = 0.5f;

    public static event System.Action OnBossDiedLocal;
    public static event System.Action<int> OnPhaseChangedLocal;
    public static event System.Action OnPhaseTransitionStartedLocal;

    private Color _baseColor = Color.white;
    private MaterialPropertyBlock _mpb;
    private Coroutine _flashRoutine;
    private Coroutine _distortRoutine;
    private Vector3 _baseScale;
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    private int _phase = 1;
    private bool _phaseTransitioning;
    private bool _phaseTransitionTriggered;
    private BossVoiceManager _voice;

    public bool Invulnerable { get; set; }

    public int MaxHealth => maxHealth;
    public int CurrentHealth => NetworkGameManager.Instance != null ? NetworkGameManager.Instance.GetBossHealth() : maxHealth;
    public bool IsAlive => NetworkGameManager.Instance == null || NetworkGameManager.Instance.IsBossAlive();
    public int Phase => _phase;
    public bool IsPhaseTransitioning => _phaseTransitioning;

    private void Start()
    {
        _voice = GetComponent<BossVoiceManager>();
        _baseScale = transform.localScale;
        if (bossRenderer == null) bossRenderer = GetComponentInChildren<Renderer>();
        if (bossRenderer != null)
        {
            _mpb = new MaterialPropertyBlock();
            if (bossRenderer.sharedMaterial != null)
            {
                if (bossRenderer.sharedMaterial.HasProperty(BaseColorId))
                    _baseColor = bossRenderer.sharedMaterial.GetColor(BaseColorId);
                else if (bossRenderer.sharedMaterial.HasProperty(ColorId))
                    _baseColor = bossRenderer.sharedMaterial.GetColor(ColorId);
            }
        }

        if (PhotonNetwork.IsMasterClient && PhotonNetwork.InRoom)
        {
            int scaledHp = NetworkGameManager.Instance != null
                ? NetworkGameManager.Instance.ScaledBossHealth
                : maxHealth;
            maxHealth = scaledHp;

            PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable
            {
                { NetworkGameManager.BossHealthKey, scaledHp },
                { NetworkGameManager.BossAliveKey, true },
                { NetworkGameManager.BossDefeatedKey, false }
            });
        }
    }

    public void TakeDamage(int amount)
    {
        if (!PhotonNetwork.IsMasterClient || !PhotonNetwork.InRoom) return;
        if (!IsAlive || amount <= 0) return;
        if (Invulnerable) return;

        int newHp = Mathf.Max(0, CurrentHealth - amount);
        PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable
        {
            { NetworkGameManager.BossHealthKey, newHp }
        });

        photonView.RPC(nameof(FlashRedRpc), RpcTarget.All);
        photonView.RPC(nameof(DistortRpc), RpcTarget.All);
        photonView.RPC(nameof(PlayBossSfxRpc), RpcTarget.All, (int)SfxId.BossHit, transform.position);

        if (_voice != null) _voice.PlayVoice(BossVoiceCategory.BossHurt);

        if (!_phaseTransitionTriggered && newHp > 0 && newHp <= maxHealth / 2)
        {
            _phaseTransitionTriggered = true;
            photonView.RPC(nameof(PhaseTransitionRpc), RpcTarget.All);
        }

        if (newHp <= 0)
        {
            PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable
            {
                { NetworkGameManager.BossAliveKey, false },
                { NetworkGameManager.BossDefeatedKey, true }
            });
            photonView.RPC(nameof(BossDiedRpc), RpcTarget.All);
            if (NetworkGameManager.Instance != null)
                NetworkGameManager.Instance.OnBossDefeated();
            StartCoroutine(DestroyAfter(2.5f));
        }
    }

    [PunRPC]
    private void FlashRedRpc()
    {
        if (bossRenderer == null) return;
        if (_flashRoutine != null) StopCoroutine(_flashRoutine);
        _flashRoutine = StartCoroutine(FlashRoutine());
    }

    private IEnumerator FlashRoutine()
    {
        SetRendererColor(hitFlashColor);
        yield return new WaitForSeconds(hitFlashDuration);
        SetRendererColor(_baseColor);
        _flashRoutine = null;
    }

    [PunRPC]
    private void DistortRpc()
    {
        if (_distortRoutine != null) StopCoroutine(_distortRoutine);
        _distortRoutine = StartCoroutine(DistortRoutine());
    }

    private IEnumerator DistortRoutine()
    {
        Vector3 baseScale = transform.localScale;
        Vector3 big = baseScale * distortScale;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime * distortSpeed;
            transform.localScale = Vector3.Lerp(baseScale, big, t);
            yield return null;
        }
        t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime * distortSpeed;
            transform.localScale = Vector3.Lerp(big, baseScale, t);
            yield return null;
        }
        transform.localScale = baseScale;
        _distortRoutine = null;
    }

    [PunRPC]
    private void PhaseTransitionRpc()
    {
        if (_phaseTransitioning) return;
        _phaseTransitioning = true;
        OnPhaseTransitionStartedLocal?.Invoke();
        GameAudio.Play(SfxId.PhaseTransition, transform.position, 0.85f);
        if (_voice != null) _voice.PlayVoice(BossVoiceCategory.PhaseTwo);
        StartCoroutine(PhaseTransitionRoutine());
    }

    [PunRPC]
    private void PlayBossSfxRpc(int sfxId, Vector3 pos)
    {
        GameAudio.Play((SfxId)sfxId, pos);
    }

    private IEnumerator PhaseTransitionRoutine()
    {
        Vector3 startPos = transform.position;
        Vector3 floatPos = startPos + Vector3.up * transitionFloatHeight;

        // 1. Float up
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / transitionFloatDuration;
            transform.position = Vector3.Lerp(startPos, floatPos, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t)));
            yield return null;
        }
        transform.position = floatPos;

        // 2. Spin while pulsing red/orange
        BossCubeAnimator anim = GetComponent<BossCubeAnimator>();
        if (anim != null)
        {
            anim.SpinRpc(transitionSpinDuration, transitionSpinSpeed);
            anim.PulseGlowRpc(new Vector3(1f, 0.4f, 0.05f), transitionSpinDuration, false);
        }
        yield return new WaitForSeconds(transitionSpinDuration * 0.55f);

        // 3. Spawn the visual shockwave (only on master so it propagates)
        if (PhotonNetwork.IsMasterClient && !string.IsNullOrEmpty(transitionShockwavePrefab))
        {
            Vector3 ringPos = new Vector3(floatPos.x, 0.1f, floatPos.z);
            PhotonNetwork.InstantiateRoomObject(transitionShockwavePrefab, ringPos, Quaternion.identity);
        }

        yield return new WaitForSeconds(transitionSpinDuration * 0.45f);

        // 4. Drop back to ground
        t = 0f;
        Vector3 dropTarget = startPos;
        while (t < 1f)
        {
            t += Time.deltaTime / transitionDropDuration;
            transform.position = Vector3.Lerp(floatPos, dropTarget, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t)));
            yield return null;
        }
        transform.position = dropTarget;

        // 5. Pause briefly, then enter Phase 2
        yield return new WaitForSeconds(transitionPostPause);
        _phase = 2;
        _phaseTransitioning = false;
        OnPhaseChangedLocal?.Invoke(_phase);
    }

    [PunRPC]
    private void BossDiedRpc()
    {
        OnBossDiedLocal?.Invoke();
        GameAudio.Play(SfxId.BossDeath, transform.position, 0.9f);
        if (_voice != null) _voice.DetachAndPlay(BossVoiceCategory.Death);
        StartCoroutine(DeathVisualSequence());
    }

    private IEnumerator DeathVisualSequence()
    {
        BossCubeAnimator anim = GetComponent<BossCubeAnimator>();

        if (anim != null)
            anim.SpinRpc(2.0f, 540f);

        float shrinkDur = 2.0f;
        float elapsed = 0f;
        while (elapsed < shrinkDur)
        {
            elapsed += Time.deltaTime;
            float k = elapsed / shrinkDur;
            float scale = Mathf.Lerp(1f, 0.05f, k * k);
            transform.localScale = _baseScale * scale;

            if (bossRenderer != null && _mpb != null)
            {
                float alpha = Mathf.Lerp(1f, 0f, k);
                Color c = new Color(_baseColor.r, _baseColor.g, _baseColor.b, alpha);
                SetRendererColor(c);
            }
            yield return null;
        }
        transform.localScale = _baseScale * 0.05f;
    }

    private IEnumerator DestroyAfter(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (PhotonNetwork.IsMasterClient)
        {
            PhotonNetwork.Destroy(gameObject);
        }
    }

    private void SetRendererColor(Color color)
    {
        if (bossRenderer == null || _mpb == null) return;
        bossRenderer.GetPropertyBlock(_mpb);
        if (bossRenderer.sharedMaterial != null && bossRenderer.sharedMaterial.HasProperty(BaseColorId))
            _mpb.SetColor(BaseColorId, color);
        if (bossRenderer.sharedMaterial != null && bossRenderer.sharedMaterial.HasProperty(ColorId))
            _mpb.SetColor(ColorId, color);
        bossRenderer.SetPropertyBlock(_mpb);
    }
}
