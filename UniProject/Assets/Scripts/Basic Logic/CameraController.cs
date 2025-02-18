using UnityEngine;

/// <summary>
/// Controls the camera's behavior in a third-person perspective.
/// The camera will rotate based on the player's movement direction to follow the player.
/// </summary>
public class ThirdPersonCameraController : MonoBehaviour
{
    [Header("Camera Settings")]
    public Transform orientation;
    public Transform player;
    public float rotationSpeed = 10f;
    public Vector3 offset = new Vector3(0, 5, -10);
    public float smoothSpeed = 0.125f;
    public float minDistance = 0.1f;

    [Header("Zoom Settings")]
    public PlayerCollectibles playerCollectibles;
    public float zoomFactor = 1.5f;
    public float maxZoomOut = 50f;

    /// <summary>
    /// Called once every frame.
    /// </summary>
    private void Update()
    {
        if (playerCollectibles != null)
        {
            AdjustCameraZoom();
        }

        // Rotate the orientation based on the player's position
        Vector3 viewDirection = player.position - new Vector3(transform.position.x, player.position.y, transform.position.z);
        orientation.forward = Vector3.Slerp(orientation.forward, viewDirection.normalized, rotationSpeed * Time.deltaTime);

        // Move the camera to the correct position based on player position + offset
        Vector3 desiredPosition = player.position + offset;
        transform.position = Vector3.Lerp(transform.position, desiredPosition, rotationSpeed * Time.deltaTime);
    }

    /// <summary>
    /// Called after all update functions have been called.
    /// </summary>
    private void LateUpdate()
    {
        if (orientation == null) return;

        Vector3 desiredPosition = orientation.position + offset;

        if (Vector3.Distance(transform.position, desiredPosition) > minDistance)
        {
            Vector3 smoothedPosition = Vector3.Lerp(transform.position, desiredPosition, smoothSpeed);
            transform.position = smoothedPosition;
        }

        // Look at the player or orientation
        transform.LookAt(orientation);
    }

    /// <summary>
    /// Adjusts the camera's offset dynamically based on the sphere's size.
    /// </summary>
    private void AdjustCameraZoom()
    {
        // Calculate the zoom distance based on the sphere's current radius
        float sphereRadius = playerCollectibles.currentRadius;
        float dynamicZoom = sphereRadius * zoomFactor;

        // Update the offset to adjust the camera distance
        offset = new Vector3(0, 5 + dynamicZoom, -10 - dynamicZoom);
    }
}