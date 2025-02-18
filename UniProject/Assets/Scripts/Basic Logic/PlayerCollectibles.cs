using System.Collections.Generic;
using System.Net.Mail;
using UnityEngine;

/// <summary>
/// Class to handle how the player and collcetibles interact with each other.
/// </summary>
public class PlayerCollectibles : MonoBehaviour
{
    public float initialRadius = 0.5f;
    public float radiusGrowthFactor = 0.5f;
    public float attachmentOffset = 0.01f;
    public int initialSpotCount = 100;
    public float compressionFactor = 0.9f;
    public float minimumDistanceBetweenCollectibles = 0.15f;

    private SphereCollider triggerCollider;
    private List<Transform> attachedCollectibles = new List<Transform>();
    private List<Spot> spots = new List<Spot>();
    public float currentRadius;
    private Transform collectiblesHolder;
    private Transform visualSphere;

    public CollectibleManager collectibleManager;

    private Rigidbody rb;

    /// <summary>
    /// Utility class to define spots.
    /// </summary>
    private class Spot
    {
        public Vector3 position;
        public bool isOccupied;
    }

    /// <summary>
    /// Called on the frame when the script is enabled.
    /// Initializes components and calls functions to get the system going.
    /// </summary>
    void Start()
    {
        SetupTriggerCollider();
        currentRadius = initialRadius;
        SetupVisualSphere();
        SetupCollectiblesHolder();
        InitializeSpots();
        rb = GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody>();
            rb.useGravity = true;
            rb.constraints = RigidbodyConstraints.FreezeRotationZ | RigidbodyConstraints.FreezeRotationX;
        }
    }

    /// <summary>
    /// Sets up the trigger collider of the sphere.
    /// </summary>
    void SetupTriggerCollider()
    {
        triggerCollider = GetComponent<SphereCollider>();
        if (triggerCollider == null)
        {
            triggerCollider = gameObject.AddComponent<SphereCollider>();
        }
        triggerCollider.isTrigger = true;
        triggerCollider.radius = initialRadius + attachmentOffset;
    }

    /// <summary>
    /// Sets up the visual appearance of the sphere.
    /// </summary>
    void SetupVisualSphere()
    {
        visualSphere = new GameObject("VisualSphere").transform;
        visualSphere.SetParent(transform);
        visualSphere.localPosition = Vector3.zero;

        var sphereMesh = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphereMesh.transform.SetParent(visualSphere);
        sphereMesh.transform.localPosition = Vector3.zero;
        sphereMesh.transform.localScale = Vector3.one * 2 * initialRadius;

        Destroy(sphereMesh.GetComponent<Collider>());
    }

    /// <summary>
    /// Sets up a child object to group collectibles.
    /// </summary>
    void SetupCollectiblesHolder()
    {
        collectiblesHolder = new GameObject("CollectiblesHolder").transform;
        collectiblesHolder.SetParent(transform);
        collectiblesHolder.localPosition = Vector3.zero;

        collectiblesHolder.gameObject.layer = LayerMask.NameToLayer("Collectible");
        collectiblesHolder.tag = "Collectible";
    }

    /// <summary>
    /// Initializes pre-determined spots on the sphere for collectibles to get attached to.
    /// </summary>
    void InitializeSpots()
    {
        spots.Clear();

        for (int i = 0; i < initialSpotCount; i++)
        {
            // Distribute spots evenly using spherical coordinates
            float theta = Mathf.Acos(1 - 2 * (i + 0.5f) / initialSpotCount);
            float phi = Mathf.PI * (1 + Mathf.Sqrt(5)) * i;

            Vector3 position = new Vector3(
                Mathf.Sin(theta) * Mathf.Cos(phi),
                Mathf.Sin(theta) * Mathf.Sin(phi),
                Mathf.Cos(theta)
            );

            spots.Add(new Spot { position = position.normalized, isOccupied = false });
        }
    }

    /// <summary>
    /// Logic to attach the collectible.
    /// </summary>
    /// <param name="other"></param>
    void OnTriggerStay(Collider other)
    {
        if (other.CompareTag("Collectible"))
        {
            float collectibleRadius = GetObjectRadius(other.transform);
            if (collectibleRadius < currentRadius)
            {
                AttachCollectible(other.gameObject, collectibleRadius);
            }
            else
            {

                // Ensure uncollectible objects collide with the sphere
                Rigidbody rb = other.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.isKinematic = false;
                }

                other.gameObject.layer = LayerMask.NameToLayer("Default");
            }
        }
    }

    /// <summary>
    /// Attaches collectibles to the sphere.
    /// </summary>
    /// <param name="collectible">The gameobject to attach.</param>
    /// <param name="collectibleRadius">The radius of the collectible to attach.</param>
    void AttachCollectible(GameObject collectible, float collectibleRadius)
    {
        // Skip if already attached
        if (attachedCollectibles.Contains(collectible.transform))
            return;

        Spot spot = FindNearestAvailableSpot(collectibleRadius);
        if (spot != null)
        {
            AttachToSpot(collectible, spot, collectibleRadius);
            collectibleManager.RegisterCollectible(collectible, spot.position);
            return;
        }

        if (TryShrinkAndAttach(collectible, collectibleRadius))
        {
            collectibleManager.RegisterCollectible(collectible, collectible.transform.localPosition.normalized);
            return;
        }

        if (ForceAttachWithSmallOverlap(collectible, collectibleRadius))
        {
            collectibleManager.RegisterCollectible(collectible, collectible.transform.localPosition.normalized);
            return;
        }

        if (AttachToOtherCollectible(collectible, collectibleRadius))
        {
            collectibleManager.RegisterCollectible(collectible, collectible.transform.localPosition.normalized);
            return;
        }

    }

    /// <summary>
    /// If no nearest spot was available for the collectible and shrinking didn't work, force attaching with a small overlap.
    /// </summary>
    /// <param name="collectible">The gameobject to attach.</param>
    /// <param name="collectibleRadius">The radius of the collectible to attach.</param>
    bool ForceAttachWithSmallOverlap(GameObject collectible, float collectibleRadius)
    {
        int maxAttempts = 50; // Limit the number of attempts to avoid infinite loops
        float overlapTolerance = 0.9f;

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            // Generate a random position on the sphere's surface
            Vector3 candidatePosition = Random.onUnitSphere * currentRadius * compressionFactor;

            bool valid = true;
            foreach (Transform attached in attachedCollectibles)
            {
                float existingRadius = GetObjectRadius(attached);
                float distance = Vector3.Distance(candidatePosition, attached.localPosition);

                // Allow overlap within the tolerance range
                if (distance < (collectibleRadius + existingRadius) * overlapTolerance)
                {
                    valid = false;
                    break;
                }
            }

            if (valid)
            {
                // Attach collectible if position is valid within the overlap tolerance
                collectible.transform.SetParent(collectiblesHolder, false);
                collectible.transform.localPosition = candidatePosition;

                attachedCollectibles.Add(collectible.transform);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Find nearest available spot based on collectibleRadius.
    /// </summary>
    /// <param name="collectibleRadius">The radius of the collectible to find the nearest available spot for.</param>
    /// <returns></returns>
    Spot FindNearestAvailableSpot(float collectibleRadius)
    {
        foreach (Spot spot in spots)
        {
            if (!spot.isOccupied && IsSpotValid(spot, collectibleRadius))
            {
                return spot;
            }
        }
        return null;
    }

    /// <summary>
    /// Attempt to slightly shrink the collected object and fit attaching it.
    /// </summary>
    /// <param name="collectible">The gameobject to attach.</param>
    /// <param name="collectibleRadius">The radius of the collectible to attach.</param>
    /// <returns></returns>
    bool TryShrinkAndAttach(GameObject collectible, float collectibleRadius)
    {
        float originalScale = collectible.transform.localScale.x;
        float shrinkFactor = 0.8f;
        int maxAttempts = 10;
        float bestFitScore = float.MaxValue;
        Quaternion bestRotation = collectible.transform.rotation;
        Spot bestSpot = null;

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            // Test multiple orientations by rotating around random axes
            for (int i = 0; i < 20; i++)
            {
                collectible.transform.Rotate(Random.onUnitSphere * 15f);

                Spot spot = FindNearestAvailableSpot(collectibleRadius * shrinkFactor);
                if (spot != null)
                {
                    float fitScore = CalculateFitScore(collectible, spot, collectibleRadius * shrinkFactor);

                    // Track the best rotation and spot
                    if (fitScore < bestFitScore)
                    {
                        bestFitScore = fitScore;
                        bestRotation = collectible.transform.rotation;
                        bestSpot = spot;
                    }
                }
            }

            // If a valid spot is found with an optimized rotation, attach the collectible
            if (bestSpot != null)
            {
                collectible.transform.rotation = bestRotation;
                AttachToSpot(collectible, bestSpot, collectibleRadius * shrinkFactor);
                return true;
            }

            collectible.transform.localScale *= shrinkFactor;
        }

        collectible.transform.localScale = Vector3.one * originalScale;
        return false;
    }

    /// <summary>
    /// Calculate a fit score for placing a collectible at a given spot.
    /// </summary>
    /// <param name="collectible">The collectible object being evaluated for placement.</param>
    /// <param name="spot">The target spot on the player.</param>
    /// <param name="radius">The radius of the collectible.</param>
    /// <returns></returns>
    float CalculateFitScore(GameObject collectible, Spot spot, float radius)
    {
        // Measure overlap or distance from other attached collectibles
        Vector3 candidatePosition = spot.position * currentRadius * compressionFactor;
        float fitScore = 0f;

        foreach (Transform attached in attachedCollectibles)
        {
            float distance = Vector3.Distance(candidatePosition, attached.localPosition);
            fitScore += Mathf.Max(0, radius - distance);
        }

        // Prefer orientations where the smallest side is aligned
        Collider collider = collectible.GetComponent<Collider>();
        if (collider != null)
        {
            Vector3 closestPoint = collider.ClosestPoint(candidatePosition);
            fitScore += Vector3.Distance(closestPoint, candidatePosition);
        }

        return fitScore;
    }

    /// <summary>
    /// Logic to attach collectible to other collectibles.
    /// </summary>
    /// <param name="collectible">Collectible to be attached.</param>
    /// <param name="collectibleRadius">Radius of the to be attached collectible.</param>
    bool AttachToOtherCollectible(GameObject collectible, float collectibleRadius)
    {
        float overlapFactor = 0.05f;

        foreach (Transform attached in attachedCollectibles)
        {
            Vector3 attachedWorldPos = attached.position;

            // Generate a random direction and calculate the candidate position
            Vector3 randomDirection = Random.onUnitSphere;
            float combinedRadius = collectibleRadius + GetObjectRadius(attached);
            Vector3 candidateWorldPosition = attachedWorldPos + randomDirection * (combinedRadius * (1 - overlapFactor));

            // Transform the candidate position into local space of the collectibles holder
            Vector3 candidateLocalPosition = collectiblesHolder.InverseTransformPoint(candidateWorldPosition);

            // Check for conflicts with other collectibles
            bool tooCloseToOthers = false;
            foreach (Transform other in attachedCollectibles)
            {
                if (other == attached) continue;
                float distance = Vector3.Distance(candidateLocalPosition, other.localPosition);
                if (distance < minimumDistanceBetweenCollectibles)
                {
                    tooCloseToOthers = true;
                    break;
                }
            }

            if (!tooCloseToOthers)
            {
                // Attach collectible to the holder with the calculated position
                collectible.transform.SetParent(collectiblesHolder, false);
                collectible.transform.localPosition = candidateLocalPosition;

                attachedCollectibles.Add(collectible.transform);

                // Remove physics components
                Rigidbody collectibleRb = collectible.GetComponent<Rigidbody>();
                if (collectibleRb != null) Destroy(collectibleRb);

                UpdateRadius(collectibleRadius);

                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Attaches a collectible to a certain spot.
    /// </summary>
    /// <param name="collectible">The collectible to attach,</param>
    /// <param name="spot">The spot at which a colllectible is meant to be placed.</param>
    /// <param name="collectibleRadius">The radius of the to be attached collectible.</param>
    void AttachToSpot(GameObject collectible, Spot spot, float collectibleRadius)
    {
        // Remove physics components
        Rigidbody collectibleRb = collectible.GetComponent<Rigidbody>();
        if (collectibleRb != null) Destroy(collectibleRb);

        Collider collectibleCollider = collectible.GetComponent<Collider>();
        if (collectibleCollider != null)
        {
            collectibleCollider.isTrigger = false;
            collectibleCollider.gameObject.layer = LayerMask.NameToLayer("Collectible");
        }

        // Attach collectible to the holder
        collectible.transform.SetParent(collectiblesHolder, false);
        collectible.transform.localPosition = spot.position * currentRadius * compressionFactor;
        spot.isOccupied = true;

        attachedCollectibles.Add(collectible.transform);

        UpdateRadius(collectibleRadius);
    }

    /// <summary>
    /// Handles checking if a determined spot is valid.
    /// </summary>
    /// <param name="spot">The spot to check.</param>
    /// <param name="collectibleRadius">The radius of the collectible to attach.</param>
    /// <returns></returns>
    bool IsSpotValid(Spot spot, float collectibleRadius)
    {
        foreach (Transform collectible in attachedCollectibles)
        {
            float existingRadius = GetObjectRadius(collectible);
            float distance = Vector3.Distance(collectible.localPosition, spot.position * currentRadius * compressionFactor);
            if (distance < (collectibleRadius + existingRadius) * compressionFactor)
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Updates the player's radius based on collected objects.
    /// </summary>
    /// <param name="collectibleRadius">The radius of the newly attached collectible.</param>
    void UpdateRadius(float collectibleRadius)
    {
        // Calculate increased volume
        float currentVolume = Mathf.Pow(currentRadius, 3);
        float collectibleVolume = Mathf.Pow(collectibleRadius, 3);
        float newVolume = currentVolume + collectibleVolume;

        // Update the radius based on the new volume
        currentRadius = Mathf.Pow(newVolume, 1f / 3f);

        // Update the trigger collider radius
        triggerCollider.radius = currentRadius + attachmentOffset + 1;
    }

    /// <summary>
    /// Estimates the radius of an object based on its collider bounds.
    /// </summary>
    /// <param name="obj">The object to check the radius of.</param>
    /// <returns>estimated radius of the object.</returns>
    float GetObjectRadius(Transform obj)
    {
        var bounds = obj.GetComponent<Collider>()?.bounds;
        if (bounds.HasValue)
        {
            // Use the maximum extent along any single axis as a more lenient radius estimate
            return Mathf.Max(bounds.Value.extents.x, bounds.Value.extents.y, bounds.Value.extents.z);
        }
        return 0.1f;
    }

    void FixedUpdate()
    {
        SimulateRolling();
    }

    /// <summary>
    /// Simulates the visual rolling motion of the player.
    /// </summary>
    void SimulateRolling()
    {
        if (rb.linearVelocity.magnitude > 0.01f)
        {
            Vector3 rotationAxis = Vector3.Cross(Vector3.up, rb.linearVelocity.normalized);
            float rotationAngle = (rb.linearVelocity.magnitude / currentRadius * Mathf.Rad2Deg * Time.fixedDeltaTime) * 0.2f;
            collectiblesHolder.Rotate(rotationAxis, rotationAngle, Space.World);
        }
    }
}