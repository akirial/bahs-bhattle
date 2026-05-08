using System.Collections;
using Photon.Pun;
using UnityEngine;

/// <summary>
/// Networked mini cube spawned by the boss swarm attack.
/// MasterClient drives movement + collision damage and destroys on death.
/// Players damage it via NetworkGunController shot requests routed to Master.
/// </summary>
[RequireComponent(typeof(PhotonView))]
public class BossMiniCube : MonoBehaviourPunCallbacks, IPunObservable
{
    [Header("Health")]
    public int maxHealth = 30;
    public int CurrentHealth { get; private set; }

    [Header("Movement (Master)")]
    public float moveSpeed = 8f;
    public float rotateSpeed = 180f;
    public float seekInterval = 0.35f;

    [Header("Contact Damage (Master)")]
    public int contactDamage = 15;
    public float contactRadius = 0.65f;

    [Header("Grounding")]
    public float groundY = 0.6f;

    [Header("Shot Reaction")]
    public float hitFlashDuration = 0.08f;
    public Vector3 hitPopScale = new Vector3(1.15f, 0.85f, 1.15f);

    private int _hp;
    private bool _consumed;
    private Vector3 _networkPosition;
    private Quaternion _networkRotation;
    private float _nextSeekTime;
    private Vector3 _seekDir = Vector3.forward;
    private Vector3 _baseScale;
    private MaterialPropertyBlock _mpb;
    private Renderer _renderer;
    private Color _baseColor = Color.white;
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private Coroutine _hitRoutine;

    private void Start()
    {
        _hp = Mathf.Max(1, maxHealth);
        CurrentHealth = _hp;
        _baseScale = transform.localScale;
        _renderer = GetComponentInChildren<Renderer>();
        if (_renderer != null)
        {
            _mpb = new MaterialPropertyBlock();
            if (_renderer.sharedMaterial != null)
            {
                if (_renderer.sharedMaterial.HasProperty(BaseColorId))
                    _baseColor = _renderer.sharedMaterial.GetColor(BaseColorId);
                else if (_renderer.sharedMaterial.HasProperty(ColorId))
                    _baseColor = _renderer.sharedMaterial.GetColor(ColorId);
            }
        }
        _networkPosition = transform.position;
        _networkRotation = transform.rotation;
        _nextSeekTime = Time.time + Random.Range(0f, seekInterval);
    }

    private void Update()
    {
        if (!PhotonNetwork.IsMasterClient)
        {
            transform.position = Vector3.Lerp(transform.position, _networkPosition, Time.deltaTime * 16f);
            transform.rotation = Quaternion.Slerp(transform.rotation, _networkRotation, Time.deltaTime * 16f);
            transform.Rotate(Vector3.up, 90f * Time.deltaTime, Space.Self);
            return;
        }
        if (_consumed) return;

        if (Time.time >= _nextSeekTime)
        {
            _nextSeekTime = Time.time + seekInterval;
            NetworkPlayerHealth target = FindClosestLivingPlayer();
            if (target != null)
            {
                Vector3 d = target.transform.position - transform.position;
                d.y = 0f;
                if (d.sqrMagnitude > 0.01f) _seekDir = d.normalized;
            }
        }

        transform.position += _seekDir * (moveSpeed * Time.deltaTime);
        Vector3 p = transform.position;
        p.y = groundY;
        transform.position = p;
        if (_seekDir.sqrMagnitude > 0.001f)
        {
            Quaternion q = Quaternion.LookRotation(_seekDir, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, q, rotateSpeed * Time.deltaTime);
        }

        CheckContactDamage();
    }

    private void CheckContactDamage()
    {
        Collider[] hits = Physics.OverlapSphere(transform.position, contactRadius);
        foreach (Collider hit in hits)
        {
            if (hit == null) continue;
            if (hit.transform.IsChildOf(transform) || transform.IsChildOf(hit.transform)) continue;

            NetworkPlayerHealth player = hit.GetComponentInParent<NetworkPlayerHealth>();
            if (player != null && player.IsAlive)
            {
                player.RequestDamageFromMaster(contactDamage);
                ConsumeAndDestroy();
                return;
            }
        }
    }

    private NetworkPlayerHealth FindClosestLivingPlayer()
    {
        NetworkPlayerHealth[] players = FindObjectsByType<NetworkPlayerHealth>(FindObjectsSortMode.None);
        NetworkPlayerHealth best = null;
        float bestD = float.PositiveInfinity;
        Vector3 p = transform.position;
        foreach (NetworkPlayerHealth pl in players)
        {
            if (pl == null || !pl.IsAlive) continue;
            float d = (pl.transform.position - p).sqrMagnitude;
            if (d < bestD)
            {
                bestD = d;
                best = pl;
            }
        }
        return best;
    }

    public void TakeDamage(int amount)
    {
        if (!PhotonNetwork.IsMasterClient) return;
        if (_consumed) return;
        if (amount <= 0) return;

        photonView.RPC(nameof(HitFxRpc), RpcTarget.All);

        _hp = Mathf.Max(0, _hp - amount);
        CurrentHealth = _hp;
        if (_hp <= 0)
            ConsumeAndDestroy();
    }

    [PunRPC]
    private void HitFxRpc()
    {
        if (_hitRoutine != null) StopCoroutine(_hitRoutine);
        _hitRoutine = StartCoroutine(HitFxRoutine());
    }

    private IEnumerator HitFxRoutine()
    {
        Vector3 from = _baseScale;
        Vector3 to = Vector3.Scale(_baseScale, hitPopScale);
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(0.01f, hitFlashDuration);
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
            transform.localScale = Vector3.Lerp(to, from, k);
            SetTint(Color.white);
            yield return null;
        }
        transform.localScale = from;
        SetTint(_baseColor);
        _hitRoutine = null;
    }

    private void SetTint(Color c)
    {
        if (_renderer == null || _mpb == null) return;
        _renderer.GetPropertyBlock(_mpb);
        if (_renderer.sharedMaterial != null && _renderer.sharedMaterial.HasProperty(BaseColorId))
            _mpb.SetColor(BaseColorId, c);
        if (_renderer.sharedMaterial != null && _renderer.sharedMaterial.HasProperty(ColorId))
            _mpb.SetColor(ColorId, c);
        _renderer.SetPropertyBlock(_mpb);
    }

    private void ConsumeAndDestroy()
    {
        if (_consumed) return;
        _consumed = true;
        if (PhotonNetwork.IsMasterClient)
            PhotonNetwork.Destroy(gameObject);
    }

    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (stream.IsWriting)
        {
            stream.SendNext(transform.position);
            stream.SendNext(transform.rotation);
        }
        else
        {
            _networkPosition = (Vector3)stream.ReceiveNext();
            _networkRotation = (Quaternion)stream.ReceiveNext();
        }
    }
}

