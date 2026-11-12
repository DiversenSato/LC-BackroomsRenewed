using System.Diagnostics;
using EasyTextEffects.Editor.MyBoxCopy.Extensions;
using GameNetcodeStuff;
using Unity.AI.Navigation;
using UnityEngine.AI;
using Logger = UnityEngine.Logger;

namespace VELDDev.BackroomsRenewed.BackroomsManagement;

public class Backrooms : NetworkBehaviour
{
    const float CELL_SIZE = 16f; // may be modified depending on how big I make the cells in blender

    public static Backrooms Instance;

    public List<BackroomThemeInfo> themes;          // Assign in inspector, available backroom themes
    public Transform CellsHolder;                   // Assign in inspector, parent transform for all cells
    public NavMeshSurface BackroomsNavMesh;          // Assign in inspector, the NavMeshSurface component to build the navmesh
    public GameObject BackroomsLightCover;          // Assign in inspector, light cover prefab to place above the backrooms to prevent light from leaking
    public BackroomsGenerator generator;            // Assign in inspector, the maze generator component
    public AnimationCurve lightTwinkleLightCurve;
    public AudioSource ambientMusicSource;
    public AudioSource ambientNoiseSource;

    [HideInInspector]
    public NetworkVariable<bool> IsGenerated = new(false);

    [HideInInspector] public NetworkList<ulong> PlayersInBackrooms;

    [HideInInspector] public CellBehaviour[,] Cells;

    public BackroomThemeInfo CurrentTheme { get; private set; }

    private readonly Dictionary<CellVariantInfo, int> _variantUsageCount = [];
    private float _timeSinceLastTwinkleCheck = 0f;
    private float _nextTwinkleCheckTime = 0f;
    
    private ManualLogSource Logger => Plugin.Instance.logger;

    void Awake()
    {
        PlayersInBackrooms = new NetworkList<ulong>();
        
        if(!Instance)
        {
            Instance = this;
        }
    }

    void Start()
    {
        generator.algorithm = LocalConfig.Singleton.GenerationAlgorithm.Value;
        var size = Random.Range(LocalConfig.Singleton.MinBackroomsSize.Value, LocalConfig.Singleton.MaxBackroomsSize.Value + 1);
        generator.width = size;
        generator.height = size;
        generator.exitCount = LocalConfig.Singleton.ExitCount.Value;
        if(IsServer)
        {
            StartCoroutine(GenerateBackrooms());
        }
    }

    private void SelectTheme()
    {
        if (themes == null || themes.Count == 0)
        {
            Logger.LogError("No themes configured! Cannot generate backrooms.");
            return;
        }

        // Single theme - no need for weighted selection
        if (themes.Count == 1)
        {
            CurrentTheme = themes[0];
            Logger.LogInfo($"Theme selected: {CurrentTheme.themeName}");
            return;
        }

        // Weighted random selection
        float totalWeight = themes.Sum(t => t.weight);
        float randomValue = Random.Range(0f, totalWeight);
        float cumulativeWeight = 0f;

        foreach (var theme in themes)
        {
            cumulativeWeight += theme.weight;
            if (randomValue <= cumulativeWeight)
            {
                CurrentTheme = theme;
                Logger.LogInfo($"Theme selected: {CurrentTheme.themeName}");
                return;
            }
        }

        // Fallback
        CurrentTheme = themes[^1];
        Logger.LogInfo($"Theme selected: {CurrentTheme.themeName}");
    }

    void FixedUpdate()
    {
        // Not denesting in case I'm adding more stuff
        if(IsServer && IsGenerated.Value)
        {
            if(_timeSinceLastTwinkleCheck < _nextTwinkleCheckTime)
            {
                _timeSinceLastTwinkleCheck += Time.deltaTime;
                return;
            }

            TwinkleRandomLightsClientRpc();
            _timeSinceLastTwinkleCheck = 0f;
            _nextTwinkleCheckTime = Random.Range(3f, 15f);
            Logger.LogInfo($"Twinkle check completed, next check in {_nextTwinkleCheckTime} seconds");
        }
    }

    /// <summary>
    /// Teleports a player to a random position in the backrooms.
    /// This method is server-authoritative - it determines the position and broadcasts to all clients.
    /// </summary>
    /// <param name="targetPlayer">The player to teleport</param>
    /// <param name="dropItems">Whether to drop held items before teleporting</param>
    public void TeleportPlayerToBackrooms(PlayerControllerB targetPlayer, bool dropItems = false)
    {
        if (IsServer)
        {
            ServerTeleportPlayer(targetPlayer.playerClientId, dropItems);
        }
        else
        {
            // Client requests teleportation from server
            RequestTeleportServerRpc(targetPlayer.playerClientId, dropItems);
        }
    }

    /// <summary>
    /// Server-side: picks a floor position inside the maze and broadcasts the teleport.
    /// If the maze isn't generated yet, waits for it instead of dropping the player into the void.
    /// </summary>
    private void ServerTeleportPlayer(ulong playerClientId, bool dropItems)
    {
        if (!IsGenerated.Value)
        {
            Logger.LogInfo($"Backrooms not generated yet, delaying teleport of player {playerClientId}");
            StartCoroutine(TeleportWhenGenerated(playerClientId, dropItems));
            return;
        }

        var randomPos = PickRandomPosOnNavmesh();
        var targetPos = randomPos ?? GetFallbackPosition();

        if (!randomPos.HasValue)
        {
            Logger.LogWarning("Failed to find valid NavMesh position, using fallback cell position");
        }

        // NetworkList can only be mutated on the server.
        if (!PlayersInBackrooms.Contains(playerClientId))
        {
            PlayersInBackrooms.Add(playerClientId);
        }

        TeleportPlayerClientRpc(playerClientId, targetPos, dropItems);
    }

    private IEnumerator TeleportWhenGenerated(ulong playerClientId, bool dropItems)
    {
        yield return new WaitUntil(() => IsGenerated.Value);
        ServerTeleportPlayer(playerClientId, dropItems);
    }

    /// <summary>
    /// Legacy method for backwards compatibility. Calls TeleportPlayerToBackrooms.
    /// </summary>
    [Obsolete("Use TeleportPlayerToBackrooms instead for server-authoritative teleportation")]
    public void TeleportLocalPlayerSomewhereInBackrooms(PlayerControllerB targetPlayer)
    {
        TeleportPlayerToBackrooms(targetPlayer, SyncedConfig.Instance.DropHeldItemsOnTeleport);
    }

    /// <summary>
    /// Request the server to teleport a player to the backrooms.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void RequestTeleportServerRpc(ulong playerClientId, bool dropItems)
    {
        ServerTeleportPlayer(playerClientId, dropItems);
    }

    /// <summary>
    /// Server broadcasts teleportation to all clients. Each client executes teleport for the target player.
    /// </summary>
    [ClientRpc]
    private void TeleportPlayerClientRpc(ulong playerClientId, Vector3 position, bool dropItems)
    {
        var targetPlayer = GetPlayerByClientId(playerClientId);
        if (targetPlayer == null)
        {
            Logger.LogWarning($"TeleportPlayerClientRpc: Could not find player with clientId {playerClientId}");
            return;
        }

        if (dropItems && targetPlayer.IsOwner)
        {
            targetPlayer.DropAllHeldItems();
            targetPlayer.DisableJetpackControlsLocally();
        }

        targetPlayer.TeleportPlayer(GetFloorSafePosition(position), true);
        targetPlayer.ResetFallGravity();
        targetPlayer.isInsideFactory = true;
        StartPlayingAmbientAudios();
    }

    /// <summary>
    /// Raycast down to the real local floor and place the player slightly above it.
    /// Players always land on top of it instead of clipping underneath.
    /// </summary>
    private Vector3 GetFloorSafePosition(Vector3 position)
    {
        const float SPAWN_HEIGHT_OFFSET = 2f;
        if (Physics.Raycast(position + Vector3.up * 2f, Vector3.down, out RaycastHit floorHit,
                12f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            return floorHit.point + Vector3.up * SPAWN_HEIGHT_OFFSET;
        }
        return position + Vector3.up * SPAWN_HEIGHT_OFFSET;
    }

    /// <summary>
    /// Removes a player from the backrooms tracking list. Server-authoritative.
    /// A NetworkList cannot be modified directly from a client.
    /// </summary>
    public void RemovePlayerFromBackrooms(ulong playerClientId)
    {
        if (IsServer)
        {
            if (PlayersInBackrooms.Contains(playerClientId))
            {
                PlayersInBackrooms.Remove(playerClientId);
            }
        }
        else
        {
            RemovePlayerFromBackroomsServerRpc(playerClientId);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void RemovePlayerFromBackroomsServerRpc(ulong playerClientId)
    {
        if (PlayersInBackrooms.Contains(playerClientId))
        {
            PlayersInBackrooms.Remove(playerClientId);
        }
    }

    /// <summary>
    /// Used when the navmesh isn't usable (e.g. still being built). Every cell has a floor at its pivot,
    /// so the center of a random cell is always a valid landing spot.
    /// </summary>
    private Vector3 GetFallbackPosition()
    {
        if (Cells != null)
        {
            for (int i = 0; i < 10; i++)
            {
                var cell = Cells[Random.Range(0, Cells.GetLength(0)), Random.Range(0, Cells.GetLength(1))];
                if (cell)
                    return cell.transform.position;
            }
        }

        // Cell (0, 0) sits at the holder's origin.
        return CellsHolder != null ? CellsHolder.position : transform.position;
    }

    private PlayerControllerB GetPlayerByClientId(ulong clientId)
    {
        foreach (var player in StartOfRound.Instance.allPlayerScripts)
        {
            if (player.playerClientId == clientId)
            {
                return player;
            }
        }
        return null;
    }

    private Vector3? PickRandomPosOnNavmesh(int maxAttempts = 30)
    {
        if (Cells == null)
            return null;

        // Cell pivots are at the center of their floor, so pick a random cell and a point inside it
        // (away from the walls). Picking from the grid's raw bounds would land half a cell outside the maze.
        const float INSET = CELL_SIZE / 2f - 1.5f;
        const float SAMPLE_RADIUS = 4f;
        const float MAX_FLOOR_HEIGHT_DELTA = 1f;

        int width = Cells.GetLength(0);
        int length = Cells.GetLength(1);

        for(int i = 0; i < maxAttempts; i++)
        {
            var cell = Cells[Random.Range(0, width), Random.Range(0, length)];
            if (!cell)
                continue;

            var floor = cell.transform.position;
            var randomPoint = floor + new Vector3(Random.Range(-INSET, INSET), 0f, Random.Range(-INSET, INSET));

            // Only accept navmesh at floor height, never anything on top of the maze.
            if(NavMesh.SamplePosition(randomPoint, out NavMeshHit hit, SAMPLE_RADIUS, NavMesh.AllAreas)
               && Mathf.Abs(hit.position.y - floor.y) <= MAX_FLOOR_HEIGHT_DELTA)
            {
                return hit.position;
            }
        }

        return null;
    }
    
    private IEnumerator GenerateBackrooms()
    {
        if(!NetworkManager.Singleton.IsHost && !IsServer)
            yield break;

        // Select theme for this generation
        SelectTheme();
        if (!CurrentTheme)
        {
            Logger.LogError($"Couldn't generate the backrooms: Selected theme is null !");
            yield break;
        }

        // Reset usage counter for new generation
        _variantUsageCount.Clear();
        foreach(var variant in CurrentTheme.CellsVariants)
        {
            _variantUsageCount[variant] = 0;
        }

        Logger.LogInfo("Starting generation...");
        yield return generator.Generate();
        Cells = new CellBehaviour[generator.width, generator.height];

        // Decide every cell's variant up front in two passes
        var variantLayout = BuildVariantLayout();
        
        // Set navmesh location and size
        /*
        var backroomsCenter = new Vector3((generator.width * CELL_SIZE) / 2f, 0, (generator.height * CELL_SIZE) / 2f);
        BackroomsNavMesh.center = backroomsCenter;
        BackroomsNavMesh.size = new Vector3(generator.width * CELL_SIZE, 1f, generator.height * CELL_SIZE);
        BackroomsNavMesh.navMeshData = new NavMeshData();
        BackroomsNavMesh.AddData();
        */

        Logger.LogInfo("Placing the cells in the world...");
        var sw = Stopwatch.StartNew();
        var globalSw = Stopwatch.StartNew();
        // Instantiate cells for all clients, should make a rectangle.
        for(int x = 0; x < generator.width; x++)
        {
            for(int y = 0; y < generator.height; y++)
            {
                var cell = generator.cells[x, y];
                var selectedVariant = variantLayout[x, y];
                var cellgo = Instantiate(selectedVariant.variantPrefab, CellsHolder);
                cellgo.transform.localPosition = new Vector3(CELL_SIZE * x, 0, CELL_SIZE * y);
                cellgo.GetComponent<NetworkObject>().Spawn(true);
                var cellmono = cellgo.GetComponent<CellBehaviour>();

                // It's hardcoded, it's not clean, but it's optimized and effective at least.
                if(y == 0)
                {
                    if(x == 0)
                    {
                        cell.walls |= WallFlags.West | WallFlags.South;
                    }
                    else if(x == generator.width - 1)
                    {
                        cell.walls |= WallFlags.East | WallFlags.South;
                    }
                    else
                    {
                        cell.walls |= WallFlags.South;
                    }
                }
                else if(y == generator.height - 1)
                {
                    if(x == 0)
                    {
                        cell.walls |= WallFlags.West | WallFlags.North;
                    }
                    else if(x == generator.width - 1)
                    {
                        cell.walls |= WallFlags.East | WallFlags.North;
                    }
                    else
                    {
                        cell.walls |= WallFlags.North;
                    }
                }
                else
                {
                    if(x == 0)
                    {
                        cell.walls |= WallFlags.West;
                    }
                    else if(x == generator.width - 1)
                    {
                        cell.walls |= WallFlags.East;
                    }
                    else
                    {
                        // This is my ingenious *cough* way to remove duplicate walls
                        // I remove these walls as the previous cells already have it !
                        // The next cell doesn't need to remove its walls !
                        // I call this easy optimization.
                        cell.walls &= ~(WallFlags.South | WallFlags.West);
                    }
                }

                const int LIGHT_GO_CHANCE_PERCENT = 60;
                const int LIGHT_ON_CHANCE_PERCENT = 90;
                var putLightFlag = Random.Range(0, 101) < LIGHT_GO_CHANCE_PERCENT;
                if(putLightFlag)
                {
                    var lightOnFlag = Random.Range(0, 101) < LIGHT_ON_CHANCE_PERCENT;
                    cellmono.InitializeClientRpc(cell, true, lightOnFlag);
                }
                else
                {
                    cellmono.InitializeClientRpc(cell, false, false);
                }
                Cells[x, y] = cellmono;
                
                //cellmono.gameObject.transform.SetParent(CellsHolder);
                
                /*
                if (!SyncedConfig.Instance.LegacyNavMeshGen && y % 5 == 0)
                {
                    sw.Restart();
                    yield return BackroomsNavMesh.UpdateNavMesh(BackroomsNavMesh.navMeshData);
                    sw.Stop();
                    Logger.LogInfo($"Navmesh refreshed in {sw.ElapsedMilliseconds:N3}ms");
                }
                */
                
                if (sw.ElapsedMilliseconds > 16)
                {
                    Logger.LogDebug($"Yielding frame; {sw.ElapsedMilliseconds:N0}ms elapsed since last frame.");
                    yield return null;
                    sw.Restart();
                }
            }
        }

        sw.Stop();
        globalSw.Stop();
        Logger.LogInfo($"Generated backrooms in {globalSw.ElapsedMilliseconds:N0}ms");

        Logger.LogInfo($"Spawning {generator.exitPositions.Count} exit door(s)...");
        foreach (var exit in generator.exitPositions)
        {
            var cellBehaviour = Cells[exit.position.x, exit.position.y];
            var socket = cellBehaviour.GetExitDoorSocket(exit.direction);
            if (socket == null)
            {
                Logger.LogWarning($"No exit socket found for cell {exit.position} facing {exit.direction}, skipping.");
                continue;
            }
            var exitGo = Instantiate(CurrentTheme.ExitPrefab, socket.position, socket.rotation);
            var exitNo = exitGo.GetComponent<NetworkObject>();
            exitNo.Spawn(true);
            _spawnedExits.Add(exitNo);
            Logger.LogDebug($"Spawned exit door at cell {exit.position} facing {exit.direction}");
        }

        // Final update in case the generation of
        // yield return BackroomsNavMesh.UpdateNavMesh(BackroomsNavMesh.navMeshData);

        Logger.LogInfo("Finished backrooms generation");
        IsGenerated.Value = true;
        SetupBackroomsClientRpc(generator.width, generator.height);
    }

    [ClientRpc]
    private void SetupBackroomsClientRpc(int width, int length)
    {
        // Adopt authoritative size sent by the server so the client navmesh is baked over the real area.
        generator.width = width;
        generator.height = length;

        // Set anti-light leak cover location and size
        BackroomsLightCover.transform.localPosition = GridLocalCenter(width, length, 10f);
        BackroomsLightCover.transform.localScale = new Vector3(width, 1f, length) * 1.5f;
        Logger.LogInfo("Set backrooms light cover");

        ConfigureNavMeshVolume(width, length);

        // Navmesh may be baked in the future by making one navmesh surface for each cell
        // and adding navmesh links between cells
        if (SyncedConfig.Instance.LegacyNavMeshGen)
        {
            var sw = Stopwatch.StartNew();
            BackroomsNavMesh.BuildNavMesh();
            sw.Stop();
            Logger.LogInfo($"Built navmesh in {sw.ElapsedMilliseconds:N0}ms");
        }
        else
        {
            Logger.LogInfo("Refreshing Navmesh...");
            var sw = Stopwatch.StartNew();
            StartCoroutine(RefreshNavmeshesAsync());
            sw.Stop();
            Logger.LogInfo($"Navmesh refreshed in {sw.ElapsedMilliseconds:N0}ms");
        }
        // More research to be done for this smh
    }

    /// <summary>
    /// Center of the cell grid in CellsHolder-local space. Cell pivots are at their floor's center,
    /// so the grid spans [-CELL_SIZE/2, (n - 0.5) * CELL_SIZE] on each axis.
    /// </summary>
    private static Vector3 GridLocalCenter(int width, int length, float y) =>
        new(((width - 1) * CELL_SIZE) / 2f, y, ((length - 1) * CELL_SIZE) / 2f);

    /// <summary>
    /// Restrict the navmesh bake to the maze itself, from just under the floor to just below the ceiling.
    /// Collecting "All" objects baked walkable navmesh on the cell roofs, the light cover, and the whole moon.
    /// </summary>
    private void ConfigureNavMeshVolume(int width, int length)
    {
        const float NAVMESH_MIN_Y = -1f;
        const float NAVMESH_MAX_Y = 7f; // ceilings sit at y = 8

        BackroomsNavMesh.collectObjects = CollectObjects.Volume;
        BackroomsNavMesh.center = GridLocalCenter(width, length, (NAVMESH_MIN_Y + NAVMESH_MAX_Y) / 2f);
        BackroomsNavMesh.size = new Vector3(width * CELL_SIZE + 2f, NAVMESH_MAX_Y - NAVMESH_MIN_Y, length * CELL_SIZE + 2f);
    }

    private IEnumerator RefreshNavmeshesAsync()
    {
        BackroomsNavMesh.navMeshData = new NavMeshData();
        BackroomsNavMesh.AddData();
        yield return BackroomsNavMesh.UpdateNavMesh(BackroomsNavMesh.navMeshData);
    }
    
    public override void OnDestroy()
    {
        if(Instance == this)
        {
            Instance = null;
        }
    }

    [ClientRpc]
    private void TwinkleRandomLightsClientRpc()
    {
        foreach(var cell in Cells)
        {
            if(!cell.hasLightSource)
                continue;
            var twinkleChance = Random.Range(0f, 100f);
            if(twinkleChance < 40f)
            {
                cell.TwinkleLight(lightTwinkleLightCurve, Random.Range(1f, lightTwinkleLightCurve.keys[^1].time));
            }
        }
    }

    private CellVariantInfo[,] BuildVariantLayout()
    {
        int width = generator.width;
        int height = generator.height;
        var layout = new CellVariantInfo[width, height];

        var basicVariants = CurrentTheme.CellsVariants
            .Where(v => !v.mustSpawnAtLeastOnce)
            .ToList();

        // First pass: fill cells with random weighted basic variant
        for(int x = 0; x < width; x++)
        {
            for(int y = 0; y < height; y++)
            {
                var available = basicVariants
                    .Where(v => v.maxAmount == -1 || _variantUsageCount[v] < v.maxAmount)
                    .ToList();

                if(available.Count > 0)
                {
                    layout[x, y] = SelectWeightedRandom(available);
                }
                else
                {
                    // No basic variant available (none defined or all maxed out).
                    var fallback = CurrentTheme.CellsVariants[0];
                    _variantUsageCount[fallback]++;
                    layout[x, y] = fallback;
                }
            }
        }

        // Second pass: scatter required variants into random cells.
        var requiredVariants = CurrentTheme.CellsVariants
            .Where(v => v.mustSpawnAtLeastOnce && v.maxAmount != 0)
            .ToList();

        if(requiredVariants.Count > 0)
        {
            var usedCells = new HashSet<(int x, int y)>();
            int totalCells = width * height;

            foreach(var required in requiredVariants)
            {
                if(usedCells.Count >= totalCells)
                {
                    Logger.LogWarning($"No free cells left to place required variant '{required.name}'.");
                    break;
                }

                (int x, int y) cell;
                do
                {
                    cell = (Random.Range(0, width), Random.Range(0, height));
                } while(usedCells.Contains(cell));

                usedCells.Add(cell);

                // We're overwriting a basic variant, so reclaim its usage count.
                var replaced = layout[cell.x, cell.y];
                _variantUsageCount[replaced]--;

                layout[cell.x, cell.y] = required;
                _variantUsageCount[required]++;
            }
        }

        return layout;
    }

    private CellVariantInfo SelectWeightedRandom(List<CellVariantInfo> variants)
    {
        float totalWeight = variants.Sum(v => v.weight);
        float randomValue = Random.Range(0f, totalWeight);

        // Select variant based on cumulative weights
        float cumulativeWeight = 0f;
        foreach(var variant in variants)
        {
            cumulativeWeight += variant.weight;
            if(randomValue <= cumulativeWeight)
            {
                _variantUsageCount[variant]++;
                return variant;
            }
        }

        // Fallback (should rarely happen)
        var selected = variants[^1];
        _variantUsageCount[selected]++;
        return selected;
    }

    public void StartPlayingAmbientAudios()
    {
        if (ambientNoiseSource && CurrentTheme.AmbientNoise)
        {
            ambientNoiseSource.clip = CurrentTheme.AmbientNoise;
            ambientNoiseSource.Play();
            ambientNoiseSource.loop = true;
        }

        if (ambientMusicSource && CurrentTheme.AmbientMusics.Count > 0)
        {
            var randomMusic = CurrentTheme.AmbientMusics.GetWeightedRandom(md => (!md.isStreamSafe && LocalConfig.Singleton.StreamerMode.Value) ? 0.0f : 1.0f);
            ambientMusicSource.clip = randomMusic.soundtrack;
            ambientMusicSource.Play();
        }
    }

    public void StopPlayingAmbientAudios()
    {
        if (ambientNoiseSource)
        {
            ambientNoiseSource.Stop();
        }

        if (ambientMusicSource)
        {
            ambientMusicSource.Stop();
        }
    }

    private readonly List<NetworkObject> _spawnedExits = new();

    public override void OnNetworkDespawn()
    {
        if (IsServer)
        {
            if (Cells != null)
                foreach (var cell in Cells)
                    if (cell && cell.NetworkObject.IsSpawned)
                        cell.NetworkObject.Despawn(true);

            foreach (var exit in _spawnedExits)
                if (exit && exit.IsSpawned)
                    exit.Despawn(true);
            _spawnedExits.Clear();
        }
        base.OnNetworkDespawn();
    }
}
