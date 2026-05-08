using Photon.Pun;
using UnityEngine;

/// <summary>
/// Expanding ring shockwave spawned by the boss slam attack. Builds a hollow
/// torus out of cube segments so it looks like an actual ring, not a solid disc.
/// MasterClient checks for player collisions and deals damage.
/// Players can jump over it.
/// </summary>
public class BossShockwaveRing : MonoBehaviourPunCallbacks
{
    [Header("Ring Settings")]
    public float expandSpeed = 55f;
    public float maxRadius = 500f;
    public float ringHeight = 1.6f;
    public float ringThickness = 2f;
    public int damage = 15;
    public float lifetime = 14f;
    [Tooltip("Player feet must be below ringCenter.y + this value to be hit. Lower = easier to jump over.")]
    public float jumpDodgeHeight = 0.6f;

    [Header("Ring Visual")]
    public int segmentCount = 128;
    public float segmentHeight = 1.4f;

    private float _currentRadius;
    private float _spawnTime;
    private bool _consumed;
    private readonly System.Collections.Generic.HashSet<int> _hitPlayers = new();

    private Transform[] _segments;
    private Material _ringMat;

    private void Start()
    {
        _spawnTime = Time.time;
        _currentRadius = 0.5f;

        Renderer rootRenderer = GetComponent<Renderer>();
        if (rootRenderer != null) rootRenderer.enabled = false;

        _ringMat = MakeRingMaterial();
        _segments = new Transform[segmentCount];

        for (int i = 0; i < segmentCount; i++)
        {
            GameObject seg = GameObject.CreatePrimitive(PrimitiveType.Cube);
            seg.name = $"RingSeg_{i}";
            Object.Destroy(seg.GetComponent<Collider>());
            seg.transform.SetParent(transform, false);
            Renderer r = seg.GetComponent<Renderer>();
            if (r != null) r.sharedMaterial = _ringMat;
            _segments[i] = seg.transform;
        }

        LayoutSegments();
    }

    private void Update()
    {
        if (_consumed) return;

        _currentRadius += expandSpeed * Time.deltaTime;
        LayoutSegments();

        if (Time.time - _spawnTime >= lifetime || _currentRadius >= maxRadius)
        {
            DestroyRing();
            return;
        }

        if (!PhotonNetwork.IsMasterClient) return;

        float innerEdge = Mathf.Max(0f, _currentRadius - ringThickness);
        float outerEdge = _currentRadius;

        NetworkPlayerHealth[] players = FindObjectsByType<NetworkPlayerHealth>(FindObjectsSortMode.None);
        foreach (NetworkPlayerHealth player in players)
        {
            if (player == null || !player.IsAlive) continue;

            int actorId = player.GetComponent<PhotonView>().Owner.ActorNumber;
            if (_hitPlayers.Contains(actorId)) continue;

            Vector3 playerPos = player.transform.position;
            Vector3 ringCenter = transform.position;
            float horizDist = Vector2.Distance(
                new Vector2(playerPos.x, playerPos.z),
                new Vector2(ringCenter.x, ringCenter.z));

            bool inRingHorizontal = horizDist >= innerEdge && horizDist <= outerEdge;
            // Player feet must be near the ground -- jumping clears the ring
            bool nearGround = playerPos.y < ringCenter.y + jumpDodgeHeight;

            if (inRingHorizontal && nearGround)
            {
                _hitPlayers.Add(actorId);
                player.RequestDamageFromMaster(damage);
            }
        }
    }

    private void LayoutSegments()
    {
        if (_segments == null) return;
        float angleStep = 360f / segmentCount;
        float arcLength = 2f * Mathf.PI * _currentRadius / segmentCount;
        float segWidth = Mathf.Max(arcLength * 1.05f, 0.15f);

        for (int i = 0; i < segmentCount; i++)
        {
            float angle = angleStep * i * Mathf.Deg2Rad;
            float x = Mathf.Cos(angle) * _currentRadius;
            float z = Mathf.Sin(angle) * _currentRadius;
            _segments[i].localPosition = new Vector3(x, 0f, z);
            _segments[i].localRotation = Quaternion.Euler(0f, -angleStep * i, 0f);
            _segments[i].localScale = new Vector3(segWidth, segmentHeight, ringThickness);
        }
    }

    private void DestroyRing()
    {
        if (_consumed) return;
        _consumed = true;
        if (PhotonNetwork.IsMasterClient)
        {
            PhotonNetwork.Destroy(gameObject);
        }
    }

    private static Material MakeRingMaterial()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        Material m = new Material(shader);
        Color c = new Color(1f, 0.45f, 0.05f, 0.85f);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        return m;
    }
}
