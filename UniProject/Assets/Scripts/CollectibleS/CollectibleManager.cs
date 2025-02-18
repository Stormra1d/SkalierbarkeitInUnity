using UnityEngine;
using System.Collections.Generic;
using System.Collections;

/// <summary>
/// Manages collectibles during runtime.
/// </summary>
public class CollectibleManager : MonoBehaviour
{
    [Header("Visibility Check Settings")]
    public float checkInterval = 0.5f;
    public float maxRayDistance = 5f;
    public LayerMask ignoreGroundLayer;

    public Transform playerTransform;
    private Transform collectiblesHolder;
    private List<CollectibleData> collectibleDataList = new List<CollectibleData>();

    private Dictionary<GameObject, Coroutine> despawnCoroutines = new Dictionary<GameObject, Coroutine>();

    private ObjectPool objectPool;

    /// <summary>
    /// Initializes the collectible manager with an object pool reference.
    /// </summary>
    /// <param name="pool">The object pool managing collectible instances.</param>
    public void Initialize(ObjectPool pool)
    {
        objectPool = pool;

        if (playerTransform == null)
        {
            Debug.LogError("[CollectibleManager] Player transform is not set.");
            return;
        }

        collectiblesHolder = playerTransform.Find("CollectiblesHolder");

        if (collectiblesHolder == null)
        {
            Debug.LogError("[CollectibleManager] CollectiblesHolder not found under Player.");
            return;
        }

        InvokeRepeating(nameof(CheckCoveredCollectibles), checkInterval, checkInterval);
    }

    /// <summary>
    /// Represents a collectible's state and position.
    /// </summary>
    private class CollectibleData
    {
        public GameObject collectible;
        public Vector3 positionOnSphere;
        public bool isCovered;

        public CollectibleData(GameObject collectible, Vector3 positionOnSphere)
        {
            this.collectible = collectible;
            this.positionOnSphere = positionOnSphere;
            this.isCovered = false;
        }
    }

    /// <summary>
    /// Registers a new collectible to be tracked by the manager.
    /// </summary>
    /// <param name="collectible">The collectible GameObject.</param>
    /// <param name="positionOnSphere">The position relative to the sphere.</param>
    public void RegisterCollectible(GameObject collectible, Vector3 positionOnSphere)
    {
        CollectibleData newCollectible = new CollectibleData(collectible, positionOnSphere);
        collectibleDataList.Add(newCollectible);
    }

    /// <summary>
    /// Checks all registered collectibles to determine if they are covered and read to be despawned.
    /// </summary>
    private void CheckCoveredCollectibles()
    {
        foreach (var collectibleData in collectibleDataList)
        {
            if (collectibleData.collectible == null || !collectibleData.collectible.activeInHierarchy) continue;

            if (!collectibleData.isCovered)
            {
                if (!IsCollectibleVisible(collectibleData.collectible))
                {
                    collectibleData.isCovered = true;
                    if (!despawnCoroutines.ContainsKey(collectibleData.collectible))
                    {
                        Coroutine coroutine = StartCoroutine(DelayedDespawn(collectibleData.collectible, 5f));
                        despawnCoroutines.Add(collectibleData.collectible, coroutine);
                    }
                }
            }
            else
            {
                if (despawnCoroutines.ContainsKey(collectibleData.collectible)) continue;
            }
        }
    }

    /// <summary>
    /// Delays the despawning of a collectible by a set amount of time.
    /// </summary>
    /// <param name="collectible">The collectible to despawn.</param>
    /// <param name="delayTime">Time in seconds before despawning.</param>
    private IEnumerator DelayedDespawn(GameObject collectible, float delayTime)
    {
        yield return new WaitForSeconds(delayTime);
        if (!IsCollectibleVisible(collectible))
        {
            DespawnCollectible(collectible);
        }
        if (despawnCoroutines.ContainsKey(collectible))
        {
            despawnCoroutines.Remove(collectible);
        }
    }

    /// <summary>
    /// Determines if a collectible is visible.
    /// </summary>
    /// <param name="collectible">The collectible to check.</param>
    /// <returns>visibility status of the collectible.</returns>
    private bool IsCollectibleVisible(GameObject collectible)
    {
        Vector3 collectiblePosition = collectible.transform.position;
        bool isVisible = false;
        for (int i = 0; i < 32; i++)
        {
            Vector3 rayOrigin = GetRandomRayOrigin(collectiblePosition, maxRayDistance);
            Vector3 rayDirection = (collectiblePosition - rayOrigin).normalized;

            RaycastHit[] hits = Physics.RaycastAll(rayOrigin, rayDirection, maxRayDistance, ~ignoreGroundLayer);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            // Go through all hits
            foreach (RaycastHit hit in hits)
            {
                if ((ignoreGroundLayer.value & (1 << hit.collider.gameObject.layer)) != 0) continue;
                if (hit.collider.gameObject == collectible)
                {
                    isVisible = true;
                    break;
                }
                else if (hit.collider.CompareTag("Collectible"))
                {
                    break;
                }
            }

            if (isVisible) break;
        }

        return isVisible;
    }

    /// <summary>
    /// Despawns a collectible by disabling it and returning it to the object pool.
    /// </summary>
    /// <param name="collectible">The collectible to despawn.</param>
    private void DespawnCollectible(GameObject collectible)
    {
        collectible.SetActive(false);
        if (objectPool != null && objectPool.poolDictionary.ContainsKey("Collectible"))
        {
            objectPool.ReturnToPool("Collectible", collectible);
        }
    }

    /// <summary>
    /// Generates a random ray origin around the target position.
    /// </summary>
    /// <param name="targetPosition">The position to generate the ray from.</param>
    /// <param name="maxDistance">The maximum distance from the target position.</param>
    /// <returns>A random point in space used as the ray origin.</returns>
    private Vector3 GetRandomRayOrigin(Vector3 targetPosition, float maxDistance)
    {
        Vector3 randomDirection = Random.onUnitSphere;
        return targetPosition + randomDirection * maxDistance;
    }

    /// <summary>
    /// Sets the player's transform reference.
    /// </summary>
    /// <param name="player">The player transform.</param>
    internal void SetPlayerTransform(Transform player)
    {
        playerTransform = player;
    }
}