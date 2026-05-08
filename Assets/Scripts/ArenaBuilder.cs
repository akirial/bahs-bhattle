using UnityEngine;

/// <summary>
/// Procedurally builds a simple arena at runtime: floor, four walls, lighting,
/// player spawn points and a boss spawn point. Attach to a GameObject in the
/// scene (e.g. the GameManager). Designed to keep the MVP fully code-driven
/// so the scene only needs a couple of components wired up.
/// </summary>
public class ArenaBuilder : MonoBehaviour
{
    [Header("Arena")]
    [Tooltip("Width and depth of the floor (square).")]
    public float arenaSize = 5000f;

    [Tooltip("Wall height in world units.")]
    public float wallHeight = 30f;

    [Tooltip("Wall thickness in world units.")]
    public float wallThickness = 6f;

    [Header("Spawns")]
    [Tooltip("Number of player spawn points to create around the arena.")]
    public int playerSpawnCount = 4;

    [Tooltip("How far players spawn from the boss (center of arena).")]
    public float spawnDistFromBoss = 60f;

    [Header("Obstacles")]
    [Tooltip("How many pillar obstacles to spawn inside the arena.")]
    public int pillarCount = 18;

    [Tooltip("Pillar radius in world units.")]
    public float pillarRadius = 2.0f;

    [Tooltip("Extra clearance from the arena walls.")]
    public float pillarWallInset = 10f;

    [Tooltip("Keep pillars away from the player spawn strip (negative Z).")]
    public float pillarSpawnClearZ = 18f;

    [Tooltip("Keep pillars away from the boss spawn at center.")]
    public float pillarBossClearRadius = 10f;

    [Header("Climb Cubes")]
    [Tooltip("How many small climbable cubes to spawn inside the arena.")]
    public int climbCubeCount = 18;

    [Tooltip("Minimum cube size (uniform) in world units.")]
    public float climbCubeMinSize = 2.2f;

    [Tooltip("Maximum cube size (uniform) in world units.")]
    public float climbCubeMaxSize = 4.5f;

    [Tooltip("Maximum cube height (top) in world units.")]
    public float climbCubeMaxTop = 6f;

    [Header("Auto-build")]
    [Tooltip("Build automatically on Awake. Disable to call Build() manually.")]
    public bool buildOnAwake = true;

    public Transform[] PlayerSpawnPoints { get; private set; }
    public Transform BossSpawnPoint { get; private set; }

    private static ArenaBuilder _instance;
    public static ArenaBuilder Instance => _instance;

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;

        arenaSize = 120f;
        wallHeight = 20f;
        wallThickness = 4f;
        spawnDistFromBoss = 30f;

        if (buildOnAwake)
        {
            Build();
        }
    }

    /// <summary>
    /// Build the arena. Safe to call once. Creates floor, walls, light, and spawns.
    /// </summary>
    public void Build()
    {
        Transform arenaRoot = new GameObject("ArenaRoot").transform;
        arenaRoot.SetParent(transform, false);

        Material floorMat = MakeURPMaterial(new Color(0.35f, 0.35f, 0.4f));
        Material wallMat = MakeURPMaterial(new Color(0.55f, 0.55f, 0.6f));
        Material pillarMat = MakeURPMaterial(new Color(0.02f, 0.02f, 0.02f));
        Material climbMat = MakeURPMaterial(new Color(0.28f, 0.28f, 0.32f));

        BuildFloor(arenaRoot, floorMat);
        BuildWalls(arenaRoot, wallMat);
        EnsureLighting(arenaRoot);
        BuildSpawnPoints(arenaRoot);
        BuildPillars(arenaRoot, pillarMat);
        BuildClimbCubes(arenaRoot, climbMat);
    }

    private void BuildFloor(Transform parent, Material mat)
    {
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "Floor";
        floor.transform.SetParent(parent, false);
        floor.transform.localScale = new Vector3(arenaSize, 0.5f, arenaSize);
        floor.transform.localPosition = new Vector3(0f, -0.25f, 0f);
        var renderer = floor.GetComponent<MeshRenderer>();
        if (renderer != null) renderer.sharedMaterial = mat;
    }

    private void BuildWalls(Transform parent, Material mat)
    {
        float half = arenaSize * 0.5f;
        float yCenter = wallHeight * 0.5f;

        BuildWall(parent, mat, "WallNorth",
            new Vector3(0f, yCenter, half + wallThickness * 0.5f),
            new Vector3(arenaSize + wallThickness * 2f, wallHeight, wallThickness));
        BuildWall(parent, mat, "WallSouth",
            new Vector3(0f, yCenter, -(half + wallThickness * 0.5f)),
            new Vector3(arenaSize + wallThickness * 2f, wallHeight, wallThickness));
        BuildWall(parent, mat, "WallEast",
            new Vector3(half + wallThickness * 0.5f, yCenter, 0f),
            new Vector3(wallThickness, wallHeight, arenaSize));
        BuildWall(parent, mat, "WallWest",
            new Vector3(-(half + wallThickness * 0.5f), yCenter, 0f),
            new Vector3(wallThickness, wallHeight, arenaSize));
    }

    private void BuildWall(Transform parent, Material mat, string name, Vector3 pos, Vector3 scale)
    {
        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = name;
        wall.transform.SetParent(parent, false);
        wall.transform.localPosition = pos;
        wall.transform.localScale = scale;
        var renderer = wall.GetComponent<MeshRenderer>();
        if (renderer != null) renderer.sharedMaterial = mat;
    }

    private void EnsureLighting(Transform parent)
    {
        // Only add a directional light if the scene doesn't already have one.
        Light existing = FindFirstObjectByType<Light>();
        if (existing != null && existing.type == LightType.Directional) return;

        GameObject lightGO = new GameObject("ArenaLight");
        lightGO.transform.SetParent(parent, false);
        lightGO.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        Light light = lightGO.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.1f;
        light.color = Color.white;
        light.shadows = LightShadows.Soft;
    }

    private void BuildSpawnPoints(Transform parent)
    {
        Transform spawnRoot = new GameObject("SpawnPoints").transform;
        spawnRoot.SetParent(parent, false);

        int count = Mathf.Max(1, playerSpawnCount);
        PlayerSpawnPoints = new Transform[count];

        float spacing = 8f;
        float groupWidth = (count - 1) * spacing;
        float startX = -groupWidth * 0.5f;

        for (int i = 0; i < count; i++)
        {
            GameObject sp = new GameObject($"PlayerSpawn_{i}");
            sp.transform.SetParent(spawnRoot, false);
            float x = startX + i * spacing;
            sp.transform.localPosition = new Vector3(x, 1f, -spawnDistFromBoss);
            sp.transform.localRotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
            PlayerSpawnPoints[i] = sp.transform;
        }

        GameObject bossSpawn = new GameObject("BossSpawn");
        bossSpawn.transform.SetParent(spawnRoot, false);
        bossSpawn.transform.localPosition = new Vector3(0f, 8f, 0f);
        bossSpawn.transform.localRotation = Quaternion.identity;
        BossSpawnPoint = bossSpawn.transform;
    }

    private void BuildPillars(Transform parent, Material mat)
    {
        int count = Mathf.Clamp(pillarCount, 0, 64);
        if (count <= 0) return;

        Transform obstaclesRoot = new GameObject("Obstacles").transform;
        obstaclesRoot.SetParent(parent, false);

        float half = arenaSize * 0.5f;
        float inset = Mathf.Max(pillarWallInset, pillarRadius + 1f);
        float minX = -(half - inset);
        float maxX = (half - inset);
        float minZ = -(half - inset);
        float maxZ = (half - inset);

        // Ensure pillars are never taller than the wall (slightly shorter for safety).
        float height = Mathf.Max(2f, wallHeight - 1f);

        // Keep them away from the player spawn strip and the center boss spawn.
        float spawnZ = -spawnDistFromBoss;
        float spawnClearMinZ = spawnZ - pillarSpawnClearZ;
        float spawnClearMaxZ = spawnZ + pillarSpawnClearZ;

        int placed = 0;
        int attempts = 0;
        int maxAttempts = 400;
        while (placed < count && attempts++ < maxAttempts)
        {
            float x = Random.Range(minX, maxX);
            float z = Random.Range(minZ, maxZ);

            // Avoid spawn strip near players.
            if (z >= spawnClearMinZ && z <= spawnClearMaxZ) continue;

            // Avoid boss center area.
            if (new Vector2(x, z).sqrMagnitude < pillarBossClearRadius * pillarBossClearRadius) continue;

            GameObject p = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            p.name = $"Pillar_{placed:00}";
            p.transform.SetParent(obstaclesRoot, false);
            p.transform.localScale = new Vector3(pillarRadius * 2f, height * 0.5f, pillarRadius * 2f);
            p.transform.localPosition = new Vector3(x, height * 0.5f, z);

            var renderer = p.GetComponent<MeshRenderer>();
            if (renderer != null) renderer.sharedMaterial = mat;

            placed++;
        }
    }

    private void BuildClimbCubes(Transform parent, Material mat)
    {
        int count = Mathf.Clamp(climbCubeCount, 0, 128);
        if (count <= 0) return;

        Transform obstaclesRoot = parent.Find("Obstacles");
        if (obstaclesRoot == null)
        {
            obstaclesRoot = new GameObject("Obstacles").transform;
            obstaclesRoot.SetParent(parent, false);
        }

        float half = arenaSize * 0.5f;
        float inset = Mathf.Max(pillarWallInset, 4f);
        float minX = -(half - inset);
        float maxX = (half - inset);
        float minZ = -(half - inset);
        float maxZ = (half - inset);

        float spawnZ = -spawnDistFromBoss;
        float spawnClearMinZ = spawnZ - pillarSpawnClearZ;
        float spawnClearMaxZ = spawnZ + pillarSpawnClearZ;

        float minSize = Mathf.Clamp(climbCubeMinSize, 1f, 20f);
        float maxSize = Mathf.Clamp(climbCubeMaxSize, minSize, 25f);
        float maxTop = Mathf.Clamp(climbCubeMaxTop, 1f, wallHeight - 1f);

        int placed = 0;
        int attempts = 0;
        int maxAttempts = 600;
        while (placed < count && attempts++ < maxAttempts)
        {
            float x = Random.Range(minX, maxX);
            float z = Random.Range(minZ, maxZ);

            if (z >= spawnClearMinZ && z <= spawnClearMaxZ) continue;
            if (new Vector2(x, z).sqrMagnitude < pillarBossClearRadius * pillarBossClearRadius) continue;

            float size = Random.Range(minSize, maxSize);
            float top = Random.Range(size, maxTop);
            float height = Mathf.Max(size, top);

            GameObject c = GameObject.CreatePrimitive(PrimitiveType.Cube);
            c.name = $"ClimbCube_{placed:00}";
            c.transform.SetParent(obstaclesRoot, false);
            c.transform.localScale = new Vector3(size, height, size);
            c.transform.localPosition = new Vector3(x, height * 0.5f, z);

            var renderer = c.GetComponent<MeshRenderer>();
            if (renderer != null) renderer.sharedMaterial = mat;

            placed++;
        }
    }

    /// <summary>
    /// Returns a player spawn point, cycling through the array based on index.
    /// </summary>
    public Transform GetPlayerSpawn(int index)
    {
        if (PlayerSpawnPoints == null || PlayerSpawnPoints.Length == 0) return transform;
        return PlayerSpawnPoints[index % PlayerSpawnPoints.Length];
    }

    /// <summary>
    /// Builds a runtime material that works with URP's Lit shader (and falls
    /// back to the standard shader if URP isn't present).
    /// </summary>
    private static Material MakeURPMaterial(Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        Material m = new Material(shader);
        // URP uses _BaseColor; Standard uses _Color. Set both so either works.
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
        if (m.HasProperty("_Color")) m.SetColor("_Color", color);
        return m;
    }
}
