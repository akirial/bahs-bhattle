using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using UnityEngine.InputSystem;
using Hashtable = ExitGames.Client.Photon.Hashtable;

/// <summary>
/// PUN player health. Health is mirrored into the owning Player's custom
/// properties so every client can read it for UI/targeting. The MasterClient
/// decides damage and calls TakeDamageRpc on the victim owner.
/// </summary>
[RequireComponent(typeof(PhotonView))]
public class NetworkPlayerHealth : MonoBehaviourPunCallbacks
{
    public const string HealthKey = "HP";
    public const string AliveKey = "Alive";

    [Header("Health")]
    public int maxHealth = 100;

    [Header("Respawn")]
    public float respawnDelay = 10f;

    public event System.Action OnLocalDeath;
    public event System.Action OnLocalHealthChanged;
    public event System.Action OnLocalDamage;
    public event System.Action OnLocalRespawn;

    public int CurrentHealth => GetHealth(photonView.Owner, maxHealth);
    public bool IsAlive => GetAlive(photonView.Owner);
    public float DeathTime { get; private set; }
    public bool CanRespawn => !IsAlive && _localDeathHandled && Time.time - DeathTime >= respawnDelay;

    private bool _localDeathHandled;

    private void Start()
    {
        if (photonView.IsMine)
        {
            SetLocalHealth(maxHealth, true);
        }
    }

    public override void OnPlayerPropertiesUpdate(Player targetPlayer, Hashtable changedProps)
    {
        if (targetPlayer != photonView.Owner) return;
        if (changedProps.ContainsKey(HealthKey) || changedProps.ContainsKey(AliveKey))
        {
            if (photonView.IsMine) OnLocalHealthChanged?.Invoke();
        }
    }

    /// <summary>Called by the MasterClient to request damage on this player.</summary>
    public void RequestDamageFromMaster(int amount)
    {
        if (!PhotonNetwork.IsMasterClient) return;
        photonView.RPC(nameof(TakeDamageRpc), photonView.Owner, amount);
    }

    [PunRPC]
    private void TakeDamageRpc(int amount, PhotonMessageInfo info)
    {
        if (!photonView.IsMine) return;
        if (info.Sender != null && !info.Sender.IsMasterClient) return;
        if (!IsAlive || amount <= 0) return;

        int newHp = Mathf.Max(0, CurrentHealth - amount);
        SetLocalHealth(newHp, newHp > 0);
        OnLocalHealthChanged?.Invoke();
        OnLocalDamage?.Invoke();
        GameAudio.PlayUI(SfxId.PlayerHurt, 0.7f);

        if (newHp <= 0)
        {
            HandleLocalDeath();
            photonView.RPC(nameof(DieVisualRpc), RpcTarget.All);
        }
    }

    [PunRPC]
    private void DieVisualRpc()
    {
        var controller = GetComponent<NetworkPlayerController>();
        if (photonView.IsMine)
        {
            HandleLocalDeath();
        }
        else if (controller != null && controller.bodyVisual != null)
        {
            controller.bodyVisual.SetActive(false);
        }
    }

    private void HandleLocalDeath()
    {
        if (_localDeathHandled) return;
        _localDeathHandled = true;
        DeathTime = Time.time;

        var controller = GetComponent<NetworkPlayerController>();
        if (controller != null) controller.SetInputEnabled(false);

        var gun = GetComponent<NetworkGunController>();
        if (gun != null) gun.SetEnabledForOwner(false);

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        OnLocalDeath?.Invoke();
    }

    private void Update()
    {
        if (!photonView.IsMine) return;
        if (CanRespawn && Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame)
        {
            Respawn();
        }
    }

    public void Respawn()
    {
        if (!photonView.IsMine || IsAlive) return;
        if (Time.time - DeathTime < respawnDelay) return;

        _localDeathHandled = false;
        SetLocalHealth(maxHealth, true);

        Vector3 spawnPos = Vector3.up * 2f;
        if (ArenaBuilder.Instance != null)
        {
            int idx = photonView.Owner.ActorNumber - 1;
            Transform spawnPoint = ArenaBuilder.Instance.GetPlayerSpawn(Mathf.Max(0, idx));
            if (spawnPoint != null) spawnPos = spawnPoint.position;
        }

        transform.position = spawnPos;

        var controller = GetComponent<NetworkPlayerController>();
        if (controller != null)
        {
            controller.SetInputEnabled(true);
            if (controller.bodyVisual != null) controller.bodyVisual.SetActive(true);
        }

        var gun = GetComponent<NetworkGunController>();
        if (gun != null) gun.SetEnabledForOwner(true);

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        photonView.RPC(nameof(RespawnVisualRpc), RpcTarget.Others);
        OnLocalRespawn?.Invoke();
        OnLocalHealthChanged?.Invoke();
    }

    [PunRPC]
    private void RespawnVisualRpc()
    {
        var controller = GetComponent<NetworkPlayerController>();
        if (controller != null && controller.bodyVisual != null)
            controller.bodyVisual.SetActive(true);
    }

    private void SetLocalHealth(int hp, bool alive)
    {
        PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable
        {
            { HealthKey, hp },
            { AliveKey, alive }
        });
    }

    public static int GetHealth(Player player, int fallback = 100)
    {
        if (player == null) return fallback;
        if (player.CustomProperties.TryGetValue(HealthKey, out object raw) && raw is int hp)
            return hp;
        return fallback;
    }

    public static bool GetAlive(Player player)
    {
        if (player == null) return false;
        if (player.CustomProperties.TryGetValue(AliveKey, out object raw) && raw is bool alive)
            return alive;
        return true;
    }
}
