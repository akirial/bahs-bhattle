using System.Text;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using Hashtable = ExitGames.Client.Photon.Hashtable;

/// <summary>
/// PUN 2 lobby with ready-up system. Includes a 3D character preview, name edit,
/// and color picker. Name is Photon NickName; color is stored in player custom properties.
/// </summary>
public class MultiplayerMenuUI : MonoBehaviourPunCallbacks
{
    public const string ReadyKey = "Ready";
    public const string GameStartedKey = "GameStarted";
    public const string ColorKey = "PColor";

    [Header("Photon Room")]
    public string defaultRoomName = "BossRoom";
    public byte maxPlayers = 4;

    private Canvas _canvas;
    private GameObject _connectPanel;
    private GameObject _lobbyPanel;

    private InputField _roomInput;
    private Text _statusText;
    private Button _hostButton;
    private Button _joinButton;

    private Text _lobbyTitle;
    private Text _playerListText;
    private Button _readyButton;
    private Text _readyButtonLabel;
    private Button _startButton;
    private Text _startButtonLabel;

    // Customization UI
    private InputField _nameInput;
    private RawImage _previewImage;
    private RectTransform _previewRect;
    private Button[] _colorButtons;

    // 3D preview objects
    private RenderTexture _previewRT;
    private Camera _previewCamera;
    private GameObject _previewCube;
    private Material _previewMat;
    private bool _draggingPreview;
    private Vector2 _dragPrevMouse;

    private bool _wantsToHost;
    private bool _inLobby;
    private bool _gameStarted;
    private int _selectedColorIndex;

    private static readonly Color[] Palette =
    {
        new Color(0.95f, 0.2f, 0.2f),
        new Color(0.2f, 0.5f, 0.95f),
        new Color(0.2f, 0.85f, 0.35f),
        new Color(1f, 0.9f, 0.2f),
        new Color(0.7f, 0.25f, 0.95f),
        new Color(1f, 0.55f, 0.15f),
        new Color(0.2f, 0.95f, 0.95f),
        Color.white
    };

    private void Awake()
    {
        PhotonNetwork.AutomaticallySyncScene = false;
        EnsureEventSystem();
    }

    private void Start()
    {
        EnsureCanvas();
        BuildUI();
        ShowConnectPanel();

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void Update()
    {
        if (_inLobby && !_gameStarted)
        {
            RefreshLobby();
            UpdatePreviewDrag();
        }
    }

    private void OnDestroy()
    {
        if (_previewRT != null) _previewRT.Release();
        if (_previewCamera != null) Object.Destroy(_previewCamera.gameObject);
        if (_previewCube != null) Object.Destroy(_previewCube);
        if (_previewMat != null) Object.Destroy(_previewMat);
    }

    // =================== Connection ===================

    private void OnHostClicked()
    {
        _wantsToHost = true;
        SetStatus("Connecting...");
        ConnectIfNeeded();
    }

    private void OnJoinClicked()
    {
        _wantsToHost = false;
        SetStatus("Connecting...");
        ConnectIfNeeded();
    }

    private void ConnectIfNeeded()
    {
        if (PhotonNetwork.IsConnectedAndReady)
        {
            EnterRoom();
            return;
        }
        if (!PhotonNetwork.IsConnected)
            PhotonNetwork.ConnectUsingSettings();
    }

    public override void OnConnectedToMaster()
    {
        EnterRoom();
    }

    private void EnterRoom()
    {
        string roomName = _roomInput != null && !string.IsNullOrWhiteSpace(_roomInput.text)
            ? _roomInput.text.Trim() : defaultRoomName;

        RoomOptions options = new RoomOptions { MaxPlayers = maxPlayers };

        if (_wantsToHost)
        {
            PhotonNetwork.CreateRoom(roomName, options, TypedLobby.Default);
            SetStatus($"Creating room: {roomName}");
        }
        else
        {
            PhotonNetwork.JoinRoom(roomName);
            SetStatus($"Joining room: {roomName}");
        }
    }

    public override void OnCreateRoomFailed(short returnCode, string message)
    {
        SetStatus($"Create failed: {message}. Trying join...");
        string roomName = _roomInput != null && !string.IsNullOrWhiteSpace(_roomInput.text)
            ? _roomInput.text.Trim() : defaultRoomName;
        PhotonNetwork.JoinRoom(roomName);
    }

    public override void OnJoinRoomFailed(short returnCode, string message)
    {
        SetStatus($"Join failed: {message}. Creating room...");
        string roomName = _roomInput != null && !string.IsNullOrWhiteSpace(_roomInput.text)
            ? _roomInput.text.Trim() : defaultRoomName;
        RoomOptions options = new RoomOptions { MaxPlayers = maxPlayers };
        PhotonNetwork.CreateRoom(roomName, options, TypedLobby.Default);
    }

    public override void OnJoinedRoom()
    {
        EnsureDefaultIdentity();
        PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable
        {
            { ReadyKey, false },
            { ColorKey, _selectedColorIndex }
        });

        if (PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(GameStartedKey, out object val)
            && val is bool started && started)
        {
            OnGameStarted();
            return;
        }

        ShowLobbyPanel();
    }

    public override void OnPlayerEnteredRoom(Player newPlayer) { RefreshLobby(); }
    public override void OnPlayerLeftRoom(Player otherPlayer) { RefreshLobby(); }
    public override void OnPlayerPropertiesUpdate(Player targetPlayer, Hashtable changedProps) { RefreshLobby(); }

    public override void OnRoomPropertiesUpdate(Hashtable changed)
    {
        if (changed.ContainsKey(GameStartedKey) && changed[GameStartedKey] is bool started && started)
        {
            OnGameStarted();
        }
    }

    // =================== Ready / Start ===================

    private void OnReadyClicked()
    {
        if (!PhotonNetwork.InRoom) return;
        bool currentReady = false;
        if (PhotonNetwork.LocalPlayer.CustomProperties.TryGetValue(ReadyKey, out object raw) && raw is bool r)
            currentReady = r;

        bool newReady = !currentReady;
        PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable { { ReadyKey, newReady } });
    }

    private void OnStartClicked()
    {
        if (!PhotonNetwork.IsMasterClient) return;
        if (!AllPlayersReady()) return;

        PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable { { GameStartedKey, true } });
    }

    private void OnGameStarted()
    {
        _gameStarted = true;
        _inLobby = false;
        if (_lobbyPanel != null) _lobbyPanel.SetActive(false);
        if (_canvas != null) _canvas.enabled = false;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        if (NetworkGameManager.Instance != null)
        {
            NetworkGameManager.Instance.SpawnLocalPlayer();
            NetworkGameManager.Instance.StartGame();
        }
    }

    private bool AllPlayersReady()
    {
        foreach (Player p in PhotonNetwork.PlayerList)
        {
            if (p.CustomProperties.TryGetValue(ReadyKey, out object raw) && raw is bool ready && ready)
                continue;
            return false;
        }
        return PhotonNetwork.PlayerList.Length > 0;
    }

    // =================== Lobby refresh ===================

    private void RefreshLobby()
    {
        if (_playerListText == null) return;

        StringBuilder sb = new();
        foreach (Player p in PhotonNetwork.PlayerList)
        {
            bool ready = p.CustomProperties.TryGetValue(ReadyKey, out object rr) && rr is bool r && r;

            int colorIdx = 0;
            if (p.CustomProperties.TryGetValue(ColorKey, out object cr) && cr is int ci)
                colorIdx = Mathf.Clamp(ci, 0, Palette.Length - 1);
            string hex = ColorUtility.ToHtmlStringRGB(Palette[colorIdx]);
            string colorBox = $"<color=#{hex}>■</color> ";

            string tag = p.IsMasterClient ? " [HOST]" : "";
            string readyStr = ready ? "  <color=#44ff44>READY</color>" : "  <color=#ff4444>NOT READY</color>";
            string you = p.IsLocal ? " (you)" : "";
            sb.AppendLine($"{colorBox}{p.NickName}{you}{tag}{readyStr}");
        }
        _playerListText.text = sb.ToString();

        if (_lobbyTitle != null)
            _lobbyTitle.text = $"LOBBY  —  {PhotonNetwork.CurrentRoom.Name}  ({PhotonNetwork.PlayerList.Length}/{maxPlayers})";

        bool localReady = PhotonNetwork.LocalPlayer.CustomProperties.TryGetValue(ReadyKey, out object lr) && lr is bool lrb && lrb;
        if (_readyButtonLabel != null)
            _readyButtonLabel.text = localReady ? "UNREADY" : "READY";
        if (_readyButton != null)
            _readyButton.GetComponent<Image>().color = localReady
                ? new Color(0.8f, 0.3f, 0.15f) : new Color(0.15f, 0.7f, 0.3f);

        if (_startButton != null)
        {
            bool isMaster = PhotonNetwork.IsMasterClient;
            _startButton.gameObject.SetActive(isMaster);
            if (isMaster)
            {
                bool canStart = AllPlayersReady();
                _startButton.interactable = canStart;
                if (_startButtonLabel != null)
                    _startButtonLabel.color = canStart ? Color.white : new Color(1, 1, 1, 0.35f);
            }
        }
    }

    // =================== UI panels ===================

    private void ShowConnectPanel()
    {
        if (_connectPanel != null) _connectPanel.SetActive(true);
        if (_lobbyPanel != null) _lobbyPanel.SetActive(false);
        _inLobby = false;
    }

    private void ShowLobbyPanel()
    {
        if (_connectPanel != null) _connectPanel.SetActive(false);
        if (_lobbyPanel != null) _lobbyPanel.SetActive(true);
        _inLobby = true;

        EnsureDefaultIdentity();
        EnsurePreviewObjects();
        SyncUIFromIdentity();

        RefreshLobby();
    }

    // =================== Customization ===================

    private void EnsureDefaultIdentity()
    {
        if (string.IsNullOrEmpty(PhotonNetwork.NickName))
            PhotonNetwork.NickName = $"Player{PhotonNetwork.LocalPlayer.ActorNumber}";

        if (PhotonNetwork.LocalPlayer != null
            && PhotonNetwork.LocalPlayer.CustomProperties.TryGetValue(ColorKey, out object raw)
            && raw is int ci)
        {
            _selectedColorIndex = Mathf.Clamp(ci, 0, Palette.Length - 1);
        }
        else
        {
            _selectedColorIndex = (PhotonNetwork.LocalPlayer.ActorNumber - 1) % Palette.Length;
        }
    }

    private void SyncUIFromIdentity()
    {
        if (_nameInput != null) _nameInput.text = PhotonNetwork.NickName;
        if (_previewMat != null) _previewMat.color = Palette[Mathf.Clamp(_selectedColorIndex, 0, Palette.Length - 1)];
    }

    private void OnNameChanged(string value)
    {
        value = string.IsNullOrWhiteSpace(value) ? "Player" : value.Trim();
        PhotonNetwork.NickName = value;
    }

    private void OnColorPicked(int idx)
    {
        _selectedColorIndex = Mathf.Clamp(idx, 0, Palette.Length - 1);
        if (_previewMat != null) _previewMat.color = Palette[_selectedColorIndex];
        if (PhotonNetwork.InRoom)
            PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable { { ColorKey, _selectedColorIndex } });
    }

    private void EnsurePreviewObjects()
    {
        if (_previewCamera != null && _previewCube != null && _previewRT != null) return;

        _previewRT = new RenderTexture(256, 256, 16, RenderTextureFormat.ARGB32) { name = "PlayerPreviewRT" };

        GameObject camGo = new GameObject("PlayerPreviewCamera");
        camGo.transform.position = new Vector3(0f, -1000f, 0f);
        camGo.transform.rotation = Quaternion.Euler(20f, 25f, 0f);
        _previewCamera = camGo.AddComponent<Camera>();
        _previewCamera.clearFlags = CameraClearFlags.SolidColor;
        _previewCamera.backgroundColor = new Color(0, 0, 0, 0);
        _previewCamera.fieldOfView = 35f;
        _previewCamera.nearClipPlane = 0.01f;
        _previewCamera.farClipPlane = 10f;
        _previewCamera.targetTexture = _previewRT;

        _previewCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        _previewCube.name = "PlayerPreviewCube";
        Object.Destroy(_previewCube.GetComponent<Collider>());
        _previewCube.transform.position = new Vector3(0f, -1000f, 2.5f);
        _previewCube.transform.localScale = new Vector3(1.2f, 2.1f, 1.2f);

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        _previewMat = new Material(shader) { color = Palette[Mathf.Clamp(_selectedColorIndex, 0, Palette.Length - 1)] };
        var r = _previewCube.GetComponent<MeshRenderer>();
        if (r != null) r.sharedMaterial = _previewMat;

        _previewCamera.transform.LookAt(_previewCube.transform.position + Vector3.up * 0.4f);
        if (_previewImage != null) _previewImage.texture = _previewRT;
    }

    private void UpdatePreviewDrag()
    {
        if (_previewRect == null || _previewCube == null) return;
        Mouse mouse = Mouse.current;
        if (mouse == null) return;

        Vector2 pos = mouse.position.ReadValue();
        bool over = RectTransformUtility.RectangleContainsScreenPoint(_previewRect, pos, null);
        if (mouse.leftButton.wasPressedThisFrame && over)
        {
            _draggingPreview = true;
            _dragPrevMouse = pos;
        }
        if (mouse.leftButton.wasReleasedThisFrame) _draggingPreview = false;
        if (!_draggingPreview) return;

        Vector2 delta = pos - _dragPrevMouse;
        _dragPrevMouse = pos;
        _previewCube.transform.Rotate(Vector3.up, -delta.x * 0.25f, Space.World);
    }

    // =================== Build UI ===================

    private void EnsureCanvas()
    {
        _canvas = GetComponent<Canvas>();
        if (_canvas == null)
        {
            _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        }
        _canvas.sortingOrder = 100;
        CanvasScaler scaler = GetComponent<CanvasScaler>();
        if (scaler == null) scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        if (GetComponent<GraphicRaycaster>() == null)
            gameObject.AddComponent<GraphicRaycaster>();
    }

    private void BuildUI()
    {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        // ---------- Connect Panel ----------
        _connectPanel = MakePanel(transform, "ConnectPanel", 460, 320);

        MakeLabel(_connectPanel.transform, "Title", "BOSS BATTLE", font, 30,
            new Vector2(0.5f, 1f), new Vector2(0, -16), new Vector2(420, 44), TextAnchor.MiddleCenter,
            new Color(1f, 0.85f, 0.3f), FontStyle.Bold);

        MakeLabel(_connectPanel.transform, "RoomLabel", "Room Name:", font, 18,
            new Vector2(0f, 1f), new Vector2(20, -80), new Vector2(160, 30), TextAnchor.MiddleLeft,
            Color.white, FontStyle.Normal);

        _roomInput = MakeInputField(_connectPanel.transform, "RoomInput", defaultRoomName, font,
            new Vector2(0f, 1f), new Vector2(20, -115), new Vector2(420, 40));

        _hostButton = MakeButton(_connectPanel.transform, "HostBtn", "HOST GAME", font,
            new Vector2(0f, 0f), new Vector2(20, 80), new Vector2(195, 52),
            new Color(0.15f, 0.7f, 0.3f));
        _hostButton.onClick.AddListener(OnHostClicked);

        _joinButton = MakeButton(_connectPanel.transform, "JoinBtn", "JOIN GAME", font,
            new Vector2(1f, 0f), new Vector2(-20, 80), new Vector2(195, 52),
            new Color(0.2f, 0.5f, 0.9f));
        _joinButton.onClick.AddListener(OnJoinClicked);

        _statusText = MakeLabel(_connectPanel.transform, "Status", "Enter a room name to begin", font, 16,
            new Vector2(0.5f, 0f), new Vector2(0, 26), new Vector2(420, 34), TextAnchor.MiddleCenter,
            new Color(1, 1, 1, 0.6f), FontStyle.Italic);

        // ---------- Lobby Panel ----------
        _lobbyPanel = MakePanel(transform, "LobbyPanel", 980, 560);
        _lobbyPanel.SetActive(false);

        _lobbyTitle = MakeLabel(_lobbyPanel.transform, "LobbyTitle", "LOBBY", font, 26,
            new Vector2(0.5f, 1f), new Vector2(0, -14), new Vector2(940, 40), TextAnchor.MiddleCenter,
            new Color(1f, 0.85f, 0.3f), FontStyle.Bold);

        MakeLabel(_lobbyPanel.transform, "PlayersLabel", "Players:", font, 18,
            new Vector2(0f, 1f), new Vector2(20, -62), new Vector2(200, 28), TextAnchor.MiddleLeft,
            new Color(1, 1, 1, 0.7f), FontStyle.Bold);

        GameObject listBg = new GameObject("ListBg", typeof(RectTransform), typeof(Image));
        listBg.transform.SetParent(_lobbyPanel.transform, false);
        var lbRT = (RectTransform)listBg.transform;
        lbRT.anchorMin = lbRT.anchorMax = lbRT.pivot = new Vector2(0f, 1f);
        lbRT.anchoredPosition = new Vector2(20, -90);
        lbRT.sizeDelta = new Vector2(460, 270);
        listBg.GetComponent<Image>().color = new Color(0, 0, 0, 0.4f);

        _playerListText = MakeLabel(listBg.transform, "PlayerList", "", font, 20,
            new Vector2(0f, 1f), new Vector2(12, -8), new Vector2(436, 250), TextAnchor.UpperLeft,
            Color.white, FontStyle.Normal);
        _playerListText.supportRichText = true;

        _readyButton = MakeButton(_lobbyPanel.transform, "ReadyBtn", "READY", font,
            new Vector2(0f, 0f), new Vector2(20, 30), new Vector2(220, 56),
            new Color(0.15f, 0.7f, 0.3f));
        _readyButton.onClick.AddListener(OnReadyClicked);
        _readyButtonLabel = _readyButton.GetComponentInChildren<Text>();

        _startButton = MakeButton(_lobbyPanel.transform, "StartBtn", "START FIGHT", font,
            new Vector2(0f, 0f), new Vector2(260, 30), new Vector2(220, 56),
            new Color(0.85f, 0.2f, 0.15f));
        _startButton.onClick.AddListener(OnStartClicked);
        _startButtonLabel = _startButton.GetComponentInChildren<Text>();
        _startButton.gameObject.SetActive(false);

        // Customization right side
        MakeLabel(_lobbyPanel.transform, "CustomizeLabel", "YOUR CHARACTER", font, 18,
            new Vector2(0f, 1f), new Vector2(520, -62), new Vector2(400, 28), TextAnchor.MiddleLeft,
            new Color(1, 1, 1, 0.7f), FontStyle.Bold);

        _nameInput = MakeInputField(_lobbyPanel.transform, "NameInput", "Player", font,
            new Vector2(0f, 1f), new Vector2(520, -98), new Vector2(420, 40));
        _nameInput.onValueChanged.AddListener(OnNameChanged);

        GameObject previewBg = new GameObject("PreviewBg", typeof(RectTransform), typeof(Image));
        previewBg.transform.SetParent(_lobbyPanel.transform, false);
        var prt = (RectTransform)previewBg.transform;
        prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0f, 1f);
        prt.anchoredPosition = new Vector2(520, -150);
        prt.sizeDelta = new Vector2(420, 300);
        previewBg.GetComponent<Image>().color = new Color(0, 0, 0, 0.4f);

        GameObject rawGo = new GameObject("PreviewImage", typeof(RectTransform), typeof(RawImage));
        rawGo.transform.SetParent(previewBg.transform, false);
        _previewRect = (RectTransform)rawGo.transform;
        _previewRect.anchorMin = Vector2.zero;
        _previewRect.anchorMax = Vector2.one;
        _previewRect.offsetMin = new Vector2(10, 10);
        _previewRect.offsetMax = new Vector2(-10, -10);
        _previewImage = rawGo.GetComponent<RawImage>();

        MakeLabel(_lobbyPanel.transform, "ColorLabel", "COLOR", font, 16,
            new Vector2(0f, 1f), new Vector2(520, -460), new Vector2(120, 26), TextAnchor.MiddleLeft,
            new Color(1, 1, 1, 0.7f), FontStyle.Bold);

        _colorButtons = new Button[Palette.Length];
        float x0 = 520f;
        float y0 = -490f;
        float sz = 44f;
        float gap = 10f;
        for (int i = 0; i < Palette.Length; i++)
        {
            int idx = i;
            Button b = MakeColorButton(_lobbyPanel.transform, $"Color_{i}",
                new Vector2(x0 + i * (sz + gap), y0), new Vector2(sz, sz), Palette[i]);
            b.onClick.AddListener(() => OnColorPicked(idx));
            _colorButtons[i] = b;
        }

        MakeLabel(transform, "VersionLabel", $"v{Application.version}", font, 14,
            new Vector2(1f, 0f), new Vector2(-14f, 14f), new Vector2(260f, 26f), TextAnchor.LowerRight,
            new Color(1f, 1f, 1f, 0.45f), FontStyle.Normal);
    }

    // =================== UI helpers ===================

    private static GameObject MakePanel(Transform parent, string name, float w, float h)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(w, h);
        rt.anchoredPosition = Vector2.zero;
        go.GetComponent<Image>().color = new Color(0.08f, 0.08f, 0.12f, 0.92f);
        return go;
    }

    private static Text MakeLabel(Transform parent, string name, string content, Font font, int size,
        Vector2 anchor, Vector2 pos, Vector2 sizeDelta, TextAnchor align, Color color, FontStyle style)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
        rt.anchoredPosition = pos;
        rt.sizeDelta = sizeDelta;
        Text t = go.AddComponent<Text>();
        t.text = content;
        t.font = font;
        t.fontSize = size;
        t.alignment = align;
        t.color = color;
        t.fontStyle = style;
        return t;
    }

    private static Button MakeButton(Transform parent, string name, string label, Font font,
        Vector2 anchor, Vector2 pos, Vector2 sizeDelta, Color bgColor)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
        rt.anchoredPosition = pos;
        rt.sizeDelta = sizeDelta;
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

        return go.GetComponent<Button>();
    }

    private static InputField MakeInputField(Transform parent, string name, string defaultText, Font font,
        Vector2 anchor, Vector2 pos, Vector2 sizeDelta)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(InputField));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
        rt.anchoredPosition = pos;
        rt.sizeDelta = sizeDelta;
        go.GetComponent<Image>().color = new Color(1, 1, 1, 0.95f);

        Text valueText = MakeLabel(go.transform, "Text", defaultText, font, 18,
            new Vector2(0, 0.5f), Vector2.zero, Vector2.zero, TextAnchor.MiddleLeft,
            Color.black, FontStyle.Normal);
        valueText.rectTransform.anchorMin = Vector2.zero;
        valueText.rectTransform.anchorMax = Vector2.one;
        valueText.rectTransform.offsetMin = new Vector2(10, 4);
        valueText.rectTransform.offsetMax = new Vector2(-10, -4);

        Text placeholder = MakeLabel(go.transform, "Placeholder", "Type...", font, 18,
            new Vector2(0, 0.5f), Vector2.zero, Vector2.zero, TextAnchor.MiddleLeft,
            new Color(0, 0, 0, 0.4f), FontStyle.Italic);
        placeholder.rectTransform.anchorMin = Vector2.zero;
        placeholder.rectTransform.anchorMax = Vector2.one;
        placeholder.rectTransform.offsetMin = new Vector2(10, 4);
        placeholder.rectTransform.offsetMax = new Vector2(-10, -4);

        InputField input = go.GetComponent<InputField>();
        input.targetGraphic = go.GetComponent<Image>();
        input.textComponent = valueText;
        input.placeholder = placeholder;
        input.text = defaultText;
        return input;
    }

    private static Button MakeColorButton(Transform parent, string name, Vector2 pos, Vector2 sizeDelta, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = sizeDelta;
        go.GetComponent<Image>().color = color;
        return go.GetComponent<Button>();
    }

    private void SetStatus(string message)
    {
        if (_statusText != null) _statusText.text = message;
        Debug.Log($"[MultiplayerMenuUI] {message}");
    }

    private static void EnsureEventSystem()
    {
        var existing = FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>();
        if (existing != null) return;
        GameObject es = new GameObject("EventSystem");
        es.AddComponent<UnityEngine.EventSystems.EventSystem>();
        es.AddComponent<InputSystemUIInputModule>();
    }
}
