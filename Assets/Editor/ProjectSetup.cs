using System;
using System.Collections.Generic;
using Photon.Pun;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One-click PUN 2 setup. Run from:
/// Tools -> Boss Battle MVP -> Build Photon Everything
/// </summary>
public static class ProjectSetup
{
    private const string ResourcesFolder = "Assets/Resources";
    private const string MatFolder = "Assets/Materials";

    [MenuItem("Tools/Boss Battle MVP/Build Photon Everything")]
    public static void BuildEverything()
    {
        CreateFolders();
        Material whiteMat = CreateMaterial("BossWhite", Color.white);
        Material redMat = CreateMaterial("ProjectileRed", new Color(0.9f, 0.15f, 0.15f));
        Material playerMat = CreateMaterial("PlayerBlue", new Color(0.2f, 0.45f, 0.85f));

        Material orangeMat = CreateMaterial("ShockwaveOrange", new Color(1f, 0.5f, 0.1f, 0.7f));
        Material meteorMat = CreateMaterial("MeteorOrange", new Color(1f, 0.35f, 0.05f));
        Material miniCubeMat = CreateMaterial("MiniCubeOrange", new Color(1f, 0.45f, 0.12f));

        GameObject playerPrefab = BuildPlayerPrefab(playerMat);
        GameObject bossPrefab = BuildBossPrefab(whiteMat);
        GameObject projectilePrefab = BuildProjectilePrefab(redMat);
        BuildShockwaveRingPrefab(orangeMat);
        BuildMeteorPrefab(meteorMat);
        BuildMiniCubePrefab(miniCubeMat);

        SetupScene(playerPrefab, bossPrefab, projectilePrefab);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[ProjectSetup] Photon setup complete. Import PUN 2 and enter your Photon App ID if you have not already.");
    }

    [MenuItem("Tools/Boss Battle MVP/Fix Voice Clip Import Settings")]
    public static void FixVoiceClipImportSettings()
    {
        string root = "Assets/Audio/BossVoice";
        if (!AssetDatabase.IsValidFolder(root))
        {
            Debug.LogError("[ProjectSetup] Voice clip folder not found: " + root);
            return;
        }

        string[] guids = AssetDatabase.FindAssets("t:AudioClip", new[] { root });
        int count = 0;
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            AudioImporter importer = AssetImporter.GetAtPath(path) as AudioImporter;
            if (importer == null) continue;

            AudioImporterSampleSettings settings = importer.defaultSampleSettings;
            bool needsReimport = false;

            if (settings.compressionFormat != AudioCompressionFormat.PCM)
            {
                settings.compressionFormat = AudioCompressionFormat.PCM;
                needsReimport = true;
            }
            if (settings.loadType != AudioClipLoadType.DecompressOnLoad)
            {
                settings.loadType = AudioClipLoadType.DecompressOnLoad;
                needsReimport = true;
            }
            if (settings.sampleRateSetting != AudioSampleRateSetting.PreserveSampleRate)
            {
                settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
                needsReimport = true;
            }
            if (importer.forceToMono)
            {
                importer.forceToMono = false;
                needsReimport = true;
            }

            bool is3D = false;
            var so = new SerializedObject(importer);
            var prop3D = so.FindProperty("m_3D");
            if (prop3D != null && prop3D.boolValue)
            {
                prop3D.boolValue = false;
                so.ApplyModifiedProperties();
                needsReimport = true;
            }

            if (settings.preloadAudioData == false)
            {
                settings.preloadAudioData = true;
                needsReimport = true;
            }

            if (needsReimport)
            {
                importer.defaultSampleSettings = settings;
                importer.SaveAndReimport();
                count++;
            }
        }
        Debug.Log($"[ProjectSetup] Reimported {count}/{guids.Length} voice clips with PCM/2D settings.");
    }

    [MenuItem("Tools/Boss Battle MVP/Assign Boss Voice Clips")]
    public static void AssignBossVoiceClips()
    {
        string prefabPath = $"{ResourcesFolder}/Boss.prefab";
        GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefabAsset == null)
        {
            Debug.LogError("[ProjectSetup] Boss prefab not found at " + prefabPath);
            return;
        }

        GameObject instance = PrefabUtility.InstantiatePrefab(prefabAsset) as GameObject;
        if (instance == null)
        {
            Debug.LogError("[ProjectSetup] Failed to instantiate Boss prefab.");
            return;
        }

        BossVoiceManager voice = instance.GetComponent<BossVoiceManager>();
        if (voice == null)
            voice = instance.AddComponent<BossVoiceManager>();

        AudioSource audioSrc = instance.GetComponent<AudioSource>();
        if (audioSrc == null)
            audioSrc = instance.AddComponent<AudioSource>();
        audioSrc.spatialBlend = 0f;
        audioSrc.dopplerLevel = 0f;
        audioSrc.bypassEffects = true;
        audioSrc.bypassListenerEffects = true;
        audioSrc.bypassReverbZones = true;
        audioSrc.pitch = 1f;
        audioSrc.playOnAwake = false;
        voice.audioSource = audioSrc;

        string root = "Assets/Audio/BossVoice";
        voice.introClips          = LoadClipsInFolder($"{root}/Intro");
        voice.slamClips           = LoadClipsInFolder($"{root}/Slam");
        voice.delayedFakeoutClips = LoadClipsInFolder($"{root}/DelayedFakeout");
        voice.rollClips           = LoadClipsInFolder($"{root}/Roll");
        voice.laserClips          = LoadClipsInFolder($"{root}/Laser");
        voice.bigLaserClips       = LoadClipsInFolder($"{root}/BigLaser");
        voice.fakeoutClips        = LoadClipsInFolder($"{root}/Fakeout");
        voice.bossHurtClips       = LoadClipsInFolder($"{root}/BossHurt");
        voice.playerHitClips      = LoadClipsInFolder($"{root}/PlayerHit");
        voice.phaseTwoClips       = LoadClipsInFolder($"{root}/PhaseTwo");
        voice.deathClips          = LoadClipsInFolder($"{root}/Death");

        PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
        UnityEngine.Object.DestroyImmediate(instance);

        int total = (voice.introClips?.Length ?? 0) + (voice.slamClips?.Length ?? 0)
            + (voice.delayedFakeoutClips?.Length ?? 0) + (voice.rollClips?.Length ?? 0)
            + (voice.laserClips?.Length ?? 0) + (voice.bigLaserClips?.Length ?? 0)
            + (voice.fakeoutClips?.Length ?? 0) + (voice.bossHurtClips?.Length ?? 0)
            + (voice.playerHitClips?.Length ?? 0) + (voice.phaseTwoClips?.Length ?? 0)
            + (voice.deathClips?.Length ?? 0);

        Debug.Log($"[ProjectSetup] Assigned {total} voice clips to BossVoiceManager on Boss prefab.");
    }

    private static AudioClip[] LoadClipsInFolder(string folderPath)
    {
        if (!AssetDatabase.IsValidFolder(folderPath))
        {
            Debug.LogWarning($"[ProjectSetup] Folder not found: {folderPath}");
            return new AudioClip[0];
        }

        string[] guids = AssetDatabase.FindAssets("t:AudioClip", new[] { folderPath });
        List<AudioClip> clips = new List<AudioClip>();
        foreach (string guid in guids)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(assetPath);
            if (clip != null) clips.Add(clip);
        }
        clips.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.Ordinal));
        return clips.ToArray();
    }

    private static void CreateFolders()
    {
        if (!AssetDatabase.IsValidFolder(ResourcesFolder))
            AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder(MatFolder))
            AssetDatabase.CreateFolder("Assets", "Materials");
    }

    private static Material CreateMaterial(string name, Color color)
    {
        string path = $"{MatFolder}/{name}.mat";
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        Material material = new Material(shader);
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    private static GameObject BuildPlayerPrefab(Material mat)
    {
        string path = $"{ResourcesFolder}/Player.prefab";
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null)
        {
            Debug.Log("[ProjectSetup] Player prefab already exists, skipping.");
            return existing;
        }

        GameObject root = new GameObject("Player");

        CharacterController cc = root.AddComponent<CharacterController>();
        cc.center = new Vector3(0f, 1f, 0f);
        cc.height = 2f;
        cc.radius = 0.4f;

        PhotonView view = root.AddComponent<PhotonView>();

        GameObject camHolder = new GameObject("CameraHolder");
        camHolder.transform.SetParent(root.transform, false);
        camHolder.transform.localPosition = new Vector3(0f, 1.6f, 0f);

        GameObject camGO = new GameObject("PlayerCamera");
        camGO.transform.SetParent(camHolder.transform, false);
        camGO.tag = "MainCamera";
        Camera cam = camGO.AddComponent<Camera>();
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 50000f;
        cam.fieldOfView = 70f;
        AudioListener listener = camGO.AddComponent<AudioListener>();

        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        body.name = "PlayerBody";
        body.transform.SetParent(root.transform, false);
        body.transform.localPosition = new Vector3(0f, 1f, 0f);
        body.transform.localScale = new Vector3(0.8f, 1.8f, 0.8f);
        UnityEngine.Object.DestroyImmediate(body.GetComponent<Collider>());
        if (mat != null) body.GetComponent<MeshRenderer>().sharedMaterial = mat;

        NetworkPlayerController controller = root.AddComponent<NetworkPlayerController>();
        controller.cameraPivot = camHolder.transform;
        controller.playerCamera = cam;
        controller.playerAudioListener = listener;
        controller.bodyVisual = body;

        root.AddComponent<NetworkPlayerHealth>();
        NetworkGunController gun = root.AddComponent<NetworkGunController>();
        gun.shootCamera = cam;

        GameObject canvasGO = new GameObject("PlayerCanvas");
        canvasGO.transform.SetParent(root.transform, false);
        canvasGO.AddComponent<GameUI>();

        TryAddPhotonTransformView(root, view, controller);

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        UnityEngine.Object.DestroyImmediate(root);
        Debug.Log($"[ProjectSetup] Created Player prefab at {path}");
        return prefab;
    }

    private static GameObject BuildBossPrefab(Material mat)
    {
        string path = $"{ResourcesFolder}/Boss.prefab";
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null)
        {
            Debug.Log("[ProjectSetup] Boss prefab already exists, skipping.");
            return existing;
        }

        GameObject root = GameObject.CreatePrimitive(PrimitiveType.Cube);
        root.name = "Boss";
        root.transform.localScale = new Vector3(4f, 4f, 4f);
        if (mat != null) root.GetComponent<MeshRenderer>().sharedMaterial = mat;

        root.AddComponent<PhotonView>();
        NetworkBossHealth health = root.AddComponent<NetworkBossHealth>();
        health.bossRenderer = root.GetComponent<MeshRenderer>();
        BossCubeAnimator animator = root.AddComponent<BossCubeAnimator>();
        animator.bossRenderer = root.GetComponent<MeshRenderer>();
        root.AddComponent<NetworkBossAttack>();

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        UnityEngine.Object.DestroyImmediate(root);
        Debug.Log($"[ProjectSetup] Created Boss prefab at {path}");
        return prefab;
    }

    private static GameObject BuildProjectilePrefab(Material mat)
    {
        string path = $"{ResourcesFolder}/BossProjectile.prefab";
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null)
        {
            Debug.Log("[ProjectSetup] BossProjectile prefab already exists, skipping.");
            return existing;
        }

        GameObject root = GameObject.CreatePrimitive(PrimitiveType.Cube);
        root.name = "BossProjectile";
        root.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f);
        if (mat != null) root.GetComponent<MeshRenderer>().sharedMaterial = mat;

        BoxCollider col = root.GetComponent<BoxCollider>();
        if (col != null) col.isTrigger = true;

        PhotonView view = root.AddComponent<PhotonView>();
        NetworkBossProjectile projectile = root.AddComponent<NetworkBossProjectile>();
        TryAddPhotonTransformView(root, view, projectile);

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        UnityEngine.Object.DestroyImmediate(root);
        Debug.Log($"[ProjectSetup] Created BossProjectile prefab at {path}");
        return prefab;
    }

    private static void BuildShockwaveRingPrefab(Material mat)
    {
        string path = $"{ResourcesFolder}/BossShockwaveRing.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
        {
            Debug.Log("[ProjectSetup] BossShockwaveRing prefab already exists, skipping.");
            return;
        }

        // Use a cylinder as the ring visual. Scale it flat and wide at runtime.
        GameObject root = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        root.name = "BossShockwaveRing";
        root.transform.localScale = new Vector3(1f, 0.3f, 1f);

        // Remove the default collider; damage is handled by script overlap checks.
        UnityEngine.Object.DestroyImmediate(root.GetComponent<Collider>());

        if (mat != null) root.GetComponent<MeshRenderer>().sharedMaterial = mat;

        root.AddComponent<PhotonView>();
        root.AddComponent<BossShockwaveRing>();

        PrefabUtility.SaveAsPrefabAsset(root, path);
        UnityEngine.Object.DestroyImmediate(root);
        Debug.Log($"[ProjectSetup] Created BossShockwaveRing prefab at {path}");
    }

    private static void BuildMeteorPrefab(Material mat)
    {
        string path = $"{ResourcesFolder}/BossMeteor.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
        {
            Debug.Log("[ProjectSetup] BossMeteor prefab already exists, skipping.");
            return;
        }

        GameObject root = GameObject.CreatePrimitive(PrimitiveType.Cube);
        root.name = "BossMeteor";
        root.transform.localScale = new Vector3(1.5f, 1.5f, 1.5f);

        BoxCollider col = root.GetComponent<BoxCollider>();
        if (col != null) col.isTrigger = true;

        if (mat != null) root.GetComponent<MeshRenderer>().sharedMaterial = mat;

        PhotonView view = root.AddComponent<PhotonView>();
        BossMeteor meteor = root.AddComponent<BossMeteor>();
        TryAddPhotonTransformView(root, view, meteor);

        PrefabUtility.SaveAsPrefabAsset(root, path);
        UnityEngine.Object.DestroyImmediate(root);
        Debug.Log($"[ProjectSetup] Created BossMeteor prefab at {path}");
    }

    private static void BuildMiniCubePrefab(Material mat)
    {
        string path = $"{ResourcesFolder}/BossMiniCube.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
        {
            Debug.Log("[ProjectSetup] BossMiniCube prefab already exists, skipping.");
            return;
        }

        GameObject root = GameObject.CreatePrimitive(PrimitiveType.Cube);
        root.name = "BossMiniCube";
        root.transform.localScale = new Vector3(0.8f, 0.8f, 0.8f);
        if (mat != null) root.GetComponent<MeshRenderer>().sharedMaterial = mat;

        // Keep solid colliders so bullets can hit; contact damage uses overlap sphere.
        PhotonView view = root.AddComponent<PhotonView>();
        BossMiniCube mini = root.AddComponent<BossMiniCube>();
        mini.maxHealth = 30;
        mini.contactDamage = 15;

        TryAddPhotonTransformView(root, view, mini);

        PrefabUtility.SaveAsPrefabAsset(root, path);
        UnityEngine.Object.DestroyImmediate(root);
        Debug.Log($"[ProjectSetup] Created BossMiniCube prefab at {path}");
    }

    private static void SetupScene(GameObject playerPrefab, GameObject bossPrefab, GameObject projectilePrefab)
    {
        Camera defaultCam = Camera.main;
        if (defaultCam != null && defaultCam.GetComponentInParent<PhotonView>() == null)
        {
            UnityEngine.Object.DestroyImmediate(defaultCam.gameObject);
        }

        GameObject gm = GameObject.Find("GameManager");
        if (gm == null) gm = new GameObject("GameManager");

        NetworkGameManager manager = gm.GetComponent<NetworkGameManager>();
        if (manager == null) manager = gm.AddComponent<NetworkGameManager>();
        manager.playerPrefabName = playerPrefab != null ? playerPrefab.name : "Player";
        manager.bossPrefabName = bossPrefab != null ? bossPrefab.name : "Boss";

        if (gm.GetComponent<ArenaBuilder>() == null)
            gm.AddComponent<ArenaBuilder>();

        GameObject menuCanvas = GameObject.Find("MenuCanvas");
        if (menuCanvas == null) menuCanvas = new GameObject("MenuCanvas");
        if (menuCanvas.GetComponent<MultiplayerMenuUI>() == null)
            menuCanvas.AddComponent<MultiplayerMenuUI>();

        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
        Debug.Log("[ProjectSetup] Scene setup complete.");
    }

    /// <summary>
    /// Adds PUN's built-in transform view when available and observes it.
    /// If a PUN version exposes a different class name, we fall back to
    /// observing the gameplay component itself (both player/projectile implement
    /// their own transform serialization paths).
    /// </summary>
    private static void TryAddPhotonTransformView(GameObject root, PhotonView view, Component fallbackObserved)
    {
        Component observed = null;
        Type transformViewType =
            Type.GetType("Photon.Pun.PhotonTransformView, Assembly-CSharp") ??
            Type.GetType("Photon.Pun.PhotonTransformViewClassic, Assembly-CSharp");

        if (transformViewType != null && root.GetComponent(transformViewType) == null)
        {
            observed = root.AddComponent(transformViewType);
        }

        if (observed == null)
            observed = fallbackObserved;

        view.ObservedComponents = new List<Component> { observed };
        view.Synchronization = ViewSynchronization.UnreliableOnChange;
    }
}
