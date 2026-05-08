using UnityEngine;

/// <summary>
/// Local-only laser beam visual + damage helper. The boss attack script owns
/// one of these as a child object and toggles it via RPCs to all clients.
/// MasterClient additionally calls TickDamage to do raycast damage.
/// Not a networked object -- visuals run on every client and damage is
/// authoritative on the MasterClient only.
/// </summary>
public class BossLaser : MonoBehaviour
{
    public LineRenderer line;
    public float maxLength = 1000f;

    [Header("Visual")]
    public float warningWidth = 0.25f;
    public float activeWidth = 1.4f;
    public Color warningColor = new Color(1f, 0.2f, 0f, 0.55f);
    public Color activeColor = new Color(1f, 0.35f, 0.05f, 1f);

    private Material _runtimeMat;
    private Vector3 _localOriginOffset;
    private Vector3 _localDirection = Vector3.forward;
    private bool _isActive;
    private float _damageTimer;

    public bool IsActive => _isActive;
    public Vector3 WorldOrigin => transform.TransformPoint(_localOriginOffset);
    public Vector3 WorldDirection => transform.TransformDirection(_localDirection).normalized;

    private void Awake()
    {
        if (line == null)
        {
            line = gameObject.AddComponent<LineRenderer>();
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        _runtimeMat = new Material(shader);
        line.material = _runtimeMat;
        line.useWorldSpace = false;
        line.positionCount = 2;
        line.numCapVertices = 4;
        line.numCornerVertices = 4;
        line.SetPosition(0, Vector3.zero);
        line.SetPosition(1, Vector3.forward * maxLength);
        line.startWidth = 0f;
        line.endWidth = 0f;
        line.enabled = false;
    }

    public void ConfigureOrigin(Vector3 localOriginOffset)
    {
        _localOriginOffset = localOriginOffset;
        transform.localPosition = _localOriginOffset;
    }

    /// <summary>Show a thin warning beam in the local +Z direction.</summary>
    public void ShowWarning(Vector3 localDir)
    {
        SetDirection(localDir);
        line.enabled = true;
        line.startWidth = warningWidth;
        line.endWidth = warningWidth;
        SetColor(warningColor);
        _isActive = false;
    }

    /// <summary>Fire the damaging beam in the local +Z direction.</summary>
    public void Fire(Vector3 localDir)
    {
        SetDirection(localDir);
        line.enabled = true;
        line.startWidth = activeWidth;
        line.endWidth = activeWidth;
        SetColor(activeColor);
        _isActive = true;
        _damageTimer = 0f;
    }

    public void Hide()
    {
        line.enabled = false;
        _isActive = false;
    }

    private void SetDirection(Vector3 localDir)
    {
        if (localDir.sqrMagnitude < 0.0001f) localDir = Vector3.forward;
        _localDirection = localDir.normalized;
        line.SetPosition(0, Vector3.zero);
        line.SetPosition(1, _localDirection * maxLength);
    }

    private void SetColor(Color c)
    {
        if (_runtimeMat != null)
        {
            if (_runtimeMat.HasProperty("_BaseColor")) _runtimeMat.SetColor("_BaseColor", c);
            if (_runtimeMat.HasProperty("_Color")) _runtimeMat.SetColor("_Color", c);
        }
        line.startColor = c;
        line.endColor = c;
    }

    /// <summary>
    /// MasterClient-only: each frame while the laser is active, raycast and
    /// damage the first player hit. Damage is rate-limited per-player by the
    /// caller using the returned tick timer; here we just enforce a global
    /// interval to avoid per-frame ticks.
    /// </summary>
    public void TickDamage(int damage, float interval, float beamRadius)
    {
        if (!_isActive) return;
        _damageTimer -= Time.deltaTime;
        if (_damageTimer > 0f) return;
        _damageTimer = interval;

        Vector3 origin = WorldOrigin;
        Vector3 dir = WorldDirection;
        if (Physics.SphereCast(origin, beamRadius, dir, out RaycastHit hit, maxLength, ~0, QueryTriggerInteraction.Ignore))
        {
            NetworkPlayerHealth player = hit.collider.GetComponentInParent<NetworkPlayerHealth>();
            if (player != null && player.IsAlive)
            {
                player.RequestDamageFromMaster(damage);
            }
        }
    }
}
