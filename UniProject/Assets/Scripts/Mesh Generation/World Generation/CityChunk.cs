using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

/// <summary>
/// Handles Chunk-level logic.
/// </summary>
public class CityChunk
{
    private bool isInitialized = false;
    public const int PERSIST_CHUNKS_BEHIND = 2;

    public Vector2Int coord;
    private GameObject chunkObject;
    public Transform parent;
    private Bounds bounds;
    private Transform playerTransform;
    public static LayerMask groundLayer;

    private RoadNetworkGenerator roadNetworkGenerator;
    private BuildingGenerator buildingGenerator;
    private SpecialStructGenerator structGenerator;
    private CollectibleManager collectibleManager;
    private CollectibleSpawner collectibleSpawner;
    private ObjectPool objectPool;

    public event System.Action<CityChunk, bool> onVisibilityChanged;
    private bool isVisible;
    private int chunkSeed;
    private Vector2 chunkSize;
    public int ChunkSeed => chunkSeed;

    public RoadNetworkGenerator RoadGenerator => roadNetworkGenerator;
    public BuildingGenerator BuildingGenerator => buildingGenerator;
    public SpecialStructGenerator StructGenerator => structGenerator;
    public CollectibleManager CollectibleManager => collectibleManager;
    public CollectibleSpawner CollectibleSpawner => collectibleSpawner;
    public ObjectPool ObjectPool => objectPool;

    private GameObject groundPlane;
    private Material groundMaterial;
    public Collider groundCollider;
    private bool groundInitialized;
    private ChunkGroundManager groundHandler;
    private LayerMask roadLayer;

    public Dictionary<Vector2Int, MacroCell> macroGrid { get; private set; }
    public const int MACRO_CELL_SIZE = 20;
    public const int SUB_CELL_SIZE = 10;
    public const int MACRO_CELL_SUBDIVISIONS = 2;
    public int macrocellsPerChunkX;
    public int macrocellsPerChunkY;

    private float minBuildingDistance = 5f;

    public CityChunk(Vector2Int coord, Vector2 chunkSize, Transform parent, int mainSeed, Transform player, GameObject groundPrefab, LayerMask roadLayer)
    {
        this.coord = coord;
        this.parent = parent;
        this.playerTransform = player;

        // Create unique seed for this chunk
        chunkSeed = mainSeed +
            coord.x * 31415 +
            coord.y * 27183 +
            (coord.x * coord.y) * 23 +
            (coord.x % 7) * (coord.y % 11) * 41;

        chunkObject = new GameObject($"Chunk_{coord.x}_{coord.y}");
        chunkObject.transform.position = new Vector3(
            coord.x * chunkSize.x,
            0,
            coord.y * chunkSize.y
        );
        chunkObject.transform.parent = parent;
        this.chunkSize = chunkSize;
        this.roadLayer = roadLayer;

        InitializeGrid(chunkSize);

        roadNetworkGenerator = chunkObject.AddComponent<RoadNetworkGenerator>();
        roadNetworkGenerator.Initialize(coord, macroGrid, chunkSeed);

        buildingGenerator = chunkObject.AddComponent<BuildingGenerator>();

        structGenerator = chunkObject.AddComponent<SpecialStructGenerator>();

        objectPool = chunkObject.AddComponent<ObjectPool>();
        collectibleManager = chunkObject.AddComponent<CollectibleManager>();
        collectibleSpawner = chunkObject.AddComponent<CollectibleSpawner>();
        collectibleSpawner.Setup(this);

        // Create Ground Plane
        groundHandler = chunkObject.AddComponent<ChunkGroundManager>();
        groundHandler.groundTilePrefab = groundPrefab;
        groundHandler.Initialize(chunkSize);


        bounds = new Bounds(
            chunkObject.transform.position,
            new Vector3(chunkSize.x, 1, chunkSize.y)
        );
        SetVisible(false);
    }

    /// <summary>
    /// Initializes a grid of macrocells.
    /// </summary>
    /// <param name="chunkSize">Size of each chunk.</param>
    public void InitializeGrid(Vector2 chunkSize)
    {
        macroGrid = new Dictionary<Vector2Int, MacroCell>();

        // Calculate number of macrocells based on chunk size and macro cell size
        macrocellsPerChunkX = Mathf.FloorToInt(chunkSize.x / MACRO_CELL_SIZE);
        macrocellsPerChunkY = Mathf.FloorToInt(chunkSize.y / MACRO_CELL_SIZE);

        // Initialize grid with local coordinates
        for (int x = 0; x < macrocellsPerChunkX; x++)
        {
            for (int y = 0; y < macrocellsPerChunkY; y++)
            {
                Vector2Int localPos = new Vector2Int(x, y);

                Vector3 worldPos = new Vector3(
                    coord.x * chunkSize.x + x * MACRO_CELL_SIZE,
                    0,
                    coord.y * chunkSize.y + y * MACRO_CELL_SIZE
                );

                MacroCell cell = new MacroCell(localPos, worldPos);

                // Initialize subcells with their world positions
                for (int sx = 0; sx < MACRO_CELL_SUBDIVISIONS; sx++)
                {
                    for (int sy = 0; sy < MACRO_CELL_SUBDIVISIONS; sy++)
                    {
                        Vector3 subCellWorldPos = new Vector3(
                            worldPos.x + sx * SUB_CELL_SIZE,
                            worldPos.y,
                            worldPos.z + sy * SUB_CELL_SIZE
                        );
                        cell.SubCells[sx, sy].WorldPosition = subCellWorldPos;
                    }
                }

                macroGrid.Add(localPos, cell);
            }
        }
    }

    /// <summary>
    /// Generates the chunk with parameters.
    /// </summary>
    /// <param name="chunkSize">Size of each chunk.</param>
    /// <param name="random">Random object including chunk seed.</param>
    public void GenerateChunk(Vector2 chunkSize, System.Random random)
    {
        chunkObject.SetActive(true);
        if (isInitialized) return;

        if (macroGrid == null || macroGrid.Count == 0)
        {
            Debug.LogError($"MacroGrid not properly initialized for chunk {coord}");
            return;
        }

        if (roadNetworkGenerator != null)
        {
            roadNetworkGenerator.Initialize(coord, macroGrid, chunkSeed);
            roadNetworkGenerator.GetComponent<RoadNetworkGenerator>().SetSeed(chunkSeed);
            roadNetworkGenerator.GenerateAndPlaceRoads(chunkObject.transform, coord);
        }

        if (structGenerator != null)
        {
            structGenerator.Initialize(
                macroGrid,
                roadNetworkGenerator.roadNetwork,
                buildingGenerator,
                coord,
                chunkSeed,
                chunkSize
            );
            structGenerator.GenerateStructures(chunkObject.transform);
        }

        if (buildingGenerator != null)
        {
            buildingGenerator.Initialize(macroGrid, coord, roadNetworkGenerator.roadNetwork, chunkSize);
            buildingGenerator.GenerateAndPlaceBuildings(chunkObject.transform);
        }

        if (collectibleManager != null)
        {
            collectibleManager.SetPlayerTransform(playerTransform);
            collectibleManager.Initialize(objectPool);
        }

        if (objectPool != null)
        {
            objectPool.Initialize();
        }

        if (collectibleSpawner != null)
        {
            collectibleSpawner.groundLayer = groundLayer;
            collectibleSpawner.spawnRadius = chunkSize.x / 2f;
            collectibleSpawner.Initialize(objectPool, chunkSeed);
        }

        isInitialized = true;
        chunkObject.SetActive(false);
    }

    /// <summary>
    /// Sets visibility based on passed bool.
    /// </summary>
    /// <param name="visible">Boolean of visibility.</param>
    public void SetVisible(bool visible)
    {
        isVisible = visible;
        chunkObject.SetActive(visible);
    }

    /// <summary>
    /// Returns the value of isVisible
    /// </summary>
    public bool IsVisible()
    {
        return isVisible;
    }
}