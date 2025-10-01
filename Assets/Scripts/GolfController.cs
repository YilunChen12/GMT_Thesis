using UnityEngine;
using Valve.VR;
using Valve.VR.InteractionSystem;
using System.Collections;

public class GolfController : MonoBehaviour
{
    [Header("Golf Ball")]
    public GameObject golfBallPrefab;
    public float ballRadius = 0.1f;
    public Material ballMaterial;
    
    [Header("Club Settings")]
    public float maxHitForce = 20f;
    public float minHitForce = 2f;
    public float forceMultiplier = 1f;
    public LayerMask surfaceLayerMask = 1;
    
    [Header("Dragging Settings")]
    public float dragSensitivity = 2f;
    public float maxDragDistance = 5f;
    public Color dragIndicatorColor = Color.yellow;
    
    [Header("VR Controller References")]
    public Hand leftHand;
    public Hand rightHand;
    
    [Header("Club Prefab")]
    public GameObject golfClubPrefab;
    
    private BackpropagationManager backpropManager;
    private GameObject currentBall;
    private Rigidbody ballRigidbody;
    private bool isDragging = false;
    private bool isHittable = true;
    private Vector3 dragStartPosition;
    private float initialBiasValue;
    
    private GameObject golfClub;
    private Hand activeHand;
    private Vector3 lastClubPosition;
    private Vector3 clubVelocity;
    
    // Ball physics
    private bool ballInMotion = false;
    private float ballStopThreshold = 0.1f;
    private float ballStopTimer = 0f;
    private float ballStopDelay = 1f;
    
    // FIXED: Use SteamVR_Actions.default_* instead of manually initialized actions for better scene transition stability
    // SteamVR input actions - REMOVED problematic manual initialization:
    // public SteamVR_Action_Boolean grabAction = SteamVR_Input.GetAction<SteamVR_Action_Boolean>("GrabGrip");
    // public SteamVR_Action_Boolean triggerAction = SteamVR_Input.GetAction<SteamVR_Action_Boolean>("Trigger");
    // public SteamVR_Action_Vector2 trackpadAction = SteamVR_Input.GetAction<SteamVR_Action_Vector2>("Trackpad");
    
    void Awake()
    {
        if (leftHand == null) leftHand = FindObjectOfType<Hand>();
        if (rightHand == null) 
        {
            Hand[] hands = FindObjectsOfType<Hand>();
            if (hands.Length > 1) rightHand = hands[1];
        }
    }
    
    void Start()
    {
        CreateGolfClub();
    }
    
    public void Initialize(BackpropagationManager manager)
    {
        backpropManager = manager;
        Debug.Log("Golf controller initialized");
    }
    
    void CreateGolfClub()
    {
        if (golfClubPrefab != null)
        {
            golfClub = Instantiate(golfClubPrefab);
            
            // Add interaction components if not present
            Interactable interactable = golfClub.GetComponent<Interactable>();
            if (interactable == null)
            {
                interactable = golfClub.AddComponent<Interactable>();
            }
            
            // Set up club properties
            //golfClub.layer = LayerMask.NameToLayer("Interactable");
            
            Debug.Log("Golf club created and configured");
        }
        else
        {
            Debug.LogWarning("Golf club prefab not assigned!");
        }
    }
    
    public void SetBallPosition(Vector3 worldPosition)
    {
        if (currentBall != null)
        {
            DestroyImmediate(currentBall);
        }
        
        CreateBall(worldPosition);
    }
    
    void CreateBall(Vector3 position)
    {
        // Create ball if prefab is assigned, otherwise create a simple sphere
        if (golfBallPrefab != null)
        {
            currentBall = Instantiate(golfBallPrefab, position, Quaternion.identity);
        }
        else
        {
            currentBall = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            currentBall.transform.position = position;
            currentBall.transform.localScale = Vector3.one * ballRadius * 2f;
        }
        
        // Ensure ball has necessary components
        ballRigidbody = currentBall.GetComponent<Rigidbody>();
        if (ballRigidbody == null)
        {
            ballRigidbody = currentBall.AddComponent<Rigidbody>();
        }
        
        // Configure rigidbody
        ballRigidbody.mass = 0.045f; // Standard golf ball mass in kg
        ballRigidbody.drag = 0.2f;
        ballRigidbody.angularDrag = 0.1f;
        ballRigidbody.useGravity = false; // Disable gravity to keep ball on surface
        
        // Add collision detection
        SphereCollider collider = currentBall.GetComponent<SphereCollider>();
        if (collider == null)
        {
            collider = currentBall.AddComponent<SphereCollider>();
        }
        collider.radius = ballRadius;
        
        // Set material
        if (ballMaterial != null)
        {
            currentBall.GetComponent<Renderer>().material = ballMaterial;
        }
        
        // Add ball interaction script
        BallInteraction ballInteraction = currentBall.GetComponent<BallInteraction>();
        if (ballInteraction == null)
        {
            ballInteraction = currentBall.AddComponent<BallInteraction>();
        }
        ballInteraction.Initialize(this);
        
        isHittable = true;
        ballInMotion = false;
        
        Debug.Log($"Ball created at position {position}");
    }
    
    void Update()
    {
        UpdateClubTracking();
        UpdateBallPhysics();
        HandleControllerInput();
    }
    
    void UpdateClubTracking()
    {
        if (golfClub == null) return;
        
        // Check if club is being held
        Interactable clubInteractable = golfClub.GetComponent<Interactable>();
        if (clubInteractable != null && clubInteractable.attachedToHand != null)
        {
            activeHand = clubInteractable.attachedToHand;
            
            // Calculate club velocity for hit detection
            Vector3 currentClubPosition = golfClub.transform.position;
            clubVelocity = (currentClubPosition - lastClubPosition) / Time.deltaTime;
            lastClubPosition = currentClubPosition;
        }
        else
        {
            activeHand = null;
            clubVelocity = Vector3.zero;
        }
    }
    
    void UpdateBallPhysics()
    {
        if (ballRigidbody == null) return;
        
        // Check if ball has stopped moving
        if (ballInMotion)
        {
            if (ballRigidbody.velocity.magnitude < ballStopThreshold)
            {
                ballStopTimer += Time.deltaTime;
                if (ballStopTimer >= ballStopDelay)
                {
                    OnBallStopped();
                }
            }
            else
            {
                ballStopTimer = 0f;
            }
        }
        
        // Keep ball constrained to surface height
        ConstrainBallToSurface();
    }
    
    void ConstrainBallToSurface()
    {
        if (currentBall == null || backpropManager == null) return;
        
        Vector3 ballPosition = currentBall.transform.position;
        Vector3 parameters = backpropManager.WorldPositionToParameters(ballPosition);
        
        // Update backpropagation manager with new parameters (w3, w4 from ball position)
        backpropManager.UpdateParameters(parameters.x, parameters.y, parameters.z);
    }
    
    void HandleControllerInput()
    {
        // Handle dragging with trigger
        HandleDragging();
        
        // Handle teleportation with trackpad
        HandleTeleportation();
    }
    
    void HandleDragging()
    {
        if (currentBall == null) return;
        
        // Check for trigger press on either controller  
        // FIXED: Use SteamVR_Actions.default_InteractUI instead of non-existent default_Trigger
        bool leftTrigger = leftHand != null && SteamVR_Actions.default_InteractUI.GetState(leftHand.handType);
        bool rightTrigger = rightHand != null && SteamVR_Actions.default_InteractUI.GetState(rightHand.handType);
        
        if ((leftTrigger || rightTrigger) && !isDragging && isHittable)
        {
            // Start dragging if close enough to ball
            Hand dragHand = leftTrigger ? leftHand : rightHand;
            float distanceToBall = Vector3.Distance(dragHand.transform.position, currentBall.transform.position);
            
            if (distanceToBall < 1f) // Within reach
            {
                StartDragging(dragHand);
            }
        }
        else if (!(leftTrigger || rightTrigger) && isDragging)
        {
            StopDragging();
        }
        
        if (isDragging)
        {
            UpdateDragging();
        }
    }
    
    void StartDragging(Hand dragHand)
    {
        isDragging = true;
        dragStartPosition = dragHand.transform.position;
        initialBiasValue = backpropManager.CurrentB5;
        
        // Stop ball physics while dragging
        if (ballRigidbody != null)
        {
            ballRigidbody.isKinematic = true;
        }
        
        // Visual feedback
        if (currentBall != null)
        {
            Renderer ballRenderer = currentBall.GetComponent<Renderer>();
            if (ballRenderer != null)
            {
                ballRenderer.material.color = dragIndicatorColor;
            }
        }
        
        Debug.Log("Started dragging ball");
    }
    
    void UpdateDragging()
    {
        Hand activeHand = SteamVR_Actions.default_InteractUI.GetState(SteamVR_Input_Sources.LeftHand) ? leftHand : rightHand;
        if (activeHand == null) return;
        
        // Calculate vertical drag distance
        float verticalDelta = activeHand.transform.position.y - dragStartPosition.y;
        verticalDelta = Mathf.Clamp(verticalDelta, -maxDragDistance, maxDragDistance);
        
        // Convert to bias change
        float biasRange = backpropManager.BiasRange.y - backpropManager.BiasRange.x;
        float biasChange = (verticalDelta / maxDragDistance) * biasRange * dragSensitivity;
        float newBias = Mathf.Clamp(initialBiasValue + biasChange, 
                                   backpropManager.BiasRange.x, 
                                   backpropManager.BiasRange.y);
        
        // Update ball position to reflect new bias
        Vector3 newBallPosition = backpropManager.ParametersToWorldPosition(
            backpropManager.CurrentW3, 
            backpropManager.CurrentW4, 
            newBias);
        
        currentBall.transform.position = newBallPosition;
        
        // Update parameters
        backpropManager.UpdateParameters(backpropManager.CurrentW3, backpropManager.CurrentW4, newBias);
    }
    
    void StopDragging()
    {
        isDragging = false;
        
        // Re-enable ball physics
        if (ballRigidbody != null)
        {
            ballRigidbody.isKinematic = false;
        }
        
        // Restore ball color
        if (currentBall != null && ballMaterial != null)
        {
            Renderer ballRenderer = currentBall.GetComponent<Renderer>();
            if (ballRenderer != null)
            {
                ballRenderer.material = ballMaterial;
            }
        }
        
        Debug.Log("Stopped dragging ball");
    }
    
    void HandleTeleportation()
    {
        // Handle trackpad for teleportation on surface
        if (leftHand != null)
        {
            // FIXED: Use SteamVR_Actions.default_TouchpadPosition instead of non-existent default_Trackpad
            Vector2 trackpadInput = SteamVR_Actions.default_TouchpadPosition.GetAxis(SteamVR_Input_Sources.LeftHand);
            if (trackpadInput.magnitude > 0.1f) // 有输入时
            {
                // TODO: Implement surface teleportation based on trackpad input
            }
        }
    }
    
    public void OnBallHit(Vector3 clubPosition, Vector3 clubVelocity)
    {
        if (!isHittable || isDragging) return;
        
        float hitForce = Mathf.Clamp(clubVelocity.magnitude * forceMultiplier, minHitForce, maxHitForce);
        Vector3 hitDirection = (currentBall.transform.position - clubPosition).normalized;
        
        // Apply force to ball
        ballRigidbody.AddForce(hitDirection * hitForce, ForceMode.Impulse);
        
        ballInMotion = true;
        isHittable = false; // Prevent multiple hits until ball stops
        
        Debug.Log($"Ball hit with force {hitForce:F1} in direction {hitDirection}");
    }
    
    void OnBallStopped()
    {
        ballInMotion = false;
        isHittable = true;
        ballStopTimer = 0f;
        
        // Update final parameters based on ball position
        Vector3 ballPosition = currentBall.transform.position;
        Vector3 parameters = backpropManager.WorldPositionToParameters(ballPosition);
        backpropManager.UpdateParameters(parameters.x, parameters.y, parameters.z);
        
        Debug.Log($"Ball stopped at parameters: w3={parameters.x:F3}, w4={parameters.y:F3}, b5={parameters.z:F3}");
    }
    
    // Called by BallInteraction when hit by club
    public void OnClubCollision(Vector3 clubVelocity, Vector3 contactPoint)
    {
        OnBallHit(contactPoint, clubVelocity);
    }
}

// Component for ball collision detection with golf club
public class BallInteraction : MonoBehaviour
{
    private GolfController golfController;
    
    public void Initialize(GolfController controller)
    {
        golfController = controller;
    }
    
    void OnCollisionEnter(Collision collision)
    {
        // Check if hit by golf club
        if (collision.gameObject.name.Contains("Club") || collision.gameObject.tag == "GolfClub")
        {
            Vector3 clubVelocity = collision.relativeVelocity;
            Vector3 contactPoint = collision.contacts[0].point;
            
            if (golfController != null)
            {
                golfController.OnClubCollision(clubVelocity, contactPoint);
            }
        }
    }
} 