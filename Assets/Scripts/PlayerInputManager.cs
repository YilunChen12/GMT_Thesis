using UnityEngine;
using Valve.VR;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.SceneManagement;

public class PlayerInputManager : MonoBehaviour
{
    [Header("Controller References")]
    public GameObject leftHandController;
    public GameObject rightHandController;
    public Canvas leftHandCanvas; // Canvas attached to left hand
    public NetworkVis networkVisualization; // Reference to the NetworkVis component

    [Header("Canvas Toggle Settings")]
    public bool enableCanvasToggle = true;
    public SteamVR_Input_Sources canvasToggleHand = SteamVR_Input_Sources.LeftHand;

    [Header("Interaction Settings")]
    public float interactionDistance = 0.1f; // Distance for neuron interaction
    public LayerMask neuronLayerMask; // Layer mask for neurons

    [Header("Bias Control Settings")]
    public float biasAdjustmentSpeed = 1.0f;
    public AFVisualizer activationVisualizer;
    private float currentBiasValue = 0f;

    [Header("VR Movement Settings")]
    [Tooltip("Enable VR movement (teleportation) in parameter box scene")]
    public bool enableVRMovement = true;
    [Tooltip("Maximum teleportation distance")]
    public float maxTeleportDistance = 10f;
    [Tooltip("Minimum teleportation distance")]
    public float minTeleportDistance = 0.5f;
    [Tooltip("Height offset for teleportation")]
    public float teleportHeightOffset = 0.1f;
    [Tooltip("Layer mask for teleportation surface")]
    public LayerMask teleportLayerMask = 1;
    [Tooltip("Teleport arc material")]
    public Material teleportArcMaterial;
    [Tooltip("Teleport target indicator prefab")]
    public GameObject teleportTargetPrefab;
    [Tooltip("Height offset when teleporting to ball")]
    public float ballTeleportHeightOffset = 1.5f;

    [Header("Parameter Plot Settings")] // NEW: Parameter plot swipe settings
    [Tooltip("Enable parameter plot swipe navigation")]
    public bool enableParameterPlotSwipe = true;
    [Tooltip("Swipe threshold for parameter switching")]
    public float swipeThreshold = 0.5f;
    [Tooltip("Cooldown between swipes")]
    public float swipeCooldown = 0.3f;

    private bool isVisualizationVisible = true;
    private GameObject lastHoveredNeuron = null;
    
    // VR Movement variables
    private bool isInParameterBoxScene = false;
    private LineRenderer teleportArc;
    private GameObject teleportTarget;
    private bool isTeleportAiming = false;
    private Vector3 teleportDestination;
    private bool isValidTeleportTarget = false;
    private SlingshotController slingshotController;
    
    // NEW: Parameter plot swipe detection
    private Vector2 lastLeftTouchpadInput = Vector2.zero;
    private float lastSwipeTime = 0f;
    private HandCanvasUI handCanvasUI;
    
    // FIXED: Use SteamVR_Actions.default_* instead of manually initialized actions for better scene transition stability
    // VR Input Actions - REMOVED problematic manual initialization:
    // public SteamVR_Action_Boolean teleportAction = SteamVR_Input.GetAction<SteamVR_Action_Boolean>("Teleport");
    // public SteamVR_Action_Vector2 touchpadAction = SteamVR_Input.GetAction<SteamVR_Action_Vector2>("TouchpadPosition");
    // public SteamVR_Action_Boolean gripAction = SteamVR_Input.GetAction<SteamVR_Action_Boolean>("GrabGrip");

    void Awake()
    {
        // Subscribe to scene events
        SceneManager.sceneLoaded += OnSceneLoaded;
        
        // Check current scene on awake
        CheckCurrentScene();
        
        // REMOVED: Don't find HandCanvasUI in Awake - do it when entering Scene 2
        // Find HandCanvasUI reference for parameter plot swipes
        // handCanvasUI = FindObjectOfType<HandCanvasUI>();
        // if (handCanvasUI == null)
        // {
        //     Debug.LogWarning("HandCanvasUI not found - parameter plot swipes won't work");
        // }
        // else
        // {
        //     Debug.Log("Found HandCanvasUI for parameter plot swipe navigation");
        // }
    }

    void OnDestroy()
    {
        // Unsubscribe from scene events
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        CheckCurrentScene();
    }

    void CheckCurrentScene()
    {
        string currentSceneName = SceneManager.GetActiveScene().name;
        isInParameterBoxScene = (currentSceneName == "BackpropagationScene");
        
        Debug.Log($"PlayerInputManager: Scene '{currentSceneName}' - VR Movement enabled: {isInParameterBoxScene && enableVRMovement}");
        
        if (isInParameterBoxScene)
        {
            // Find slingshot controller for ball teleportation
            slingshotController = FindObjectOfType<SlingshotController>();
            if (slingshotController != null)
            {
                Debug.Log("Found SlingshotController for ball teleportation");
            }
            
            // NEW: Find HandCanvasUI reference for parameter plot swipes when entering Scene 2
            handCanvasUI = FindObjectOfType<HandCanvasUI>();
            if (handCanvasUI == null)
            {
                Debug.LogWarning("HandCanvasUI not found in BackpropagationScene - parameter plot swipes won't work");
                
                // Try again after a short delay in case HandCanvasUI is being initialized
                StartCoroutine(RetryFindHandCanvasUI());
            }
            else
            {
                Debug.Log("Found HandCanvasUI for parameter plot swipe navigation");
            }
            
            // Initialize teleportation system
            InitializeTeleportSystem();
        }
        else
        {
            // Clean up teleportation system in other scenes
            CleanupTeleportSystem();
            
            // Clear HandCanvasUI reference when leaving Scene 2
            if (handCanvasUI != null)
            {
                Debug.Log("Clearing HandCanvasUI reference when leaving BackpropagationScene");
                handCanvasUI = null;
            }
        }
    }
    
    // NEW: Retry finding HandCanvasUI with delay
    System.Collections.IEnumerator RetryFindHandCanvasUI()
    {
        int retryCount = 0;
        int maxRetries = 5;
        
        while (handCanvasUI == null && retryCount < maxRetries && isInParameterBoxScene)
        {
            yield return new WaitForSeconds(0.5f); // Wait 0.5 seconds between retries
            retryCount++;
            
            handCanvasUI = FindObjectOfType<HandCanvasUI>();
            
            if (handCanvasUI != null)
            {
                Debug.Log($"Found HandCanvasUI on retry {retryCount} - parameter plot swipes now available");
                break;
            }
            else
            {
                Debug.Log($"HandCanvasUI retry {retryCount}/{maxRetries} - still not found");
            }
        }
        
        if (handCanvasUI == null)
        {
            Debug.LogError($"Failed to find HandCanvasUI after {maxRetries} retries - parameter plot swipes will not work");
        }
    }

    void InitializeTeleportSystem()
    {
        if (!enableVRMovement) return;
        
        // Create teleport arc
        if (teleportArc == null)
        {
            GameObject arcObject = new GameObject("TeleportArc");
            arcObject.transform.SetParent(transform);
            teleportArc = arcObject.AddComponent<LineRenderer>();
            
            // Configure arc
            teleportArc.material = teleportArcMaterial ?? CreateDefaultTeleportMaterial();
            teleportArc.material.color = Color.cyan;
            teleportArc.startWidth = 0.02f;
            teleportArc.endWidth = 0.02f;
            teleportArc.positionCount = 50;
            teleportArc.useWorldSpace = true;
            teleportArc.enabled = false;
        }
        
        // Create teleport target indicator
        if (teleportTarget == null)
        {
            if (teleportTargetPrefab != null)
            {
                teleportTarget = Instantiate(teleportTargetPrefab);
            }
            else
            {
                teleportTarget = CreateDefaultTeleportTarget();
            }
            teleportTarget.SetActive(false);
        }
        
        Debug.Log("VR teleportation system initialized");
    }

    void CleanupTeleportSystem()
    {
        if (teleportArc != null)
        {
            DestroyImmediate(teleportArc.gameObject);
            teleportArc = null;
        }
        
        if (teleportTarget != null)
        {
            DestroyImmediate(teleportTarget);
            teleportTarget = null;
        }
        
        isTeleportAiming = false;
    }

    Material CreateDefaultTeleportMaterial()
    {
        Material mat = new Material(Shader.Find("Sprites/Default"));
        mat.color = Color.cyan;
        return mat;
    }

    GameObject CreateDefaultTeleportTarget()
    {
        GameObject target = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        target.name = "TeleportTarget";
        target.transform.localScale = new Vector3(0.5f, 0.01f, 0.5f);
        
        // Remove collider to prevent interference
        Collider col = target.GetComponent<Collider>();
        if (col != null) DestroyImmediate(col);
        
        // Set material
        Renderer renderer = target.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.material = CreateDefaultTeleportMaterial();
        }
        
        return target;
    }

    private void Update()
    {
        // FIXED: Add null checking for SteamVR system to prevent errors during scene transitions
        if (!IsSteamVRReady())
        {
            return;
        }
        
        // Always handle canvas toggle
        HandleControllerInputs();
        
        // Scene-specific input handling
        if (isInParameterBoxScene)
        {
            // Scene 2: Parameter Box Scene
            HandleParameterBoxInputs();
        }
        else
        {
            // Scene 1: Network Visualization Scene
            HandleNetworkVisualizationInputs();
        }
    }

    /// <summary>
    /// Check if SteamVR system is ready for input
    /// </summary>
    bool IsSteamVRReady()
    {
        // Check if SteamVR is initialized and actions are available
        return SteamVR_Actions.default_TouchpadPosition != null && 
               SteamVR_Actions.default_InteractUI != null && 
               SteamVR_Actions.default_GrabGrip != null;
    }

    // NEW: Handle inputs for Parameter Box Scene (Scene 2)
    void HandleParameterBoxInputs()
    {
        // VR Movement (teleportation)
        if (enableVRMovement)
        {
            HandleVRMovement();
        }
        
        // Parameter plot swipe navigation
        if (enableParameterPlotSwipe)
        {
            HandleParameterPlotSwipes();
        }
    }

    // NEW: Handle inputs for Network Visualization Scene (Scene 1)
    void HandleNetworkVisualizationInputs()
    {
        // Neuron interaction
        HandleNeuronInteraction();
        
        // Bias adjustment
        HandleBiasAdjustment();
    }

    // NEW: Handle parameter plot swipe detection with improved error handling
    void HandleParameterPlotSwipes()
    {
        if (Time.time - lastSwipeTime < swipeCooldown) return;
        if (leftHandController == null) return; // Same safety check as teleportation
        
        try
        {
            // Get left controller touchpad input using the same method as teleportation
            Vector2 leftTouchpadValue = SteamVR_Actions.default_TouchpadPosition.GetAxis(SteamVR_Input_Sources.LeftHand);
            
            // Detect horizontal swipe with improved logic
            bool isCurrentlySwipingHorizontally = Mathf.Abs(leftTouchpadValue.x) > swipeThreshold;
            bool wasNotSwipingBefore = Mathf.Abs(lastLeftTouchpadInput.x) < swipeThreshold;
            
            // Only trigger on the initial swipe motion (like teleportation pattern)
            if (isCurrentlySwipingHorizontally && wasNotSwipingBefore)
            {
                if (leftTouchpadValue.x > 0)
                {
                    // Swipe right - next parameter
                    OnSwipeRight();
                    Debug.Log($"Parameter swipe RIGHT detected - touchpad X: {leftTouchpadValue.x:F2}");
                }
                else
                {
                    // Swipe left - previous parameter  
                    OnSwipeLeft();
                    Debug.Log($"Parameter swipe LEFT detected - touchpad X: {leftTouchpadValue.x:F2}");
                }
                
                lastSwipeTime = Time.time;
            }
            
            lastLeftTouchpadInput = leftTouchpadValue;
        }
        catch (System.Exception ex)
        {
            // Handle any SteamVR input errors gracefully
            Debug.LogWarning($"SteamVR input error in parameter swipe detection: {ex.Message}");
            
            // Try alternative input method if primary fails
            TryAlternativeSwipeInput();
        }
    }
    
    // NEW: Alternative swipe input method using button presses if touchpad fails
    void TryAlternativeSwipeInput()
    {
        if (Time.time - lastSwipeTime < swipeCooldown) return;
        
        try
        {
            // Alternative 1: Use left/right on the same touchpad as boolean detection
            bool leftPressed = SteamVR_Actions.default_InteractUI.GetStateDown(SteamVR_Input_Sources.LeftHand);
            
            if (leftPressed)
            {
                // Get rough direction from controller position change
                Vector3 currentControllerPos = GetControllerPosition(SteamVR_Input_Sources.LeftHand);
                
                // Simple direction detection based on controller movement
                // This is a fallback method that doesn't rely on skeletal data
                OnSwipeRight(); // Default to next parameter for button press
                lastSwipeTime = Time.time;
                
                Debug.Log("Used alternative parameter switching input (button press)");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"Alternative input method also failed: {ex.Message}");
        }
    }

    // NEW: Handle swipe right
    void OnSwipeRight()
    {
        Debug.Log("Parameter plot swipe right detected");
        
        if (handCanvasUI != null)
        {
            handCanvasUI.SwitchToNextParameter();
        }
        else
        {
            Debug.LogWarning("Cannot switch parameter - HandCanvasUI not found");
        }
    }

    // NEW: Handle swipe left
    void OnSwipeLeft()
    {
        Debug.Log("Parameter plot swipe left detected");
        
        if (handCanvasUI != null)
        {
            handCanvasUI.SwitchToPreviousParameter();
        }
        else
        {
            Debug.LogWarning("Cannot switch parameter - HandCanvasUI not found");
        }
    }

    void HandleVRMovement()
    {
        HandleTeleportation();
        HandleTeleportToBall();
    }

    void HandleTeleportation()
    {
        if (rightHandController == null) return;
        
        // Get touchpad input
        Vector2 touchpadValue = SteamVR_Actions.default_TouchpadPosition.GetAxis(SteamVR_Input_Sources.RightHand);
        bool isTouchpadTouched = touchpadValue.magnitude > 0.1f;
        bool isTouchpadPressed = SteamVR_Actions.default_InteractUI.GetStateDown(SteamVR_Input_Sources.RightHand);
        
        // Show teleport indicator when touchpad is touched
        if (isTouchpadTouched && !isTeleportAiming)
        {
            StartTeleportAiming();
        }
        else if (!isTouchpadTouched && isTeleportAiming)
        {
            // Cancel teleportation when touchpad is released
            StopTeleportAiming();
        }
        
        // Execute teleportation when touchpad is pressed down
        if (isTouchpadPressed && isTeleportAiming && isValidTeleportTarget)
        {
            ExecuteTeleport();
            StopTeleportAiming();
        }
        
        // Update teleport indicator while touchpad is touched
        if (isTeleportAiming)
        {
            UpdateTeleportAiming(touchpadValue);
        }
    }

    void HandleTeleportToBall()
    {
        // Use grip button on left controller for teleport to ball
        if (SteamVR_Actions.default_GrabGrip.GetStateDown(SteamVR_Input_Sources.LeftHand))
        {
            TeleportToBall();
        }
    }

    void StartTeleportAiming()
    {
        isTeleportAiming = true;
        if (teleportArc != null)
        {
            teleportArc.enabled = true;
        }
        if (teleportTarget != null)
        {
            teleportTarget.SetActive(true);
        }
        Debug.Log("Started teleport aiming");
    }

    void StopTeleportAiming()
    {
        isTeleportAiming = false;
        if (teleportArc != null)
        {
            teleportArc.enabled = false;
        }
        if (teleportTarget != null)
        {
            teleportTarget.SetActive(false);
        }
        isValidTeleportTarget = false;
    }

    void UpdateTeleportAiming(Vector2 touchpadValue)
    {
        if (rightHandController == null) return;
        
        Vector3 controllerPos = rightHandController.transform.position;
        Vector3 controllerForward = rightHandController.transform.forward;
        
        // Calculate teleport distance based on touchpad Y value
        float touchpadDistance = Mathf.Clamp(touchpadValue.y, -1f, 1f);
        float teleportDistance = Mathf.Lerp(minTeleportDistance, maxTeleportDistance, (touchpadDistance + 1f) * 0.5f);
        
        // Calculate straight line teleport destination
        Vector3 teleportDirection = controllerForward;
        Vector3 targetPosition = controllerPos + teleportDirection * teleportDistance;
        
        // Update straight line visualization
        teleportArc.positionCount = 2;
        teleportArc.SetPosition(0, controllerPos);
        teleportArc.SetPosition(1, targetPosition);
        
        // Store destination for teleportation
        teleportDestination = targetPosition;
        
        // Check if destination is within parameter box bounds
        CheckParameterBoxTeleportValidity(targetPosition);
        
        // Update target indicator
        if (teleportTarget != null)
        {
            teleportTarget.transform.position = targetPosition;
            
            // Scale indicator based on distance for better visual feedback
            float indicatorScale = Mathf.Lerp(0.3f, 0.8f, teleportDistance / maxTeleportDistance);
            teleportTarget.transform.localScale = Vector3.one * indicatorScale;
        }
    }

    List<Vector3> CalculateTeleportArc(Vector3 startPos, Vector3 direction)
    {
        List<Vector3> points = new List<Vector3>();
        
        float arcLength = 5f;
        float gravity = 9.81f;
        float velocity = 10f;
        
        // Calculate arc with physics simulation
        Vector3 currentPos = startPos;
        Vector3 currentVelocity = direction * velocity;
        
        for (int i = 0; i < 50; i++)
        {
            float t = i * 0.1f;
            Vector3 nextPos = startPos + currentVelocity * t + 0.5f * Vector3.down * gravity * t * t;
            
            // Check for ground collision
            if (Physics.Raycast(currentPos, nextPos - currentPos, out RaycastHit hit, Vector3.Distance(currentPos, nextPos), teleportLayerMask))
            {
                points.Add(hit.point);
                teleportDestination = hit.point;
                break;
            }
            
            points.Add(nextPos);
            currentPos = nextPos;
            
            // Stop if arc goes too far down
            if (nextPos.y < startPos.y - 5f)
            {
                teleportDestination = nextPos;
                break;
            }
        }
        
        return points;
    }

    void CheckTeleportValidity(Vector3 targetPos)
    {
        // Check if target is within acceptable range
        float distance = Vector3.Distance(transform.position, targetPos);
        isValidTeleportTarget = distance >= minTeleportDistance && distance <= maxTeleportDistance;
        
        // Update target indicator
        if (teleportTarget != null)
        {
            teleportTarget.transform.position = targetPos + Vector3.up * teleportHeightOffset;
            
            // Change color based on validity
            Renderer targetRenderer = teleportTarget.GetComponent<Renderer>();
            if (targetRenderer != null)
            {
                targetRenderer.material.color = isValidTeleportTarget ? Color.green : Color.red;
            }
        }
        
        // Update arc color
        if (teleportArc != null)
        {
            teleportArc.material.color = isValidTeleportTarget ? Color.green : Color.red;
        }
    }

    void CheckParameterBoxTeleportValidity(Vector3 targetPos)
    {
        // Find parameter box manager to check bounds
        ParameterBoxManager paramBoxManager = FindObjectOfType<ParameterBoxManager>();
        
        if (paramBoxManager != null)
        {
            // Convert target position to parameter box local space
            Vector3 localPos = paramBoxManager.transform.InverseTransformPoint(targetPos);
            
            // Check if position is within parameter box bounds
            Vector3 boxSize = paramBoxManager.BoxSize;
            bool withinBounds = Mathf.Abs(localPos.x) <= boxSize.x * 0.5f &&
                               Mathf.Abs(localPos.y) <= boxSize.y * 0.5f &&
                               Mathf.Abs(localPos.z) <= boxSize.z * 0.5f;
            
            isValidTeleportTarget = withinBounds;
        }
        else
        {
            // Fallback to distance check if parameter box not found
            float distance = Vector3.Distance(transform.position, targetPos);
            isValidTeleportTarget = distance >= minTeleportDistance && distance <= maxTeleportDistance;
        }
        
        // Update visual feedback
        UpdateTeleportVisuals();
    }

    void UpdateTeleportVisuals()
    {
        // Update target indicator color
        if (teleportTarget != null)
        {
            Renderer targetRenderer = teleportTarget.GetComponent<Renderer>();
            if (targetRenderer != null)
            {
                targetRenderer.material.color = isValidTeleportTarget ? Color.green : Color.red;
            }
        }
        
        // Update line color
        if (teleportArc != null)
        {
            teleportArc.material.color = isValidTeleportTarget ? Color.cyan : Color.red;
        }
    }

    void ExecuteTeleport()
    {
        if (!isValidTeleportTarget) return;
        
        // For parameter box teleportation, use the exact destination without height adjustments
        Vector3 finalPos = teleportDestination;
        
        // Teleport player
        transform.position = finalPos;
        
        Debug.Log($"Teleported to parameter box position: {finalPos}");
        
        // Optional: Add teleport effect
        StartCoroutine(TeleportEffect());
    }

    System.Collections.IEnumerator TeleportEffect()
    {
        // Simple fade effect could be added here
        yield return new WaitForSeconds(0.1f);
        // Fade in/out implementation would go here
    }

    void TeleportToBall()
    {
        if (slingshotController == null)
        {
            Debug.LogWarning("Cannot teleport to ball - SlingshotController not found");
            return;
        }
        
        // Get ball position from slingshot controller
        GameObject currentBall = slingshotController.CurrentBall;
        if (currentBall == null)
        {
            Debug.LogWarning("Cannot teleport to ball - no ball found");
            return;
        }
        
        // Calculate teleport position near the ball
        Vector3 ballPosition = currentBall.transform.position;
        Vector3 teleportPosition = ballPosition + Vector3.up * ballTeleportHeightOffset;
        
        // Add slight offset to avoid spawning exactly on the ball
        teleportPosition += Vector3.back * 0.5f;
        
        // Teleport player
        transform.position = teleportPosition;
        
        Debug.Log($"Teleported to ball at: {teleportPosition}");
        
        // Optional: Add teleport effect
        StartCoroutine(TeleportEffect());
    }

    private void HandleNeuronInteraction()
    {
        if (!isVisualizationVisible) return;

        // Get the controller position and forward direction
        Vector3 controllerPos = GetControllerPosition(SteamVR_Input_Sources.RightHand);
        Vector3 controllerForward = GetControllerRotation(SteamVR_Input_Sources.RightHand) * Vector3.forward;

        // Debug ray visualization
        Debug.DrawRay(controllerPos, controllerForward * interactionDistance, Color.red);

        // Cast ray from controller
        Ray ray = new Ray(controllerPos, controllerForward);
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit, interactionDistance, neuronLayerMask))
        {
            // Found a neuron
            GameObject hitNeuron = hit.collider.gameObject;
            //Debug.Log($"Hit neuron: {hitNeuron.name} at distance {hit.distance}");
            
            // Handle hover effect
            if (lastHoveredNeuron != hitNeuron)
            {
                if (lastHoveredNeuron != null)
                {
                    // Reset last hovered neuron
                    ResetNeuronHover(lastHoveredNeuron);
                }
                
                // Set new hovered neuron
                lastHoveredNeuron = hitNeuron;
                SetNeuronHover(hitNeuron);
            }

            // Handle click
            if (SteamVR_Actions.default_InteractUI.GetStateDown(SteamVR_Input_Sources.RightHand))
            {
                Debug.Log($"Clicked on neuron: {hitNeuron.name}");
                // Get the NeuronInteraction component and trigger the click
                NeuronInteraction neuronInteraction = hitNeuron.GetComponent<NeuronInteraction>();
                if (neuronInteraction != null)
                {
                    neuronInteraction.OnPointerClick(null);
                }
            }
        }
        else if (lastHoveredNeuron != null)
        {
            // No neuron hit, reset hover state
            ResetNeuronHover(lastHoveredNeuron);
            lastHoveredNeuron = null;
        }
    }

    private void SetNeuronHover(GameObject neuron)
    {
        // Add hover effect (e.g., scale up slightly)
        neuron.transform.localScale *= 1.1f;
        
        // Change color to indicate hover
        Renderer renderer = neuron.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.material.color = Color.yellow;
        }

        // Show neuron result plot if available
        if (activationVisualizer != null)
        {
            // 通过NeuronInteraction组件获取layerIndex和neuronIndex
            NeuronInteraction neuronInteraction = neuron.GetComponent<NeuronInteraction>();
            // if (neuronInteraction != null)
            // {
            //     activationVisualizer.ShowNeuronResultPlot(neuronInteraction.LayerIndex, neuronInteraction.NeuronIndex);
            // }
        }
    }

    private void ResetNeuronHover(GameObject neuron)
    {
        // Reset hover effect
        neuron.transform.localScale /= 1.1f;
        
        // Reset color
        Renderer renderer = neuron.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.material.color = Color.white;
        }
    }

    private void HandleControllerInputs()
    {
        // Handle Canvas Toggle
        if (enableCanvasToggle && SteamVR_Actions.default_InteractUI.GetStateDown(canvasToggleHand))
        {
            ToggleVisualization();
        }

        // Add other input handling here
        // For example:
        // HandleGrabInput();
        // HandleTeleportInput();
        // etc.
    }

    private void ToggleVisualization()
    {
        isVisualizationVisible = !isVisualizationVisible;

        // Toggle Canvas and its children
        if (leftHandCanvas != null)
        {
            leftHandCanvas.enabled = isVisualizationVisible;
            // Disable all children of the canvas
            foreach (Transform child in leftHandCanvas.transform)
            {
                child.gameObject.SetActive(isVisualizationVisible);
            }
        }

        // Toggle Network Visualization
        if (networkVisualization != null)
        {
            networkVisualization.ToggleVisibility(isVisualizationVisible);
        }
    }

    private void HandleBiasAdjustment()
    {
        if (activationVisualizer == null) return;

        // Get touchpad input from right controller
        Vector2 touchpadValue = SteamVR_Actions.default_TouchpadPosition.GetAxis(SteamVR_Input_Sources.RightHand);

        // Only process vertical movement (y-axis)
        if (Mathf.Abs(touchpadValue.y) > 0.1f)
        {
            // Update bias value based on touchpad input
            currentBiasValue += touchpadValue.y * biasAdjustmentSpeed * Time.deltaTime;
            
            // Apply bias to visualization
            activationVisualizer.ApplyBiasShift(currentBiasValue);
            
            // Debug feedback
            Debug.Log($"Bias value: {currentBiasValue}");
        }
    }

    // Helper method to get controller position
    public Vector3 GetControllerPosition(SteamVR_Input_Sources hand)
    {
        if (hand == SteamVR_Input_Sources.LeftHand && leftHandController != null)
        {
            return leftHandController.transform.position;
        }
        else if (hand == SteamVR_Input_Sources.RightHand && rightHandController != null)
        {
            return rightHandController.transform.position;
        }
        return Vector3.zero;
    }

    // Helper method to get controller rotation
    public Quaternion GetControllerRotation(SteamVR_Input_Sources hand)
    {
        if (hand == SteamVR_Input_Sources.LeftHand && leftHandController != null)
        {
            return leftHandController.transform.rotation;
        }
        else if (hand == SteamVR_Input_Sources.RightHand && rightHandController != null)
        {
            return rightHandController.transform.rotation;
        }
        return Quaternion.identity;
    }

    // NEW: Context menu debugging methods for parameter switching
    [ContextMenu("Test Parameter Switch Right")]
    public void TestParameterSwitchRight()
    {
        Debug.Log("=== TESTING PARAMETER SWITCH RIGHT ===");
        if (isInParameterBoxScene)
        {
            OnSwipeRight();
        }
        else
        {
            Debug.LogWarning("Not in parameter box scene - switch to Scene 2 first");
        }
    }
    
    [ContextMenu("Test Parameter Switch Left")]
    public void TestParameterSwitchLeft()
    {
        Debug.Log("=== TESTING PARAMETER SWITCH LEFT ===");
        if (isInParameterBoxScene)
        {
            OnSwipeLeft();
        }
        else
        {
            Debug.LogWarning("Not in parameter box scene - switch to Scene 2 first");
        }
    }
    
    [ContextMenu("Debug Input State")]
    public void DebugInputState()
    {
        Debug.Log("=== INPUT STATE DEBUG ===");
        Debug.Log($"isInParameterBoxScene: {isInParameterBoxScene}");
        Debug.Log($"enableParameterPlotSwipe: {enableParameterPlotSwipe}");
        Debug.Log($"handCanvasUI found: {handCanvasUI != null}");
        Debug.Log($"leftHandController: {leftHandController != null}");
        
        if (isInParameterBoxScene && enableParameterPlotSwipe)
        {
            try
            {
                Vector2 touchpadValue = SteamVR_Actions.default_TouchpadPosition.GetAxis(SteamVR_Input_Sources.LeftHand);
                Debug.Log($"Current left touchpad value: {touchpadValue}");
                Debug.Log($"Swipe threshold: {swipeThreshold}");
                Debug.Log($"Time since last swipe: {Time.time - lastSwipeTime:F2}s");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Error reading left touchpad: {ex.Message}");
            }
        }
    }
} 