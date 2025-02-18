using UnityEngine;

/// <summary>
/// Helper class to determine if the player is stuck and help him out.
/// </summary>
public class StuckDetector : MonoBehaviour
{
    [SerializeField] private float stuckTimeThreshold = 2f;
    [SerializeField] private float minVelocity = 0.1f;
    [SerializeField] private float positionDeltaThreshold = 0.01f;
    [SerializeField] private float recoveryForce = 15f;
    [SerializeField] private float checkInterval = 0.5f;

    private Rigidbody rb;
    private float stuckTimer;
    private Vector3 previousPosition;
    private bool isStuck;
    private float lastCheckTime;

    /// <summary>
    /// Called on the frame when the script is enabled.
    /// Initializes components and saves the current position.
    /// </summary>
    void Start()
    {
        rb = GetComponent<Rigidbody>();
        previousPosition = transform.position;
    }

    /// <summary>
    /// Called every frame.
    /// Runs a timed check if the player is stuck.
    /// </summary>
    void Update()
    {
        if (Time.time - lastCheckTime < checkInterval) return;
        lastCheckTime = Time.time;

        float positionDelta = Vector3.Distance(transform.position, previousPosition);
        previousPosition = transform.position;

        bool isMoving = rb.linearVelocity.magnitude > minVelocity || positionDelta > positionDeltaThreshold;

        // If stuck, count time
        if (!isMoving)
        {
            stuckTimer += checkInterval;

            if (stuckTimer >= stuckTimeThreshold && !isStuck)
            {
                isStuck = true;
                Debug.Log("Stuck detected!");
                HandleBeingStuck();
            }
        }
        else
        {
            stuckTimer = 0f;
            isStuck = false;
        }
    }

    /// <summary>
    /// Applies an additional force to unstuck the player.
    /// </summary>
    void HandleBeingStuck()
    {
        Vector3 escapeDirection = FindBestEscapeDirection();

        if (escapeDirection != Vector3.zero)
        {
            Debug.Log("Apply Force");
            rb.AddForce(escapeDirection * recoveryForce, ForceMode.Impulse);
        }
        else
        {
            // If no direction is available, apply a small upward force
            Debug.Log("Apply Backup Force");
            rb.AddForce(Vector3.up * recoveryForce * 0.5f, ForceMode.Impulse);
        }
    }

    /// <summary>
    /// Determines the direction the player should get pushed towards in order to unstuck them.
    /// </summary>
    /// <returns>Direction of force required to unstuck the player.</returns>
    Vector3 FindBestEscapeDirection()
    {
        // Try moving in all six cardinal directions
        Vector3[] directions = { Vector3.up, -Vector3.up, transform.right, -transform.right, transform.forward, -transform.forward };
        Vector3 bestDirection = Vector3.zero;

        foreach (Vector3 dir in directions)
        {
            // Check if applying force in this direction results in movement
            rb.AddForce(dir * 0.1f, ForceMode.Impulse);
            if (rb.linearVelocity.magnitude > minVelocity)
            {
                bestDirection = dir;
                break;
            }
        }

        return bestDirection;
    }
}
