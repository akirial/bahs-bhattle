using System.Collections;
using Photon.Pun;
using UnityEngine;

/// <summary>
/// Centralized squash/stretch + glow + movement helper for the cube boss.
/// All visuals run on every client via PunRPC. Scale values are stored as
/// relative multipliers of the boss's base scale so they stay correct
/// regardless of how big the boss prefab is.
/// </summary>
[RequireComponent(typeof(PhotonView))]
public class BossCubeAnimator : MonoBehaviourPunCallbacks
{
    public Renderer bossRenderer;

    private Vector3 _baseScale = Vector3.one;
    private Color _baseColor = Color.white;
    private MaterialPropertyBlock _mpb;

    private GameObject _shield;
    private Material _shieldMat;
    private Coroutine _shieldRoutine;

    private Coroutine _scaleRoutine;
    private Coroutine _glowRoutine;
    private Coroutine _moveRoutine;
    private Coroutine _spinRoutine;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    public Vector3 BaseScale => _baseScale;

    private void Awake()
    {
        _baseScale = transform.localScale;
        if (bossRenderer == null) bossRenderer = GetComponentInChildren<Renderer>();
        _mpb = new MaterialPropertyBlock();

        if (bossRenderer != null && bossRenderer.sharedMaterial != null)
        {
            if (bossRenderer.sharedMaterial.HasProperty(BaseColorId))
                _baseColor = bossRenderer.sharedMaterial.GetColor(BaseColorId);
            else if (bossRenderer.sharedMaterial.HasProperty(ColorId))
                _baseColor = bossRenderer.sharedMaterial.GetColor(ColorId);
        }
    }

    [PunRPC]
    public void ShowShieldRpc()
    {
        if (_shield == null)
        {
            _shield = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _shield.name = "BossShield";
            Destroy(_shield.GetComponent<Collider>());
            _shield.transform.SetParent(transform, false);
            _shield.transform.localPosition = Vector3.zero;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            _shieldMat = new Material(shader);
            if (_shieldMat.HasProperty("_BaseColor")) _shieldMat.SetColor("_BaseColor", new Color(1f, 0.9f, 0.2f, 0.35f));
            if (_shieldMat.HasProperty("_Color")) _shieldMat.SetColor("_Color", new Color(1f, 0.9f, 0.2f, 0.35f));
            _shieldMat.renderQueue = 3000;

            var r = _shield.GetComponent<MeshRenderer>();
            if (r != null) r.sharedMaterial = _shieldMat;
        }

        _shield.SetActive(true);
        if (_shieldRoutine != null) StopCoroutine(_shieldRoutine);
        _shieldRoutine = StartCoroutine(ShieldPulseRoutine());
    }

    [PunRPC]
    public void HideShieldRpc()
    {
        if (_shieldRoutine != null) StopCoroutine(_shieldRoutine);
        _shieldRoutine = null;
        if (_shield != null) _shield.SetActive(false);
    }

    private IEnumerator ShieldPulseRoutine()
    {
        while (_shield != null && _shield.activeSelf)
        {
            // keep shield proportional to current boss scale
            float t = Time.time;
            float pulse = 1.2f + Mathf.Sin(t * 3.2f) * 0.05f;
            _shield.transform.localScale = Vector3.one * pulse;
            yield return null;
        }
    }

    // ---------------- Scale animations ----------------

    /// <summary>
    /// Animate the cube through a slam sequence: squash -> launch stretch ->
    /// hold -> impact squash -> ease back. All scale values are relative
    /// multipliers of base scale (e.g. (1.2, 0.75, 1.2) keeps base size).
    /// </summary>
    [PunRPC]
    public void SlamSequenceRpc(
        float squashDur, Vector3 squashRel,
        float launchDur, Vector3 launchRel,
        float holdDur,
        float impactDur, Vector3 impactRel,
        float recoverDur)
    {
        if (_scaleRoutine != null) StopCoroutine(_scaleRoutine);
        _scaleRoutine = StartCoroutine(SlamSequence(
            squashDur, squashRel, launchDur, launchRel,
            holdDur, impactDur, impactRel, recoverDur));
    }

    private IEnumerator SlamSequence(
        float squashDur, Vector3 squashRel,
        float launchDur, Vector3 launchRel,
        float holdDur,
        float impactDur, Vector3 impactRel,
        float recoverDur)
    {
        Vector3 squashTarget = Vector3.Scale(_baseScale, squashRel);
        Vector3 launchTarget = Vector3.Scale(_baseScale, launchRel);
        Vector3 impactTarget = Vector3.Scale(_baseScale, impactRel);

        if (squashDur > 0f) yield return Lerp(transform.localScale, squashTarget, squashDur, true);
        if (launchDur > 0f) yield return Lerp(squashTarget, launchTarget, launchDur, true);
        if (holdDur > 0f) yield return new WaitForSeconds(holdDur);
        if (impactDur > 0f) yield return Lerp(launchTarget, impactTarget, impactDur, false);
        if (recoverDur > 0f) yield return Lerp(impactTarget, _baseScale, recoverDur, true);
        transform.localScale = _baseScale;
        _scaleRoutine = null;
    }

    /// <summary>
    /// One-shot scale animation. Used for simpler hops/dips.
    /// </summary>
    [PunRPC]
    public void AnimateScaleRpc(Vector3 targetRel, float duration, bool ease)
    {
        if (_scaleRoutine != null) StopCoroutine(_scaleRoutine);
        Vector3 target = Vector3.Scale(_baseScale, targetRel);
        _scaleRoutine = StartCoroutine(LerpRoutine(transform.localScale, target, duration, ease));
    }

    [PunRPC]
    public void ResetScaleRpc(float duration)
    {
        if (_scaleRoutine != null) StopCoroutine(_scaleRoutine);
        _scaleRoutine = StartCoroutine(LerpRoutine(transform.localScale, _baseScale, duration, true));
    }

    private IEnumerator LerpRoutine(Vector3 from, Vector3 to, float duration, bool ease)
    {
        yield return Lerp(from, to, duration, ease);
        _scaleRoutine = null;
    }

    private IEnumerator Lerp(Vector3 from, Vector3 to, float duration, bool ease)
    {
        if (duration <= 0f) { transform.localScale = to; yield break; }
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / duration;
            float k = ease ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t)) : Mathf.Clamp01(t);
            transform.localScale = Vector3.Lerp(from, to, k);
            yield return null;
        }
        transform.localScale = to;
    }

    /// <summary>Bouncy impact: snap to impact scale, overshoot stretch, settle.
    /// Makes slams feel rubbery/bouncy.</summary>
    [PunRPC]
    public void BounceImpactRpc(
        Vector3 impactRel, float impactDur,
        Vector3 overshootRel, float overshootDur,
        float settleDur)
    {
        if (_scaleRoutine != null) StopCoroutine(_scaleRoutine);
        _scaleRoutine = StartCoroutine(BounceImpactRoutine(
            impactRel, impactDur, overshootRel, overshootDur, settleDur));
    }

    private IEnumerator BounceImpactRoutine(
        Vector3 impactRel, float impactDur,
        Vector3 overshootRel, float overshootDur,
        float settleDur)
    {
        Vector3 impact = Vector3.Scale(_baseScale, impactRel);
        Vector3 overshoot = Vector3.Scale(_baseScale, overshootRel);
        Vector3 from = transform.localScale;
        // Sharp snap to impact
        yield return LerpEaseOutCubic(from, impact, impactDur);
        // Spring up to overshoot (back-out for that "boing" feel)
        yield return LerpEaseOutBack(impact, overshoot, overshootDur, 1.7f);
        // Settle to base with a soft overshoot
        yield return LerpEaseOutBack(overshoot, _baseScale, settleDur, 0.8f);
        transform.localScale = _baseScale;
        _scaleRoutine = null;
    }

    private IEnumerator LerpEaseOutCubic(Vector3 from, Vector3 to, float duration)
    {
        if (duration <= 0f) { transform.localScale = to; yield break; }
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / duration;
            float k = Mathf.Clamp01(t);
            float e = 1f - (1f - k) * (1f - k) * (1f - k);
            transform.localScale = Vector3.Lerp(from, to, e);
            yield return null;
        }
        transform.localScale = to;
    }

    private IEnumerator LerpEaseOutBack(Vector3 from, Vector3 to, float duration, float overshoot)
    {
        if (duration <= 0f) { transform.localScale = to; yield break; }
        float c1 = overshoot;
        float c3 = c1 + 1f;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / duration;
            float k = Mathf.Clamp01(t);
            float p = k - 1f;
            float e = 1f + c3 * p * p * p + c1 * p * p;
            transform.localScale = Vector3.Lerp(from, to, e);
            yield return null;
        }
        transform.localScale = to;
    }

    // ---------------- Color / glow ----------------

    [PunRPC]
    public void PulseGlowRpc(Vector3 colorRgb, float duration, bool flicker)
    {
        if (_glowRoutine != null) StopCoroutine(_glowRoutine);
        _glowRoutine = StartCoroutine(PulseGlowRoutine(new Color(colorRgb.x, colorRgb.y, colorRgb.z, 1f), duration, flicker));
    }

    public void PulseGlow(Color color, float duration, bool flicker)
    {
        photonView.RPC(nameof(PulseGlowRpc), Photon.Pun.RpcTarget.All,
            new Vector3(color.r, color.g, color.b), duration, flicker);
    }

    [PunRPC]
    public void SetGlowRpc(Vector3 colorRgb)
    {
        if (_glowRoutine != null) StopCoroutine(_glowRoutine);
        SetColor(new Color(colorRgb.x, colorRgb.y, colorRgb.z, 1f));
    }

    public void SetGlow(Color color)
    {
        photonView.RPC(nameof(SetGlowRpc), Photon.Pun.RpcTarget.All,
            new Vector3(color.r, color.g, color.b));
    }

    [PunRPC]
    public void StopGlowRpc()
    {
        if (_glowRoutine != null) StopCoroutine(_glowRoutine);
        _glowRoutine = null;
        SetColor(_baseColor);
    }

    private IEnumerator PulseGlowRoutine(Color color, float duration, bool flicker)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float pulse = Mathf.PingPong(elapsed * 6f, 1f);
            if (flicker) pulse *= (Random.value > 0.4f ? 1f : 0.2f);
            Color c = Color.Lerp(_baseColor, color, pulse);
            SetColor(c);
            yield return null;
        }
        SetColor(_baseColor);
        _glowRoutine = null;
    }

    private void SetColor(Color color)
    {
        if (bossRenderer == null) return;
        bossRenderer.GetPropertyBlock(_mpb);
        if (bossRenderer.sharedMaterial != null && bossRenderer.sharedMaterial.HasProperty(BaseColorId))
            _mpb.SetColor(BaseColorId, color);
        if (bossRenderer.sharedMaterial != null && bossRenderer.sharedMaterial.HasProperty(ColorId))
            _mpb.SetColor(ColorId, color);
        bossRenderer.SetPropertyBlock(_mpb);
    }

    // ---------------- Movement ----------------

    /// <summary>
    /// Smoothly moves the boss to a target position over duration.
    /// Used for hops, slams, rises and the phase transition float.
    /// </summary>
    [PunRPC]
    public void MoveToRpc(Vector3 target, float duration, int easeType)
    {
        if (_moveRoutine != null) StopCoroutine(_moveRoutine);
        _moveRoutine = StartCoroutine(MoveTo(target, duration, easeType));
    }

    private IEnumerator MoveTo(Vector3 target, float duration, int easeType)
    {
        Vector3 start = transform.position;
        if (duration <= 0f) { transform.position = target; _moveRoutine = null; yield break; }
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / duration;
            float k = Mathf.Clamp01(t);
            switch (easeType)
            {
                case 1: k = Mathf.SmoothStep(0f, 1f, k); break; // smooth in/out
                case 2: k = k * k * k; break;                   // strong ease-in (slam down)
                case 3: k = 1f - (1f - k) * (1f - k); break;    // ease-out (rise)
                case 4:                                          // bouncy out-back (overshoot)
                {
                    float c1 = 1.4f;
                    float c3 = c1 + 1f;
                    float p = k - 1f;
                    k = 1f + c3 * p * p * p + c1 * p * p;
                    break;
                }
            }
            transform.position = Vector3.Lerp(start, target, k);
            yield return null;
        }
        transform.position = target;
        _moveRoutine = null;
    }

    // ---------------- Spin (phase transition) ----------------

    [PunRPC]
    public void SpinRpc(float duration, float degreesPerSecond)
    {
        if (_spinRoutine != null) StopCoroutine(_spinRoutine);
        _spinRoutine = StartCoroutine(SpinRoutine(duration, degreesPerSecond));
    }

    private IEnumerator SpinRoutine(float duration, float degreesPerSecond)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            transform.Rotate(Vector3.up, degreesPerSecond * Time.deltaTime, Space.World);
            yield return null;
        }
        _spinRoutine = null;
    }

    // ---------------- Reset ----------------

    public void ResetToBase()
    {
        if (_scaleRoutine != null) StopCoroutine(_scaleRoutine);
        if (_glowRoutine != null) StopCoroutine(_glowRoutine);
        if (_moveRoutine != null) StopCoroutine(_moveRoutine);
        if (_spinRoutine != null) StopCoroutine(_spinRoutine);
        if (_shieldRoutine != null) StopCoroutine(_shieldRoutine);
        _scaleRoutine = _glowRoutine = _moveRoutine = _spinRoutine = null;
        _shieldRoutine = null;
        transform.localScale = _baseScale;
        SetColor(_baseColor);
        if (_shield != null) _shield.SetActive(false);
    }
}
