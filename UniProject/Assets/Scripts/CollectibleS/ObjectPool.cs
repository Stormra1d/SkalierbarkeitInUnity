using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manages the pooling of game objects for efficient reuse, preventing frequent instantiation.
/// </summary>
public class ObjectPool : MonoBehaviour
{
    [System.Serializable]
    public class Pool
    {
        public string tag;
        public List<GameObject> prefabs;
        public int size;
    }

    public List<Pool> pools;
    public Dictionary<string, Queue<GameObject>> poolDictionary;
    private bool isInitialized = false;

    /// <summary>
    /// Initializes this specific ObjectPool instance.
    /// </summary>
    public void Initialize()
    {
        if (isInitialized)
        {
            Debug.LogWarning($"ObjectPool '{gameObject.name}' is already initialized.");
            return;
        }

        poolDictionary = new Dictionary<string, Queue<GameObject>>();

        foreach (Pool pool in pools)
        {
            Queue<GameObject> objectPool = new Queue<GameObject>();

            for (int i = 0; i < pool.size; i++)
            {
                GameObject prefab = pool.prefabs[Random.Range(0, pool.prefabs.Count)];
                GameObject obj = Instantiate(prefab, transform);
                obj.SetActive(false);
                objectPool.Enqueue(obj);
            }

            poolDictionary.Add(pool.tag, objectPool);
        }

        isInitialized = true;
    }

    /// <summary>
    /// Spawns a game object from the pool with the given tag, position, and rotation.
    /// </summary>
    public GameObject SpawnFromPool(string tag, Vector3 position, Quaternion rotation)
    {
        if (!isInitialized)
        {
            Debug.LogError($"ObjectPool '{gameObject.name}' is not initialized! Call Initialize() before using it.");
            return null;
        }

        if (!poolDictionary.ContainsKey(tag))
        {
            Debug.LogError($"Tag '{tag}' does not exist in poolDictionary of ObjectPool '{gameObject.name}'.");
            return null;
        }

        if (poolDictionary[tag].Count == 0)
        {
            Debug.LogWarning($"Pool with tag '{tag}' is empty!");
            return null;
        }

        GameObject objectToSpawn = poolDictionary[tag].Dequeue();

        if (objectToSpawn == null)
        {
            Debug.LogError($"Object to spawn from pool with tag '{tag}' is null in ObjectPool '{gameObject.name}'.");
            return null;
        }

        objectToSpawn.SetActive(true);
        objectToSpawn.transform.position = position;
        objectToSpawn.transform.rotation = rotation;

        return objectToSpawn;
    }

    /// <summary>
    /// Helper method to return a collectible back to the pool.
    /// </summary>
    /// <param name="tag">The tag of the pool.</param>
    /// <param name="obj">The object to return to the pool.</param>
    public void ReturnToPool(string tag, GameObject obj)
    {
        if (!poolDictionary.ContainsKey(tag))
        {
            Debug.LogError($"Tag '{tag}' does not exist in poolDictionary of ObjectPool '{gameObject.name}'.");
            return;
        }

        obj.SetActive(false);
        poolDictionary[tag].Enqueue(obj);
    }
}