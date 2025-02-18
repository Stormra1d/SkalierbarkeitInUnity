using UnityEngine;

/// <summary>
/// Manages the behavior of a collectible object, including fallback randomization of scale and position.
/// </summary>
public class Collectible : MonoBehaviour
{
    [Header("Scale Settings")]
    public Vector3 defaultScale = Vector3.one;
    public bool randomizeScale = false;
    public Vector2 randomScaleRange = new Vector2(0.5f, 1.5f);

    [Header("Position Settings")]
    public bool randomizePosition = false;
    public Vector3 positionOffsetRange = new Vector3(1f, 0f, 1f);
    private Vector3 initialPosition;

    [Header("Physics Settings")]
    public float baseMass = 100f;

    private Rigidbody rb;

    /// <summary>
    /// Initializes the collectible by storing its initial position.
    /// </summary>
    private void Awake()
    {
        initialPosition = transform.position;
    }

    /// <summary>
    /// Called on the frame when the script is enabled.
    /// Initializes components, including both the trigger and physical compiler.
    /// </summary>
    void Start()
    {
        // Ensure the collectible has a Rigidbody
        rb = GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody>();
        }

        rb.isKinematic = false;
        rb.useGravity = true;

        // Calculate and set mass based on scale
        float volume = transform.localScale.x * transform.localScale.y * transform.localScale.z;
        rb.mass = baseMass * volume;

        // Set colliders: Trigger collider for interaction, non-trigger for physical collision
        Collider col = GetComponent<Collider>();
        if (col != null)
        {
            col.isTrigger = true;
        }

        Collider col2 = GetComponentInChildren<Collider>();
        if (col2 != null)
        {
            col2.isTrigger = false;
        }
    }

    /// <summary>
    /// Resets the collectible's scale and position when it is enabled.
    /// </summary>
    private void OnEnable()
    {
        // Reset scale
        if (randomizeScale)
        {
            float randomScale = Random.Range(randomScaleRange.x, randomScaleRange.y);
            transform.localScale = Vector3.one * randomScale;
        }
        else
        {
            transform.localScale = defaultScale;
        }

        // Ensure the collider is enabled when the collectible is spawned or activated
        Collider collider = GetComponent<Collider>();
        if (collider == null)
        {
            Debug.LogWarning("Collider component is missing on collectible.");
        }
        else
        {
            collider.enabled = true;
        }
    }
}