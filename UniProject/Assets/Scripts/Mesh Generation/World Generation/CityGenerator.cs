using System.Collections;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using static ObjectPool;

/// <summary>
/// Handles setting up the procedurally generated city.
/// </summary>
public class CityGenerator : MonoBehaviour
{
    public Transform viewer;
    public Vector2 chunkSize;
    public int chunksVisibleInViewDistance;
    public int chunksWithActiveColliders = 1;


    [Header("Road Prefabs")]
    public GameObject[] roadLanePrefab;
    public GameObject[] roadCornerPrefab;
    public GameObject[] roadIntersectionPrefab;
    public GameObject[] tIntersectionPrefab;
    public GameObject[] roadEndPrefab;
    public GameObject[] roadCleanupPrefab;

    [Header("Building Prefabs")]
    public GameObject[] skyscraperPrefabs;
    public GameObject[] smallSkyscraperPrefabs;
    public GameObject[] wideShopPrefabs;
    public GameObject[] normalShopPrefabs;
    public GameObject[] factoryPrefabs;
    public GameObject[] footballStadiumPrefabs;
    public GameObject[] specialBuildingPrefabs;
    public GameObject[] normalHousePrefabs;
    public GameObject[] wideHousePrefabs;
    public GameObject[] superWideHousePrefabs;
    public GameObject[] residentialBlockPrefabs;

    [Header("Special Structures")]
    public GameObject grassPrefab;
    public GameObject waterTilePrefab;
    public Material riverMaterial;
    public GameObject[] vegetationPrefabs;
    public GameObject[] carPrefabs;

    [Header("Object Pools")]
    public List<Pool> pools;
    public LayerMask groundLayer;

    [Header("Ground Settings")]
    public LayerMask roadLayer;
    public GameObject groundPrefab;

    private Dictionary<Vector2Int, CityChunk> cityChunks = new Dictionary<Vector2Int, CityChunk>();
    private List<CityChunk> visibleChunks = new List<CityChunk>();
    private HashSet<Vector2Int> activeChunkCoords = new HashSet<Vector2Int>();
    public static List<CityChunk> ActiveChunks = new List<CityChunk>();
    private System.Random random;
    private int currentSeed;
    public static HashSet<Vector2Int> RoadMacrocells = new HashSet<Vector2Int>();
    public static HashSet<Vector2Int> RoadSubcells = new HashSet<Vector2Int>();

    private Vector2 viewerPosition;
    private Vector2 viewerPositionOld;

    public bool enableLogging = true;

    /// <summary>
    /// Called on the frame when the script is enabled.
    /// Initializes parameters and updates visible chunks.
    /// </summary>
    void Start()
    {
        currentSeed = System.DateTime.Now.Ticks.GetHashCode();
        random = new System.Random(currentSeed);
        Random.InitState(currentSeed);
        CityChunk.groundLayer = groundLayer;
        UpdateVisibleChunksWithJobs();
    }

    /// <summary>
    /// Called every frame.
    /// </summary>
    void Update()
    {
        viewerPosition = new Vector2(viewer.position.x, viewer.position.z);

        if (viewerPosition != viewerPositionOld)
        {
            UpdateVisibleChunksWithJobs();
            viewerPositionOld = viewerPosition;
        }
    }

    /// <summary>
    /// Handles updating which chunks are loaded.
    /// </summary>
    public void UpdateVisibleChunksWithJobs()
    {
        Vector2 playerPosition = new Vector2(viewer.position.x, viewer.position.z);
        Vector2Int currentChunkCoord = new Vector2Int(
            Mathf.FloorToInt(playerPosition.x / chunkSize.x),
            Mathf.FloorToInt(playerPosition.y / chunkSize.y)
        );

        int chunksPerAxis = 2 * chunksVisibleInViewDistance + 1;
        int maxChunks = chunksPerAxis * chunksPerAxis;

        // Ensure we don't create empty NativeArrays
        NativeArray<Vector2Int> existingChunkCoords = new NativeArray<Vector2Int>(
            Mathf.Max(cityChunks.Count, 1), Allocator.TempJob
        );
        NativeList<Vector2Int> missingChunkCoords = new NativeList<Vector2Int>(Allocator.TempJob);
        NativeArray<Vector2Int> visibleChunkCoords = new NativeArray<Vector2Int>(maxChunks, Allocator.TempJob);

        int index = 0;
        foreach (var key in cityChunks.Keys)
        {
            existingChunkCoords[index] = key;
            index++;
        }

        // Schedule the job to calculate which chunks should be active and which are missing
        ChunkVisibilityJob visibilityJob = new ChunkVisibilityJob
        {
            playerChunk = currentChunkCoord,
            chunksVisibleInViewDistance = chunksVisibleInViewDistance,
            existingChunks = existingChunkCoords,
            visibleChunksOut = visibleChunkCoords,
            missingChunksOut = missingChunkCoords
        };

        JobHandle visibilityHandle = visibilityJob.Schedule();
        visibilityHandle.Complete();

        HashSet<Vector2Int> visibleChunkSet = new HashSet<Vector2Int>();
        for (int i = 0; i < visibleChunkCoords.Length; i++)
        {
            visibleChunkSet.Add(visibleChunkCoords[i]);
        }

        List<Vector2Int> chunksToRemove = new List<Vector2Int>();
        foreach (var chunk in cityChunks)
        {
            if (!visibleChunkSet.Contains(chunk.Key))
            {
                chunk.Value.SetVisible(false);
                chunksToRemove.Add(chunk.Key);
            }
        }

        foreach (var key in chunksToRemove)
        {
            cityChunks.Remove(key);
        }

        // Create missing chunks on the main thread after job completion
        for (int i = 0; i < missingChunkCoords.Length; i++)
        {
            Vector2Int chunkCoord = missingChunkCoords[i];

            if (!cityChunks.ContainsKey(chunkCoord))
            {
                CityChunk newChunk = new CityChunk(chunkCoord, chunkSize, transform, currentSeed, viewer, groundPrefab, roadLayer);
                SetupRoadGenerator(newChunk);
                SetupBuildingGenerator(newChunk);
                SetupSpecialStructGenerator(newChunk);
                SetupCollectibleGenerator(newChunk);
                newChunk.GenerateChunk(chunkSize, random);
                cityChunks.Add(chunkCoord, newChunk);
                newChunk.SetVisible(true);
            }
        }

        // Dispose Native Containers
        existingChunkCoords.Dispose();
        visibleChunkCoords.Dispose();
        missingChunkCoords.Dispose();
    }

    /// <summary>
    /// Initializes road generator.
    /// </summary>
    /// <param name="chunk">The chunk to initialize the generator for.</param>
    private void SetupRoadGenerator(CityChunk chunk)
    {
        RoadNetworkGenerator roadGenerator = chunk.RoadGenerator;
        if (roadGenerator != null)
        {
            roadGenerator.roadLanePrefab = roadLanePrefab;
            roadGenerator.roadCornerPrefab = roadCornerPrefab;
            roadGenerator.roadIntersectionPrefab = roadIntersectionPrefab;
            roadGenerator.tIntersectionPrefab = tIntersectionPrefab;
            roadGenerator.roadEndPrefab = roadEndPrefab;
            roadGenerator.roadCleanupPrefab = roadCleanupPrefab;
        }
    }

    /// <summary>
    /// Initializes struct generator.
    /// </summary>
    /// <param name="chunk">The chunk to initialize the generator for.</param>
    private void SetupSpecialStructGenerator(CityChunk chunk)
    {
        SpecialStructGenerator specialGenerator = chunk.StructGenerator;
        if (specialGenerator != null)
        {
            specialGenerator.vegetationPrefabs = vegetationPrefabs;
            specialGenerator.grassPrefab = grassPrefab;
            specialGenerator.waterTilePrefab = waterTilePrefab;
            specialGenerator.riverMaterial = riverMaterial;
            specialGenerator.roadObjectPrefabs = carPrefabs;

            specialGenerator.enableDetailedDebug = enableLogging;
        }
    }

    /// <summary>
    /// Initializes collectible generator.
    /// </summary>
    /// <param name="chunk">The chunk to initialize the generator for.</param>
    private void SetupCollectibleGenerator(CityChunk chunk)
    {
        CollectibleSpawner collectibleSpawner = chunk.CollectibleSpawner;

        CollectibleManager collectibleManager = chunk.CollectibleManager;

        ObjectPool objectPool = chunk.ObjectPool;
        if (objectPool != null)
        {
            objectPool.pools = pools;
        }
    }

    /// <summary>
    /// Initializes building generator.
    /// </summary>
    /// <param name="chunk">The chunk to initialize the generator for.</param>
    private void SetupBuildingGenerator(CityChunk chunk)
    {
        BuildingGenerator buildingGenerator = chunk.BuildingGenerator;
        if (buildingGenerator != null)
        {
            // Initialize the building types array with prefabs from inspector
            buildingGenerator.buildingTypes = new BuildingTypeConfig[]
            {
                new BuildingTypeConfig
                {
                    type = BuildingType.Skyscraper,
                    prefabs = skyscraperPrefabs,
                    spawnChance = 0.05f,
                    size = new Vector2Int(1, 1)
                },
                new BuildingTypeConfig
                {
                    type = BuildingType.SmallSkyscraper,
                    prefabs = smallSkyscraperPrefabs,
                    spawnChance = 0.08f,
                    size = new Vector2Int(1, 1)
                },
                new BuildingTypeConfig
                {
                    type = BuildingType.WideShop,
                    prefabs = wideShopPrefabs,
                    spawnChance = 0.3f,
                    size = new Vector2Int(3, 1)
                },
                new BuildingTypeConfig
                {
                    type = BuildingType.NormalShop,
                    prefabs = normalShopPrefabs,
                    spawnChance = 0.3f,
                    size = new Vector2Int(1, 1)
                },
                new BuildingTypeConfig
                {
                    type = BuildingType.Factory,
                    prefabs = factoryPrefabs,
                    spawnChance = 0.02f,
                    size = new Vector2Int(2, 3)
                },
                new BuildingTypeConfig
                {
                    type = BuildingType.FootballStadium,
                    prefabs = footballStadiumPrefabs,
                    spawnChance = 0.02f,
                    size = new Vector2Int(4, 4)
                },
                new BuildingTypeConfig
                {
                    type = BuildingType.Special,
                    prefabs = specialBuildingPrefabs,
                    spawnChance = 0.1f,
                    size = new Vector2Int(3, 1)
                },
                new BuildingTypeConfig
                {
                    type = BuildingType.NormalHouse,
                    prefabs = normalHousePrefabs,
                    spawnChance = 0.3f,
                    size = new Vector2Int(1, 1)
                },
                new BuildingTypeConfig
                {
                    type = BuildingType.WideHouse,
                    prefabs = wideHousePrefabs,
                    spawnChance = 0.2f,
                    size = new Vector2Int(2, 1)
                },
                new BuildingTypeConfig
                {
                    type = BuildingType.WideHouse,
                    prefabs = superWideHousePrefabs,
                    spawnChance = 0.05f,
                    size = new Vector2Int(3, 1)
                },
                new BuildingTypeConfig
                {
                    type = BuildingType.ResidentialBlock,
                    prefabs = residentialBlockPrefabs,
                    spawnChance = 0.15f,
                    size = new Vector2Int(2, 2)
                }
            };
        }
    }

    /// <summary>
    /// Sets visible chunks as active.
    /// </summary>
    /// <param name="chunk">Chunk to change visibility for.</param>
    /// <param name="isVisible">Boolean determining visibility.</param>
    void OnChunkVisibilityChanged(CityChunk chunk, bool isVisible)
    {
        if (isVisible)
        {
            visibleChunks.Add(chunk);
            ActiveChunks.Add(chunk);
        }
        else
        {
            visibleChunks.Remove(chunk);
            ActiveChunks.Remove(chunk);
        }
    }

    /// <summary>
    /// Determines which chunk the player is currently in.
    /// </summary>
    Vector2Int GetPlayerChunkCoord()
    {
        return new Vector2Int(
            Mathf.FloorToInt(viewer.position.x / chunkSize.x),
            Mathf.FloorToInt(viewer.position.z / chunkSize.y)
        );
    }

    [BurstCompile]
    struct ChunkVisibilityJob : IJob
    {
        public Vector2Int playerChunk;

        [ReadOnly] public NativeArray<Vector2Int> existingChunks;
        public NativeArray<Vector2Int> visibleChunksOut;
        public NativeList<Vector2Int> missingChunksOut;
        public int chunksVisibleInViewDistance;

        public void Execute()
        {
            int visibilityRange = chunksVisibleInViewDistance;
            int chunksPerAxis = 2 * visibilityRange + 1;

            for (int xOffset = -visibilityRange; xOffset <= visibilityRange; xOffset++)
            {
                for (int zOffset = -visibilityRange; zOffset <= visibilityRange; zOffset++)
                {
                    Vector2Int chunkCoord = new Vector2Int(playerChunk.x + xOffset, playerChunk.y + zOffset);

                    int xIndex = xOffset + visibilityRange;
                    int zIndex = zOffset + visibilityRange;
                    int flatIndex = xIndex + zIndex * chunksPerAxis;

                    if (flatIndex >= visibleChunksOut.Length)
                    {
                        continue; // Prevent out-of-bounds access
                    }

                    if (existingChunks.Contains(chunkCoord))
                    {
                        visibleChunksOut[flatIndex] = chunkCoord;
                    }
                    else
                    {
                        missingChunksOut.Add(chunkCoord);
                    }
                }
            }
        }
    }
}