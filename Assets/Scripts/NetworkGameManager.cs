using System.Collections.Generic;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using Hashtable = ExitGames.Client.Photon.Hashtable;

/// <summary>
/// Photon room coordinator. PUN 2 has no dedicated server, so the MasterClient
/// acts as the authoritative host for boss spawn, boss HP, boss attacks, and
/// projectile damage decisions. Boss HP scales with player count.
/// </summary>
public class NetworkGameManager : MonoBehaviourPunCallbacks
{
    public const string BossHealthKey = "BossHP";
    public const string BossAliveKey = "BossAlive";
    public const string BossDefeatedKey = "BossDefeated";

    public static NetworkGameManager Instance { get; private set; }
    public static event System.Action OnBossDefeatedEvent;

    [Header("Resources Prefab Names")]
    public string playerPrefabName = "Player";
    public string bossPrefabName = "Boss";

    [Header("Defaults")]
    public int bossBaseHealth = 3000;
    [Tooltip("Extra HP added per player beyond the first.")]
    public int bossHealthPerExtraPlayer = 1000;

    private bool _localPlayerSpawned;
    private bool _gameStarted;

    public int ScaledBossHealth
    {
        get
        {
            int playerCount = PhotonNetwork.InRoom ? PhotonNetwork.PlayerList.Length : 1;
            return bossBaseHealth + bossHealthPerExtraPlayer * Mathf.Max(0, playerCount - 1);
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public override void OnJoinedRoom()
    {
        if (PhotonNetwork.InRoom
            && PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(MultiplayerMenuUI.GameStartedKey, out object val)
            && val is bool started && started)
        {
            _gameStarted = true;
            SpawnLocalPlayer();
            if (PhotonNetwork.IsMasterClient)
            {
                EnsureRoomState();
                EnsureBossSpawned();
            }
        }
    }

    public override void OnMasterClientSwitched(Player newMasterClient)
    {
        if (PhotonNetwork.IsMasterClient && _gameStarted)
        {
            EnsureRoomState();
            EnsureBossSpawned();
        }
    }

    public override void OnRoomPropertiesUpdate(Hashtable propertiesThatChanged)
    {
        if (propertiesThatChanged.ContainsKey(BossDefeatedKey)
            && TryGetBool(BossDefeatedKey, out bool defeated)
            && defeated)
        {
            OnBossDefeatedEvent?.Invoke();
        }
    }

    /// <summary>
    /// Called by MultiplayerMenuUI when all players are ready and host clicks Start.
    /// </summary>
    public void StartGame()
    {
        _gameStarted = true;
        if (PhotonNetwork.IsMasterClient)
        {
            EnsureRoomState();
            EnsureBossSpawned();
        }
    }

    public void SpawnLocalPlayer()
    {
        if (_localPlayerSpawned || !PhotonNetwork.InRoom) return;

        GetSpawnFor(PhotonNetwork.LocalPlayer.ActorNumber, out Vector3 pos, out Quaternion rot);
        PhotonNetwork.Instantiate(playerPrefabName, pos, rot);
        _localPlayerSpawned = true;
    }

    public void EnsureRoomState()
    {
        if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient) return;

        Hashtable props = new Hashtable();
        if (!PhotonNetwork.CurrentRoom.CustomProperties.ContainsKey(BossHealthKey))
            props[BossHealthKey] = ScaledBossHealth;
        if (!PhotonNetwork.CurrentRoom.CustomProperties.ContainsKey(BossAliveKey))
            props[BossAliveKey] = true;
        if (!PhotonNetwork.CurrentRoom.CustomProperties.ContainsKey(BossDefeatedKey))
            props[BossDefeatedKey] = false;

        if (props.Count > 0)
            PhotonNetwork.CurrentRoom.SetCustomProperties(props);
    }

    public void EnsureBossSpawned()
    {
        if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient) return;
        if (FindFirstObjectByType<NetworkBossHealth>() != null) return;

        Vector3 pos = Vector3.zero;
        Quaternion rot = Quaternion.identity;
        if (ArenaBuilder.Instance != null && ArenaBuilder.Instance.BossSpawnPoint != null)
        {
            pos = ArenaBuilder.Instance.BossSpawnPoint.position;
            rot = ArenaBuilder.Instance.BossSpawnPoint.rotation;
        }

        PhotonNetwork.InstantiateRoomObject(bossPrefabName, pos, rot);
    }

    public void OnBossDefeated()
    {
        if (!PhotonNetwork.IsMasterClient) return;
        PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable
        {
            { BossAliveKey, false },
            { BossDefeatedKey, true }
        });
        OnBossDefeatedEvent?.Invoke();
    }

    public NetworkPlayerHealth GetRandomLivingPlayer()
    {
        NetworkPlayerHealth[] allPlayers = FindObjectsByType<NetworkPlayerHealth>(FindObjectsSortMode.None);
        List<NetworkPlayerHealth> living = new List<NetworkPlayerHealth>(allPlayers.Length);
        foreach (NetworkPlayerHealth player in allPlayers)
        {
            if (player != null && player.IsAlive)
                living.Add(player);
        }
        if (living.Count == 0) return null;
        return living[Random.Range(0, living.Count)];
    }

    public void GetSpawnFor(int actorNumber, out Vector3 position, out Quaternion rotation)
    {
        position = Vector3.up;
        rotation = Quaternion.identity;
        if (ArenaBuilder.Instance == null) return;

        Transform spawn = ArenaBuilder.Instance.GetPlayerSpawn(Mathf.Max(0, actorNumber - 1));
        if (spawn == null) return;
        position = spawn.position;
        rotation = spawn.rotation;
    }

    public int GetBossHealth()
    {
        if (!PhotonNetwork.InRoom) return ScaledBossHealth;
        object value;
        return PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(BossHealthKey, out value) ? (int)value : ScaledBossHealth;
    }

    public bool IsBossAlive()
    {
        if (!PhotonNetwork.InRoom) return true;
        return TryGetBool(BossAliveKey, out bool alive) ? alive : true;
    }

    private static bool TryGetBool(string key, out bool value)
    {
        value = false;
        if (!PhotonNetwork.InRoom) return false;
        if (!PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(key, out object raw)) return false;
        if (raw is bool b)
        {
            value = b;
            return true;
        }
        return false;
    }
}
