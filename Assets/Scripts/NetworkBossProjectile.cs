using Photon.Pun;
using UnityEngine;

/// <summary>
/// MasterClient-controlled Photon projectile. Position is synced by
/// PhotonTransformView on the prefab; damage and destroy happen only on Master.
/// </summary>
[RequireComponent(typeof(PhotonView))]
public class NetworkBossProjectile : MonoBehaviourPunCallbacks, IPunObservable
{
    public float speed = 10f;
    public int damage = 10;
    public float lifetime = 5f;
    public float overlapRadius = 0.4f;

    private Vector3 _direction = Vector3.forward;
    private Vector3 _networkPosition;
    private Quaternion _networkRotation;
    private float _spawnTime;
    private bool _consumed;

    private void Start()
    {
        _spawnTime = Time.time;
        object[] data = photonView.InstantiationData;
        if (data != null && data.Length > 0 && data[0] is Vector3 dir && dir.sqrMagnitude > 0.001f)
        {
            _direction = dir.normalized;
            transform.rotation = Quaternion.LookRotation(_direction);
        }
        else
        {
            _direction = transform.forward;
        }
        _networkPosition = transform.position;
        _networkRotation = transform.rotation;
    }

    private void Update()
    {
        if (!PhotonNetwork.IsMasterClient)
        {
            transform.position = Vector3.Lerp(transform.position, _networkPosition, Time.deltaTime * 16f);
            transform.rotation = Quaternion.Slerp(transform.rotation, _networkRotation, Time.deltaTime * 16f);
            return;
        }
        if (_consumed) return;

        transform.position += _direction * (speed * Time.deltaTime);

        if (Time.time - _spawnTime >= lifetime)
        {
            DestroyProjectile();
            return;
        }

        Collider[] hits = Physics.OverlapSphere(transform.position, overlapRadius);
        foreach (Collider hit in hits)
        {
            if (hit == null) continue;
            if (hit.transform.IsChildOf(transform) || transform.IsChildOf(hit.transform)) continue;

            NetworkPlayerHealth player = hit.GetComponentInParent<NetworkPlayerHealth>();
            if (player != null && player.IsAlive)
            {
                player.RequestDamageFromMaster(damage);
                DestroyProjectile();
                return;
            }
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!PhotonNetwork.IsMasterClient) return;
        if (_consumed) return;

        NetworkPlayerHealth player = other.GetComponentInParent<NetworkPlayerHealth>();
        if (player != null && player.IsAlive)
        {
            player.RequestDamageFromMaster(damage);
            DestroyProjectile();
        }
    }

    private void DestroyProjectile()
    {
        if (_consumed) return;
        _consumed = true;
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
