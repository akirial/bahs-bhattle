using System.Collections;
using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;

/// <summary>
/// Two-phase Dark Souls-style cube boss AI. MasterClient picks attacks
/// based on context (distance, last attack, player behavior), runs the
/// coroutine, and broadcasts visuals/damage to all clients via PunRPC.
/// Phase is read from NetworkBossHealth.Phase (1 -> 2 at 50% HP).
/// </summary>
[RequireComponent(typeof(NetworkBossHealth))]
[RequireComponent(typeof(BossCubeAnimator))]
public class NetworkBossAttack : MonoBehaviourPunCallbacks
{
    public enum BossAttack
    {
        FastBounceSlam,
        DelayedBounceSlam,
        BasicFaceLaser,
        CubeRollCharge,
        SimpleShockwaveSlam,
        MiniCubeSwarm,
        TripleHopCombo,
        SweepingFaceLaser,
        FakeHopIntoLaser,
        LaserFakeoutIntoSlam,
        CornerHammerEdgeLaser,
        AllFaceLaserBurst
    }

    [Header("Prefabs / Names")]
    public string shockwavePrefabName = "BossShockwaveRing";
    public string miniCubePrefabName = "BossMiniCube";

    [Header("Mini Cube Swarm (Phase 1)")]
    public float miniSwarmDuration = 40f;
    public float miniSwarmSpawnInterval = 1.5f;
    public int miniSwarmMinPerWave = 3;
    public int miniSwarmMaxPerWave = 5;
    public int miniSwarmMaxAlive = 100;
    public float miniSwarmSpawnMinRadius = 8f;
    public float miniSwarmSpawnMaxRadius = 20f;
    public Vector3 miniSwarmYellow = new Vector3(1f, 0.95f, 0.2f);

    [Header("General Timing")]
    public float phase1AttackInterval = 2.0f;
    public float phase2MinInterval = 0.7f;
    public float phase2MaxInterval = 1.1f;

    [Header("Distance Thresholds")]
    public float closeDistance = 30f;
    public float farDistance = 80f;

    [Header("Tracking")]
    public float rotateSpeedIdle = 3.5f;
    public float rotateSpeedTracking = 6f;
    [Tooltip("Laser tracking speed during fire phase. Higher = harder to dodge.")]
    public float trackingLaserSpeed = 2.5f;

    [Header("Boss Body Hitbox")]
    public float bodyHitRadius = 2.8f;

    [Header("Fakeouts (Phase 2)")]
    [Range(0f, 1f)] public float slamFakeoutChance = 0.30f;
    [Range(0f, 1f)] public float laserFakeoutChance = 0.25f;
    public float dodgeBonusFakeout = 0.15f;

    [Header("Combo Chaining (Phase 2)")]
    [Range(0f, 1f)] public float comboChance = 0.55f;
    [Range(0f, 1f)] public float tripleComboChance = 0.20f;

    [Header("AI tracking")]
    public float dodgeMemoryDuration = 4f;
    public float circleMemoryDuration = 3f;
    public float dodgeSpeedThreshold = 12f;

    [Header("Float / Ground Heights")]
    public float floatHeight = 8f;
    public float groundY = 2f;

    [Header("Laser")]
    public Color laserWarning = new Color(1f, 0.2f, 0f, 0.5f);
    public Color laserActive = new Color(1f, 0.35f, 0.05f, 1f);

    private NetworkBossHealth _health;
    private BossCubeAnimator _anim;
    private BossLaser _frontLaser;
    private Transform _laserAnchor;
    private BossVoiceManager _voice;

    private bool _isBusy;
    private bool _suppressTracking;
    private BossAttack _lastAttack;
    private float _attackTimer;
    private int _attackCount;
    private bool _hasLastAttack;

    private float _dodgeMemoryTimer;
    private float _circleMemoryTimer;
    private readonly Dictionary<int, Vector3> _playerLastPos = new();
    private readonly Dictionary<int, float> _playerCircleAngle = new();

    private NetworkPlayerHealth _currentTarget;
    private Vector3 _basePosition;

    private static readonly Vector3 BaseScaleRel = Vector3.one;
    private static readonly Vector3 SquashScaleRel = new Vector3(1.35f, 0.55f, 1.35f);
    private static readonly Vector3 LaunchScaleRel = new Vector3(0.7f, 1.45f, 0.7f);
    private static readonly Vector3 ImpactScaleRel = new Vector3(1.55f, 0.45f, 1.55f);
    private static readonly Vector3 OvershootScaleRel = new Vector3(0.85f, 1.2f, 0.85f);

    private void Awake()
    {
        _health = GetComponent<NetworkBossHealth>();
        _anim = GetComponent<BossCubeAnimator>();
        if (_anim == null) _anim = gameObject.AddComponent<BossCubeAnimator>();
        _voice = GetComponent<BossVoiceManager>();
        if (_voice == null) _voice = gameObject.AddComponent<BossVoiceManager>();
    }

    private bool _introPlayed;

    private void Start()
    {
        _basePosition = transform.position;
        if (Mathf.Abs(_basePosition.y - groundY) > 0.5f)
            _basePosition.y = floatHeight;

        GameObject anchor = new GameObject("LaserAnchor");
        anchor.transform.SetParent(transform, false);
        anchor.transform.localPosition = Vector3.zero;
        _laserAnchor = anchor.transform;
        _frontLaser = anchor.AddComponent<BossLaser>();
        _frontLaser.warningColor = laserWarning;
        _frontLaser.activeColor = laserActive;
        _frontLaser.ConfigureOrigin(Vector3.zero);
        _frontLaser.Hide();
    }

    private void Update()
    {
        if (!_suppressTracking && _health != null && _health.IsAlive && !_health.IsPhaseTransitioning)
        {
            RotateTowardsClosestPlayer(_isBusy ? rotateSpeedIdle * 0.4f : rotateSpeedIdle);
        }

        if (!PhotonNetwork.IsMasterClient) return;
        if (!PhotonNetwork.InRoom) return;
        if (_health == null || !_health.IsAlive) return;
        if (_health.IsPhaseTransitioning) { _attackTimer = 0f; return; }
        if (_isBusy) return;

        UpdateAIMemory();

        _attackTimer += Time.deltaTime;
        float interval = _health.Phase >= 2
            ? Random.Range(phase2MinInterval, phase2MaxInterval)
            : phase1AttackInterval;
        if (_attackTimer < interval) return;
        _attackTimer = 0f;
        _attackCount++;

        StartCoroutine(RunAttack());
    }

    // ---------------------------- AI selection ----------------------------

    private IEnumerator RunAttack()
    {
        _isBusy = true;

        if (!_introPlayed)
        {
            _introPlayed = true;
            if (_voice != null) _voice.PlayVoice(BossVoiceCategory.Intro);
            yield return new WaitForSeconds(1.2f);
        }

        BossAttack chosen = PickAttack();
        chosen = MaybeFakeout(chosen);

        yield return RunSpecificAttack(chosen);
        BossAttack just = chosen;
        _lastAttack = just;
        _hasLastAttack = true;

        if (_health.Phase >= 2 && Random.value < comboChance)
        {
            BossAttack? combo = PickCombo(just);
            if (combo.HasValue)
            {
                BossAttack next = MaybeFakeout(combo.Value);
                yield return RunSpecificAttack(next);
                _lastAttack = next;

                if (Random.value < tripleComboChance)
                {
                    BossAttack? triple = PickCombo(next);
                    if (triple.HasValue)
                    {
                        BossAttack third = MaybeFakeout(triple.Value);
                        yield return RunSpecificAttack(third);
                        _lastAttack = third;
                    }
                }
            }
        }

        _isBusy = false;
    }

    private IEnumerator RunSpecificAttack(BossAttack a)
    {
        switch (a)
        {
            case BossAttack.FastBounceSlam:        yield return FastBounceSlam(); break;
            case BossAttack.DelayedBounceSlam:     yield return DelayedBounceSlam(); break;
            case BossAttack.BasicFaceLaser:        yield return BasicFaceLaser(); break;
            case BossAttack.CubeRollCharge:        yield return CubeRollCharge(); break;
            case BossAttack.SimpleShockwaveSlam:   yield return SimpleShockwaveSlam(); break;
            case BossAttack.MiniCubeSwarm:         yield return MiniCubeSwarm(); break;
            case BossAttack.TripleHopCombo:        yield return TripleHopCombo(); break;
            case BossAttack.SweepingFaceLaser:     yield return SweepingFaceLaser(); break;
            case BossAttack.FakeHopIntoLaser:      yield return FakeHopIntoLaser(); break;
            case BossAttack.LaserFakeoutIntoSlam:  yield return LaserFakeoutIntoSlam(); break;
            case BossAttack.CornerHammerEdgeLaser: yield return CornerHammerEdgeLaser(); break;
            case BossAttack.AllFaceLaserBurst:     yield return AllFaceLaserBurst(); break;
        }
    }

    private BossAttack PickAttack()
    {
        NetworkPlayerHealth target = GetClosestLivingPlayer();
        _currentTarget = target;
        float dist = target != null ? Vector3.Distance(transform.position, target.transform.position) : closeDistance + 1f;
        bool playerClose = dist < closeDistance;
        bool playerFar = dist > farDistance;
        bool dodgedRecently = _dodgeMemoryTimer > 0f;
        bool circling = _circleMemoryTimer > 0f;
        int phase = _health.Phase;

        List<(BossAttack atk, float weight)> options = new List<(BossAttack, float)>();

        options.Add((BossAttack.FastBounceSlam,      playerClose ? 3f : 1f));
        options.Add((BossAttack.DelayedBounceSlam,   dodgedRecently ? 3.5f : 1.5f));
        options.Add((BossAttack.BasicFaceLaser,      playerFar ? 3f : 1.2f));
        options.Add((BossAttack.CubeRollCharge,      playerFar ? 3f : 1f));
        options.Add((BossAttack.SimpleShockwaveSlam, playerClose ? 2f : 1f));
        if (phase < 2 && _attackCount > 2)
            options.Add((BossAttack.MiniCubeSwarm, 1.5f));

        if (phase >= 2)
        {
            options.Add((BossAttack.TripleHopCombo,       playerClose ? 3.5f : 2.0f));
            options.Add((BossAttack.SweepingFaceLaser,    circling ? 5f : (playerFar ? 3.5f : 2.0f)));
            options.Add((BossAttack.FakeHopIntoLaser,     dodgedRecently ? 2.5f : 1.2f));
            options.Add((BossAttack.LaserFakeoutIntoSlam, dodgedRecently ? 2.2f : 1.0f));
            options.Add((BossAttack.CornerHammerEdgeLaser, playerClose ? 2.5f : 1.2f));

            if (_attackCount > 3)
                options.Add((BossAttack.AllFaceLaserBurst, 1.5f));
        }

        if (_hasLastAttack)
        {
            for (int i = 0; i < options.Count; i++)
            {
                if (options[i].atk == _lastAttack) options[i] = (options[i].atk, 0f);
            }
        }

        float total = 0f;
        foreach (var o in options) total += o.weight;
        if (total <= 0f) return BossAttack.FastBounceSlam;
        float roll = Random.Range(0f, total);
        float acc = 0f;
        foreach (var o in options)
        {
            acc += o.weight;
            if (roll <= acc) return o.atk;
        }
        return options[0].atk;
    }

    private BossAttack MaybeFakeout(BossAttack chosen)
    {
        if (_health.Phase < 2) return chosen;
        float bonus = _dodgeMemoryTimer > 0f ? dodgeBonusFakeout : 0f;

        if (chosen == BossAttack.FastBounceSlam || chosen == BossAttack.SimpleShockwaveSlam)
        {
            if (Random.value < slamFakeoutChance + bonus)
                return BossAttack.FakeHopIntoLaser;
        }
        if (chosen == BossAttack.BasicFaceLaser)
        {
            if (Random.value < laserFakeoutChance + bonus)
                return BossAttack.LaserFakeoutIntoSlam;
        }
        return chosen;
    }

    private BossAttack? PickCombo(BossAttack just)
    {
        switch (just)
        {
            case BossAttack.TripleHopCombo:         return BossAttack.SweepingFaceLaser;
            case BossAttack.DelayedBounceSlam:      return BossAttack.BasicFaceLaser;
            case BossAttack.FastBounceSlam:          return BossAttack.CubeRollCharge;
            case BossAttack.BasicFaceLaser:          return BossAttack.FastBounceSlam;
            case BossAttack.SweepingFaceLaser:       return BossAttack.CubeRollCharge;
            case BossAttack.SimpleShockwaveSlam:     return BossAttack.BasicFaceLaser;
            case BossAttack.FakeHopIntoLaser:        return BossAttack.FastBounceSlam;
            case BossAttack.LaserFakeoutIntoSlam:    return null;
            case BossAttack.CornerHammerEdgeLaser:   return null;
            case BossAttack.AllFaceLaserBurst:       return null;
            case BossAttack.CubeRollCharge:
                NetworkPlayerHealth t = GetClosestLivingPlayer();
                float d = t != null ? Vector3.Distance(transform.position, t.transform.position) : 0f;
                return d > farDistance * 0.6f ? BossAttack.FastBounceSlam : BossAttack.BasicFaceLaser;
        }
        return null;
    }

    private void UpdateAIMemory()
    {
        if (_dodgeMemoryTimer > 0f) _dodgeMemoryTimer -= Time.deltaTime;
        if (_circleMemoryTimer > 0f) _circleMemoryTimer -= Time.deltaTime;

        NetworkPlayerHealth[] players = FindObjectsByType<NetworkPlayerHealth>(FindObjectsSortMode.None);
        Vector3 bossPos = transform.position;
        foreach (NetworkPlayerHealth p in players)
        {
            if (p == null || !p.IsAlive) continue;
            int id = p.GetComponent<PhotonView>().Owner.ActorNumber;
            Vector3 pos = p.transform.position;
            if (_playerLastPos.TryGetValue(id, out Vector3 prev))
            {
                Vector3 delta = pos - prev;
                float dt = Mathf.Max(0.0001f, Time.deltaTime);
                float speed = delta.magnitude / dt;
                if (speed > dodgeSpeedThreshold)
                {
                    _dodgeMemoryTimer = dodgeMemoryDuration;
                }
                Vector2 prevDir = new Vector2(prev.x - bossPos.x, prev.z - bossPos.z);
                Vector2 nowDir = new Vector2(pos.x - bossPos.x, pos.z - bossPos.z);
                if (prevDir.sqrMagnitude > 4f && nowDir.sqrMagnitude > 4f)
                {
                    float angDelta = Vector2.SignedAngle(prevDir, nowDir);
                    float prevAng = _playerCircleAngle.TryGetValue(id, out float a) ? a : 0f;
                    float accum = prevAng * 0.92f + angDelta;
                    _playerCircleAngle[id] = accum;
                    if (Mathf.Abs(accum) > 80f) _circleMemoryTimer = circleMemoryDuration;
                }
            }
            _playerLastPos[id] = pos;
        }
    }

    // ---------------------------- Helpers ----------------------------

    private NetworkPlayerHealth GetClosestLivingPlayer()
    {
        NetworkPlayerHealth[] players = FindObjectsByType<NetworkPlayerHealth>(FindObjectsSortMode.None);
        NetworkPlayerHealth best = null;
        float bestDist = float.MaxValue;
        foreach (NetworkPlayerHealth p in players)
        {
            if (p == null || !p.IsAlive) continue;
            float d = Vector3.Distance(transform.position, p.transform.position);
            if (d < bestDist) { bestDist = d; best = p; }
        }
        return best;
    }

    private NetworkPlayerHealth GetRandomLivingPlayer()
    {
        if (NetworkGameManager.Instance != null)
            return NetworkGameManager.Instance.GetRandomLivingPlayer();
        return GetClosestLivingPlayer();
    }

    private void RotateTowardsClosestPlayer(float speed)
    {
        NetworkPlayerHealth t = GetClosestLivingPlayer();
        if (t == null) return;
        Vector3 dir = t.transform.position - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.01f) return;
        Quaternion target = Quaternion.LookRotation(dir);
        transform.rotation = Quaternion.Slerp(transform.rotation, target, Time.deltaTime * speed);
    }

    private void DoBodyDamage(int damage, Vector3 center, float radius, HashSet<int> alreadyHit = null)
    {
        if (!PhotonNetwork.IsMasterClient) return;
        bool hitAny = false;
        Collider[] hits = Physics.OverlapSphere(center, radius, ~0, QueryTriggerInteraction.Ignore);
        foreach (Collider c in hits)
        {
            NetworkPlayerHealth p = c.GetComponentInParent<NetworkPlayerHealth>();
            if (p == null || !p.IsAlive) continue;
            int id = p.GetComponent<PhotonView>().Owner.ActorNumber;
            if (alreadyHit != null)
            {
                if (alreadyHit.Contains(id)) continue;
                alreadyHit.Add(id);
            }
            p.RequestDamageFromMaster(damage);
            hitAny = true;
        }
        if (hitAny && _voice != null)
            _voice.PlayVoice(BossVoiceCategory.PlayerHit);
    }

    private void SpawnShockwave(Vector3 pos, int damage, float maxRadius, float expandSpeed)
    {
        if (!PhotonNetwork.IsMasterClient) return;
        GameObject go = PhotonNetwork.InstantiateRoomObject(shockwavePrefabName, pos, Quaternion.identity);
        if (go != null)
        {
            BossShockwaveRing ring = go.GetComponent<BossShockwaveRing>();
            if (ring != null)
            {
                ring.damage = damage;
                ring.maxRadius = maxRadius;
                ring.expandSpeed = expandSpeed;
            }
        }
    }

    [PunRPC]
    private void LaserShowWarningRpc(Vector3 localDir)
    {
        if (_frontLaser != null) _frontLaser.ShowWarning(localDir);
    }

    [PunRPC]
    private void LaserFireRpc(Vector3 localDir)
    {
        if (_frontLaser != null) _frontLaser.Fire(localDir);
    }

    [PunRPC]
    private void LaserHideRpc()
    {
        if (_frontLaser != null) _frontLaser.Hide();
    }

    [PunRPC]
    private void AllFaceLaserBurstRpc(float duration)
    {
        StartCoroutine(SpawnLocalAllFaceBeams(duration));
    }

    [PunRPC]
    private void PlaySfxRpc(int sfxId, Vector3 pos)
    {
        GameAudio.Play((SfxId)sfxId, pos);
    }

    private void PlaySfx(SfxId id, Vector3 pos)
    {
        photonView.RPC(nameof(PlaySfxRpc), RpcTarget.All, (int)id, pos);
    }

    private IEnumerator SpawnLocalAllFaceBeams(float duration)
    {
        Vector3[] dirs = {
            Vector3.forward, Vector3.back, Vector3.left, Vector3.right, Vector3.up, Vector3.down
        };
        List<GameObject> beams = new List<GameObject>(6);
        Material mat = null;
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        mat = new Material(shader);
        Color c = laserActive;
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);

        foreach (Vector3 d in dirs)
        {
            GameObject g = new GameObject("AllFaceBeam");
            g.transform.SetParent(transform, false);
            LineRenderer lr = g.AddComponent<LineRenderer>();
            lr.material = mat;
            lr.useWorldSpace = false;
            lr.positionCount = 2;
            lr.SetPosition(0, Vector3.zero);
            lr.SetPosition(1, d * 200f);
            lr.startWidth = lr.endWidth = 1.2f;
            lr.startColor = lr.endColor = c;
            beams.Add(g);
        }

        yield return new WaitForSeconds(duration);

        foreach (GameObject g in beams) if (g != null) Object.Destroy(g);
    }

    // ============================================================
    // PHASE 1 ATTACKS
    // ============================================================

    private IEnumerator FastBounceSlam()
    {
        _suppressTracking = false;
        Vector3 startPos = transform.position;
        Vector3 jumpTarget = GetTargetGroundLockedPos(startPos, 7f);
        Vector3 apex = jumpTarget + Vector3.up * 6f;

        _anim.photonView.RPC(nameof(BossCubeAnimator.AnimateScaleRpc), RpcTarget.All, SquashScaleRel, 0.18f, true);
        if (_voice != null) _voice.PlayVoice(BossVoiceCategory.Slam);
        yield return new WaitForSeconds(0.18f);

        _suppressTracking = true;
        _anim.photonView.RPC(nameof(BossCubeAnimator.AnimateScaleRpc), RpcTarget.All, LaunchScaleRel, 0.28f, true);
        _anim.photonView.RPC(nameof(BossCubeAnimator.MoveToRpc), RpcTarget.All, apex, 0.28f, 4);
        PlaySfx(SfxId.BossHop, transform.position);
        yield return new WaitForSeconds(0.28f);

        Vector3 ground = new Vector3(jumpTarget.x, groundY, jumpTarget.z);
        _anim.photonView.RPC(nameof(BossCubeAnimator.MoveToRpc), RpcTarget.All, ground, 0.22f, 2);
        yield return new WaitForSeconds(0.22f);

        _anim.photonView.RPC(nameof(BossCubeAnimator.BounceImpactRpc), RpcTarget.All,
            ImpactScaleRel, 0.08f, OvershootScaleRel, 0.14f, 0.18f);
        DoBodyDamage(20, ground, bodyHitRadius);
        SpawnShockwave(new Vector3(ground.x, 0.1f, ground.z), 15, 120f, 50f);
        PlaySfx(SfxId.BossSlam, ground);
        PlaySfx(SfxId.Shockwave, ground);
        yield return new WaitForSeconds(0.4f);

        Vector3 returnPos = new Vector3(ground.x, floatHeight, ground.z);
        _anim.photonView.RPC(nameof(BossCubeAnimator.MoveToRpc), RpcTarget.All, returnPos, 0.45f, 4);
        yield return new WaitForSeconds(0.5f);
        _suppressTracking = false;
    }

    private IEnumerator DelayedBounceSlam()
    {
        Vector3 startPos = transform.position;
        Vector3 jumpTarget = GetTargetGroundLockedPos(startPos, 8f);
        Vector3 apex = jumpTarget + Vector3.up * 7f;

        _anim.photonView.RPC(nameof(BossCubeAnimator.AnimateScaleRpc), RpcTarget.All, SquashScaleRel, 0.18f, true);
        yield return new WaitForSeconds(0.18f);

        _suppressTracking = true;
        _anim.photonView.RPC(nameof(BossCubeAnimator.AnimateScaleRpc), RpcTarget.All, LaunchScaleRel, 0.32f, true);
        _anim.photonView.RPC(nameof(BossCubeAnimator.MoveToRpc), RpcTarget.All, apex, 0.32f, 4);
        PlaySfx(SfxId.BossHop, transform.position);
        yield return new WaitForSeconds(0.32f);

        if (_voice != null) _voice.PlayVoice(BossVoiceCategory.DelayedFakeout);
        yield return new WaitForSeconds(0.8f);

        jumpTarget = GetTargetGroundLockedPos(transform.position, 7f);

        Vector3 ground = new Vector3(jumpTarget.x, groundY, jumpTarget.z);
        _anim.photonView.RPC(nameof(BossCubeAnimator.MoveToRpc), RpcTarget.All, ground, 0.22f, 2);
        yield return new WaitForSeconds(0.22f);

        _anim.photonView.RPC(nameof(BossCubeAnimator.BounceImpactRpc), RpcTarget.All,
            ImpactScaleRel, 0.08f, OvershootScaleRel, 0.16f, 0.2f);
        DoBodyDamage(25, ground, bodyHitRadius + 0.5f);
        SpawnShockwave(new Vector3(ground.x, 0.1f, ground.z), 20, 160f, 55f);
        PlaySfx(SfxId.BossSlam, ground);
        PlaySfx(SfxId.Shockwave, ground);
        yield return new WaitForSeconds(0.45f);

        Vector3 returnPos = new Vector3(ground.x, floatHeight, ground.z);
        _anim.photonView.RPC(nameof(BossCubeAnimator.MoveToRpc), RpcTarget.All, returnPos, 0.5f, 4);
        yield return new WaitForSeconds(0.6f);
        _suppressTracking = false;
    }

    private IEnumerator BasicFaceLaser()
    {
        NetworkPlayerHealth target = GetClosestLivingPlayer();
        if (target == null) { yield break; }

        _suppressTracking = true;
        float t = 0f;
        while (t < 0.5f && target != null && target.IsAlive)
        {
            Vector3 d = target.transform.position - transform.position;
            if (d.sqrMagnitude > 0.01f)
            {
                Quaternion q = Quaternion.LookRotation(d);
                transform.rotation = Quaternion.Slerp(transform.rotation, q, Time.deltaTime * rotateSpeedTracking * 1.5f);
            }
            t += Time.deltaTime;
            yield return null;
        }

        Vector3 laserDir = GetLaserDirToTarget(target);

        bool isPhase2 = _health.Phase >= 2;
        float chargeDur = isPhase2 ? 0.7f : 0.5f;

        photonView.RPC(nameof(LaserShowWarningRpc), RpcTarget.All, laserDir);
        _anim.photonView.RPC(nameof(BossCubeAnimator.PulseGlowRpc), RpcTarget.All, new Vector3(1f, 0.2f, 0.0f), chargeDur, false);
        PlaySfx(SfxId.LaserCharge, transform.position);
        if (_voice != null) _voice.PlayVoice(BossVoiceCategory.Laser);
        yield return new WaitForSeconds(chargeDur);

        photonView.RPC(nameof(LaserFireRpc), RpcTarget.All, laserDir);
        PlaySfx(SfxId.LaserFire, transform.position);
        float fireDur = isPhase2 ? 1.4f : 0.6f;
        float laserTrackSpeed = isPhase2 ? trackingLaserSpeed * 1.3f : trackingLaserSpeed;
        float elapsed = 0f;
        while (elapsed < fireDur)
        {
            elapsed += Time.deltaTime;
            target = GetClosestLivingPlayer();
            if (target != null && target.IsAlive)
            {
                Vector3 d = target.transform.position - transform.position;
                if (d.sqrMagnitude > 0.01f)
                {
                    Quaternion q = Quaternion.LookRotation(d);
                    transform.rotation = Quaternion.Slerp(transform.rotation, q, Time.deltaTime * laserTrackSpeed);
                }
            }
            if (PhotonNetwork.IsMasterClient && _frontLaser != null)
                _frontLaser.TickDamage(20, 0.3f, 1.8f);
            yield return null;
        }

        photonView.RPC(nameof(LaserHideRpc), RpcTarget.All);
        _anim.photonView.RPC(nameof(BossCubeAnimator.StopGlowRpc), RpcTarget.All);

        ResetToUprightRotation(0.3f);
        yield return new WaitForSeconds(0.4f);
        _suppressTracking = false;
    }

    private IEnumerator CubeRollCharge()
    {
        NetworkPlayerHealth target = GetClosestLivingPlayer();
        if (target == null) { yield break; }

        if (_voice != null) _voice.PlayVoice(BossVoiceCategory.Roll);
        _suppressTracking = true;

        _anim.photonView.RPC(nameof(BossCubeAnimator.AnimateScaleRpc), RpcTarget.All,
            new Vector3(0.85f, 1.1f, 1.3f), 0.4f, true);

        Vector3 startGround = new Vector3(transform.position.x, groundY + 0.5f, transform.position.z);
        _anim.photonView.RPC(nameof(BossCubeAnimator.MoveToRpc), RpcTarget.All, startGround, 0.4f, 2);
        yield return new WaitForSeconds(0.4f);

        Vector3 dir = target.transform.position - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.01f) dir = transform.forward;
        dir.Normalize();

        _anim.photonView.RPC(nameof(BossCubeAnimator.AnimateScaleRpc), RpcTarget.All, BaseScaleRel, 0.15f, false);
        PlaySfx(SfxId.BossCharge, transform.position);

        float rollSpeed = 22f;
        float rollDuration = 1.5f;
        HashSet<int> alreadyHit = new HashSet<int>();
        float rollElapsed = 0f;
        bool stopped = false;
        while (rollElapsed < rollDuration && !stopped)
        {
            float dt = Time.deltaTime;
            rollElapsed += dt;
            Vector3 step = dir * rollSpeed * dt;
            if (Physics.Raycast(transform.position, dir, out RaycastHit wallHit, step.magnitude + 2.5f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (wallHit.collider.GetComponentInParent<NetworkPlayerHealth>() == null)
                {
                    stopped = true;
                }
            }
            transform.position = transform.position + step;
            Vector3 axis = Vector3.Cross(Vector3.up, dir).normalized;
            transform.Rotate(axis, rollSpeed * dt * 12f, Space.World);
            DoBodyDamage(25, transform.position, bodyHitRadius, alreadyHit);
            yield return null;
        }

        SpawnShockwave(new Vector3(transform.position.x, 0.1f, transform.position.z), 15, 90f, 45f);
        _anim.photonView.RPC(nameof(BossCubeAnimator.BounceImpactRpc), RpcTarget.All,
            ImpactScaleRel, 0.1f, OvershootScaleRel, 0.15f, 0.18f);
        PlaySfx(SfxId.BossSlam, transform.position);
        PlaySfx(SfxId.Shockwave, transform.position);
        yield return new WaitForSeconds(0.2f);

        _anim.photonView.RPC(nameof(BossCubeAnimator.ResetScaleRpc), RpcTarget.All, 0.5f);
        Vector3 returnPos = new Vector3(transform.position.x, floatHeight, transform.position.z);
        _anim.photonView.RPC(nameof(BossCubeAnimator.MoveToRpc), RpcTarget.All, returnPos, 0.6f, 1);
        yield return new WaitForSeconds(1.2f);
        _suppressTracking = false;
    }

    private IEnumerator SimpleShockwaveSlam()
    {
        Vector3 startPos = transform.position;
        Vector3 apex = startPos + Vector3.up * 4f;
        Vector3 ground = new Vector3(startPos.x, groundY, startPos.z);

        _suppressTracking = true;
        _anim.photonView.RPC(nameof(BossCubeAnimator.AnimateScaleRpc), RpcTarget.All, SquashScaleRel, 0.16f, true);
        _anim.photonView.RPC(nameof(BossCubeAnimator.MoveToRpc), RpcTarget.All, apex, 0.32f, 4);
        PlaySfx(SfxId.BossHop, transform.position);
        if (_voice != null) _voice.PlayVoice(BossVoiceCategory.Slam);
        yield return new WaitForSeconds(0.5f);

        _anim.photonView.RPC(nameof(BossCubeAnimator.MoveToRpc), RpcTarget.All, ground, 0.25f, 2);
        yield return new WaitForSeconds(0.25f);

        _anim.photonView.RPC(nameof(BossCubeAnimator.BounceImpactRpc), RpcTarget.All,
            ImpactScaleRel, 0.1f, OvershootScaleRel, 0.16f, 0.2f);
        SpawnShockwave(new Vector3(ground.x, 0.1f, ground.z), 15, 140f, 48f);
        PlaySfx(SfxId.BossSlam, ground);
        PlaySfx(SfxId.Shockwave, ground);
        yield return new WaitForSeconds(0.45f);

        Vector3 returnPos = new Vector3(ground.x, floatHeight, ground.z);
        _anim.photonView.RPC(nameof(BossCubeAnimator.MoveToRpc), RpcTarget.All, returnPos, 0.5f, 4);
        yield return new WaitForSeconds(0.7f);
        _suppressTracking = false;
    }

    private IEnumerator MiniCubeSwarm()
    {
        // Phase 1 only (safety)
        if (_health != null && _health.Phase >= 2) yield break;

        _suppressTracking = true;

        if (_voice != null) _voice.PlayVoice(BossVoiceCategory.BigLaser);

        if (_health != null) _health.Invulnerable = true;
        if (_anim != null)
        {
            _anim.photonView.RPC(nameof(BossCubeAnimator.AnimateScaleRpc), RpcTarget.All, new Vector3(2.5f, 2.5f, 2.5f), 1.5f, true);
            _anim.photonView.RPC(nameof(BossCubeAnimator.SetGlowRpc), RpcTarget.All, miniSwarmYellow);
            _anim.photonView.RPC(nameof(BossCubeAnimator.ShowShieldRpc), RpcTarget.All);
        }

        // brief windup
        yield return new WaitForSeconds(1.5f);

        float endTime = Time.time + Mathf.Max(1f, miniSwarmDuration);
        float nextSpawn = Time.time;
        while (Time.time < endTime && _health != null && _health.IsAlive)
        {
            if (PhotonNetwork.IsMasterClient && Time.time >= nextSpawn)
            {
                nextSpawn = Time.time + Mathf.Max(0.2f, miniSwarmSpawnInterval);

                // cap alive cubes for performance
                int alive = FindObjectsByType<BossMiniCube>(FindObjectsSortMode.None).Length;
                if (alive < miniSwarmMaxAlive && !string.IsNullOrEmpty(miniCubePrefabName))
                {
                    int toSpawn = Random.Range(miniSwarmMinPerWave, miniSwarmMaxPerWave + 1);
                    int allowed = Mathf.Max(0, miniSwarmMaxAlive - alive);
                    toSpawn = Mathf.Min(toSpawn, allowed);

                    for (int i = 0; i < toSpawn; i++)
                    {
                        float a = Random.Range(0f, 360f) * Mathf.Deg2Rad;
                        float r = Random.Range(miniSwarmSpawnMinRadius, miniSwarmSpawnMaxRadius);
                        Vector3 offset = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
                        // Spawn near ground so they don't hover above players.
                        Vector3 pos = transform.position + offset + Vector3.up * Random.Range(0.6f, 1.2f);
                        PhotonNetwork.InstantiateRoomObject(miniCubePrefabName, pos, Random.rotation);
                    }
                }
            }

            yield return null;
        }

        if (_anim != null)
        {
            _anim.photonView.RPC(nameof(BossCubeAnimator.HideShieldRpc), RpcTarget.All);
            _anim.photonView.RPC(nameof(BossCubeAnimator.StopGlowRpc), RpcTarget.All);
            _anim.photonView.RPC(nameof(BossCubeAnimator.AnimateScaleRpc), RpcTarget.All, Vector3.one, 1.0f, true);
        }
        if (_health != null) _health.Invulnerable = false;

        yield return new WaitForSeconds(1.0f);
        _suppressTracking = false;
    }

    // ============================================================
    // PHASE 2 ATTACKS
    // ============================================================

    private IEnumerator TripleHopCombo()
    {
        if (_voice != null) _voice.PlayVoice(BossVoiceCategory.Slam);
        int hopCount = _health.Phase >= 2 ? 5 : 3;

        for (int i = 0; i < hopCount; i++)
        {
            NetworkPlayerHealth target = GetClosestLivingPlayer();
            if (target == null) yield break;

            float windup, hangTime, dropDur;
            int dmg, shockDmg;
            float shockRadius;

            if (hopCount == 5)
            {
                if (i <= 1)      { windup = 0.18f; hangTime = 0f;   dropDur = 0.15f; dmg = 10; shockDmg = 8;  shockRadius = 60f; }
                else if (i == 2) { windup = 0.25f; hangTime = 0.3f; dropDur = 0.18f; dmg = 18; shockDmg = 15; shockRadius = 100f; }
                else if (i == 3) { windup = 0.28f; hangTime = 0.5f; dropDur = 0.16f; dmg = 25; shockDmg = 20; shockRadius = 130f; }
                else             { windup = 0.22f; hangTime = 0.6f; dropDur = 0.13f; dmg = 35; shockDmg = 30; shockRadius = 160f; }
            }
            else
            {
                if (i == 0)      { windup = 0.22f; hangTime = 0f;   dropDur = 0.18f; dmg = 12; shockDmg = 10; shockRadius = 70f; }
                else if (i == 1) { windup = 0.3f;  hangTime = 0.3f; dropDur = 0.18f; dmg = 18; shockDmg = 15; shockRadius = 100f; }
                else             { windup = 0.22f; hangTime = 0f;   dropDur = 0.15f; dmg = 28; shockDmg = 25; shockRadius = 140f; }
            }

            _suppressTracking = true;
            Vector3 jumpTarget = GetTargetGroundLockedPos(transform.position, 6f);
            float apexHeight = (hopCount == 5 && i == 4) ? 9f : (i >= 2 ? 7f : 5f);
            Vector3 apex = jumpTarget + Vector3.up * apexHeight;

            _anim.photonView.RPC(nameof(BossCubeAnimator.AnimateScaleRpc), RpcTarget.All, SquashScaleRel, windup * 0.4f, true);
            yield return new WaitForSeconds(windup * 0.4f);

            _anim.photonView.RPC(nameof(BossCubeAnimator.AnimateScaleRpc), RpcTarget.All, LaunchScaleRel, windup * 0.6f, true);
            _anim.photonView.RPC(nameof(BossCubeAnimator.MoveToRpc), RpcTarget.All, apex, windup * 0.6f, 4);
            PlaySfx(SfxId.BossHop, transform.position);
            yield return new WaitForSeconds(windup * 0.6f);

            if (hangTime > 0f)
            {
                if (hopCount == 5 && i == 4)
                    jumpTarget = GetTargetGroundLockedPos(transform.position, 4f);
                yield return new WaitForSeconds(hangTime);
            }

            Vector3 ground = new Vector3(jumpTarget.x, groundY, jumpTarget.z);
            _anim.photonView.RPC(nameof(BossCubeAnimator.MoveToRpc), RpcTarget.All, ground, dropDur, 2);
            yield return new WaitForSeconds(dropDur);

            _anim.photonView.RPC(nameof(BossCubeAnimator.BounceImpactRpc), RpcTarget.All,
                ImpactScaleRel, 0.07f, OvershootScaleRel, 0.13f, 0.13f);
            DoBodyDamage(dmg, ground, bodyHitRadius);
            SpawnShockwave(new Vector3(ground.x, 0.1f, ground.z), shockDmg, shockRadius, 48f);
            PlaySfx(SfxId.BossSlam, ground);
            PlaySfx(SfxId.Shockwave, ground);
            yield return new WaitForSeconds(0.14f);
        }

        Vector3 returnPos = new Vector3(transform.position.x, floatHeight, transform.position.z);
        _anim.photonView.RPC(nameof(BossCubeAnimator.MoveToRpc), RpcTarget.All, returnPos, 0.55f, 4);
        yield return new WaitForSeconds(1.0f);
        _suppressTracking = false;
    }

    private IEnumerator SweepingFaceLaser()
    {
        NetworkPlayerHealth target = GetClosestLivingPlayer();
        if (target == null) yield break;

        _suppressTracking = true;

        Vector3 d = target.transform.position - transform.position;
        if (d.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(d);

        Vector3 laserDir = GetLaserDirToTarget(target);

        _anim.photonView.RPC(nameof(BossCubeAnimator.PulseGlowRpc), RpcTarget.All, new Vector3(1f, 0.15f, 0f), 0.6f, false);
        photonView.RPC(nameof(LaserShowWarningRpc), RpcTarget.All, laserDir);
        PlaySfx(SfxId.LaserCharge, transform.position);
        if (_voice != null) _voice.PlayVoice(BossVoiceCategory.BigLaser);
        yield return new WaitForSeconds(0.6f);

        photonView.RPC(nameof(LaserFireRpc), RpcTarget.All, laserDir);
        PlaySfx(SfxId.LaserFire, transform.position);

        float sweepDuration = 3.0f;
        float elapsed = 0f;
        while (elapsed < sweepDuration)
        {
            elapsed += Time.deltaTime;
            target = GetClosestLivingPlayer();
            if (target != null && target.IsAlive)
            {
                Vector3 toTarget = target.transform.position - transform.position;
                if (toTarget.sqrMagnitude > 0.01f)
                {
                    Quaternion desired = Quaternion.LookRotation(toTarget);
                    transform.rotation = Quaternion.Slerp(transform.rotation, desired, Time.deltaTime * trackingLaserSpeed * 0.8f);
                }
            }
            if (PhotonNetwork.IsMasterClient && _frontLaser != null)
                _frontLaser.TickDamage(22, 0.4f, 1.8f);
            yield return null;
        }

        photonView.RPC(nameof(LaserHideRpc), RpcTarget.All);
        _anim.photonView.RPC(nameof(BossCubeAnimator.StopGlowRpc), RpcTarget.All);

        ResetToUprightRotation(0.4f);
        yield return new WaitForSeconds(0.8f);
        _suppressTracking = false;
    }

    private IEnumerator FakeHopIntoLaser()
    {
        NetworkPlayerHealth target = GetClosestLivingPlayer();
        if (target == null) yield break;

        Vector3 startPos = transform.position;
        _suppressTracking = true;
        _anim.photonView.RPC(nameof(BossCubeAnimator.AnimateScaleRpc), RpcTarget.All, SquashScaleRel, 0.18f, true);
        yield return new WaitForSeconds(0.18f);

        Vector3 apex = new Vector3(startPos.x, floatHeight + 6f, startPos.z);
        _anim.photonView.RPC(nameof(BossCubeAnimator.AnimateScaleRpc), RpcTarget.All, LaunchScaleRel, 0.32f, true);
        _anim.photonView.RPC(nameof(BossCubeAnimator.MoveToRpc), RpcTarget.All, apex, 0.32f, 4);
        PlaySfx(SfxId.BossHop, transform.position);
        yield return new WaitForSeconds(0.32f);

        yield return new WaitForSeconds(0.5f);

        if (_voice != null) _voice.PlayVoice(BossVoiceCategory.Fakeout);

        _anim.photonView.RPC(nameof(BossCubeAnimator.PulseGlowRpc), RpcTarget.All, new Vector3(1f, 0.2f, 0f), 0.4f, true);
        photonView.RPC(nameof(LaserShowWarningRpc), RpcTarget.All, Vector3.down);
        PlaySfx(SfxId.LaserCharge, transform.position);
        yield return new WaitForSeconds(0.4f);

        photonView.RPC(nameof(LaserFireRpc), RpcTarget.All, Vector3.down);
        PlaySfx(SfxId.LaserFire, transform.position);
        float fireDur = 0.5f;
        float elapsed = 0f;
        while (elapsed < fireDur)
        {
            elapsed += Time.deltaTime;
            if (PhotonNetwork.IsMasterClient && _frontLaser != null)
                _frontLaser.TickDamage(25, 0.3f, 1.6f);
            yield return null;
        }

        photonView.RPC(nameof(LaserHideRpc), RpcTarget.All);
        _anim.photonView.RPC(nameof(BossCubeAnimator.StopGlowRpc), RpcTarget.All);

        Vector3 returnPos = new Vector3(startPos.x, floatHeight, startPos.z);
        _anim.photonView.RPC(nameof(BossCubeAnimator.MoveToRpc), RpcTarget.All, returnPos, 0.4f, 2);
        _anim.photonView.RPC(nameof(BossCubeAnimator.ResetScaleRpc), RpcTarget.All, 0.4f);
        yield return new WaitForSeconds(0.5f);
        _suppressTracking = false;
    }

    private IEnumerator LaserFakeoutIntoSlam()
    {
        NetworkPlayerHealth target = GetClosestLivingPlayer();
        if (target == null) yield break;

        _suppressTracking = true;

        Vector3 d = target.transform.position - transform.position;
        if (d.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(d);

        Vector3 laserDir = GetLaserDirToTarget(target);
        _anim.photonView.RPC(nameof(BossCubeAnimator.PulseGlowRpc), RpcTarget.All, new Vector3(1f, 0.2f, 0f), 1.1f, true);
        photonView.RPC(nameof(LaserShowWarningRpc), RpcTarget.All, laserDir);
        PlaySfx(SfxId.LaserCharge, transform.position);
        yield return new WaitForSeconds(1.1f);

        photonView.RPC(nameof(LaserHideRpc), RpcTarget.All);
        _anim.photonView.RPC(nameof(BossCubeAnimator.StopGlowRpc), RpcTarget.All);

        if (_voice != null) _voice.PlayVoice(BossVoiceCategory.Fakeout);
        yield return new WaitForSeconds(0.08f);

        _anim.photonView.RPC(nameof(BossCubeAnimator.AnimateScaleRpc), RpcTarget.All, SquashScaleRel, 0.08f, false);
        yield return new WaitForSeconds(0.08f);

        Vector3 jumpTarget = GetTargetGroundLockedPos(transform.position, 7f);
        Vector3 apex = jumpTarget + Vector3.up * 4f;
        _anim.photonView.RPC(nameof(BossCubeAnimator.MoveToRpc), RpcTarget.All, apex, 0.18f, 4);
        _anim.photonView.RPC(nameof(BossCubeAnimator.AnimateScaleRpc), RpcTarget.All, LaunchScaleRel, 0.18f, true);
        PlaySfx(SfxId.BossHop, transform.position);
        yield return new WaitForSeconds(0.18f);

        Vector3 ground = new Vector3(jumpTarget.x, groundY, jumpTarget.z);
        _anim.photonView.RPC(nameof(BossCubeAnimator.MoveToRpc), RpcTarget.All, ground, 0.22f, 2);
        yield return new WaitForSeconds(0.22f);

        _anim.photonView.RPC(nameof(BossCubeAnimator.BounceImpactRpc), RpcTarget.All,
            ImpactScaleRel, 0.08f, OvershootScaleRel, 0.14f, 0.18f);
        DoBodyDamage(25, ground, bodyHitRadius);
        SpawnShockwave(new Vector3(ground.x, 0.1f, ground.z), 20, 140f, 50f);
        PlaySfx(SfxId.BossSlam, ground);
        PlaySfx(SfxId.Shockwave, ground);
        yield return new WaitForSeconds(0.4f);

        Vector3 returnPos = new Vector3(ground.x, floatHeight, ground.z);
        _anim.photonView.RPC(nameof(BossCubeAnimator.MoveToRpc), RpcTarget.All, returnPos, 0.5f, 4);
        yield return new WaitForSeconds(0.5f);
        _suppressTracking = false;
    }

    private IEnumerator CornerHammerEdgeLaser()
    {
        NetworkPlayerHealth target = GetClosestLivingPlayer();
        if (target == null) yield break;

        _suppressTracking = true;

        Quaternion startRot = transform.rotation;
        Quaternion tilted = startRot * Quaternion.Euler(35f, 0f, 35f);
        float elapsed = 0f;
        while (elapsed < 0.8f)
        {
            elapsed += Time.deltaTime;
            transform.rotation = Quaternion.Slerp(startRot, tilted, Mathf.SmoothStep(0f, 1f, elapsed / 0.8f));
            yield return null;
        }
        transform.rotation = tilted;

        _anim.photonView.RPC(nameof(BossCubeAnimator.PulseGlowRpc), RpcTarget.All, new Vector3(1f, 0.15f, 0f), 0.4f, false);
        if (_voice != null) _voice.PlayVoice(BossVoiceCategory.Laser);
        yield return new WaitForSeconds(0.4f);

        Vector3 ground = new Vector3(transform.position.x, groundY, transform.position.z);
        _anim.photonView.RPC(nameof(BossCubeAnimator.MoveToRpc), RpcTarget.All, ground, 0.20f, 2);
        yield return new WaitForSeconds(0.20f);

        _anim.photonView.RPC(nameof(BossCubeAnimator.BounceImpactRpc), RpcTarget.All,
            ImpactScaleRel, 0.08f, OvershootScaleRel, 0.14f, 0.18f);
        DoBodyDamage(25, ground, bodyHitRadius + 0.5f);
        SpawnShockwave(new Vector3(ground.x, 0.1f, ground.z), 15, 100f, 48f);
        PlaySfx(SfxId.BossSlam, ground);
        PlaySfx(SfxId.Shockwave, ground);

        Vector3 edgeLaserDir = GetLaserDirToTarget(GetClosestLivingPlayer());
        photonView.RPC(nameof(LaserShowWarningRpc), RpcTarget.All, edgeLaserDir);
        PlaySfx(SfxId.LaserCharge, transform.position);
        yield return new WaitForSeconds(0.3f);
        photonView.RPC(nameof(LaserFireRpc), RpcTarget.All, edgeLaserDir);
        PlaySfx(SfxId.LaserFire, transform.position);

        float spinDur = 0.9f;
        elapsed = 0f;
        while (elapsed < spinDur)
        {
            elapsed += Time.deltaTime;
            target = GetClosestLivingPlayer();
            if (target != null)
            {
                Vector3 toTarget = target.transform.position - transform.position;
                toTarget.y = 0f;
                if (toTarget.sqrMagnitude > 0.01f)
                {
                    Quaternion desired = Quaternion.LookRotation(toTarget) * Quaternion.Euler(35f, 0f, 35f);
                    transform.rotation = Quaternion.Slerp(transform.rotation, desired, Time.deltaTime * 4f);
                }
            }
            if (PhotonNetwork.IsMasterClient && _frontLaser != null)
                _frontLaser.TickDamage(20, 0.4f, 1.3f);
            yield return null;
        }

        photonView.RPC(nameof(LaserHideRpc), RpcTarget.All);
        _anim.photonView.RPC(nameof(BossCubeAnimator.StopGlowRpc), RpcTarget.All);

        elapsed = 0f;
        Quaternion recoverStart = transform.rotation;
        Quaternion upright;
        NetworkPlayerHealth t2 = GetClosestLivingPlayer();
        if (t2 != null)
        {
            Vector3 dirFwd = t2.transform.position - transform.position; dirFwd.y = 0f;
            upright = dirFwd.sqrMagnitude > 0.01f ? Quaternion.LookRotation(dirFwd) : Quaternion.identity;
        }
        else upright = Quaternion.identity;
        while (elapsed < 0.4f)
        {
            elapsed += Time.deltaTime;
            transform.rotation = Quaternion.Slerp(recoverStart, upright, elapsed / 0.4f);
            yield return null;
        }
        transform.rotation = upright;

        Vector3 returnPos = new Vector3(transform.position.x, floatHeight, transform.position.z);
        _anim.photonView.RPC(nameof(BossCubeAnimator.MoveToRpc), RpcTarget.All, returnPos, 0.5f, 1);
        yield return new WaitForSeconds(1.0f);
        _suppressTracking = false;
    }

    private IEnumerator AllFaceLaserBurst()
    {
        _suppressTracking = true;
        bool isPhase2 = _health.Phase >= 2;

        Vector3 startPos = transform.position;
        Vector3 burstPos = new Vector3(startPos.x, floatHeight + 4f, startPos.z);
        _anim.photonView.RPC(nameof(BossCubeAnimator.MoveToRpc), RpcTarget.All, burstPos, 0.5f, 3);
        yield return new WaitForSeconds(0.5f);

        _anim.photonView.RPC(nameof(BossCubeAnimator.PulseGlowRpc), RpcTarget.All, new Vector3(1f, 0.3f, 0f), 1.0f, false);
        PlaySfx(SfxId.LaserCharge, transform.position);
        if (_voice != null) _voice.PlayVoice(BossVoiceCategory.BigLaser);
        yield return new WaitForSeconds(1.0f);

        float fireDur = isPhase2 ? 3.5f : 2.5f;
        float chargeSpeed = isPhase2 ? 12f : 0f;
        float spinSpeed = 540f;
        float damageInterval = 0.25f;

        photonView.RPC(nameof(AllFaceLaserBurstRpc), RpcTarget.All, fireDur);
        PlaySfx(SfxId.LaserFire, transform.position);

        if (PhotonNetwork.IsMasterClient)
        {
            float elapsed = 0f;
            float damageTimer = 0f;
            HashSet<int> hitThisCast = new HashSet<int>();
            while (elapsed < fireDur)
            {
                float dt = Time.deltaTime;
                elapsed += dt;

                transform.Rotate(Vector3.up, spinSpeed * dt, Space.World);

                if (chargeSpeed > 0f)
                {
                    NetworkPlayerHealth chargeTarget = GetClosestLivingPlayer();
                    if (chargeTarget != null)
                    {
                        Vector3 toTarget = chargeTarget.transform.position - transform.position;
                        toTarget.y = 0f;
                        if (toTarget.sqrMagnitude > 1f)
                        {
                            Vector3 moveStep = toTarget.normalized * chargeSpeed * dt;
                            transform.position += moveStep;
                        }
                    }
                }

                damageTimer -= dt;
                if (damageTimer <= 0f)
                {
                    damageTimer = damageInterval;
                    hitThisCast.Clear();
                    Vector3[] dirs = {
                        transform.forward, -transform.forward,
                        transform.right, -transform.right,
                        transform.up, -transform.up
                    };
                    foreach (Vector3 dd in dirs)
                    {
                        if (Physics.SphereCast(transform.position, 1.4f, dd, out RaycastHit hit, 200f, ~0, QueryTriggerInteraction.Ignore))
                        {
                            NetworkPlayerHealth p = hit.collider.GetComponentInParent<NetworkPlayerHealth>();
                            if (p == null || !p.IsAlive) continue;
                            int id = p.GetComponent<PhotonView>().Owner.ActorNumber;
                            if (hitThisCast.Contains(id)) continue;
                            hitThisCast.Add(id);
                            p.RequestDamageFromMaster(30);
                        }
                    }
                }
                yield return null;
            }
        }
        else
        {
            float elapsed = 0f;
            while (elapsed < fireDur)
            {
                elapsed += Time.deltaTime;
                transform.Rotate(Vector3.up, spinSpeed * Time.deltaTime, Space.World);
                if (chargeSpeed > 0f)
                {
                    NetworkPlayerHealth chargeTarget = GetClosestLivingPlayer();
                    if (chargeTarget != null)
                    {
                        Vector3 toTarget = chargeTarget.transform.position - transform.position;
                        toTarget.y = 0f;
                        if (toTarget.sqrMagnitude > 1f)
                            transform.position += toTarget.normalized * chargeSpeed * Time.deltaTime;
                    }
                }
                yield return null;
            }
        }

        _anim.photonView.RPC(nameof(BossCubeAnimator.StopGlowRpc), RpcTarget.All);

        Vector3 crashPos = new Vector3(transform.position.x, groundY, transform.position.z);
        _anim.photonView.RPC(nameof(BossCubeAnimator.MoveToRpc), RpcTarget.All, crashPos, 0.3f, 2);
        yield return new WaitForSeconds(0.3f);

        _anim.photonView.RPC(nameof(BossCubeAnimator.BounceImpactRpc), RpcTarget.All,
            ImpactScaleRel, 0.1f, OvershootScaleRel, 0.15f, 0.2f);
        DoBodyDamage(25, crashPos, bodyHitRadius + 1f);
        SpawnShockwave(new Vector3(crashPos.x, 0.1f, crashPos.z), 20, 160f, 55f);
        PlaySfx(SfxId.BossSlam, crashPos);
        PlaySfx(SfxId.Shockwave, crashPos);
        yield return new WaitForSeconds(0.4f);

        Vector3 returnPos = new Vector3(transform.position.x, floatHeight, transform.position.z);
        _anim.photonView.RPC(nameof(BossCubeAnimator.MoveToRpc), RpcTarget.All, returnPos, 0.6f, 1);
        yield return new WaitForSeconds(1.2f);
        _suppressTracking = false;
    }

    // ---------------------------- Helpers ----------------------------

    private Vector3 GetLaserDirToTarget(NetworkPlayerHealth target)
    {
        if (target == null) return (Vector3.forward + Vector3.down).normalized;
        Vector3 worldDir = (target.transform.position - transform.position).normalized;
        Vector3 localDir = _laserAnchor.InverseTransformDirection(worldDir);
        if (localDir.sqrMagnitude < 0.0001f) localDir = Vector3.forward;
        return localDir.normalized;
    }

    private void ResetToUprightRotation(float duration)
    {
        StartCoroutine(UprightRoutine(duration));
    }

    private IEnumerator UprightRoutine(float duration)
    {
        Quaternion start = transform.rotation;
        Vector3 fwd = transform.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.001f) fwd = Vector3.forward;
        Quaternion upright = Quaternion.LookRotation(fwd.normalized, Vector3.up);
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(0.01f, duration);
            transform.rotation = Quaternion.Slerp(start, upright, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t)));
            yield return null;
        }
        transform.rotation = upright;
    }

    private Vector3 GetTargetGroundLockedPos(Vector3 fallback, float maxOffsetFromTarget)
    {
        NetworkPlayerHealth t = GetClosestLivingPlayer();
        if (t == null) return new Vector3(fallback.x, groundY, fallback.z);
        Vector3 p = t.transform.position;
        if (maxOffsetFromTarget > 0f)
        {
            Vector2 jitter = Random.insideUnitCircle * maxOffsetFromTarget;
            p.x += jitter.x;
            p.z += jitter.y;
        }
        return new Vector3(p.x, groundY, p.z);
    }
}
