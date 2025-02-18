using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Handles generating special structures.
/// </summary>
public class SpecialStructGenerator : MonoBehaviour
{
    [Header("Vegetation")]
    public GameObject[] vegetationPrefabs;
    [Range(0f, 0.1f)] public float vegetationDensity = 0.1f;

    [Header("Road Objects")]
    public GameObject[] roadObjectPrefabs;
    [Range(0f, 0.1f)] public float roadObjectDensity = 0.15f;
    public float roadObjectOffset = 2f;

    [Header("Parks")]
    public GameObject grassPrefab;
    [Range(0f, 0.05f)] public float parkSpawnChance = 0.05f;
    public int minParkSize = 2;
    public int maxParkSize = 6;
    private HashSet<Vector2Int> parkCells = new HashSet<Vector2Int>();

    [Header("Water Features")]
    public GameObject waterTilePrefab;
    [Range(0f, 0.02f)] public float lakeSpawnChance = 0.01f;
    public int minLakeSize = 4;
    public int maxLakeSize = 8;
    public Material riverMaterial;

    private Dictionary<Vector2Int, MacroCell> macroGrid;
    private Dictionary<Vector2Int, GameObject> roadNetwork;
    private System.Random random;
    private Vector2Int chunkCoord;
    private BuildingGenerator buildingGenerator;
    public bool enableDetailedDebug = false;
    private const float ROAD_TILE_SPACING = 20f;
    private const float MIN_DISTANCE_FROM_ROAD = 20f;
    private const float CLEANUP_DISTANCE = 22f;
    private Vector2 chunkSize;

    private HashSet<Vector2Int> roadGridPositions;
    private LayerMask forbiddenLayers;

    /// <summary>
    /// Initializes the generator with its variables.
    /// </summary>
    /// <param name="grid">The grid.</param>
    /// <param name="roads">The existing road network.</param>
    /// <param name="buildingGen">The Building Generator.</param>
    /// <param name="coord">The coordinates of the current chunk.</param>
    /// <param name="seed">The seed of the current chunk.</param>
    /// <param name="chunkSize">The chunk size.</param>
    public void Initialize(
        Dictionary<Vector2Int, MacroCell> grid,
        Dictionary<Vector2Int, GameObject> roads,
        BuildingGenerator buildingGen,
        Vector2Int coord,
        int seed,
        Vector2 chunkSize
    )
    {
        this.chunkSize = chunkSize;
        macroGrid = grid;
        roadNetwork = roads;
        buildingGenerator = buildingGen;
        chunkCoord = coord;
        random = new System.Random(seed);

        forbiddenLayers = LayerMask.GetMask("RoadLayer", "Buildings");
    }

    /// <summary>
    /// Initial method to kickstart generating structures.
    /// </summary>
    /// <param name="parent">Parent that will be assigned to the new object.</param>
    public void GenerateStructures(Transform parent)
    {
        if (macroGrid == null || roadNetwork == null)
        {
            Debug.LogError("SpecialStructGenerator: Grid or road network not initialized!");
            return;
        }

        ValidateCoordinateSystem();
        PlaceVegetation(parent);
        GenerateParks(parent);
        PlaceCars(parent);
    }

    /// <summary>
    /// Places vegetation around the scene.
    /// </summary>
    /// <param name="parent">Parent that will be assigned to the new object.</param>
    private void PlaceVegetation(Transform parent)
    {
        roadGridPositions = GetAllRoadGridPositions();

        foreach (var macroCell in macroGrid.Values)
        {
            if (macroCell.Type == CellType.Road) continue;

            for (int x = 0; x < 2; x++)
            {
                for (int y = 0; y < 2; y++)
                {
                    SubCell subCell = macroCell.SubCells[x, y];
                    if (subCell.Type != CellType.Road && !subCell.isReserved)
                    {
                        Vector3 proposedPos = CalculateVegetationPosition(subCell);
                        if (!IsPositionNearRoad(proposedPos, roadGridPositions) &&
                            random.NextDouble() < vegetationDensity)
                        {
                            PlaceSingleVegetation(subCell, parent);
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Places singular vegetation.
    /// </summary>
    /// <param name="subCell">The subcell calculated to place vegetation prefab in.</param>
    /// <param name="parent">Parent that will be assigned to the new object.</param>
    private void PlaceSingleVegetation(SubCell subCell, Transform parent)
    {
        Vector3 position = CalculateVegetationPosition(subCell);

        if (enableDetailedDebug)
        {
            Vector2Int gridPos = GetGlobalGridPositionFromWorld(position);
        }

        // Check grid-based road proximity first
        if (IsPositionNearRoad(position, roadGridPositions))
        {
            if (enableDetailedDebug)
            return;
        }

        // Perform raycast checks
        Vector3 rayStart = position + Vector3.up * 10f;
        float rayDistance = 30f;

        bool hitRoad = Physics.SphereCast(
            rayStart,
            2f,
            Vector3.down,
            out RaycastHit hit,
            rayDistance,
            forbiddenLayers,
            QueryTriggerInteraction.Collide
        );

        if (hitRoad)
            return;

        var vegetationInstance = Instantiate(
            vegetationPrefabs[random.Next(vegetationPrefabs.Length)],
            position,
            Quaternion.Euler(0, (float)random.NextDouble() * 360, 0),
            parent
        );
    }

    private void OnValidate()
    {
        forbiddenLayers = LayerMask.GetMask("RoadLayer", "Buildings");

        // List all layers included in the mask
        for (int i = 0; i < 32; i++)
        {
            if ((forbiddenLayers.value & (1 << i)) != 0)
            {
                Debug.Log($"Layer {i} ({LayerMask.LayerToName(i)}) is included in forbidden layers");
            }
        }
    }

    /// <summary>
    /// Retrieves the world position for all roads.
    /// </summary>
    /// <returns>A list of all road world position.</returns>
    private List<Vector3> GetAllRoadWorldPositions()
    {
        List<Vector3> allRoads = new List<Vector3>();
        foreach (var chunk in CityGenerator.ActiveChunks)
        {
            if (!chunk.IsVisible()) continue;

            Vector3 chunkWorldOrigin = new Vector3(
                chunk.coord.x * chunkSize.x * RoadNetworkGenerator.roadTileSize,
                0,
                chunk.coord.y * chunkSize.y * RoadNetworkGenerator.roadTileSize
            );

            foreach (var roadEntry in chunk.RoadGenerator.roadNetwork)
            {
                Vector3 roadWorldPos = chunkWorldOrigin + new Vector3(
                    roadEntry.Key.x * RoadNetworkGenerator.roadTileSize,
                    0.1f,
                    roadEntry.Key.y * RoadNetworkGenerator.roadTileSize
                );
                allRoads.Add(roadWorldPos);
            }
        }
        return allRoads;
    }

    /// <summary>
    /// Checks if the passed position is too close to a road.
    /// </summary>
    /// <param name="position">The position to check.</param>
    /// <param name="roadPositions">All road positions.</param>
    /// <returns></returns>
    private bool IsTooCloseToAnyRoad(Vector3 position, List<Vector3> roadPositions)
    {
        Vector2 pos2D = new Vector2(position.x, position.z);
        foreach (var roadPos in roadPositions)
        {
            Vector2 road2D = new Vector2(roadPos.x, roadPos.z);
            float distance = Vector2.Distance(pos2D, road2D);

            if (distance < MIN_DISTANCE_FROM_ROAD)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Removes any accidentally placed vegetation from roads.
    /// </summary>
    /// <param name="parent"></param>
    private void CleanupVegetationOnRoads(Transform parent)
    {
        HashSet<Vector2Int> roadGridPositions = GetAllRoadGridPositions();
        List<GameObject> toDestroy = new List<GameObject>();

        foreach (Transform child in parent)
        {
            if (!IsVegetation(child.gameObject)) continue;
            Vector2Int gridPos = GetGlobalGridPositionFromWorld(child.position);

            // Check if vegetation is within 1 cell of any road
            if (IsPositionNearRoad(child.position, roadGridPositions))
            {
                toDestroy.Add(child.gameObject);
            }
        }

        foreach (GameObject vegetation in toDestroy)
        {
            DestroyImmediate(vegetation);
        }
    }


    private bool IsVegetation(GameObject obj)
    {
        return vegetationPrefabs.Any(prefab => obj.name.StartsWith(prefab.name));
    }

    private void GenerateParks(Transform parent)
    {
        if (random.NextDouble() > parkSpawnChance) return;

        // Get all potential park areas
        List<List<Vector2Int>> potentialAreas = FindPotentialParkAreas();

        if (potentialAreas.Count == 0) return;

        // Sort areas by size descending
        var sortedAreas = potentialAreas
            .OrderByDescending(area => area.Count)
            .ToList();

        // Try to place at least one park
        foreach (var area in sortedAreas)
        {
            if (TryCreatePark(area, parent))
            {
                break;
            }
        }
    }

    private List<List<Vector2Int>> FindPotentialParkAreas()
    {
        HashSet<Vector2Int> checkedCells = new HashSet<Vector2Int>();
        List<List<Vector2Int>> potentialAreas = new List<List<Vector2Int>>();

        HashSet<Vector2Int> allRoads = GetAllRoadGridPositions();

        foreach (var cell in macroGrid)
        {
            if (checkedCells.Contains(cell.Key)) continue;
            if (!IsValidParkCell(cell.Value, allRoads)) continue;

            // Flood fill to find contiguous area
            List<Vector2Int> area = new List<Vector2Int>();
            Queue<Vector2Int> toCheck = new Queue<Vector2Int>();
            toCheck.Enqueue(cell.Key);

            while (toCheck.Count > 0)
            {
                Vector2Int current = toCheck.Dequeue();
                if (checkedCells.Contains(current)) continue;
                if (!macroGrid.ContainsKey(current)) continue;
                if (!IsValidParkCell(macroGrid[current], allRoads)) continue;

                checkedCells.Add(current);
                area.Add(current);

                // Check 4-directional neighbors
                foreach (var dir in new[] { Vector2Int.up, Vector2Int.down,
                                      Vector2Int.left, Vector2Int.right })
                {
                    Vector2Int neighbor = current + dir;
                    if (!checkedCells.Contains(neighbor))
                    {
                        toCheck.Enqueue(neighbor);
                    }
                }
            }

            if (area.Count >= minParkSize)
            {
                potentialAreas.Add(area);
            }
        }

        return potentialAreas;
    }

    private bool IsValidParkCell(MacroCell cell, HashSet<Vector2Int> allRoads)
    {
        if (cell.Type != CellType.Empty) return false;
        if (cell.isReserved) return false;

        // Convert to global position for road check
        Vector2Int globalPos = ConvertLocalToGlobalCell(cell.Position);

        // Check distance to roads
        foreach (var roadPos in allRoads)
        {
            if (Vector2Int.Distance(globalPos, roadPos) < 2)
            {
                return false;
            }
        }

        // Check subcells
        for (int x = 0; x < 2; x++)
        {
            for (int y = 0; y < 2; y++)
            {
                if (cell.SubCells[x, y].Type == CellType.Road ||
                    cell.SubCells[x, y].isReserved)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private bool TryCreatePark(List<Vector2Int> area, Transform parent)
    {
        // Determine park size
        int size = Mathf.Min((int)Mathf.Sqrt(area.Count), maxParkSize);
        size = Mathf.Max(size, minParkSize);

        // Find best fitting rectangle
        Vector2Int min = new Vector2Int(int.MaxValue, int.MaxValue);
        Vector2Int max = new Vector2Int(int.MinValue, int.MinValue);

        foreach (var pos in area)
        {
            min.x = Mathf.Min(min.x, pos.x);
            min.y = Mathf.Min(min.y, pos.y);
            max.x = Mathf.Max(max.x, pos.x);
            max.y = Mathf.Max(max.y, pos.y);
        }

        int width = max.x - min.x + 1;
        int height = max.y - min.y + 1;

        if (width < minParkSize || height < minParkSize) return false;

        // Create park
        HashSet<Vector3> vegetationPositions = new HashSet<Vector3>();

        for (int x = min.x; x <= max.x; x++)
        {
            for (int y = min.y; y <= max.y; y++)
            {
                Vector2Int pos = new Vector2Int(x, y);
                if (!macroGrid.ContainsKey(pos)) continue;

                CreateGrassTile(pos, parent, vegetationPositions);

                if ((x - min.x) >= maxParkSize || (y - min.y) >= maxParkSize)
                    continue;
            }
        }

        return true;
    }

    private void CreateGrassTile(Vector2Int position, Transform parent, HashSet<Vector3> vegetationPositions)
    {
        Vector3 worldPos = new Vector3(
            macroGrid[position].WorldPosition.x,
            0.1f,
            macroGrid[position].WorldPosition.z
        );

        Instantiate(grassPrefab, worldPos, Quaternion.identity, parent);

        // Mark cell and subcells as reserved
        MacroCell cell = macroGrid[position];
        cell.Type = CellType.Park;
        cell.isReserved = true;

        for (int x = 0; x < 2; x++)
        {
            for (int y = 0; y < 2; y++)
            {
                cell.SubCells[x, y].isReserved = true;
            }
        }

        macroGrid[position] = cell;

        // Place vegetation
        PlaceParkVegetation(worldPos, RoadNetworkGenerator.roadTileSize,
                           parent, vegetationPositions);
    }

    /// <summary>
    /// Places Vegetation with different probability based on parks.
    /// </summary>
    /// <param name="parkCenter">Center point of the park to spawn vegetation in.</param>
    /// <param name="parkSize">Size of the park to spawn vegetation in.</param>
    /// <param name="parent">The parent transform.</param>
    /// <param name="vegetationPositions">Hashset of positions of vegetations.</param>
    private void PlaceParkVegetation(Vector3 parkCenter, float parkSize,
                                   Transform parent, HashSet<Vector3> vegetationPositions)
    {
        int vegetationDensity = Mathf.RoundToInt(parkSize * 0.5f);
        float spacing = 3f;

        for (int i = 0; i < vegetationDensity; i++)
        {
            Vector3 position = new Vector3(
                parkCenter.x + Random.Range(-parkSize / 2, parkSize / 2),
                0.1f,
                parkCenter.z + Random.Range(-parkSize / 2, parkSize / 2)
            );

            // Check spacing
            bool tooClose = vegetationPositions.Any(p =>
                Vector3.Distance(p, position) < spacing);

            if (!tooClose)
            {
                Instantiate(
                    vegetationPrefabs[random.Next(vegetationPrefabs.Length)],
                    position,
                    Quaternion.Euler(0, Random.Range(0, 360), 0),
                    parent
                );
                vegetationPositions.Add(position);
            }
        }
    }

    /// <summary>
    /// Gets grid position based on passed world position.
    /// </summary>
    /// <param name="position">The world position to convert to grid position.</param>
    /// <returns>The grid position.</returns>
    private Vector2Int GetGridPositionFromWorld(Vector3 position)
    {
        float gridSize = RoadNetworkGenerator.roadTileSize;
        Vector2Int chunkOffset = chunkCoord * RoadNetworkGenerator.MacroCellsPerChunk;

        // Use rounding instead of flooring for edge cases
        int globalGridX = Mathf.RoundToInt(position.x / gridSize);
        int globalGridZ = Mathf.RoundToInt(position.z / gridSize);

        return new Vector2Int(
            globalGridX - chunkOffset.x,
            globalGridZ - chunkOffset.y
        );
    }

    /// <summary>
    /// Gets global cell from local cell.
    /// </summary>
    private Vector2Int ConvertLocalToGlobalCell(Vector2Int localCell)
    {
        return new Vector2Int(
            chunkCoord.x * RoadNetworkGenerator.MacroCellsPerChunk + localCell.x,
            chunkCoord.y * RoadNetworkGenerator.MacroCellsPerChunk + localCell.y
        );
    }

    /// <summary>
    /// Get world position based on passed grid position.
    /// </summary>
    /// <param name="gridPosition">Position of the cell to get world coordinates for.</param>
    private Vector3 WorldPositionForCell(Vector2Int gridPosition)
    {
        Vector2Int chunkOffset = chunkCoord * RoadNetworkGenerator.MacroCellsPerChunk;
        Vector2Int globalPosition = gridPosition + chunkOffset;

        return new Vector3(
            globalPosition.x * RoadNetworkGenerator.roadTileSize,
            0.1f,
            globalPosition.y * RoadNetworkGenerator.roadTileSize
        );
    }

    /// <summary>
    /// Debug function to validate the coordinate system.
    /// </summary>
    private void ValidateCoordinateSystem()
    {
        if (!enableDetailedDebug) return;

        foreach (var roadEntry in roadNetwork)
        {
            Vector2Int globalGridPos = roadEntry.Key;
            Vector3 worldPos = roadEntry.Value.transform.position;

            Vector2Int calculatedGridPos = GetGridPositionFromWorld(worldPos);
            Vector2Int expectedLocalPos = globalGridPos - (chunkCoord * RoadNetworkGenerator.MacroCellsPerChunk);

            if (calculatedGridPos != expectedLocalPos)
            {
                Debug.LogError($"Coordinate mismatch! Global: {globalGridPos} | " +
                    $"Expected Local: {calculatedGridPos}");
            }
        }
    }

    /// <summary>
    /// Places cars on roads.
    /// </summary>
    /// <param name="parent">Transform of the parent.</param>
    private void PlaceCars(Transform parent)
    {
        if (roadObjectPrefabs == null || roadObjectPrefabs.Length == 0) return;

        // Get ALL road positions across chunks
        HashSet<Vector2Int> allRoadGridPositions = GetAllRoadGridPositions();

        foreach (var roadEntry in roadNetwork)
        {
            if (random.NextDouble() > roadObjectDensity) continue;

            GameObject roadTile = roadEntry.Value;
            if (roadTile == null) continue;

            Vector3 basePosition = roadTile.transform.position;
            Quaternion roadRotation = roadTile.transform.rotation;

            // Get validated offset direction with base offset
            Vector3 offsetDirection = GetRoadEdgeOffset(roadRotation);
            Vector3 spawnPosition = basePosition + offsetDirection * roadObjectOffset;

            float randomXOffset = (float)(random.NextDouble() * 14 - 7);
            float randomZOffset = (float)(random.NextDouble() * 14 - 7);
            spawnPosition += new Vector3(randomXOffset, 0, randomZOffset);

            Vector2Int spawnGlobalGrid = GetGlobalGridPositionFromWorld(spawnPosition);

            // Check against all roads in all chunks
            if (allRoadGridPositions.Contains(spawnGlobalGrid))
            {
                continue;
            }

            if (IsTooCloseToAnyRoad(spawnPosition, GetAllRoadWorldPositions())) continue;

            // Create completely random rotation
            Quaternion randomRotation = Quaternion.Euler(0, (float)random.NextDouble() * 360, 0);

            Instantiate(
                roadObjectPrefabs[random.Next(roadObjectPrefabs.Length)],
                spawnPosition,
                randomRotation,
                parent
            );
        }
    }

    /// <summary>
    /// Calculates an offset from the road's edge based on its rotation.
    /// </summary>
    /// <param name="roadRotation">The rotation of the road.</param>
    /// <returns>A Vector3 representing the offset direction.</returns>
    private Vector3 GetRoadEdgeOffset(Quaternion roadRotation)
    {
        float yRotation = roadRotation.eulerAngles.y;

        bool isHorizontal = Mathf.Abs(yRotation % 180) < 0.1f;

        // Get perpendicular direction
        Vector3 offset = isHorizontal ?
            Vector3.forward :
            Vector3.right;

        float side = random.Next(2) * 2 - 1;

        return offset * side;
    }

    /// <summary>
    /// Retrieves all road positions within active and visible chunks.
    /// </summary>
    /// <returns>A HashSet containing all road grid positions.</returns>
    private HashSet<Vector2Int> GetAllRoadGridPositions()
    {
        HashSet<Vector2Int> allRoads = new HashSet<Vector2Int>();
        foreach (var chunk in CityGenerator.ActiveChunks)
        {
            if (!chunk.IsVisible()) continue;
            Vector2Int chunkOffset = chunk.coord * RoadNetworkGenerator.MacroCellsPerChunk * 2;

            foreach (var roadPos in chunk.RoadGenerator.roadNetwork.Keys)
            {
                // Convert to global subcell positions
                Vector2Int globalSubcellPos = new Vector2Int(
                    chunkOffset.x + roadPos.x,
                    chunkOffset.y + roadPos.y
                );
                allRoads.Add(globalSubcellPos);
            }
        }
        return allRoads;
    }

    /// <summary>
    /// Converts a world position into a global grid position.
    /// </summary>
    /// <param name="position">The world position to convert.</param>
    /// <returns>A Vector2Int representing the global grid position.</returns>
    private Vector2Int GetGlobalGridPositionFromWorld(Vector3 position)
    {
        float roadSpacing = RoadNetworkGenerator.roadTileSize;
        return new Vector2Int(
            Mathf.FloorToInt(position.x / roadSpacing),
            Mathf.FloorToInt(position.z / roadSpacing)
        );
    }

    /// <summary>
    /// Calculates a randomized position for vegetation within a subcell.
    /// </summary>
    /// <param name="subCell">The subcell in which the vegetation is placed.</param>
    /// <returns>A Vector3 representing the vegetation's position.</returns>
    private Vector3 CalculateVegetationPosition(SubCell subCell)
    {
        float subcellSize = RoadNetworkGenerator.roadTileSize / 2f;
        return subCell.WorldPosition + new Vector3(
            (float)(random.NextDouble() * subcellSize - subcellSize / 2f),
            0.1f,
            (float)(random.NextDouble() * subcellSize - subcellSize / 2f)
        );
    }

    /// <summary>
    /// Determines whether a given world position is near any road.
    /// </summary>
    /// <param name="worldPos">The world position to check.</param>
    /// <param name="roadGridPositions">A set of road positions to compare against.</param>
    /// <returns>if the position is near a road</returns>
    private bool IsPositionNearRoad(Vector3 worldPos, HashSet<Vector2Int> roadGridPositions)
    {
        if (enableDetailedDebug)
        {
            foreach (var roadPos in roadGridPositions)
            {
                Vector3 roadWorldPos = new Vector3(
                    roadPos.x * RoadNetworkGenerator.roadTileSize,
                    0,
                    roadPos.y * RoadNetworkGenerator.roadTileSize
                );
                float distance = Vector3.Distance(worldPos, roadWorldPos);

                if (distance < MIN_DISTANCE_FROM_ROAD)
                {
                    Debug.Log($"Position {worldPos} is too close to road at {roadWorldPos} (distance: {distance})");
                }
            }
        }

        Vector2Int gridPos = GetGlobalGridPositionFromWorld(worldPos);

        for (int x = -1; x <= 1; x++)
        {
            for (int y = -1; y <= 1; y++)
            {
                Vector2Int checkPos = gridPos + new Vector2Int(x, y);
                if (roadGridPositions.Contains(checkPos))
                {
                    return true;
                }
            }
        }

        return false;
    }
}