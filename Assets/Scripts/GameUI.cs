using System.Collections;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Hashtable = ExitGames.Client.Photon.Hashtable;

/// <summary>
/// Local player HUD for PUN. Active only on the local player's PhotonView.
/// Reads player health from Player Custom Properties and boss health from Room
/// Custom Properties.
/// </summary>
public class GameUI : MonoBehaviourPunCallbacks
{
    [Header("Optional - leave empty to auto-build")]
    public Text healthText;
    public Text ammoText;
    public Image crosshair;
    public GameObject deathMessage;
    public GameObject bossDefeatedMessage;
    public Image bossHealthFill;
    public Text bossHealthText;
    public Image damageOverlay;
    public GameObject phaseBanner;
    public Text phaseBannerText;

    [Header("Damage Tint")]
    public float damageFlashAlpha = 0.4f;
    public float damageFadeDuration = 0.45f;

    [Header("Boss Bar Phase Colors")]
    public Color phase1FillColor = new Color(0.85f, 0.15f, 0.15f);
    public Color phase2FillColor = new Color(0.65f, 0.05f, 0.55f);

    private Text _respawnText;
    private GameObject _pausePanel;
    private GameObject _settingsPanel;
    private Slider _voiceVolumeSlider;
    private Text _voiceVolumeLabel;
    private Slider _sensitivitySlider;
    private Text _sensitivityLabel;
    private bool _pauseOpen;
    private Text _versionText;

    private NetworkPlayerHealth _playerHealth;
    private NetworkGunController _gun;
    private PhotonView _playerView;
    private Canvas _canvas;
    private NetworkBossHealth _cachedBoss;
    private Coroutine _damageFlashRoutine;
    private Coroutine _phaseBannerRoutine;
    private int _bossPhase = 1;
    private bool _isDead;

    private void Awake()
    {
        _playerHealth = GetComponentInParent<NetworkPlayerHealth>();
        _gun = GetComponentInParent<NetworkGunController>();
        _playerView = GetComponentInParent<PhotonView>();

        _canvas = GetComponent<Canvas>();
        if (_canvas == null) _canvas = gameObject.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        if (GetComponent<CanvasScaler>() == null)
        {
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
        }
        if (GetComponent<GraphicRaycaster>() == null) gameObject.AddComponent<GraphicRaycaster>();

        _canvas.enabled = false;
    }

    private void Start()
    {
        if (_playerView == null || !_playerView.IsMine) return;

        if (healthText == null || ammoText == null || crosshair == null
            || deathMessage == null || bossDefeatedMessage == null || bossHealthFill == null)
        {
            BuildAutoUI();
        }

        EnsureVersionLabel();

        _canvas.enabled = true;
        if (deathMessage != null) deathMessage.SetActive(false);
        if (bossDefeatedMessage != null) bossDefeatedMessage.SetActive(false);
        RefreshAll();
    }

    private void OnEnable()
    {
        NetworkBossHealth.OnBossDiedLocal += HandleBossDefeated;
        NetworkBossHealth.OnPhaseChangedLocal += HandlePhaseChanged;
        NetworkBossHealth.OnPhaseTransitionStartedLocal += HandlePhaseTransitionStarted;
        NetworkGameManager.OnBossDefeatedEvent += HandleBossDefeated;
        if (_playerHealth != null)
        {
            _playerHealth.OnLocalDeath += HandleLocalDeath;
            _playerHealth.OnLocalHealthChanged += RefreshAll;
            _playerHealth.OnLocalDamage += HandleLocalDamage;
            _playerHealth.OnLocalRespawn += HandleLocalRespawn;
        }
        if (_gun != null) _gun.OnAmmoChanged += RefreshAll;
    }

    private void OnDisable()
    {
        NetworkBossHealth.OnBossDiedLocal -= HandleBossDefeated;
        NetworkBossHealth.OnPhaseChangedLocal -= HandlePhaseChanged;
        NetworkBossHealth.OnPhaseTransitionStartedLocal -= HandlePhaseTransitionStarted;
        NetworkGameManager.OnBossDefeatedEvent -= HandleBossDefeated;
        if (_playerHealth != null)
        {
            _playerHealth.OnLocalDeath -= HandleLocalDeath;
            _playerHealth.OnLocalHealthChanged -= RefreshAll;
            _playerHealth.OnLocalDamage -= HandleLocalDamage;
            _playerHealth.OnLocalRespawn -= HandleLocalRespawn;
        }
        if (_gun != null) _gun.OnAmmoChanged -= RefreshAll;
    }

    private void Update()
    {
        if (_playerView == null || !_playerView.IsMine) return;
        RefreshAll();
        UpdateRespawnUI();

        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            TogglePauseMenu();
    }

    public override void OnRoomPropertiesUpdate(Hashtable propertiesThatChanged)
    {
        if (_playerView == null || !_playerView.IsMine) return;
        RefreshAll();
        if (propertiesThatChanged.ContainsKey(NetworkGameManager.BossDefeatedKey)
            && propertiesThatChanged[NetworkGameManager.BossDefeatedKey] is bool defeated
            && defeated)
        {
            HandleBossDefeated();
        }
    }

    public override void OnPlayerPropertiesUpdate(Player targetPlayer, Hashtable changedProps)
    {
        if (_playerView == null || !_playerView.IsMine) return;
        if (targetPlayer == PhotonNetwork.LocalPlayer)
            RefreshAll();
    }

    private void RefreshAll()
    {
        if (!_canvas.enabled) return;

        if (healthText != null && _playerHealth != null)
            healthText.text = $"HP: {_playerHealth.CurrentHealth}";

        if (ammoText != null && _gun != null)
            ammoText.text = _gun.IsReloading ? "Reloading..." : $"Ammo: {_gun.CurrentAmmo} / {_gun.MagazineSize}";

        UpdateBossHealth();
    }

    private void UpdateBossHealth()
    {
        if (bossHealthFill == null) return;

        if (_cachedBoss == null)
            _cachedBoss = FindFirstObjectByType<NetworkBossHealth>();

        int maxHp = _cachedBoss != null ? _cachedBoss.MaxHealth : 1000;
        int hp = NetworkGameManager.Instance != null ? NetworkGameManager.Instance.GetBossHealth() : maxHp;

        float ratio = maxHp > 0 ? Mathf.Clamp01((float)hp / maxHp) : 0f;
        // Scale the fill rect via anchorMax instead of fillAmount (works without a source sprite)
        var rt = bossHealthFill.rectTransform;
        rt.anchorMax = new Vector2(ratio, 1f);
        if (bossHealthText != null)
            bossHealthText.text = $"BOSS  {hp} / {maxHp}";
    }

    private void HandleLocalDeath()
    {
        _isDead = true;
        if (deathMessage != null) deathMessage.SetActive(true);
        if (crosshair != null) crosshair.enabled = false;
        EnsureRespawnText();
        if (_respawnText != null) _respawnText.gameObject.SetActive(true);
    }

    private void HandleLocalRespawn()
    {
        _isDead = false;
        if (deathMessage != null) deathMessage.SetActive(false);
        if (crosshair != null) crosshair.enabled = true;
        if (_respawnText != null) _respawnText.gameObject.SetActive(false);
    }

    private void UpdateRespawnUI()
    {
        if (!_isDead || _respawnText == null || _playerHealth == null) return;

        float elapsed = Time.time - _playerHealth.DeathTime;
        float remaining = _playerHealth.respawnDelay - elapsed;

        if (remaining > 0f)
            _respawnText.text = $"Respawn in {Mathf.CeilToInt(remaining)}...";
        else
            _respawnText.text = "Press <b>F</b> to Respawn";
    }

    private void EnsureRespawnText()
    {
        if (_respawnText != null) return;
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        _respawnText = MakeText(transform, "RespawnText", "", font, 36, TextAnchor.MiddleCenter);
        _respawnText.supportRichText = true;
        _respawnText.color = new Color(1f, 1f, 1f, 0.9f);
        var rt = _respawnText.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(0, -60);
        rt.sizeDelta = new Vector2(600, 60);
        _respawnText.gameObject.SetActive(false);
    }

    // =================== Pause Menu ===================

    private void TogglePauseMenu()
    {
        if (_pauseOpen)
            ClosePauseMenu();
        else
            OpenPauseMenu();
    }

    private void OpenPauseMenu()
    {
        _pauseOpen = true;
        EnsurePausePanel();
        _pausePanel.SetActive(true);
        if (_settingsPanel != null) _settingsPanel.SetActive(false);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void ClosePauseMenu()
    {
        _pauseOpen = false;
        if (_pausePanel != null) _pausePanel.SetActive(false);
        if (_settingsPanel != null) _settingsPanel.SetActive(false);
        if (!_isDead)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    private void OnResumeClicked() { ClosePauseMenu(); }

    private void OnSettingsClicked()
    {
        if (_pausePanel != null) _pausePanel.SetActive(false);
        EnsureSettingsPanel();
        _settingsPanel.SetActive(true);
    }

    private void OnSettingsBackClicked()
    {
        if (_settingsPanel != null) _settingsPanel.SetActive(false);
        if (_pausePanel != null) _pausePanel.SetActive(true);
    }

    private void OnReturnToMenuClicked()
    {
        ClosePauseMenu();
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        GameObject survivor = new GameObject("ReturnToMenuHelper");
        DontDestroyOnLoad(survivor);
        survivor.AddComponent<ReturnToMenuHelper>();
    }

    private void OnVoiceVolumeChanged(float value)
    {
        BossVoiceManager.VoiceVolume = value;
        if (_voiceVolumeLabel != null)
            _voiceVolumeLabel.text = $"Voice Volume: {Mathf.RoundToInt(value * 100)}%";
    }

    private void OnSensitivityChanged(float value)
    {
        NetworkPlayerController.SensitivityOverride = value;
        if (_sensitivityLabel != null)
            _sensitivityLabel.text = $"Sensitivity: {value:F2}";
    }

    private void EnsurePausePanel()
    {
        if (_pausePanel != null) return;
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        _pausePanel = new GameObject("PausePanel", typeof(RectTransform), typeof(Image));
        _pausePanel.transform.SetParent(transform, false);
        var rt = (RectTransform)_pausePanel.transform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        _pausePanel.GetComponent<Image>().color = new Color(0, 0, 0, 0.7f);

        GameObject box = new GameObject("PauseBox", typeof(RectTransform), typeof(Image));
        box.transform.SetParent(_pausePanel.transform, false);
        var brt = (RectTransform)box.transform;
        brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(0.5f, 0.5f);
        brt.sizeDelta = new Vector2(400, 340);
        box.GetComponent<Image>().color = new Color(0.1f, 0.1f, 0.14f, 0.95f);

        MakePauseLabel(box.transform, "PAUSED", font, 36, new Vector2(0, 60),
            new Color(1f, 0.85f, 0.3f), FontStyle.Bold);

        MakePauseButton(box.transform, "Resume", "RESUME", font,
            new Vector2(0, 0), new Color(0.15f, 0.7f, 0.3f), OnResumeClicked);
        MakePauseButton(box.transform, "Settings", "SETTINGS", font,
            new Vector2(0, -65), new Color(0.25f, 0.45f, 0.8f), OnSettingsClicked);
        MakePauseButton(box.transform, "ReturnMenu", "RETURN TO MENU", font,
            new Vector2(0, -130), new Color(0.8f, 0.2f, 0.15f), OnReturnToMenuClicked);

        _pausePanel.SetActive(false);
    }

    private void EnsureSettingsPanel()
    {
        if (_settingsPanel != null) return;
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        _settingsPanel = new GameObject("SettingsPanel", typeof(RectTransform), typeof(Image));
        _settingsPanel.transform.SetParent(transform, false);
        var rt = (RectTransform)_settingsPanel.transform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        _settingsPanel.GetComponent<Image>().color = new Color(0, 0, 0, 0.7f);

        GameObject box = new GameObject("SettingsBox", typeof(RectTransform), typeof(Image));
        box.transform.SetParent(_settingsPanel.transform, false);
        var brt = (RectTransform)box.transform;
        brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(0.5f, 0.5f);
        brt.sizeDelta = new Vector2(440, 420);
        box.GetComponent<Image>().color = new Color(0.1f, 0.1f, 0.14f, 0.95f);

        MakePauseLabel(box.transform, "SETTINGS", font, 30, new Vector2(0, 155),
            new Color(1f, 0.85f, 0.3f), FontStyle.Bold);

        // Voice volume
        _voiceVolumeLabel = MakePauseLabel(box.transform, "Voice Volume: 80%", font, 20,
            new Vector2(0, 95), Color.white, FontStyle.Normal);
        _voiceVolumeSlider = BuildSettingsSlider(box.transform, "VoiceSlider", new Vector2(0, 55));
        _voiceVolumeSlider.minValue = 0f;
        _voiceVolumeSlider.maxValue = 1f;
        _voiceVolumeSlider.value = BossVoiceManager.VoiceVolume;
        _voiceVolumeSlider.onValueChanged.AddListener(OnVoiceVolumeChanged);

        // Mouse sensitivity
        float curSens = NetworkPlayerController.SensitivityOverride > 0f
            ? NetworkPlayerController.SensitivityOverride : 0.15f;
        _sensitivityLabel = MakePauseLabel(box.transform, $"Sensitivity: {curSens:F2}", font, 20,
            new Vector2(0, 5), Color.white, FontStyle.Normal);
        _sensitivitySlider = BuildSettingsSlider(box.transform, "SensSlider", new Vector2(0, -35));
        _sensitivitySlider.minValue = 0.02f;
        _sensitivitySlider.maxValue = 0.60f;
        _sensitivitySlider.value = curSens;
        _sensitivitySlider.onValueChanged.AddListener(OnSensitivityChanged);

        MakePauseButton(box.transform, "BackBtn", "BACK", font,
            new Vector2(0, -120), new Color(0.4f, 0.4f, 0.45f), OnSettingsBackClicked);

        _settingsPanel.SetActive(false);
    }

    private Text MakePauseLabel(Transform parent, string text, Font font, int size,
        Vector2 pos, Color color, FontStyle style)
    {
        GameObject go = new GameObject("Label", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(380, 44);
        Text t = go.AddComponent<Text>();
        t.text = text;
        t.font = font;
        t.fontSize = size;
        t.fontStyle = style;
        t.alignment = TextAnchor.MiddleCenter;
        t.color = color;
        return t;
    }

    private Slider BuildSettingsSlider(Transform parent, string name, Vector2 pos)
    {
        GameObject sliderGo = new GameObject(name, typeof(RectTransform), typeof(Slider));
        sliderGo.transform.SetParent(parent, false);
        var srt = (RectTransform)sliderGo.transform;
        srt.anchorMin = srt.anchorMax = srt.pivot = new Vector2(0.5f, 0.5f);
        srt.anchoredPosition = pos;
        srt.sizeDelta = new Vector2(340, 30);

        GameObject bg = new GameObject("Background", typeof(RectTransform), typeof(Image));
        bg.transform.SetParent(sliderGo.transform, false);
        var bgrt = (RectTransform)bg.transform;
        bgrt.anchorMin = new Vector2(0, 0.25f); bgrt.anchorMax = new Vector2(1, 0.75f);
        bgrt.offsetMin = Vector2.zero; bgrt.offsetMax = Vector2.zero;
        bg.GetComponent<Image>().color = new Color(0.25f, 0.25f, 0.3f);

        GameObject fillArea = new GameObject("FillArea", typeof(RectTransform));
        fillArea.transform.SetParent(sliderGo.transform, false);
        var frt = (RectTransform)fillArea.transform;
        frt.anchorMin = new Vector2(0, 0.25f); frt.anchorMax = new Vector2(1, 0.75f);
        frt.offsetMin = Vector2.zero; frt.offsetMax = Vector2.zero;

        GameObject fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        fill.transform.SetParent(fillArea.transform, false);
        var flrt = (RectTransform)fill.transform;
        flrt.anchorMin = Vector2.zero; flrt.anchorMax = Vector2.one;
        flrt.offsetMin = Vector2.zero; flrt.offsetMax = Vector2.zero;
        fill.GetComponent<Image>().color = new Color(0.3f, 0.7f, 1f);

        GameObject handleArea = new GameObject("HandleSlideArea", typeof(RectTransform));
        handleArea.transform.SetParent(sliderGo.transform, false);
        var hart = (RectTransform)handleArea.transform;
        hart.anchorMin = Vector2.zero; hart.anchorMax = Vector2.one;
        hart.offsetMin = Vector2.zero; hart.offsetMax = Vector2.zero;

        GameObject handle = new GameObject("Handle", typeof(RectTransform), typeof(Image));
        handle.transform.SetParent(handleArea.transform, false);
        var hrt = (RectTransform)handle.transform;
        hrt.sizeDelta = new Vector2(24, 24);
        handle.GetComponent<Image>().color = Color.white;

        Slider slider = sliderGo.GetComponent<Slider>();
        slider.fillRect = flrt;
        slider.handleRect = hrt;
        slider.targetGraphic = handle.GetComponent<Image>();
        return slider;
    }

    private void MakePauseButton(Transform parent, string name, string label, Font font,
        Vector2 pos, Color bgColor, UnityEngine.Events.UnityAction onClick)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(280, 50);
        go.GetComponent<Image>().color = bgColor;

        GameObject labelGo = new GameObject("Label", typeof(RectTransform));
        labelGo.transform.SetParent(go.transform, false);
        var lrt = (RectTransform)labelGo.transform;
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
        lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;
        Text t = labelGo.AddComponent<Text>();
        t.text = label;
        t.font = font;
        t.fontSize = 20;
        t.fontStyle = FontStyle.Bold;
        t.alignment = TextAnchor.MiddleCenter;
        t.color = Color.white;

        go.GetComponent<Button>().onClick.AddListener(onClick);
    }

    private void HandleBossDefeated()
    {
        if (bossDefeatedMessage != null) bossDefeatedMessage.SetActive(true);
    }

    private void HandleLocalDamage()
    {
        if (damageOverlay == null) return;
        if (_damageFlashRoutine != null) StopCoroutine(_damageFlashRoutine);
        _damageFlashRoutine = StartCoroutine(DamageFlashRoutine());
    }

    private void HandlePhaseTransitionStarted()
    {
        ShowPhaseBanner("PHASE 2", new Color(1f, 0.4f, 0.05f), 3.5f);
    }

    private void HandlePhaseChanged(int newPhase)
    {
        _bossPhase = newPhase;
        if (bossHealthFill != null)
        {
            bossHealthFill.color = newPhase >= 2 ? phase2FillColor : phase1FillColor;
        }
    }

    private void ShowPhaseBanner(string text, Color color, float duration)
    {
        if (phaseBanner == null || phaseBannerText == null) return;
        phaseBannerText.text = text;
        phaseBannerText.color = color;
        phaseBanner.SetActive(true);
        if (_phaseBannerRoutine != null) StopCoroutine(_phaseBannerRoutine);
        _phaseBannerRoutine = StartCoroutine(HidePhaseBannerAfter(duration));
    }

    private IEnumerator HidePhaseBannerAfter(float duration)
    {
        yield return new WaitForSeconds(duration);
        if (phaseBanner != null) phaseBanner.SetActive(false);
        _phaseBannerRoutine = null;
    }

    private IEnumerator DamageFlashRoutine()
    {
        damageOverlay.color = new Color(1f, 0f, 0f, damageFlashAlpha);
        float t = 0f;
        while (t < damageFadeDuration)
        {
            t += Time.deltaTime;
            float a = Mathf.Lerp(damageFlashAlpha, 0f, t / damageFadeDuration);
            damageOverlay.color = new Color(1f, 0f, 0f, a);
            yield return null;
        }
        damageOverlay.color = new Color(1f, 0f, 0f, 0f);
        _damageFlashRoutine = null;
    }

    private void BuildAutoUI()
    {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        GameObject overlay = new GameObject("DamageOverlay", typeof(RectTransform), typeof(Image));
        overlay.transform.SetParent(transform, false);
        var oRT = (RectTransform)overlay.transform;
        oRT.anchorMin = Vector2.zero;
        oRT.anchorMax = Vector2.one;
        oRT.offsetMin = Vector2.zero;
        oRT.offsetMax = Vector2.zero;
        damageOverlay = overlay.GetComponent<Image>();
        damageOverlay.color = new Color(1f, 0f, 0f, 0f);
        damageOverlay.raycastTarget = false;

        GameObject bossBarBg = new GameObject("BossHealthBar", typeof(RectTransform), typeof(Image));
        bossBarBg.transform.SetParent(transform, false);
        var bgRT = (RectTransform)bossBarBg.transform;
        bgRT.anchorMin = new Vector2(0.5f, 1f);
        bgRT.anchorMax = new Vector2(0.5f, 1f);
        bgRT.pivot = new Vector2(0.5f, 1f);
        bgRT.anchoredPosition = new Vector2(0, -20);
        bgRT.sizeDelta = new Vector2(900, 44);
        bossBarBg.GetComponent<Image>().color = new Color(0, 0, 0, 0.6f);

        GameObject fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        fill.transform.SetParent(bossBarBg.transform, false);
        var fRT = (RectTransform)fill.transform;
        fRT.anchorMin = Vector2.zero;
        fRT.anchorMax = Vector2.one;
        fRT.offsetMin = new Vector2(3, 3);
        fRT.offsetMax = new Vector2(-3, -3);
        bossHealthFill = fill.GetComponent<Image>();
        bossHealthFill.color = phase1FillColor;

        // 50% divider line on the boss bar so players know where phase 2 starts
        GameObject divider = new GameObject("PhaseDivider", typeof(RectTransform), typeof(Image));
        divider.transform.SetParent(bossBarBg.transform, false);
        var dRT = (RectTransform)divider.transform;
        dRT.anchorMin = new Vector2(0.5f, 0f);
        dRT.anchorMax = new Vector2(0.5f, 1f);
        dRT.pivot = new Vector2(0.5f, 0.5f);
        dRT.anchoredPosition = Vector2.zero;
        dRT.sizeDelta = new Vector2(4, -6);
        divider.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.85f);

        bossHealthText = MakeText(bossBarBg.transform, "BossText", "BOSS  1000 / 1000", font, 22, TextAnchor.MiddleCenter);
        bossHealthText.rectTransform.anchorMin = Vector2.zero;
        bossHealthText.rectTransform.anchorMax = Vector2.one;
        bossHealthText.rectTransform.offsetMin = Vector2.zero;
        bossHealthText.rectTransform.offsetMax = Vector2.zero;

        // Phase banner (shown briefly during phase transition)
        phaseBanner = new GameObject("PhaseBanner", typeof(RectTransform));
        phaseBanner.transform.SetParent(transform, false);
        var pbRT = (RectTransform)phaseBanner.transform;
        pbRT.anchorMin = pbRT.anchorMax = pbRT.pivot = new Vector2(0.5f, 0.5f);
        pbRT.anchoredPosition = new Vector2(0, 200);
        pbRT.sizeDelta = new Vector2(900, 160);
        phaseBannerText = phaseBanner.AddComponent<Text>();
        phaseBannerText.text = "PHASE 2";
        phaseBannerText.font = font;
        phaseBannerText.fontSize = 110;
        phaseBannerText.fontStyle = FontStyle.Bold;
        phaseBannerText.alignment = TextAnchor.MiddleCenter;
        phaseBannerText.color = new Color(1f, 0.4f, 0.05f);
        phaseBanner.SetActive(false);

        GameObject ch = new GameObject("Crosshair", typeof(RectTransform), typeof(Image));
        ch.transform.SetParent(transform, false);
        var chRT = (RectTransform)ch.transform;
        chRT.anchorMin = chRT.anchorMax = chRT.pivot = new Vector2(0.5f, 0.5f);
        chRT.sizeDelta = new Vector2(14, 14);
        crosshair = ch.GetComponent<Image>();
        crosshair.color = new Color(1, 1, 1, 0.9f);

        healthText = MakeText(transform, "HealthText", "HP: 100", font, 38, TextAnchor.LowerLeft);
        healthText.rectTransform.anchorMin = healthText.rectTransform.anchorMax = healthText.rectTransform.pivot = new Vector2(0, 0);
        healthText.rectTransform.anchoredPosition = new Vector2(30, 28);
        healthText.rectTransform.sizeDelta = new Vector2(400, 55);

        ammoText = MakeText(transform, "AmmoText", "Ammo: 12 / 12", font, 38, TextAnchor.LowerRight);
        ammoText.rectTransform.anchorMin = ammoText.rectTransform.anchorMax = new Vector2(1, 0);
        ammoText.rectTransform.pivot = new Vector2(1, 0);
        ammoText.rectTransform.anchoredPosition = new Vector2(-30, 28);
        ammoText.rectTransform.sizeDelta = new Vector2(460, 55);

        deathMessage = MakeMessage("DeathMessage", "YOU DIED", font, new Color(1f, 0.2f, 0.2f), 50f);
        bossDefeatedMessage = MakeMessage("BossDefeatedMessage", "BOSS DEFEATED", font, new Color(0.3f, 1f, 0.4f), 100f);

        BuildControlsPanel(font);
    }

    private void EnsureVersionLabel()
    {
        if (_versionText != null) return;

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        _versionText = MakeText(transform, "VersionText", $"v{Application.version}", font, 15, TextAnchor.LowerRight);
        _versionText.color = new Color(1f, 1f, 1f, 0.45f);
        _versionText.raycastTarget = false;
        var rt = _versionText.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot = new Vector2(1f, 0f);
        rt.anchoredPosition = new Vector2(-14f, 10f);
        rt.sizeDelta = new Vector2(280f, 24f);
    }

    private void BuildControlsPanel(Font font)
    {
        // Background panel pinned to the top-left
        GameObject panel = new GameObject("ControlsPanel", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(transform, false);
        var pRT = (RectTransform)panel.transform;
        pRT.anchorMin = pRT.anchorMax = pRT.pivot = new Vector2(0f, 1f);
        pRT.anchoredPosition = new Vector2(20, -20);
        pRT.sizeDelta = new Vector2(330, 310);
        panel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.45f);
        panel.GetComponent<Image>().raycastTarget = false;

        Text title = MakeText(panel.transform, "Title", "CONTROLS", font, 24, TextAnchor.UpperLeft);
        title.fontStyle = FontStyle.Bold;
        title.color = new Color(1f, 0.85f, 0.3f);
        title.rectTransform.anchorMin = new Vector2(0, 1);
        title.rectTransform.anchorMax = new Vector2(1, 1);
        title.rectTransform.pivot = new Vector2(0, 1);
        title.rectTransform.anchoredPosition = new Vector2(14, -10);
        title.rectTransform.sizeDelta = new Vector2(-20, 32);

        string body =
            "<b>WASD</b>      Move\n" +
            "<b>Mouse</b>     Look\n" +
            "<b>LMB</b>       Shoot\n" +
            "<b>R</b>         Reload\n" +
            "<b>Space</b>     Jump\n" +
            "<b>Shift</b>     Sprint\n" +
            "<b>Q</b>         Dash\n" +
            "<b>Ctrl / C</b>  Slide\n" +
            "<b>W + Air</b>   Mantle ledge";

        Text list = MakeText(panel.transform, "List", body, font, 19, TextAnchor.UpperLeft);
        list.supportRichText = true;
        list.color = new Color(1f, 1f, 1f, 0.92f);
        list.rectTransform.anchorMin = new Vector2(0, 0);
        list.rectTransform.anchorMax = new Vector2(1, 1);
        list.rectTransform.pivot = new Vector2(0, 1);
        list.rectTransform.anchoredPosition = new Vector2(14, -42);
        list.rectTransform.sizeDelta = new Vector2(-20, -50);
        list.raycastTarget = false;
    }

    private GameObject MakeMessage(string name, string text, Font font, Color color, float y)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(transform, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(0, y);
        rt.sizeDelta = new Vector2(1100, 150);
        Text label = go.AddComponent<Text>();
        label.text = text;
        label.alignment = TextAnchor.MiddleCenter;
        label.fontSize = 100;
        label.color = color;
        label.font = font;
        go.SetActive(false);
        return go;
    }

    private static Text MakeText(Transform parent, string name, string content, Font font, int size, TextAnchor anchor)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        Text t = go.AddComponent<Text>();
        t.text = content;
        t.font = font;
        t.fontSize = size;
        t.alignment = anchor;
        t.color = Color.white;
        return t;
    }
}
