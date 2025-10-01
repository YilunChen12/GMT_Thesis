using UnityEngine;
using Valve.VR;
using Valve.VR.InteractionSystem;
using System.Collections;
using System.Linq;

public class SlingshotController : MonoBehaviour
{
    [Header("Slingshot Ball")]
    public GameObject ballPrefab;
    public float ballRadius = 0.1f;
    public Material ballMaterial;
    
    [Header("Slingshot Base")]
    public GameObject slingshotBasePrefab;
    public Vector3 slingshotOffset = new Vector3(0f, 0f, 0.1f);
    
    [Header("Force Settings")]
    public float minForce = 2f;
    public float maxForce = 20f;
    public float maxStretchDistance = 1f;
    public AnimationCurve forceCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
    
    [Header("Launch Settings")]
    [Tooltip("Minimum stretch distance required to launch the ball")]
    public float launchDeadzone = 0.2f;
    [Tooltip("Show trajectory prediction line when stretching")]
    public bool showTrajectoryPreview = true;
    [Tooltip("Material for the trajectory prediction line")]
    public Material trajectoryLineMaterial;
    [Tooltip("Color of trajectory line when launch is possible (stretch > deadzone)")]
    public Color trajectoryValidColor = Color.green;
    [Tooltip("Color of trajectory line when launch is not possible (stretch < deadzone)")]
    public Color trajectoryInvalidColor = Color.red;
    
    [Header("Rubber Band Visualization")]
    public LineRenderer rubberBandLeft;
    public LineRenderer rubberBandRight;
    public Material rubberBandMaterial;
    [Tooltip("Color when rubber band is fully stretched (high force)")]
    public Color stretchColor = Color.red;
    [Tooltip("Color when rubber band is relaxed (low/no force)")]
    public Color releaseColor = Color.green;
    
    [Header("Physics Settings")]
    public LayerMask surfaceLayerMask = 1;
    public float ballDrag = 0.5f;
    public bool enableGravity = false; // Disabled for parameter space movement
    [Tooltip("Maximum distance ball travels based on force")]
    public float maxTravelDistance = 5f;
    [Tooltip("Time for ball to travel from launch to destination")]
    public float travelTime = 2f;
    
    [Header("Learning Rate-Based Movement")]
    [Tooltip("Enable learning rate-based ball movement distance")]
    public bool useLearningRateMovement = true;
    [Tooltip("Base movement distance when learning rate is 0.1")]
    public float baseMovementDistance = 3f;
    [Tooltip("Learning rate scaling factor for movement distance")]
    public float learningRateScale = 10f;
    [Tooltip("Minimum movement distance regardless of learning rate")]
    public float minMovementDistance = 0.5f;
    [Tooltip("Maximum movement distance regardless of learning rate")]
    public float maxMovementDistance = 8f;
    
    [Header("Testing Mode")]
    [Tooltip("When enabled, places the ball in front of the player for interaction testing")]
    public bool isTesting = false;
    [Tooltip("Position in front of player where ball spawns during testing")]
    public Vector3 testingBallPosition = new Vector3(0f, 1.2f, 1f);
    
    private BackpropagationManager backpropManager;
    private GameObject currentBall;
    private GameObject slingshotBase;
    
    // Public property to access current ball
    public GameObject CurrentBall => currentBall;
    private Rigidbody ballRigidbody;
    
    // VR Hand references - automatically found at runtime
    private Hand leftHand;
    private Hand rightHand;
    
    // Slingshot mechanics
    private bool isBallGrabbed = false;
    private bool isStretching = false;
    private Vector3 initialBallPosition;
    private Vector3 stretchStartPosition;
    private float currentStretchDistance = 0f;
    private Vector3 launchDirection = Vector3.forward;
    private float launchForce = 0f;
    
    // Trajectory prediction
    private LineRenderer trajectoryLine;
    private bool canLaunch = false; // Whether current stretch distance exceeds deadzone
    
    // VR Input
    // FIXED: Use SteamVR_Actions.default_* instead of manually initialized actions for better scene transition stability
    // public SteamVR_Action_Boolean grabAction = SteamVR_Input.GetAction<SteamVR_Action_Boolean>("GrabGrip");
    // public SteamVR_Action_Boolean triggerAction = SteamVR_Input.GetAction<SteamVR_Action_Boolean>("InteractUI"); // Fixed: Use existing action
    
    void Awake()
    {
        Debug.Log("=== SLINGSHOT CONTROLLER AWAKE ===");
        
        // Ensure this controller object doesn't have unwanted physics components
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = true; // Prevent physics interactions
            Debug.Log("Set SlingshotController Rigidbody to kinematic");
        }
        
        Collider col = GetComponent<Collider>();
        if (col != null)
        {
            col.isTrigger = true; // Prevent collision interactions
            Debug.Log("Set SlingshotController Collider to trigger");
        }
        
        FindVRHands();
    }
    
    void Start()
    {
        // Validate settings for proper rubber band functionality
        if (maxStretchDistance <= 0)
        {
            Debug.LogWarning($"maxStretchDistance is {maxStretchDistance} - setting to default value of 1.0f for rubber band color changes to work");
            maxStretchDistance = 1.0f;
        }
        
        CreateSlingshotBase();
        SetupRubberBandVisuals();
        AttachToLeftHand();
        
        // In testing mode, create ball immediately for interaction testing
        if (isTesting)
        {
            Debug.Log("Testing mode enabled - creating ball at testing position");
            CreateBall(testingBallPosition);
        }
        
        Debug.Log($"Slingshot initialized - Max stretch: {maxStretchDistance}, Colors: {releaseColor} -> {stretchColor}");
    }
    
    public void Initialize(BackpropagationManager manager)
    {
        Debug.Log("=== SLINGSHOT CONTROLLER INITIALIZE START ===");
        backpropManager = manager;
        
        if (isTesting)
        {
            Debug.Log("Testing mode enabled - backprop manager is optional for testing");
        }
        else
        {
            Debug.Log("Normal mode - backpropagation manager reference set");
        }
        
        Debug.Log("=== SLINGSHOT CONTROLLER INITIALIZE COMPLETE ===");
    }
    
    /// <summary>
    /// Find VR hands in the scene
    /// </summary>
    void FindVRHands()
    {
        Debug.Log("Searching for VR hands...");
        
        Hand[] allHands = FindObjectsOfType<Hand>();
        Debug.Log($"Found {allHands.Length} Hand components in scene");
        
        foreach (var hand in allHands)
        {
            Debug.Log($"Hand found: {hand.name}, HandType: {hand.handType}");
            
            if (hand.name.ToLower().Contains("left") || hand.handType == SteamVR_Input_Sources.LeftHand)
            {
                leftHand = hand;
                Debug.Log($"Assigned left hand: {hand.name}");
            }
            else if (hand.name.ToLower().Contains("right") || hand.handType == SteamVR_Input_Sources.RightHand)
            {
                rightHand = hand;
                Debug.Log($"Assigned right hand: {hand.name}");
            }
        }
        
        Debug.Log($"Final hand assignment - Left: {(leftHand?.name ?? "None")}, Right: {(rightHand?.name ?? "None")}");
    }
    
    void CreateSlingshotBase()
    {
        Debug.Log("=== CREATING SLINGSHOT BASE ===");
        
        if (slingshotBasePrefab != null)
        {
            Debug.Log("Using assigned slingshot base prefab");
            slingshotBase = Instantiate(slingshotBasePrefab);
            
            // Ensure prefab slingshot doesn't have unwanted physics
            ConfigureSlingshotPhysics(slingshotBase);
        }
        else
        {
            Debug.LogWarning("Slingshot base prefab not assigned! Creating basic slingshot from primitives");
            slingshotBase = CreateBasicSlingshot();
        }
        
        if (slingshotBase != null)
        {
            // Set slingshot base as child of this controller (will be attached to hand via AttachToLeftHand)
            slingshotBase.transform.SetParent(transform);
            slingshotBase.transform.localPosition = slingshotOffset;
            slingshotBase.transform.localRotation = Quaternion.identity;
            
            Debug.Log($"Slingshot base created and attached to controller");
        }
        else
        {
            Debug.LogError("Failed to create slingshot base!");
        }
    }
    
    /// <summary>
    /// Configure physics for slingshot to prevent unwanted interactions
    /// </summary>
    void ConfigureSlingshotPhysics(GameObject slingshotObject)
    {
        // Remove or configure physics components recursively
        Rigidbody[] rigidbodies = slingshotObject.GetComponentsInChildren<Rigidbody>();
        foreach (var rb in rigidbodies)
        {
            rb.isKinematic = true; // Prevent physics interactions
            Debug.Log($"Set {rb.gameObject.name} Rigidbody to kinematic");
        }
        
        Collider[] colliders = slingshotObject.GetComponentsInChildren<Collider>();
        foreach (var col in colliders)
        {
            col.isTrigger = true; // Prevent collision interactions but allow trigger events
            Debug.Log($"Set {col.gameObject.name} Collider to trigger");
        }
    }
    
    GameObject CreateBasicSlingshot()
    {
        GameObject slingshot = new GameObject("SlingshotBase");
        
        // Create Y-shaped slingshot frame
        GameObject handle = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        handle.transform.SetParent(slingshot.transform);
        handle.transform.localPosition = Vector3.zero;
        handle.transform.localScale = new Vector3(0.03f, 0.15f, 0.03f);
        handle.GetComponent<Renderer>().material.color = new Color(0.6f, 0.3f, 0.1f); // Brown
        
        // Remove collider since we don't want slingshot to be affected by physics
        DestroyImmediate(handle.GetComponent<Collider>());
        
        // Left fork
        GameObject leftFork = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        leftFork.transform.SetParent(slingshot.transform);
        leftFork.transform.localPosition = new Vector3(-0.05f, 0.1f, 0f);
        leftFork.transform.localRotation = Quaternion.Euler(0f, 0f, 30f);
        leftFork.transform.localScale = new Vector3(0.02f, 0.08f, 0.02f);
        leftFork.GetComponent<Renderer>().material.color = new Color(0.6f, 0.3f, 0.1f);
        
        // Remove collider
        DestroyImmediate(leftFork.GetComponent<Collider>());
        
        // Right fork
        GameObject rightFork = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        rightFork.transform.SetParent(slingshot.transform);
        rightFork.transform.localPosition = new Vector3(0.05f, 0.1f, 0f);
        rightFork.transform.localRotation = Quaternion.Euler(0f, 0f, -30f);
        rightFork.transform.localScale = new Vector3(0.02f, 0.08f, 0.02f);
        rightFork.GetComponent<Renderer>().material.color = new Color(0.6f, 0.3f, 0.1f);
        
        // Remove collider
        DestroyImmediate(rightFork.GetComponent<Collider>());
        
        Debug.Log("Basic slingshot created without colliders (won't be affected by physics)");
        
        return slingshot;
    }
    
    void SetupRubberBandVisuals()
    {
        Debug.Log("Setting up rubber band visuals...");
        
        if (rubberBandLeft == null)
        {
            GameObject leftBand = new GameObject("RubberBandLeft");
            leftBand.transform.SetParent(transform);
            rubberBandLeft = leftBand.AddComponent<LineRenderer>();
        }
        
        if (rubberBandRight == null)
        {
            GameObject rightBand = new GameObject("RubberBandRight");
            rightBand.transform.SetParent(transform);
            rubberBandRight = rightBand.AddComponent<LineRenderer>();
        }
        
        // Setup trajectory prediction line
        if (trajectoryLine == null)
        {
            GameObject trajectoryObj = new GameObject("TrajectoryLine");
            trajectoryObj.transform.SetParent(transform);
            trajectoryLine = trajectoryObj.AddComponent<LineRenderer>();
            ConfigureTrajectoryLine();
        }
        
        // Configure line renderers
        ConfigureRubberBand(rubberBandLeft);
        ConfigureRubberBand(rubberBandRight);
        
        // Initially hide rubber bands and trajectory line
        rubberBandLeft.enabled = false;
        rubberBandRight.enabled = false;
        trajectoryLine.enabled = false;
    }
    
    void ConfigureRubberBand(LineRenderer lineRenderer)
    {
        // Each LineRenderer needs its own material instance for color changes to work
        if (rubberBandMaterial != null)
        {
            // Create instance of the assigned material so we can change colors independently
            lineRenderer.material = new Material(rubberBandMaterial);
        }
        else
        {
            // Create a new material with a shader that supports color changes
            lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
        }
        
        // Set initial color
        lineRenderer.material.color = releaseColor;
        
        lineRenderer.startWidth = 0.01f;
        lineRenderer.endWidth = 0.01f;
        lineRenderer.positionCount = 2;
        lineRenderer.useWorldSpace = true;
        
        Debug.Log($"Configured rubber band with material: {lineRenderer.material.name}, initial color: {lineRenderer.material.color}");
    }
    
    void ConfigureTrajectoryLine()
    {
        // Configure trajectory line material
        if (trajectoryLineMaterial != null)
        {
            trajectoryLine.material = new Material(trajectoryLineMaterial);
        }
        else
        {
            // Create a new material with a shader that supports color changes
            trajectoryLine.material = new Material(Shader.Find("Sprites/Default"));
        }
        
        // Set initial properties
        trajectoryLine.material.color = trajectoryInvalidColor; // Start as invalid
        trajectoryLine.startWidth = 0.02f; // Slightly thicker than rubber bands
        trajectoryLine.endWidth = 0.015f; // Tapered for better visual effect
        trajectoryLine.positionCount = 2; // Start and end point
        trajectoryLine.useWorldSpace = true;
        
        // Make trajectory line slightly transparent
        Color trajColor = trajectoryLine.material.color;
        trajColor.a = 0.8f;
        trajectoryLine.material.color = trajColor;
        
        Debug.Log($"Configured trajectory line with material: {trajectoryLine.material.name}");
    }
    
    public void SetBallPosition(Vector3 worldPosition)
    {
        Debug.Log($"=== SETTING BALL POSITION ===");
        
        Vector3 finalPosition;
        if (isTesting)
        {
            finalPosition = testingBallPosition;
            Debug.Log($"TESTING MODE: Using testing position: {finalPosition}");
        }
        else
        {
            finalPosition = worldPosition;
            Debug.Log($"NORMAL MODE: Using neural network position: {finalPosition}");
        }
        
        if (currentBall != null)
        {
            Debug.Log("Destroying existing ball");
            Destroy(currentBall);
        }
        
        CreateBall(finalPosition);
        
        if (currentBall != null)
        {
            Debug.Log($"Ball successfully created at: {currentBall.transform.position}");
        }
        else
        {
            Debug.LogError("Failed to create ball!");
        }
    }
    
    void CreateBall(Vector3 position)
    {
        Debug.Log($"Creating ball at position: {position}");
        
        // Create ball
        if (ballPrefab != null)
        {
            Debug.Log("Using assigned ball prefab");
            currentBall = Instantiate(ballPrefab, position, Quaternion.identity);
        }
        else
        {
            Debug.LogWarning("Ball prefab not assigned! Creating basic ball from sphere primitive");
            currentBall = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            currentBall.transform.position = position;
            currentBall.transform.localScale = Vector3.one * ballRadius * 2f;
            currentBall.name = "Slingshot Ball";
            
            Renderer ballRenderer = currentBall.GetComponent<Renderer>();
            if (ballRenderer != null)
            {
                // Make ball highly visible for debugging
                UpdateBallColor(); // Set initial ball color based on current loss
                Debug.Log("Set initial ball color based on current parameters");
            }
        }
        
        if (currentBall == null)
        {
            Debug.LogError("Failed to create ball object!");
            return;
        }
        
        // Ensure ball is visible and properly positioned
        currentBall.SetActive(true);
        currentBall.transform.position = position; // Double-check position
        
        Debug.Log($"Ball active: {currentBall.activeInHierarchy}");
        Debug.Log($"Ball position after creation: {currentBall.transform.position}");
        Debug.Log($"Ball scale: {currentBall.transform.localScale}");
        
        // Setup ball physics
        ballRigidbody = currentBall.GetComponent<Rigidbody>();
        if (ballRigidbody == null)
        {
            ballRigidbody = currentBall.AddComponent<Rigidbody>();
            Debug.Log("Added Rigidbody component to ball");
        }
        
        ballRigidbody.mass = 0.045f;
        ballRigidbody.drag = ballDrag;
        ballRigidbody.useGravity = enableGravity; // Should be false for parameter space movement
        ballRigidbody.isKinematic = true; // Start kinematic until grabbed
        
        Debug.Log($"Ball physics configured - Gravity: {ballRigidbody.useGravity}, Kinematic: {ballRigidbody.isKinematic}");
        
        // Add collider
        SphereCollider collider = currentBall.GetComponent<SphereCollider>();
        if (collider == null)
        {
            collider = currentBall.AddComponent<SphereCollider>();
            Debug.Log("Added SphereCollider component to ball");
        }
        collider.radius = ballRadius;
        
        // Set material
        if (ballMaterial != null)
        {
            currentBall.GetComponent<Renderer>().material = ballMaterial;
            Debug.Log("Applied custom ball material");
        }
        
        // Store initial position for slingshot mechanics
        initialBallPosition = position;
        
        Debug.Log($"Ball successfully created at position {currentBall.transform.position}");
    }
    
    void Update()
    {
        // FIXED: Add null checking for SteamVR system to prevent errors during scene transitions
        if (!IsSteamVRReady())
        {
            return;
        }
        
        HandleSlingshotInput();
        UpdateRubberBandVisuals();
        
        // Update parameter and UI displays if ball is moving
        if (ballRigidbody != null && !ballRigidbody.isKinematic)
        {
            UpdateParametersFromBallPosition();
        }
        
        // Update ball color more frequently for better visual feedback
        if (currentBall != null && backpropManager != null && !isTesting)
        {
            UpdateBallColor();
        }
        
        // Constrain ball movement to parameter box bounds
        if (currentBall != null && !isTesting)
        {
            ConstrainBallToParameterBox();
        }
    }
    
    /// <summary>
    /// Safely stops ball movement without Unity warnings
    /// </summary>
    void SafelyStopBallMovement()
    {
        if (ballRigidbody != null && !ballRigidbody.isKinematic)
        {
            // Only set velocity on non-kinematic rigidbodies to avoid Unity warnings
            ballRigidbody.velocity = Vector3.zero;
            ballRigidbody.angularVelocity = Vector3.zero;
        }
        // If rigidbody is kinematic, it automatically has zero velocity - no need to set it
    }
    
    /// <summary>
    /// Constrain ball movement to stay within parameter box bounds
    /// </summary>
    void ConstrainBallToParameterBox()
    {
        if (currentBall == null || isTesting) return;
        
        // Use ParameterBoxManager's constraint method
        ParameterBoxManager paramBoxManager = FindObjectOfType<ParameterBoxManager>();
        if (paramBoxManager != null)
        {
            bool wasConstrained = paramBoxManager.ConstrainBallToBox(currentBall, true);
            if (wasConstrained)
            {
                // Ball was moved back to valid bounds and movement was stopped
                SafelyStopBallMovement();
            }
        }
        else
        {
            Debug.LogWarning("ParameterBoxManager not found - cannot constrain ball to parameter box");
        }
    }
    
    void HandleSlingshotInput()
    {
        if (currentBall == null || rightHand == null) return;
        
        // FIXED: Use SteamVR_Actions.default_InteractUI instead of manually initialized triggerAction
        bool rightTrigger = SteamVR_Actions.default_InteractUI.GetState(rightHand.handType);
        
        if (rightTrigger && !isBallGrabbed)
        {
            // Check if right hand is close enough to ball to grab it
            float distanceToBall = Vector3.Distance(rightHand.transform.position, currentBall.transform.position);
            if (distanceToBall < 0.3f) // Grab range
            {
                StartBallGrab();
            }
        }
        else if (!rightTrigger && isBallGrabbed)
        {
            // Release ball
            ReleaseBall();
        }
        
        if (isBallGrabbed)
        {
            UpdateBallStretching();
        }
    }
    
    void StartBallGrab()
    {
        Debug.Log("Starting ball grab...");
        isBallGrabbed = true;
        isStretching = true;
        stretchStartPosition = currentBall.transform.position;
        canLaunch = false; // Reset launch capability
        
        // Make ball kinematic while grabbed
        if (ballRigidbody != null)
        {
            ballRigidbody.isKinematic = true;
        }
        
        // Show rubber bands and trajectory line
        rubberBandLeft.enabled = true;
        rubberBandRight.enabled = true;
        if (showTrajectoryPreview && trajectoryLine != null)
        {
            trajectoryLine.enabled = true;
        }
    }
    
    void UpdateBallStretching()
    {
        if (rightHand == null || slingshotBase == null) return;
        
        // Position ball at right hand location
        currentBall.transform.position = rightHand.transform.position;
        
        // Calculate stretch distance and direction
        Vector3 basePosition = slingshotBase.transform.position;
        Vector3 stretchVector = currentBall.transform.position - basePosition;
        float newStretchDistance = stretchVector.magnitude;
        
        // Update current stretch distance for rubber band visuals
        currentStretchDistance = newStretchDistance;
        
        // Limit stretch distance
        if (currentStretchDistance > maxStretchDistance)
        {
            Vector3 limitedPosition = basePosition + stretchVector.normalized * maxStretchDistance;
            currentBall.transform.position = limitedPosition;
            currentStretchDistance = maxStretchDistance;
            
            // Recalculate stretch vector after limiting
            stretchVector = currentBall.transform.position - basePosition;
        }
        
        // Calculate launch direction (opposite of stretch)
        launchDirection = -stretchVector.normalized;
        
        // Calculate launch force
        float stretchRatio = Mathf.Clamp01(currentStretchDistance / maxStretchDistance);
        launchForce = Mathf.Lerp(minForce, maxForce, forceCurve.Evaluate(stretchRatio));
        
        // Check if stretch distance exceeds deadzone for launch capability
        canLaunch = currentStretchDistance > launchDeadzone;
        
        // Update trajectory prediction
        UpdateTrajectoryPrediction();
        
        // Debug stretch info (every 30 frames to avoid spam) - DISABLED - too verbose
        // if (Time.frameCount % 30 == 0)
        // {
        //     Debug.Log($"Ball Stretching - Distance: {currentStretchDistance:F2}/{maxStretchDistance:F2} (Deadzone: {launchDeadzone:F2}), Ratio: {stretchRatio:F2}, Force: {launchForce:F2}, Can Launch: {canLaunch}");
        // }
        
        // Update parameter displays in real-time
        UpdateParametersFromBallPosition();
    }
    
    /// <summary>
    /// Calculate movement distance based on current learning rate and slingshot force
    /// </summary>
    float CalculateLearningRateBasedDistance()
    {
        if (!useLearningRateMovement || backpropManager == null)
        {
            return maxTravelDistance; // Fallback to original behavior
        }
        
        // Get current learning rate from neural network
        NeuralNetwork neuralNetwork = NeuralNetwork.Instance;
        if (neuralNetwork == null)
        {
            Debug.LogWarning("NeuralNetwork instance not found - using fallback movement distance");
            return maxTravelDistance;
        }
        
        float currentLearningRate = neuralNetwork.GetCurrentLearningRate();
        
        // Calculate maximum possible distance based on learning rate
        // Higher learning rate = larger maximum distance
        float learningRateFactor = currentLearningRate * learningRateScale;
        float maxPossibleDistance = baseMovementDistance * learningRateFactor;
        
        // Clamp maximum distance to valid range
        float clampedMaxDistance = Mathf.Clamp(maxPossibleDistance, minMovementDistance, maxMovementDistance);
        
        // Calculate how much of the maximum distance to use based on slingshot force
        float forceRatio = Mathf.Clamp01(launchForce / maxForce);
        float actualTravelDistance = clampedMaxDistance * forceRatio;
        
        // Ensure minimum travel distance even with very light pulls
        float finalDistance = Mathf.Max(actualTravelDistance, minMovementDistance * 0.5f);
        
        Debug.Log($"Learning rate-based movement calculation:");
        Debug.Log($"  Current learning rate: {currentLearningRate:F4}");
        Debug.Log($"  Learning rate factor: {learningRateFactor:F2}");
        Debug.Log($"  Max possible distance: {maxPossibleDistance:F2}");
        Debug.Log($"  Clamped max distance: {clampedMaxDistance:F2}");
        Debug.Log($"  Launch force: {launchForce:F2}/{maxForce:F2} (ratio: {forceRatio:F2})");
        Debug.Log($"  Actual travel distance: {actualTravelDistance:F2}");
        Debug.Log($"  Final distance: {finalDistance:F2}");
        
        return finalDistance;
    }
    
    /// <summary>
    /// Update trajectory prediction with learning rate-based distance
    /// </summary>
    void UpdateTrajectoryPrediction()
    {
        if (!showTrajectoryPreview || trajectoryLine == null || !trajectoryLine.enabled) return;
        
        // Calculate trajectory endpoint using learning rate-based distance
        float travelDistance = CalculateLearningRateBasedDistance();
        Vector3 startPosition = currentBall.transform.position;
        Vector3 endPosition = startPosition + (launchDirection * travelDistance);
        
        // Update trajectory line positions
        trajectoryLine.SetPosition(0, startPosition);
        trajectoryLine.SetPosition(1, endPosition);
        
        // Update trajectory line color based on launch capability
        Color trajectoryColor = canLaunch ? trajectoryValidColor : trajectoryInvalidColor;
        
        // Maintain transparency
        trajectoryColor.a = 0.8f;
        
        if (trajectoryLine.material != null)
        {
            trajectoryLine.material.color = trajectoryColor;
        }
        
        // Optional: Update line width based on force for better visual feedback
        float baseWidth = 0.02f;
        float widthMultiplier = canLaunch ? (1f + (launchForce / maxForce) * 0.5f) : 0.7f;
        trajectoryLine.startWidth = baseWidth * widthMultiplier;
        trajectoryLine.endWidth = (baseWidth * 0.75f) * widthMultiplier;
        
        // Debug trajectory info occasionally
        if (Time.frameCount % 60 == 0)
        {
            NeuralNetwork neuralNetwork = NeuralNetwork.Instance;
            if (neuralNetwork != null)
            {
                float currentLR = neuralNetwork.GetCurrentLearningRate();
                float forceRatio = Mathf.Clamp01(launchForce / maxForce);
                float learningRateFactor = currentLR * learningRateScale;
                float maxPossibleDistance = baseMovementDistance * learningRateFactor;
                float clampedMaxDistance = Mathf.Clamp(maxPossibleDistance, minMovementDistance, maxMovementDistance);
                
                Debug.Log($"Trajectory: Start={startPosition}, End={endPosition}, Distance={travelDistance:F2}");
                Debug.Log($"  LR: {currentLR:F3} | Force: {forceRatio:F2} | Max: {clampedMaxDistance:F2} | Valid: {canLaunch}");
            }
            else
            {
                Debug.Log($"Trajectory: Start={startPosition}, End={endPosition}, Distance={travelDistance:F2}, Valid={canLaunch}, Learning Rate Based={useLearningRateMovement}");
            }
        }
    }
    
    void ReleaseBall()
    {
        Debug.Log($"Attempting to release ball - Stretch distance: {currentStretchDistance:F2}, Deadzone: {launchDeadzone:F2}, Can launch: {canLaunch}");
        
        // Reset grab state
        isBallGrabbed = false;
        isStretching = false;
        
        // Hide rubber bands and trajectory line
        rubberBandLeft.enabled = false;
        rubberBandRight.enabled = false;
        if (trajectoryLine != null)
        {
            trajectoryLine.enabled = false;
        }
        
        // Check deadzone - only launch if stretch distance exceeds deadzone
        if (!canLaunch)
        {
            Debug.Log($"Ball release cancelled - stretch distance {currentStretchDistance:F2} is below deadzone {launchDeadzone:F2}. Ball stays at current position.");
            
            // Keep ball at current position (where player released it)
            // Don't reset to initialBallPosition - let it stay where the player released it
            
            // Ensure ball remains kinematic and stationary at current position
            if (ballRigidbody != null)
            {
                ballRigidbody.isKinematic = true;
                // Don't set velocity on kinematic bodies - Unity will warn about this
                // The kinematic body will automatically have zero velocity
            }
            
            return; // Don't launch the ball
        }
        
        // Ball can be launched - proceed with normal launch
        Debug.Log($"Launching ball with force: {launchForce}");
        
        // Calculate travel distance based on learning rate (not force)
        float travelDistance = CalculateLearningRateBasedDistance();
        Vector3 startPosition = currentBall.transform.position;
        Vector3 endPosition = startPosition + (launchDirection * travelDistance);
        
        Debug.Log($"Ball launching from {startPosition} to {endPosition} (distance: {travelDistance:F2})");
        Debug.Log($"Movement distance based on learning rate: {useLearningRateMovement}");
        
        // Keep ball kinematic and move it manually in straight line
        if (ballRigidbody != null)
        {
            ballRigidbody.isKinematic = true; // Keep kinematic for controlled movement
            ballRigidbody.useGravity = false; // Ensure no gravity
        }
        
        // Start straight-line movement
        StartCoroutine(MoveBallInStraightLine(startPosition, endPosition, travelTime));

        // Gameplay: Count a backprop step when releasing in backprop scene
        if (backpropManager != null && NeuralNetwork.Instance != null)
        {
            NeuralNetwork.Instance.ReportBackpropStep();
        }
    }
    
    void UpdateRubberBandVisuals()
    {
        if (!isStretching || slingshotBase == null || currentBall == null) 
        {
            // Debug why rubber bands might not be updating
            if (!isStretching) Debug.Log("Rubber bands not updating: isStretching = false");
            if (slingshotBase == null) Debug.Log("Rubber bands not updating: slingshotBase is null");
            if (currentBall == null) Debug.Log("Rubber bands not updating: currentBall is null");
            return;
        }
        
        Vector3 basePos = slingshotBase.transform.position;
        Vector3 ballPos = currentBall.transform.position;
        
        // Get fork positions (approximate)
        Vector3 leftForkPos = basePos + slingshotBase.transform.TransformDirection(new Vector3(-0.05f, 0.1f, 0f));
        Vector3 rightForkPos = basePos + slingshotBase.transform.TransformDirection(new Vector3(0.05f, 0.1f, 0f));
        
        // Update rubber band lines
        if (rubberBandLeft != null)
        {
            rubberBandLeft.SetPosition(0, leftForkPos);
            rubberBandLeft.SetPosition(1, ballPos);
        }
        
        if (rubberBandRight != null)
        {
            rubberBandRight.SetPosition(0, rightForkPos);
            rubberBandRight.SetPosition(1, ballPos);
        }
        
        // Color based on stretch - make sure we have valid stretch distance
        if (maxStretchDistance > 0)
        {
            float stretchRatio = Mathf.Clamp01(currentStretchDistance / maxStretchDistance);
            Color bandColor = Color.Lerp(releaseColor, stretchColor, stretchRatio);
            
            // Debug color changes - DISABLED - too verbose
            // if (Time.frameCount % 30 == 0) // Log every 30 frames to avoid spam
            // {
            //     Debug.Log($"Rubber band color update - Stretch: {currentStretchDistance:F2}/{maxStretchDistance:F2}, Ratio: {stretchRatio:F2}, Color: {bandColor}");
            // }
            
            // Apply color to both rubber bands and adjust width for better visual feedback
            if (rubberBandLeft != null && rubberBandLeft.material != null)
            {
                rubberBandLeft.material.color = bandColor;
                // Make rubber band thicker when stretched more
                float bandWidth = Mathf.Lerp(0.01f, 0.02f, stretchRatio);
                rubberBandLeft.startWidth = bandWidth;
                rubberBandLeft.endWidth = bandWidth;
            }
            
            if (rubberBandRight != null && rubberBandRight.material != null)
            {
                rubberBandRight.material.color = bandColor;
                // Make rubber band thicker when stretched more  
                float bandWidth = Mathf.Lerp(0.01f, 0.02f, stretchRatio);
                rubberBandRight.startWidth = bandWidth;
                rubberBandRight.endWidth = bandWidth;
            }
        }
        else
        {
            Debug.LogWarning("maxStretchDistance is 0 or negative - rubber band color won't change!");
        }
    }
    
    void UpdateParametersFromBallPosition()
    {
        if (currentBall == null) return;
        
        // In testing mode, don't update neural network parameters
        if (isTesting)
        {
            Debug.Log($"Testing mode: Ball position is {currentBall.transform.position}, but not updating neural network parameters");
            return;
        }
        
        if (backpropManager == null) return;
        
        // Convert ball world position to neural network parameters
        Vector3 ballWorldPos = currentBall.transform.position;
        
        // Check for landmark snapping
        Vector3 snappedPosition;
        ParameterBoxManager paramBoxManager = FindObjectOfType<ParameterBoxManager>();
        if (paramBoxManager != null && paramBoxManager.CheckLandmarkSnap(ballWorldPos, out snappedPosition))
        {
            // Snap ball to landmark position
            currentBall.transform.position = snappedPosition;
            ballWorldPos = snappedPosition;
            
            // Stop ball movement when snapped to landmark
            SafelyStopBallMovement();
            
            Debug.Log($"Ball snapped to landmark at position: {snappedPosition}");
        }
        
        Vector3 parameters = backpropManager.WorldPositionToParameters(ballWorldPos);
        
        // Update backpropagation manager with new parameters
        backpropManager.UpdateParameters(parameters.x, parameters.y, parameters.z);
        
        // Update ball color based on new loss value
        UpdateBallColor();
    }
    
    /// <summary>
    /// Move ball in straight line without physics (for parameter space movement)
    /// </summary>
    IEnumerator MoveBallInStraightLine(Vector3 startPos, Vector3 endPos, float duration)
    {
        Debug.Log($"Starting ball movement from {startPos} to {endPos} over {duration} seconds");
        
        // Get reference to ParameterBoxManager for constraint checking
        ParameterBoxManager paramBoxManager = FindObjectOfType<ParameterBoxManager>();
        
        // Ensure start and end positions are within parameter box bounds
        if (paramBoxManager != null)
        {
            startPos = paramBoxManager.ConstrainWorldPositionToBox(startPos);
            endPos = paramBoxManager.ConstrainWorldPositionToBox(endPos);
            Debug.Log($"Constrained movement: start={startPos}, end={endPos}");
        }
        
        float elapsedTime = 0f;
        Vector3 lastValidPosition = startPos; // Track last valid position inside the box
        
        while (elapsedTime < duration)
        {
            elapsedTime += Time.deltaTime;
            float progress = elapsedTime / duration;
            
            // Use smooth curve for more natural movement
            float smoothProgress = Mathf.SmoothStep(0f, 1f, progress);
            
            // Interpolate position
            Vector3 targetPosition = Vector3.Lerp(startPos, endPos, smoothProgress);
            
            // Constrain position to parameter box bounds
            Vector3 constrainedPosition = targetPosition;
            if (paramBoxManager != null)
            {
                constrainedPosition = paramBoxManager.ConstrainWorldPositionToBox(targetPosition);
                
                // Check if position was constrained (ball would have gone outside)
                bool wasConstrained = Vector3.Distance(targetPosition, constrainedPosition) > 0.001f;
                if (wasConstrained)
                {
                    Debug.Log($"Ball trajectory constrained during movement:");
                    Debug.Log($"  Target position: {targetPosition}");
                    Debug.Log($"  Constrained to: {constrainedPosition}");
                    Debug.Log($"  Stopping movement at parameter box boundary");
                    
                    // Ball hit the boundary - stop movement here
                    currentBall.transform.position = constrainedPosition;
                    UpdateParametersFromBallPosition();
                    break; // Exit movement loop when hitting boundary
                }
            }
            
            // Update ball position
            currentBall.transform.position = constrainedPosition;
            lastValidPosition = constrainedPosition; // Update last valid position
            
            // Check for landmark snapping during movement
            Vector3 snappedPosition;
            if (paramBoxManager != null && paramBoxManager.CheckLandmarkSnap(constrainedPosition, out snappedPosition))
            {
                // Snap to landmark and stop movement
                currentBall.transform.position = snappedPosition;
                Debug.Log($"Ball snapped to landmark during movement at position: {snappedPosition}");
                break; // Exit the movement loop
            }
            
            // Update parameters in real-time during movement
            UpdateParametersFromBallPosition();
            
            yield return null; // Wait for next frame
        }
        
        // Ensure ball is at a valid position when movement completes
        if (currentBall != null)
        {
            // Final constraint check
            if (paramBoxManager != null)
            {
                Vector3 finalPosition = paramBoxManager.ConstrainWorldPositionToBox(currentBall.transform.position);
                currentBall.transform.position = finalPosition;
                Debug.Log($"Ball movement completed at constrained position: {finalPosition}");
            }
            else
            {
                Debug.Log($"Ball movement completed at position: {currentBall.transform.position}");
            }
            
            UpdateParametersFromBallPosition();
        }
    }
    
    IEnumerator TrackBallLanding()
    {
        if (ballRigidbody == null) yield break;
        
        // Wait for ball to settle
        while (ballRigidbody.velocity.magnitude > 0.1f)
        {
            yield return new WaitForFixedUpdate();
        }
        
        Debug.Log("Ball has settled, finalizing parameter update");
        UpdateParametersFromBallPosition();
    }
    
    /// <summary>
    /// Reset ball to testing position - useful for debugging during testing mode
    /// </summary>
    [ContextMenu("Reset Ball to Testing Position")]
    public void ResetBallToTestingPosition()
    {
        if (isTesting)
        {
            Debug.Log("Resetting ball to testing position");
            SetBallPosition(testingBallPosition);
        }
        else
        {
            Debug.LogWarning("Not in testing mode - ball position not reset");
        }
    }
    
    /// <summary>
    /// Move ball to a high-loss position to test the color system
    /// </summary>
    [ContextMenu("Test Ball Color - Move to High Loss Position")]
    public void TestBallColorHighLoss()
    {
        if (backpropManager == null)
        {
            Debug.LogWarning("Cannot test ball color - BackpropagationManager not found");
            return;
        }
        
        // Move to a position far from optimal (should be red)
        float highLossW3 = backpropManager.WeightRange.y; // Maximum W3
        float highLossW4 = backpropManager.WeightRange.y; // Maximum W4
        float highLossB5 = backpropManager.BiasRange.y;   // Maximum B5
        
        Vector3 highLossPosition = backpropManager.ParametersToWorldPosition(highLossW3, highLossW4, highLossB5);
        
        Debug.Log($"Moving ball to high-loss position: W3={highLossW3:F3}, W4={highLossW4:F3}, B5={highLossB5:F3}");
        Debug.Log($"World position: {highLossPosition}");
        
        SetBallPosition(highLossPosition);
        
        // Force immediate color update
        UpdateBallColor();
    }
    
    /// <summary>
    /// Test trajectory prediction at different stretch distances
    /// </summary>
    [ContextMenu("Test Trajectory Prediction")]
    public void TestTrajectoryPrediction()
    {
        if (currentBall == null)
        {
            Debug.LogWarning("No ball available for trajectory testing");
            return;
        }
        
        Debug.Log("=== TRAJECTORY PREDICTION TEST ===");
        
        // Test different stretch distances
        float[] testDistances = { 0.1f, 0.3f, 0.5f, 0.8f, 1.0f };
        
        foreach (float testDistance in testDistances)
        {
            // Simulate stretch distance
            currentStretchDistance = testDistance;
            float stretchRatio = Mathf.Clamp01(currentStretchDistance / maxStretchDistance);
            float testForce = Mathf.Lerp(minForce, maxForce, forceCurve.Evaluate(stretchRatio));
            float travelDistance = Mathf.Lerp(0.5f, maxTravelDistance, testForce / maxForce);
            bool wouldLaunch = currentStretchDistance > launchDeadzone;
            
            Debug.Log($"Stretch: {testDistance:F2} | Force: {testForce:F2} | Travel: {travelDistance:F2} | Can Launch: {wouldLaunch}");
        }
        
        Debug.Log($"Deadzone: {launchDeadzone:F2} | Max Stretch: {maxStretchDistance:F2} | Max Travel: {maxTravelDistance:F2}");
    }
    
    /// <summary>
    /// Move ball to optimal position to test the color system
    /// </summary>
    [ContextMenu("Test Ball Color - Move to Optimal Position")]
    public void TestBallColorOptimal()
    {
        if (backpropManager == null || !backpropManager.HasOptimalParameters)
        {
            Debug.LogWarning("Cannot test ball color - BackpropagationManager not found or no optimal parameters");
            return;
        }
        
        // Move to optimal position (should be green)
        float optimalW3 = backpropManager.OptimalW3;
        float optimalW4 = backpropManager.OptimalW4;
        float optimalB5 = backpropManager.OptimalB5;
        
        Vector3 optimalPosition = backpropManager.ParametersToWorldPosition(optimalW3, optimalW4, optimalB5);
        
        Debug.Log($"Moving ball to optimal position: W3={optimalW3:F3}, W4={optimalW4:F3}, B5={optimalB5:F3}");
        Debug.Log($"World position: {optimalPosition}");
        
        SetBallPosition(optimalPosition);
        
        // Force immediate color update
        UpdateBallColor();
    }
    
    /// <summary>
    /// Test learning rate-based movement calculation
    /// </summary>
    [ContextMenu("Test Learning Rate Movement")]
    public void TestLearningRateMovement()
    {
        Debug.Log("=== LEARNING RATE MOVEMENT TEST ===");
        
        NeuralNetwork neuralNetwork = NeuralNetwork.Instance;
        if (neuralNetwork == null)
        {
            Debug.LogError("NeuralNetwork instance not found!");
            return;
        }
        
        float currentLearningRate = neuralNetwork.GetCurrentLearningRate();
        float calculatedDistance = CalculateLearningRateBasedDistance();
        
        Debug.Log($"Current learning rate: {currentLearningRate:F4}");
        Debug.Log($"Learning rate scale: {learningRateScale}");
        Debug.Log($"Base movement distance: {baseMovementDistance}");
        Debug.Log($"Calculated movement distance: {calculatedDistance:F2}");
        Debug.Log($"Distance range: {minMovementDistance:F2} to {maxMovementDistance:F2}");
        Debug.Log($"Learning rate-based movement enabled: {useLearningRateMovement}");
        
        // Test different learning rates
        float[] testLearningRates = { 0.01f, 0.05f, 0.1f, 0.2f, 0.3f };
        Debug.Log("\nTesting different learning rates (with max force):");
        
        foreach (float testRate in testLearningRates)
        {
            float learningRateFactor = testRate * learningRateScale;
            float maxPossibleDistance = baseMovementDistance * learningRateFactor;
            float clampedMaxDistance = Mathf.Clamp(maxPossibleDistance, minMovementDistance, maxMovementDistance);
            
            Debug.Log($"  LR: {testRate:F3} -> Max Distance: {clampedMaxDistance:F2}");
        }
        
        // Test different force levels with current learning rate
        float[] testForces = { 0.1f, 0.3f, 0.5f, 0.7f, 1.0f };
        Debug.Log($"\nTesting different force levels (with current LR: {currentLearningRate:F3}):");
        
        foreach (float testForceRatio in testForces)
        {
            float testForce = testForceRatio * maxForce;
            float learningRateFactor = currentLearningRate * learningRateScale;
            float maxPossibleDistance = baseMovementDistance * learningRateFactor;
            float clampedMaxDistance = Mathf.Clamp(maxPossibleDistance, minMovementDistance, maxMovementDistance);
            float actualDistance = clampedMaxDistance * testForceRatio;
            float finalDistance = Mathf.Max(actualDistance, minMovementDistance * 0.5f);
            
            Debug.Log($"  Force: {testForceRatio:F1} ({testForce:F1}/{maxForce:F1}) -> Distance: {finalDistance:F2}");
        }
    }
    
    /// <summary>
    /// Get current learning rate information for display
    /// </summary>
    public string GetLearningRateInfo()
    {
        NeuralNetwork neuralNetwork = NeuralNetwork.Instance;
        if (neuralNetwork == null)
        {
            return "Neural Network not found";
        }
        
        float currentLR = neuralNetwork.GetCurrentLearningRate();
        float movementDistance = CalculateLearningRateBasedDistance();
        
        return $"LR: {currentLR:F3} | Distance: {movementDistance:F2}";
    }
    
    public void AttachToLeftHand()
    {
        Debug.Log("=== ATTACHING SLINGSHOT TO LEFT HAND ===");
        
        // If leftHand is still null, try to find it again
        if (leftHand == null)
        {
            var foundLeftHand = FindObjectsOfType<Hand>()
                .FirstOrDefault(h => h.handType == SteamVR_Input_Sources.LeftHand || h.name.ToLower().Contains("left"));
            if (foundLeftHand != null)
            {
                leftHand = foundLeftHand;
                Debug.Log($"Auto-found left hand: {foundLeftHand.name}");
            }
        }

        if (leftHand != null)
        {
            // Set entire slingshot controller as child of left hand
            transform.SetParent(leftHand.transform, false);
            transform.localPosition = Vector3.zero; // Can be adjusted as needed
            transform.localRotation = Quaternion.identity;
            
            Debug.Log($"Slingshot controller successfully attached to left hand: {leftHand.name}");
        }
        else
        {
            Debug.LogWarning("Left hand not found, slingshot cannot attach! Available hands in scene:");
            var allHands = FindObjectsOfType<Hand>();
            foreach (var hand in allHands)
            {
                Debug.LogWarning($"  - {hand.name} (HandType: {hand.handType})");
            }
        }
    }
    
    /// <summary>
    /// Update ball color based on current parameter loss value
    /// </summary>
    void UpdateBallColor()
    {
        if (backpropManager == null || currentBall == null)
        {
            Debug.LogWarning("Cannot update ball color - missing BackpropagationManager or current ball");
            return;
        }

        // Get current parameters from the backprop manager
        float w3 = backpropManager.CurrentW3;
        float w4 = backpropManager.CurrentW4;
        float b5 = backpropManager.CurrentB5;

        // Calculate current loss
        float currentLoss = backpropManager.CalculateLoss(w3, w4, b5);

        // Calculate optimal loss for comparison
        float optimalLoss = 0f;
        if (backpropManager.HasOptimalParameters)
        {
            optimalLoss = backpropManager.CalculateLoss(
                backpropManager.OptimalW3, 
                backpropManager.OptimalW4, 
                backpropManager.OptimalB5
            );
        }

        // Get parameter box manager for color calculation
        ParameterBoxManager paramBoxManager = FindObjectOfType<ParameterBoxManager>();
        if (paramBoxManager != null)
        {
            Color lossColor = paramBoxManager.GetBallColorForLoss(currentLoss);
            
            // Get current renderer
            Renderer ballRenderer = currentBall.GetComponent<Renderer>();
            if (ballRenderer != null)
            {
                // Create a new material instance to avoid affecting other balls
                if (ballRenderer.material.name.Contains("Instance"))
                {
                    // Material is already instanced
                    ballRenderer.material.color = lossColor;
                }
                else
                {
                    // Create new material instance
                    Material newMaterial = new Material(ballRenderer.material);
                    newMaterial.color = lossColor;
                    ballRenderer.material = newMaterial;
                }
                
                // Enhanced debug every 60 frames to avoid spam - DISABLED - too verbose
                // if (Time.frameCount % 60 == 0)
                // {
                //     float distanceToOptimal = Vector3.Distance(
                //         new Vector3(w3, w4, b5),
                //         new Vector3(backpropManager.OptimalW3, backpropManager.OptimalW4, backpropManager.OptimalB5)
                //     );
                //     
                //     // Check if we're actually close to optimal
                //     bool isCloseToOptimal = distanceToOptimal < 0.5f;
                //     
                //     Debug.Log($"=== BALL COLOR DEBUG ===");
                //     Debug.Log($"Current params: W3={w3:F3}, W4={w4:F3}, B5={b5:F3}");
                //     Debug.Log($"Optimal params: W3={backpropManager.OptimalW3:F3}, W4={backpropManager.OptimalW4:F3}, B5={backpropManager.OptimalB5:F3}");
                //     Debug.Log($"Current loss: {currentLoss:F4}");
                //     Debug.Log($"Optimal loss: {optimalLoss:F4}");
                //     Debug.Log($"Distance to optimal: {distanceToOptimal:F3} (Close: {isCloseToOptimal})");
                //     Debug.Log($"Ball color: {lossColor} (R:{lossColor.r:F2}, G:{lossColor.g:F2}, B:{lossColor.b:F2})");
                //     Debug.Log($"Max loss for coloring: {paramBoxManager.maxLossForColoring:F2}");
                //     
                //     // Check if loss is normalized properly
                //     float normalizedLoss = currentLoss / paramBoxManager.maxLossForColoring;
                //     Debug.Log($"Normalized loss: {normalizedLoss:F4} (clamped: {Mathf.Clamp01(normalizedLoss):F4})");
                //     
                //     // Color interpretation
                //     if (lossColor.g > 0.8f && lossColor.r < 0.3f)
                //     {
                //         Debug.Log("Ball is GREEN - indicating LOW LOSS (close to optimal)");
                //     }
                //     else if (lossColor.r > 0.8f && lossColor.g < 0.3f)
                //     {
                //         Debug.Log("Ball is RED - indicating HIGH LOSS (far from optimal)");
                //     }
                //     else
                //     {
                //         Debug.Log("Ball is YELLOW/ORANGE - indicating MEDIUM LOSS");
                //     }
                // }
            }
            else
            {
                Debug.LogWarning("Ball renderer not found for color update");
            }
        }
        else
        {
            // Fallback to manual color calculation
            Color fallbackColor = CalculateFallbackBallColor(currentLoss, optimalLoss);
            Renderer ballRenderer = currentBall.GetComponent<Renderer>();
            if (ballRenderer != null)
            {
                ballRenderer.material.color = fallbackColor;
            }
            Debug.LogWarning($"ParameterBoxManager not found - using fallback color calculation. Loss: {currentLoss:F4}, Color: {fallbackColor}");
        }
    }

    /// <summary>
    /// Fallback color calculation if ParameterBoxManager is not available
    /// </summary>
    Color CalculateFallbackBallColor(float currentLoss, float optimalLoss)
    {
        // Use a simple gradient from green (optimal) to red (high loss)
        float maxLoss = 5f; // Assume max loss of 5 for normalization
        float normalizedLoss = Mathf.Clamp01(currentLoss / maxLoss);
        
        // Green at optimal loss, red at high loss
        Color ballColor = Color.Lerp(Color.green, Color.red, normalizedLoss);
        
        return ballColor;
    }

    /// <summary>
    /// Check if SteamVR system is ready for input
    /// </summary>
    bool IsSteamVRReady()
    {
        // Check if SteamVR is initialized and actions are available
        return SteamVR_Actions.default_GrabGrip != null && 
               SteamVR_Actions.default_InteractUI != null;
    }
}