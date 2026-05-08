using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// First-person PUN player controller. Only the owner reads input and owns the
/// camera/audio listener. Remote players are moved by PhotonTransformView.
/// </summary>
[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(PhotonView))]
public class NetworkPlayerController : MonoBehaviourPunCallbacks, IPunObservable
{
    [Header("References")]
    public Transform cameraPivot;
    public Camera playerCamera;
    public AudioListener playerAudioListener;
    public GameObject bodyVisual;

    [Header("Movement")]
    public float moveSpeed = 16f;
    public float gravity = -20f;
    public float jumpHeight = 2.0f;

    [Header("Sprint")]
    public float sprintMultiplier = 1.8f;

    [Header("Slide")]
    public float slideSpeed = 14f;
    public float slideDuration = 0.8f;
    public float slideHeight = 1.0f;
    public float slideFriction = 12f;
    public float slideCameraDrop = 1.1f;

    [Header("Dash")]
    public float dashSpeed = 34f;
    public float dashDuration = 0.24f;
    public float dashCooldown = 0.9f;
    public float dashFovBonus = 18f;

    [Header("FOV")]
    [Tooltip("Extra FOV added while sprinting.")]
    public float sprintFovBonus = 8f;
    [Tooltip("Extra FOV added while sliding (on top of sprint).")]
    public float slideFovBonus = 14f;
    [Tooltip("How fast FOV lerps toward its target.")]
    public float fovLerpSpeed = 8f;

    [Header("Mantle")]
    public float mantleReach = 2.2f;
    public float mantleForwardDist = 0.8f;
    public float mantleCooldown = 0.25f;

    [Header("Look")]
    public float mouseSensitivity = 0.15f;
    public static float SensitivityOverride { get; set; } = -1f;
    public float minPitch = -85f;
    public float maxPitch = 85f;

    private CharacterController _controller;
    private float _pitch;
    private Vector3 _velocity;
    private bool _inputEnabled = true;
    private Keyboard _kb;
    private Mouse _mouse;
    private Vector3 _networkPosition;
    private Quaternion _networkRotation;

    private bool _isSprinting;
    private bool _isSliding;
    private float _slideTimer;
    private Vector3 _slideVelocity;
    private float _defaultHeight;
    private Vector3 _defaultCenter;
    private float _defaultCameraY;
    private float _mantleCooldownTimer;
    private float _baseFov;
    private bool _isDashing;
    private float _dashTimer;
    private float _dashCooldownTimer;
    private Vector3 _dashDirection;

    private Canvas _nameTagCanvas;
    private Text _nameTagText;
    private Camera _nameTagCamera;
    private int _appliedColorIndex = -1;

    public bool HasInput => photonView != null && photonView.IsMine && _inputEnabled;

    public void SetInputEnabled(bool enabled) { _inputEnabled = enabled; }

    private void Awake()
    {
        _controller = GetComponent<CharacterController>();
        _defaultHeight = _controller.height;
        _defaultCenter = _controller.center;
    }

    private void Start()
    {
        bool isMine = photonView.IsMine;
        _networkPosition = transform.position;
        _networkRotation = transform.rotation;
        if (playerCamera != null) playerCamera.enabled = isMine;
        if (playerAudioListener != null) playerAudioListener.enabled = isMine;

        ApplyColorFromOwnerProperties();
        if (!isMine) EnsureNameTag();

        if (isMine)
        {
            if (bodyVisual != null) bodyVisual.SetActive(false);
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            _kb = Keyboard.current;
            _mouse = Mouse.current;
            if (cameraPivot != null) _defaultCameraY = cameraPivot.localPosition.y;
            if (playerCamera != null)
            {
                playerCamera.farClipPlane = 50000f;
                _baseFov = playerCamera.fieldOfView;
            }
        }
    }

    private void OnDestroy()
    {
        if (photonView != null && photonView.IsMine)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    private void Update()
    {
        if (!photonView.IsMine)
        {
            transform.position = Vector3.Lerp(transform.position, _networkPosition, Time.deltaTime * 12f);
            transform.rotation = Quaternion.Slerp(transform.rotation, _networkRotation, Time.deltaTime * 12f);
            UpdateNameTagBillboard();
            return;
        }
        if (!_inputEnabled)
        {
            ApplyGravityOnly();
            return;
        }

        HandleLook();
        HandleMovement();
    }

    public override void OnPlayerPropertiesUpdate(Player targetPlayer, ExitGames.Client.Photon.Hashtable changedProps)
    {
        if (targetPlayer != photonView.Owner) return;
        if (changedProps != null && changedProps.ContainsKey(MultiplayerMenuUI.ColorKey))
            ApplyColorFromOwnerProperties();
    }

    private void ApplyColorFromOwnerProperties()
    {
        if (bodyVisual == null) return;
        MeshRenderer r = bodyVisual.GetComponent<MeshRenderer>();
        if (r == null) return;

        int idx = 0;
        if (photonView.Owner != null
            && photonView.Owner.CustomProperties.TryGetValue(MultiplayerMenuUI.ColorKey, out object raw)
            && raw is int ci)
        {
            idx = Mathf.Clamp(ci, 0, 7);
        }

        if (idx == _appliedColorIndex) return;
        _appliedColorIndex = idx;

        Color c = GetPaletteColor(idx);
        Material m = r.material; // instance per-player
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Color")) m.SetColor("_Color", c);
    }

    private static Color GetPaletteColor(int idx)
    {
        return idx switch
        {
            0 => new Color(0.95f, 0.2f, 0.2f),
            1 => new Color(0.2f, 0.5f, 0.95f),
            2 => new Color(0.2f, 0.85f, 0.35f),
            3 => new Color(1f, 0.9f, 0.2f),
            4 => new Color(0.7f, 0.25f, 0.95f),
            5 => new Color(1f, 0.55f, 0.15f),
            6 => new Color(0.2f, 0.95f, 0.95f),
            _ => Color.white
        };
    }

    private void EnsureNameTag()
    {
        if (_nameTagCanvas != null) return;
        if (bodyVisual == null) return;

        GameObject root = new GameObject("NameTag");
        root.transform.SetParent(transform, false);
        root.transform.localPosition = new Vector3(0f, 3.2f, 0f);

        _nameTagCanvas = root.AddComponent<Canvas>();
        _nameTagCanvas.renderMode = RenderMode.WorldSpace;
        _nameTagCanvas.worldCamera = Camera.main;
        _nameTagCanvas.sortingOrder = 200;

        RectTransform rt = _nameTagCanvas.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(2.2f, 0.5f);
        rt.localScale = Vector3.one * 0.01f;

        GameObject textGo = new GameObject("Text");
        textGo.transform.SetParent(root.transform, false);
        _nameTagText = textGo.AddComponent<Text>();
        _nameTagText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        _nameTagText.fontSize = 64;
        _nameTagText.alignment = TextAnchor.MiddleCenter;
        _nameTagText.color = Color.white;
        _nameTagText.text = photonView.Owner != null ? photonView.Owner.NickName : "Player";

        RectTransform trt = _nameTagText.rectTransform;
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = Vector2.zero;
        trt.offsetMax = Vector2.zero;
    }

    private void UpdateNameTagBillboard()
    {
        if (_nameTagCanvas == null) return;
        if (_nameTagText != null && photonView.Owner != null)
            _nameTagText.text = photonView.Owner.NickName;

        if (_nameTagCamera == null) _nameTagCamera = Camera.main;
        if (_nameTagCamera == null) return;
        Transform t = _nameTagCanvas.transform;
        Vector3 forward = t.position - _nameTagCamera.transform.position;
        forward.y = 0f;
        if (forward.sqrMagnitude > 0.001f)
            t.rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
    }

    private void HandleLook()
    {
        if (Cursor.lockState != CursorLockMode.Locked) return;
        if (_mouse == null) return;

        Vector2 delta = _mouse.delta.ReadValue();
        float sens = SensitivityOverride > 0f ? SensitivityOverride : mouseSensitivity;
        float mx = delta.x * sens;
        float my = delta.y * sens;

        transform.Rotate(0f, mx, 0f, Space.Self);

        _pitch = Mathf.Clamp(_pitch - my, minPitch, maxPitch);
        if (cameraPivot != null)
        {
            Vector3 e = cameraPivot.localEulerAngles;
            e.x = _pitch;
            cameraPivot.localEulerAngles = e;
        }
    }

    private void HandleMovement()
    {
        if (_kb == null) return;

        float h = 0f, v = 0f;
        if (_kb.dKey.isPressed) h += 1f;
        if (_kb.aKey.isPressed) h -= 1f;
        if (_kb.wKey.isPressed) v += 1f;
        if (_kb.sKey.isPressed) v -= 1f;

        Vector3 inputDir = transform.right * h + transform.forward * v;
        if (inputDir.sqrMagnitude > 1f) inputDir.Normalize();

        bool grounded = _controller.isGrounded;
        bool sprintHeld = _kb.leftShiftKey.isPressed || _kb.rightShiftKey.isPressed;
        bool slideHeld = _kb.leftCtrlKey.isPressed || _kb.rightCtrlKey.isPressed || _kb.cKey.isPressed;

        _isSprinting = sprintHeld && grounded && v > 0.1f && !_isSliding && !_isDashing;

        // Dash: Q key (or double-tap shift could be added later)
        if (_dashCooldownTimer > 0f) _dashCooldownTimer -= Time.deltaTime;
        if (_kb.qKey.wasPressedThisFrame && !_isDashing && _dashCooldownTimer <= 0f && !_isSliding)
        {
            StartDash(inputDir);
        }

        if (_isDashing)
        {
            _dashTimer -= Time.deltaTime;
            if (_dashTimer <= 0f) EndDash();
            Vector3 motion = _dashDirection * dashSpeed * Time.deltaTime;
            motion.y = _velocity.y * Time.deltaTime;
            _velocity.y += gravity * 0.3f * Time.deltaTime;
            _controller.Move(motion);
            UpdateSlideCamera();
            UpdateFov();
            return;
        }

        if (!_isSliding && _isSprinting && slideHeld && grounded)
        {
            StartSlide();
        }

        Vector3 horizontal;
        if (_isSliding)
        {
            _slideTimer -= Time.deltaTime;
            _slideVelocity = Vector3.MoveTowards(_slideVelocity, Vector3.zero, slideFriction * Time.deltaTime);
            Vector3 steer = inputDir * (moveSpeed * 0.35f);
            horizontal = _slideVelocity + steer;
            if (_slideTimer <= 0f || _slideVelocity.magnitude < moveSpeed)
            {
                EndSlide();
            }
        }
        else
        {
            float speed = _isSprinting ? moveSpeed * sprintMultiplier : moveSpeed;
            horizontal = inputDir * speed;
        }

        if (grounded && _velocity.y < 0f) _velocity.y = -2f;
        if (_kb.spaceKey.wasPressedThisFrame && grounded && !_isSliding)
        {
            _velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }
        _velocity.y += gravity * Time.deltaTime;

        if (_mantleCooldownTimer > 0f) _mantleCooldownTimer -= Time.deltaTime;
        if (!grounded && _mantleCooldownTimer <= 0f && v > 0.5f)
        {
            TryMantle();
        }

        Vector3 motion2 = (horizontal + new Vector3(0f, _velocity.y, 0f)) * Time.deltaTime;
        _controller.Move(motion2);

        UpdateSlideCamera();
        UpdateFov();
    }

    private void StartDash(Vector3 inputDir)
    {
        _isDashing = true;
        _dashTimer = dashDuration;
        _dashCooldownTimer = dashCooldown;
        Vector3 lookDir = playerCamera != null
            ? playerCamera.transform.forward
            : transform.forward;
        lookDir.y = 0f;
        if (lookDir.sqrMagnitude < 0.01f) lookDir = transform.forward;
        _dashDirection = lookDir.normalized;
        GameAudio.PlayUI(SfxId.BossHop, 0.3f);
    }

    private void EndDash()
    {
        _isDashing = false;
        _dashTimer = 0f;
    }

    private void UpdateFov()
    {
        if (playerCamera == null || _baseFov <= 0f) return;
        float target = _baseFov;
        if (_isDashing) target += dashFovBonus;
        else if (_isSliding) target += sprintFovBonus + slideFovBonus;
        else if (_isSprinting) target += sprintFovBonus;
        playerCamera.fieldOfView = Mathf.Lerp(playerCamera.fieldOfView, target, Time.deltaTime * fovLerpSpeed);
    }

    private void StartSlide()
    {
        _isSliding = true;
        _slideTimer = slideDuration;
        Vector3 forward = transform.forward;
        forward.y = 0f;
        forward.Normalize();
        _slideVelocity = forward * slideSpeed;
        _controller.height = slideHeight;
        Vector3 c = _defaultCenter;
        c.y = _defaultCenter.y - (_defaultHeight - slideHeight) * 0.5f;
        _controller.center = c;
    }

    private void EndSlide()
    {
        _isSliding = false;
        _slideTimer = 0f;
        _slideVelocity = Vector3.zero;
        _controller.height = _defaultHeight;
        _controller.center = _defaultCenter;
    }

    private void UpdateSlideCamera()
    {
        if (cameraPivot == null) return;
        float targetY = _isSliding ? _defaultCameraY - slideCameraDrop : _defaultCameraY;
        Vector3 lp = cameraPivot.localPosition;
        lp.y = Mathf.Lerp(lp.y, targetY, Time.deltaTime * 14f);
        cameraPivot.localPosition = lp;
    }

    private void TryMantle()
    {
        Vector3 fwd = transform.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.001f) return;
        fwd.Normalize();

        Vector3 chest = transform.position + Vector3.up * 1.2f + fwd * 0.45f;

        RaycastHit[] wallHits = Physics.RaycastAll(chest, fwd, 1.1f, ~0, QueryTriggerInteraction.Ignore);
        RaycastHit wallHit = default;
        bool foundWall = false;
        foreach (RaycastHit h in wallHits)
        {
            if (h.collider == null) continue;
            if (h.collider.transform.IsChildOf(transform) || h.collider.transform == transform) continue;
            wallHit = h;
            foundWall = true;
            break;
        }
        if (!foundWall) return;

        Vector3 above = wallHit.point + fwd * mantleForwardDist;
        above.y = transform.position.y + mantleReach;

        RaycastHit[] downHits = Physics.RaycastAll(above, Vector3.down, mantleReach + 0.5f, ~0, QueryTriggerInteraction.Ignore);
        RaycastHit topHit = default;
        bool foundTop = false;
        float bestY = float.NegativeInfinity;
        foreach (RaycastHit h in downHits)
        {
            if (h.collider == null) continue;
            if (h.collider.transform.IsChildOf(transform) || h.collider.transform == transform) continue;
            if (h.point.y > bestY)
            {
                bestY = h.point.y;
                topHit = h;
                foundTop = true;
            }
        }
        if (!foundTop) return;

        float topY = topHit.point.y;
        float boostHeight = topY - transform.position.y;
        if (boostHeight < 0.4f || boostHeight > mantleReach) return;

        Vector3 target = new Vector3(above.x, topY + 0.05f, above.z);
        Vector3 delta = target - transform.position;
        _controller.Move(delta);
        _velocity.y = 2f;
        _mantleCooldownTimer = mantleCooldown;
    }

    private void ApplyGravityOnly()
    {
        if (_controller == null || !_controller.enabled) return;
        if (_controller.isGrounded && _velocity.y < 0f) _velocity.y = -2f;
        _velocity.y += gravity * Time.deltaTime;
        _controller.Move(new Vector3(0f, _velocity.y, 0f) * Time.deltaTime);
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
