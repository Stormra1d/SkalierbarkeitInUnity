using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Handles spawning collectibles from the object pool.
/// </summary>
public class CollectibleSpawner : MonoBehaviour
{
    [Header("Spawner Settings")]
    public string collectiblePoolTag = "Collectible";
    public int collectiblesPerMacroCell = 2;
    public float spawnRadius = 10f;
    public int maxSpawnAttempts = 20;

    [Header("Collision Settings")]
    public LayerMask groundLayer;
    public LayerMask collectibleLayer;
    public LayerMask buildingLayer;
    public float minSpawnHeight = 0.1f;
    public float maxSpawnHeight = 1.0f;

    [Header("Distribution Settings")]
    public float minDistanceBetweenCollectibles = 5f;

    [Header("Randomization Settings")]
    public Vector2 randomScaleRange = new Vector2(0.5f, 2f);

    private System.Random random;
    private int seed;
    private ObjectPool objectPool;
    private const float MACRO_CELL_SIZE = CityChunk.MACRO_CELL_SIZE;
    private const float SUB_CELL_SIZE = CityChunk.SUB_CELL_SIZE;
    private List<Vector3> spawnedPositions = new List<Vector3>();

    private CityChunk cityChunkReference;
    private Dictionary<Vector2Int, MacroCell> macroGrid;

    /// <summary>
    /// Sets up required parameters.
    /// </summary>
    /// <param name="cityChunk">The City Chunk to set up the spawner for.</param>
    public void Setup(CityChunk cityChunk)
    {
        cityChunkReference = cityChunk;
        macroGrid = cityChunk.macroGrid;
        spawnedPositions.Clear();
    }

    /// <summary>
    /// Initializes the Spawner.
    /// </summary>
    /// <param name="pool">The assigned object pool.</param>
    /// <param name="chunkSeed">The seed of the chunk for randomization.</param>
    public void Initialize(ObjectPool pool, int chunkSeed)
    {
        if (cityChunkReference == null || macroGrid == null)
        {
            Debug.LogError($"CollectibleSpawner not properly setup! cityChunkReference: {(cityChunkReference == null ? "null" : "valid")}, macroGrid: {(macroGrid == null ? "null" : "valid")}");
            return;
        }

        this.seed = chunkSeed;
        random = new System.Random(seed);
        objectPool = pool;
        spawnedPositions.Clear();

        SpawnCollectiblesInChunk();
    }

    /// <summary>
    /// Spawns collectibles in the chunk boundaries.
    /// </summary>
    private void SpawnCollectiblesInChunk()
    {
        if (macroGrid == null)
        {
            Debug.LogError("Macro grid is not initialized!");
            return;
        }

        List<SubCell> availableSubCells = new List<SubCell>();

        // Gather all available subcells
        foreach (var macroCellKvp in macroGrid)
        {
            MacroCell macroCell = macroCellKvp.Value;
            if (macroCell.Type == CellType.Building)
                continue;

            for (int x = 0; x < CityChunk.MACRO_CELL_SUBDIVISIONS; x++)
            {
                for (int y = 0; y < CityChunk.MACRO_CELL_SUBDIVISIONS; y++)
                {
                    SubCell subCell = macroCell.SubCells[x, y];
                    availableSubCells.Add(subCell);
                }
            }
        }

        // Shuffle the available subcells
        for (int i = availableSubCells.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            SubCell temp = availableSubCells[i];
            availableSubCells[i] = availableSubCells[j];
            availableSubCells[j] = temp;
        }

        // Try to spawn in shuffled subcells
        foreach (SubCell subCell in availableSubCells)
        {
            if (random.NextDouble() < 0.3f)
            {
                SpawnCollectibleInSubCell(subCell);
            }
        }
    }

    /// <summary>
    /// Spawns collectibles, fine tuned in subcells.
    /// </summary>
    /// <param name="subCell">The SubCell in question.</param>
    private void SpawnCollectibleInSubCell(SubCell subCell)
    {
        Vector3 subCellCenter = subCell.WorldPosition;

        for (int attempt = 0; attempt < maxSpawnAttempts; attempt++)
        {
            Vector2 randomOffset = GetRandomOffsetInSubCell();
            Vector3 spawnPosition = new Vector3(
                subCellCenter.x + randomOffset.x,
                0.1f,
                subCellCenter.z + randomOffset.y
            );

            // Check proximity
            if (IsTooCloseToOtherCollectibles(spawnPosition))
                continue;

            if (IsValidSpawnPosition(spawnPosition))
            {
                GameObject collectible = objectPool.SpawnFromPool(collectiblePoolTag, spawnPosition, Quaternion.identity);
                if (collectible != null)
                {
                    SetupCollectible(collectible, spawnPosition);
                    subCell.isReserved = true;
                    spawnedPositions.Add(spawnPosition);
                    return;
                }
            }
        }
    }

    /// <summary>
    /// Checks for distance to other collectibles.
    /// </summary>
    /// <param name="position">Position to check for.</param>
    /// <returns>If the position in question is too close to other collectibles.</returns>
    private bool IsTooCloseToOtherCollectibles(Vector3 position)
    {
        foreach (Vector3 existingPosition in spawnedPositions)
        {
            if (Vector3.Distance(position, existingPosition) < minDistanceBetweenCollectibles)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Applies random offset to spread the position in the subcell.
    /// </summary>
    /// <returns>A 2D Vector determining the offset from the originally calculated position.</returns>
    private Vector2 GetRandomOffsetInSubCell()
    {
        return new Vector2(
            (float)((random.NextDouble() * 2 - 1) * SUB_CELL_SIZE * 0.49f),
            (float)((random.NextDouble() * 2 - 1) * SUB_CELL_SIZE * 0.49f)
        );
    }

    /// <summary>
    /// Checks for overlaps with buildings or collectibles.
    /// </summary>
    /// <param name="position">The position to check for.</param>
    /// <returns>If the position in question is a valid spawn position.</returns>
    private bool IsValidSpawnPosition(Vector3 position)
    {
        // Check for building overlaps with smaller radius
        Collider[] buildingOverlaps = Physics.OverlapSphere(position, MACRO_CELL_SIZE * 0.2f, buildingLayer);
        if (buildingOverlaps.Length > 0)
            return false;

        // Check for collectible overlaps with smaller radius
        Collider[] collectibleOverlaps = Physics.OverlapSphere(position, MACRO_CELL_SIZE * 0.2f, collectibleLayer);
        if (collectibleOverlaps.Length > 0)
            return false;

        return true;
    }

    /// <summary>
    /// Sets up base parameters for the to be spawned collectible.
    /// </summary>
    /// <param name="collectible">The collectible to spawn.</param>
    /// <param name="position">The position to spawn the collectible in.</param>
    private void SetupCollectible(GameObject collectible, Vector3 position)
    {
        float randomScale = UnityEngine.Random.Range(randomScaleRange.x, randomScaleRange.y);
        collectible.transform.localScale = Vector3.one * randomScale;
        collectible.transform.rotation = Quaternion.Euler(0, UnityEngine.Random.Range(0f, 360f), 0);

        // Always set position with minSpawnHeight
        collectible.transform.position = position;
    }
}