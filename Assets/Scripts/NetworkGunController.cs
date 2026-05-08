using System.Collections;
using Photon.Pun;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// PUN gun controller. Owner handles local ammo/reload and raycasts from the
/// local camera. Hit requests go to the MasterClient, which applies boss damage.
/// </summary>
[RequireComponent(typeof(PhotonView))]
public class NetworkGunController : MonoBehaviourPunCallbacks
{
    [Header("References")]
    public Camera shootCamera;

    [Header("Gun stats")]
    public int damagePerShot = 25;
    public float fireRate = 0.2f;
    public int magazineSize = 12;
    public float reloadTime = 1.5f;
    public float maxRange = 200f;

    [Header("Layer masks")]
    public LayerMask hitMask = ~0;

    public int CurrentAmmo { get; private set; }
    public bool IsReloading { get; private set; }
    public int MagazineSize => magazineSize;

    public event System.Action OnAmmoChanged;

    private float _nextFireTime;
    private bool _ownerEnabled = true;
    private Mouse _mouse;
    private Keyboard _kb;

    public void SetEnabledForOwner(bool enabled) { _ownerEnabled = enabled; }

    private void Start()
    {
        if (!photonView.IsMine) return;
        if (shootCamera == null) shootCamera = GetComponentInChildren<Camera>(true);
        CurrentAmmo = magazineSize;
        _mouse = Mouse.current;
        _kb = Keyboard.current;
        OnAmmoChanged?.Invoke();
    }

    private void Update()
    {
        if (!photonView.IsMine) return;
        if (!_ownerEnabled) return;
        if (IsReloading) return;

        if (_kb != null && _kb.rKey.wasPressedThisFrame && CurrentAmmo < magazineSize)
        {
            StartCoroutine(ReloadCoroutine());
            return;
        }

        if (_mouse != null && _mouse.leftButton.isPressed && Time.time >= _nextFireTime)
        {
            TryShoot();
        }
    }

    private void TryShoot()
    {
        if (CurrentAmmo <= 0)
        {
            StartCoroutine(ReloadCoroutine());
            return;
        }

        _nextFireTime = Time.time + fireRate;
        CurrentAmmo--;
        OnAmmoChanged?.Invoke();
        photonView.RPC(nameof(GunshotRpc), RpcTarget.All, transform.position);

        if (shootCamera == null) return;

        Ray ray = new Ray(shootCamera.transform.position, shootCamera.transform.forward);
        if (Physics.Raycast(ray, out RaycastHit hit, maxRange, hitMask, QueryTriggerInteraction.Ignore))
        {
            PhotonView hitView = hit.collider.GetComponentInParent<PhotonView>();
            int hitViewId = hitView != null ? hitView.ViewID : 0;
            photonView.RPC(nameof(RequestShotRpc), RpcTarget.MasterClient, hitViewId, hit.point);
        }
        else
        {
            photonView.RPC(nameof(RequestShotRpc), RpcTarget.MasterClient, 0, ray.origin + ray.direction * maxRange);
        }
    }

    [PunRPC]
    private void RequestShotRpc(int hitViewId, Vector3 hitPoint, PhotonMessageInfo info)
    {
        if (!PhotonNetwork.IsMasterClient) return;
        if (hitViewId == 0) return;

        PhotonView hitView = PhotonView.Find(hitViewId);
        if (hitView == null) return;

        NetworkBossHealth boss = hitView.GetComponent<NetworkBossHealth>();
        if (boss != null)
        {
            boss.TakeDamage(damagePerShot);
            return;
        }

        BossMiniCube mini = hitView.GetComponent<BossMiniCube>();
        if (mini != null)
        {
            mini.TakeDamage(damagePerShot);
        }
    }

    [PunRPC]
    private void GunshotRpc(Vector3 pos)
    {
        GameAudio.Play(SfxId.Gunshot, pos, 0.45f);
    }

    private IEnumerator ReloadCoroutine()
    {
        IsReloading = true;
        OnAmmoChanged?.Invoke();
        yield return new WaitForSeconds(reloadTime);
        CurrentAmmo = magazineSize;
        IsReloading = false;
        OnAmmoChanged?.Invoke();
    }
}
