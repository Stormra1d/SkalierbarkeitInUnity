using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Handles generation of buildings for each chunk.
/// </summary>
public class BuildingGenerator : MonoBehaviour
{
    public BuildingTypeConfig[] buildingTypes;

    private Dictionary<Vector2Int, MacroCell> gridCells;
    private HashSet<Vector2Int> occupiedPositions = new HashSet<Vector2Int>();
    private Vector2Int chunkCoord;
    private Vector2 chunkSize;
    private Vector3 chunkWorldOffset;
    private List<Vector2Int> roadPositions;

    public HashSet<Vector2Int> GetOccupiedPositions() => new HashSet<Vector2Int>(occupiedPositions);
    public void RemoveOccupiedPosition(Vector2Int position) => occupiedPositions.Remove(position);

    /// <summary>
    /// Initializes the generator with its initial values from the CityGenerator.
    /// </summary>
    /// <param name="grid">A dictionary with grid values.</param>
    /// <param name="currentChunkCoord">The coordinates of the chunk the specific generator is associated to.</param>
    /// <param name="roadNetwork">A dictionary with the roadnetwork.</param>
    public void Initialize(Dictionary<Vector2Int, MacroCell> grid, Vector2Int currentChunkCoord, Dictionary<Vector2Int, GameObject> roadNetwork, Vector2 chunkDimensions)
    {
        if (grid == null || roadNetwork == null)
        {
            Debug.LogError("BuildingGenerator initialization failed: Missing required parameters.");
            return;
        }

        gridCells = grid;
        chunkCoord = currentChunkCoord;
        occupiedPositions.Clear();
        chunkSize = chunkDimensions;

        chunkWorldOffset = new Vector3(
            currentChunkCoord.x * chunkDimensions.x,
            0,
            currentChunkCoord.y * chunkDimensions.y
        );

        roadPositions = new List<Vector2Int>();
        foreach (var globalPos in roadNetwork.Keys)
        {
            // Convert global position to local chunk position
            Vector2Int localPos = new Vector2Int(
                globalPos.x - (currentChunkCoord.x * RoadNetworkGenerator.MacroCellsPerChunk),
                globalPos.y - (currentChunkCoord.y * RoadNetworkGenerator.MacroCellsPerChunk)
            );

            // Only add roads that belong to this chunk
            if (localPos.x >= 0 && localPos.x < RoadNetworkGenerator.MacroCellsPerChunk &&
                localPos.y >= 0 && localPos.y < RoadNetworkGenerator.MacroCellsPerChunk)
            {
                roadPositions.Add(localPos);
            }
        }
    }

    /// <summary>
    /// Calculates valid spots around roads and attempts to place buildings depending on their spawn chance.
    /// </summary>
    /// <param name="parent">Parent that will be assigned to the new object</param>
    public void GenerateAndPlaceBuildings(Transform parent)
    {
        if (gridCells == null)
        {
            Debug.LogError("GenerateAndPlaceBuildings: Grid cells not initialized.");
            return;
        }
        if (roadPositions == null || roadPositions.Count == 0)
        {
            Debug.LogWarning("GenerateAndPlaceBuildings: No road positions available.");
            return;
        }

        foreach (var roadPosition in roadPositions)
        {
            List<Vector2Int> validSpots = GetAdjacentEmptyCells(roadPosition);

            foreach (var spot in validSpots)
            {
                if (occupiedPositions.Contains(spot))
                    continue;

                // Try to place each building type based on its spawn chance
                foreach (var buildingConfig in buildingTypes)
                {
                    if (Random.value <= buildingConfig.spawnChance &&
                        CanPlaceBuildingAtPosition(spot, buildingConfig.size))
                    {
                        PlaceBuilding(spot, buildingConfig, parent);
                        break;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Attempts to find empty cells around a passed road position.
    /// </summary>
    /// <param name="roadPosition">The road position to find empty cells for.</param>
    /// <returns>A list of 2D-vectors of empty cells.</returns>
    private List<Vector2Int> GetAdjacentEmptyCells(Vector2Int roadPosition)
    {
        List<Vector2Int> validSpots = new List<Vector2Int>();
        Vector2Int[] directions = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

        foreach (var direction in directions)
        {
            Vector2Int adjacentPos = roadPosition + direction;

            // Skip positions outside the current chunk's valid grid
            if (adjacentPos.x < 0 || adjacentPos.x >= RoadNetworkGenerator.MacroCellsPerChunk ||
                adjacentPos.y < 0 || adjacentPos.y >= RoadNetworkGenerator.MacroCellsPerChunk)
            {
                continue;
            }

            if (gridCells.TryGetValue(adjacentPos, out MacroCell cell))
            {
                if (cell.Type == CellType.Empty && !cell.isReserved)
                {
                    validSpots.Add(adjacentPos);
                }
            }
        }
        return validSpots;
    }

    /// <summary>
    /// Places the building prefab.
    /// </summary>
    /// <param name="position">The position to place the building in.</param>
    /// <param name="buildingConfig">The configuration to determine the type of building to place.</param>
    /// <param name="parent">Parent that will be assigned to the new object</param>
    private void PlaceBuilding(Vector2Int position, BuildingTypeConfig buildingConfig, Transform parent)
    {
        if (buildingConfig.prefabs == null || buildingConfig.prefabs.Length == 0)
        {
            Debug.LogError($"No prefabs available for building type {buildingConfig.type}");
            return;
        }

        if (!gridCells.ContainsKey(position))
        {
            Debug.LogError($"Cannot place building at {position}: Position outside grid.");
            return;
        }

        GameObject buildingPrefab = buildingConfig.prefabs[Random.Range(0, buildingConfig.prefabs.Length)];

        if (buildingPrefab == null)
        {
            Debug.LogError("Building prefab is null.");
            return;
        }

        Vector3 worldPos = GetWorldPosition(position);
        Vector3 sizeOffset = new Vector3(
            (buildingConfig.size.x - 1) * RoadNetworkGenerator.roadTileSize,
            0,
            (buildingConfig.size.y - 1) * RoadNetworkGenerator.roadTileSize
            );

        Vector3 centeredPosition = worldPos + sizeOffset;

        Quaternion rotation = GetBuildingRotation(position);
        GameObject building = Instantiate(buildingPrefab, centeredPosition, rotation, parent);

        // Update grid cell
        for (int x = 0; x < buildingConfig.size.x; x++)
        {
            for (int y = 0; y < buildingConfig.size.y; y++)
            {
                Vector2Int occupyPos = position + new Vector2Int(x, y);
                if (gridCells.TryGetValue(occupyPos, out MacroCell cell))
                {
                    cell.Type = CellType.Building;
                    cell.isReserved = true;

                    // Reserve all subcells in this macrocell
                    for (int sx = 0; sx < 2; sx++)
                    {
                        for (int sy = 0; sy < 2; sy++)
                        {
                            cell.SubCells[sx, sy].Type = CellType.Building;
                            cell.SubCells[sx, sy].isReserved = true;
                        }
                    }

                    gridCells[occupyPos] = cell;
                    occupiedPositions.Add(occupyPos);
                    ReserveAdjacentCells(occupyPos);
                }
            }
        }
    }

    /// <summary>
    /// Reserves adjacent cells for buildings.
    /// </summary>
    /// <param name="center">Position of the center.</param>
    /// <summary>
    private void ReserveAdjacentCells(Vector2Int center)
    {
        // Reserve adjacent macrocells and their subcells
        Vector2Int[] directions = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
        foreach (var dir in directions)
        {
            Vector2Int adjacentPos = center + dir;
            if (gridCells.TryGetValue(adjacentPos, out MacroCell cell))
            {
                cell.isReserved = true;

                for (int x = 0; x < 2; x++)
                {
                    for (int y = 0; y < 2; y++)
                    {
                        cell.SubCells[x, y].isReserved = true;
                    }
                }

                gridCells[adjacentPos] = cell;
            }
        }
    }

    /// <summary>
    /// Rotates the building to face the nearest road.
    /// </summary>
    /// <param name="position">The position of the building to rotate.</param>
    /// <returns>The rotation amount necessary for the building to face the road.</returns>
    private Quaternion GetBuildingRotation(Vector2Int position)
    {
        // Find the nearest road
        Vector2Int nearestRoad = FindNearestRoadPosition(position);
        if (nearestRoad == Vector2Int.zero) return Quaternion.identity;

        // Calculate direction from the building to the road
        Vector2Int direction = nearestRoad - position;

        // Determine rotation based on direction
        if (direction == Vector2Int.up) return Quaternion.Euler(0, 0, 0);
        if (direction == Vector2Int.down) return Quaternion.Euler(0, 180, 0);
        if (direction == Vector2Int.left) return Quaternion.Euler(0, 270, 0);
        if (direction == Vector2Int.right) return Quaternion.Euler(0, 90, 0);

        return Quaternion.identity;
    }

    /// <summary>
    /// Helper function to find the nearest road from a specific position.
    /// </summary>
    /// <param name="position">The origin position to find the nearest road for.</param>
    /// <returns>A 2D vector determining the nearest road position.</returns>
    private Vector2Int FindNearestRoadPosition(Vector2Int position)
    {
        float minDistance = float.MaxValue;
        Vector2Int nearest = Vector2Int.zero;

        // Get the building's WORLD position
        Vector3 buildingWorldPos = GetWorldPosition(position);

        foreach (var localRoadPos in roadPositions)
        {
            // Convert local road position to WORLD position
            Vector3 roadWorldPos = GetWorldPosition(localRoadPos);

            float distance = Vector3.Distance(buildingWorldPos, roadWorldPos);
            if (distance < minDistance)
            {
                minDistance = distance;
                nearest = localRoadPos;
            }
        }

        return nearest;
    }

    /// <summary>
    /// Get world position for given position.
    /// </summary>
    /// <param name="position">Position to retrieve world coordinates for.</param>
    /// <returns>3D Vector of world coordinates.</returns>
    private Vector3 GetWorldPosition(Vector2Int position)
    {
        return new Vector3(
            chunkWorldOffset.x + position.x * RoadNetworkGenerator.roadTileSize,
            0,
            chunkWorldOffset.z + position.y * RoadNetworkGenerator.roadTileSize
        );
    }

    /// <summary>
    /// Checks if given building can be placed at given position.
    /// </summary>
    /// <param name="position">Position to check.</param>
    /// <param name="size">Size of the building to check.</param>
    /// <returns>if the building can be placed.</returns>
    private bool CanPlaceBuildingAtPosition(Vector2Int position, Vector2Int size)
    {
        for (int x = 0; x < size.x; x++)
        {
            for (int y = 0; y < size.y; y++)
            {
                Vector2Int checkPos = position + new Vector2Int(x, y);

                if (!gridCells.ContainsKey(checkPos))
                    return false;

                MacroCell cell = gridCells[checkPos];

                // Check if any subcell is occupied
                if (cell.SubCells.Cast<SubCell>().Any(sc => sc.isReserved || sc.Type != CellType.Empty))
                    return false;
            }
        }
        return true;
    }
}