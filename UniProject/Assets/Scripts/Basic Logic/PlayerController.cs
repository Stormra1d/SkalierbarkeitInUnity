using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Handles controlling player-related actions.
/// </summary>
public class PlayerController : MonoBehaviour
{
    [Header("Movement Settings")]
    public float jumpForce = 7f;
    public float airMultiplier = 0.5f;
    public float groundDrag = 8f;

    [Header("Keybinds")]
    public KeyCode jumpKey = KeyCode.Space;

    [Header("Ground Check")]
    public float playerHeight = 1.0f;
    public LayerMask groundLayerMask;
    public LayerMask roadLayerMask;
    private bool isGrounded;
    private const float groundCheckOffset = 0.01f;

    [Header("Speed Control")]
    public float acceleration = 2f;
    public float maxSpeed = 10f;
    public float deceleration = 2f;

    [Header("Components")]
    private Rigidbody rb;
    private bool canJump = true;

    [Header("Input Variables")]
    private float inputX;
    private float inputY;
    private Vector3 movementDirection;

    public Transform playerOrientation;
    private float currentRadius;

    private Transform visualContainer;
    private PlayerCollectibles playerCollectibles;

    public float maxRotationSpeed = 360f;
    public float rotationDamping = 5f;

    private Vector3 previousVelocity = Vector3.zero;
    private Vector3 currentRotationAxis = Vector3.zero;
    private float currentRotationSpeed = 0f;
    private Vector3 currentVelocity;

    /// <summary>
    /// Sets up initial configurations for the player.
    /// </summary>
    private void Awake()
    {
        // Check for required components at runtime
        if (TryGetComponent<Rigidbody>(out rb))
        {
            rb.freezeRotation = true;
        }
        else
        {
            Debug.LogError("Rigidbody is missing! Please attach a Rigidbody to the player.");
        }

        if (!playerOrientation)
        {
            Debug.LogError("Orientation Transform is missing! Please assign it in the inspector.");
        }

        if (!GetComponentInChildren<SphereCollider>())
        {
            Debug.LogError("SphereCollider is missing! Ensure a SphereCollider is attached to the player.");
        }

        if (!Input.GetKey(jumpKey))
        {
            Debug.LogWarning("Jump key is not assigned or detected. Check input bindings!");
        }

        visualContainer = new GameObject("VisualContainer").transform;
        visualContainer.SetParent(transform);
        visualContainer.localPosition = Vector3.zero;
        playerCollectibles = GetComponent<PlayerCollectibles>();
    }

    /// <summary>
    /// Called every frame.
    /// Runs the necessary checks for inputs and grounded checks.
    /// </summary>
    private void Update()
    {
        // Dynamically update the height based on the current size of the collider
        playerHeight = GetComponentInChildren<SphereCollider>().bounds.size.y;

        // Create raycasts around the base of the player
        int numRays = 8;
        float raycastRadius = 0.2f;
        isGrounded = false;

        for (int i = 0; i < numRays; i++)
        {
            float angle = i * Mathf.PI * 2 / numRays;
            Vector3 rayOrigin = transform.position + new Vector3(Mathf.Cos(angle) * raycastRadius, 0, Mathf.Sin(angle) * raycastRadius);

            if (Physics.Raycast(rayOrigin, Vector3.down, playerHeight * 0.5f + groundCheckOffset, groundLayerMask | roadLayerMask))
            {
                isGrounded = true;
                break;
            }
        }

        HandleInput();
        ControlDrag();
    }

    /// <summary>
    /// Handles physics-based calculations.
    /// </summary>
    private void FixedUpdate()
    {
        MovePlayer();
        ApplyRolling();
    }

    /// <summary>
    /// Processes player inputs for movement and jumping.
    /// </summary>
    private void HandleInput()
    {
        // Retrieve raw input
        inputX = Input.GetAxisRaw("Horizontal");
        inputY = Input.GetAxisRaw("Vertical");

        // Check if the player is allowed to jump
        if (Input.GetKey(jumpKey) && canJump && isGrounded)
        {
            Jump();
            canJump = false;
            StartCoroutine(ResetJumpCooldown());
        }
    }

    /// <summary>
    /// Handles the actual force application and movement of the player.
    /// </summary>
    private void MovePlayer()
    {
        // Calculate move direction based on input and player orientation
        Vector3 forward = playerOrientation.forward;
        Vector3 right = playerOrientation.right;
        forward.y = 0;
        right.y = 0;
        forward.Normalize();
        right.Normalize();
        movementDirection = (forward * inputY + right * inputX).normalized;

        // Calculate target velocity
        Vector3 targetVelocity = movementDirection * maxSpeed;

        currentVelocity = Vector3.Lerp(currentVelocity, targetVelocity, Time.fixedDeltaTime * (movementDirection.magnitude > 0.1f ? acceleration : deceleration));

        // Apply horizontal movement
        rb.linearVelocity = new Vector3(currentVelocity.x, rb.linearVelocity.y, currentVelocity.z);
    }

    /// <summary>
    /// Handles jumping.
    /// </summary>
    private void Jump()
    {
        rb.linearVelocity = new Vector3(rb.linearVelocity.x, jumpForce, rb.linearVelocity.z);
    }

    /// <summary>
    /// Resets the canJump variable.
    /// </summary>
    private IEnumerator ResetJumpCooldown()
    {
        yield return new WaitForSeconds(0.25f);
        canJump = true;
    }

    /// <summary>
    /// Adjusts the drag on the Rigidbody based on whether the player is grounded.
    /// </summary>
    private void ControlDrag()
    {
        rb.linearDamping = isGrounded ? groundDrag : 0f;
    }

    /// <summary>
    /// Helper function to create the rolling effect of the player.
    /// </summary>
    private void ApplyRolling()
    {
        // Only apply rolling when grounded and moving
        if (!isGrounded || rb.linearVelocity.magnitude < 0.1f)
        {
            currentRotationSpeed = 0f;
            return;
        }

        // Calculate rotation axis based on movement direction
        Vector3 movementDirection = new Vector3(rb.linearVelocity.x, 0, rb.linearVelocity.z).normalized;
        Vector3 rotationAxis = Vector3.Cross(movementDirection, Vector3.up);

        // Calculate rotation speed based on actual ground movement
        float targetRotationSpeed = rb.linearVelocity.magnitude * 100f;
        targetRotationSpeed = Mathf.Clamp(targetRotationSpeed, 0f, maxRotationSpeed);

        // Smoothly interpolate rotation speed
        currentRotationSpeed = Mathf.Lerp(currentRotationSpeed, targetRotationSpeed, Time.deltaTime * rotationDamping);

        if (!isGrounded)
        {
            currentRotationSpeed = 0f;
            return;
        }
    }
}