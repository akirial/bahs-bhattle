using Photon.Pun;
using UnityEngine;

/// <summary>
/// Falling meteor spawned during the boss meteor barrage attack.
/// MasterClient drives the fall and checks for ground impact / AoE damage.
/// All clients see it via IPunObservable position sync.
/// </summary>
[RequireComponent(typeof(PhotonView))]
public class BossMeteor : MonoBehaviourPunCallbacks, IPunObservable
{
    [Header("Fall")]
    public float fallSpeed = 20f;
    public float groundY = 0.5f;
    public float maxLifetime = 6f;

    [Header("Damage")]
    public int damage = 20;
    public float damageRadius = 3f;

    private Vector3 _networkPosition;
    private Quaternion _networkRotation;
    private float _spawnTime;
    private bool _consumed;

    private void Start()
    {
        _spawnTime = Time.time;
        _networkPosition = transform.position;
        _networkRotation = transform.rotation;
    }

    private void Update()
    {
        if (!PhotonNetwork.IsMasterClient)
        {
            transform.position = Vector3.Lerp(transform.position, _networkPosition, Time.deltaTime * 18f);
            transform.rotation = Quaternion.Slerp(transform.rotation, _networkRotation, Time.deltaTime * 12f);
            return;
        }
        if (_consumed) return;

        transform.position += Vector3.down * (fallSpeed * Time.deltaTime);
        transform.Rotate(Vector3.up * (90f * Time.deltaTime), Space.World);

        if (transform.position.y <= groundY || Time.time - _spawnTime >= maxLifetime)
        {
            Impact();
        }
    }

    private void Impact()
    {
        if (_consumed) return;
        _consumed = true;

        Vector3 center = transform.position;
        center.y = Mathf.Max(center.y, groundY);

        NetworkPlayerHealth[] players = FindObjectsByType<NetworkPlayerHealth>(FindObjectsSortMode.None);
        foreach (NetworkPlayerHealth player in players)
        {
            if (player == null || !player.IsAlive) continue;
            Vector3 pp = player.transform.position;
            float dx = pp.x - center.x;
            float dz = pp.z - center.z;
            float dy = Mathf.Max(0f, pp.y - center.y);
            float horiz = Mathf.Sqrt(dx * dx + dz * dz);
            if (horiz <= damageRadius && dy < 3.5f)
            {
                player.RequestDamageFromMaster(damage);
            }
        }

        if (PhotonNetwork.IsMasterClient)
        {
            PhotonNetwork.Destroy(gameObject);
        }
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
