using UnityEngine;

/// <summary>
/// Handles a single ground plane per chunk and enables/disables its collider based on chunk activity.
/// </summary>
public class ChunkGroundManager : MonoBehaviour
{
    public GameObject groundTilePrefab;
    private GameObject groundTile;
    private Collider groundCollider;
    private Vector2 chunkSize;

    /// <summary>
    /// Initializes the ground for the chunk.
    /// </summary>
    public void Initialize(Vector2 chunkSize)
    {
        this.chunkSize = chunkSize;
        GenerateGroundTile();
    }

    /// <summary>
    /// Creates a single ground tile.
    /// </summary>
    private void GenerateGroundTile()
    {
        Vector3 tilePosition = new Vector3(transform.position.x + chunkSize.x / 2f, 0, transform.position.z + chunkSize.y / 2f);
        groundTile = Instantiate(groundTilePrefab, tilePosition, Quaternion.identity, transform);
        groundTile.transform.localScale = new Vector3(RoadNetworkGenerator.roadTileSize, 1, RoadNetworkGenerator.roadTileSize);
        groundCollider = groundTile.GetComponent<Collider>();
        if (groundCollider == null)
        {
            groundCollider = groundTile.AddComponent<BoxCollider>();
        }
    }
}
