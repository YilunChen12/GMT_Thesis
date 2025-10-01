using UnityEngine;
using Valve.VR;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Input manager for Scene 2 (Backpropagation / Parameter Box Scene)
/// Handles teleportation, parameter plot swipes, and ball movement
/// UPDATED: Enhanced SteamVR input corruption handling and recovery
/// </summary>
public class BackpropagationInputManager : MonoBehaviour
{
    [Header("Controller References")]
    public GameObject leftHandController;
    public GameObject rightHandController;

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

    [Header("Parameter Plot Settings")]
    [Tooltip("Enable parameter plot swipe navigation")]
    public bool enableParameterPlotSwipe = true;
    [Tooltip("Swipe threshold for parameter switching")]
    public float swipeThreshold = 0.5f;
    [Tooltip("Cooldown between swipes")]
    public float swipeCooldown = 0.3f;

    [Header("SteamVR Input Recovery")] // NEW: Enhanced input corruption handling
    [Tooltip("Enable enhanced input recovery system")]
    public bool enableInputRecovery = true;
    [Tooltip("Enable alternative input when touchpad fails")]
    public bool enableAlternativeInput = true;
    [Tooltip("Cooldown between alternative input activations")]
    public float alternativeInputCooldown = 0.5f;

    // VR Movement variables
    private LineRenderer teleportArc;
    private GameObject teleportTarget;
    private bool isTeleportAiming = false;
    private Vector3 teleportDestination;
    private bool isValidTeleportTarget = false;
    private SlingshotController slingshotController;
    
    // Parameter plot swipe detection
    private Vector2 lastLeftTouchpadInput = Vector2.zero;
    private float lastSwipeTime = 0f;
    private HandCanvasUI handCanvasUI;
    
    // NEW: Enhanced input recovery and alternative input system
    private bool touchpadInputWorking = false;
    private float lastAlternativeInputTime = 0f;
    private bool isUsingAlternativeInput = false;
    private float lastInputRecoveryCheck = 0f;
    private int inputRecoveryAttempts = 0;
    private const int maxInputRecoveryAttempts = 3;
    
    // FIXED: Use static SteamVR_Actions instead of manually initialized actions
    // These are more stable across scene transitions
    // Removed problematic manual initialization:
    // public SteamVR_Action_Boolean teleportAction = SteamVR_Input.GetAction<SteamVR_Action_Boolean>("Teleport");
    // public SteamVR_Action_Vector2 touchpadAction = SteamVR_Input.GetAction<SteamVR_Action_Vector2>("TouchpadPosition");
    // public SteamVR_Action_Boolean gripAction = SteamVR_Input.GetAction<SteamVR_Action_Boolean>("GrabGrip");

    void Start()
    {
        Debug.Log("BackpropagationInputManager: Initialized for parameter box scene");
        
        // Auto-detect components
        AutoDetectComponents();
        
        // Initialize teleportation system
        if (enableVRMovement)
        {
            InitializeTeleportSystem();
        }
        
        // Find HandCanvasUI for parameter switching
        StartCoroutine(FindHandCanvasUIWithRetry());
        
        // NEW: Start enhanced input recovery system
        if (enableInputRecovery)
        {
            StartCoroutine(InitializeInputRecoverySystem());
        }
    }

    void AutoDetectComponents()
    {
        // Auto-detect controllers if not assigned
        if (leftHandController == null || rightHandController == null)
        {
            var hands = FindObjectsOfType<Valve.VR.InteractionSystem.Hand>();
            
            foreach (var hand in hands)
            {
                if (hand.handType == SteamVR_Input_Sources.LeftHand && leftHandController == null)
                {
                    leftHandController = hand.gameObject;
                    Debug.Log($"Auto-detected left hand controller: {hand.name}");
                }
                else if (hand.handType == SteamVR_Input_Sources.RightHand && rightHandController == null)
                {
                    rightHandController = hand.gameObject;
                    Debug.Log($"Auto-detected right hand controller: {hand.name}");
                }
            }
        }
        
        // Find slingshot controller for ball teleportation
        slingshotController = FindObjectOfType<SlingshotController>();
        if (slingshotController != null)
        {
            Debug.Log("Found SlingshotController for ball teleportation");
        }
    }

    System.Collections.IEnumerator FindHandCanvasUIWithRetry()
    {
        int retryCount = 0;
        int maxRetries = 5;
        
        while (handCanvasUI == null && retryCount < maxRetries)
        {
            yield return new WaitForSeconds(0.5f);
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

    /// <summary>
    /// NEW: Initialize enhanced input recovery system
    /// </summary>
    System.Collections.IEnumerator InitializeInputRecoverySystem()
    {
        Debug.Log("=== INITIALIZING ENHANCED INPUT RECOVERY SYSTEM ===");
        
        // Wait for scene to fully load
        yield return new WaitForSeconds(1.0f);
        
        // Initial touchpad validation
        touchpadInputWorking = ValidateTouchpadInput();
        
        if (touchpadInputWorking)
        {
            Debug.Log("✅ Initial touchpad input validation successful");
            isUsingAlternativeInput = false;
        }
        else
        {
            Debug.LogWarning("⚠️ Initial touchpad input validation failed - enabling alternative input");
            isUsingAlternativeInput = true;
        }
        
        Debug.Log($"Input recovery system initialized. Touchpad working: {touchpadInputWorking}, Alternative input: {isUsingAlternativeInput}");
    }

    /// <summary>
    /// NEW: Validate touchpad input functionality
    /// </summary>
    bool ValidateTouchpadInput()
    {
        try
        {
            if (SteamVR_Actions.default_TouchpadPosition == null)
            {
                Debug.LogWarning("SteamVR_Actions.default_TouchpadPosition is null");
                return false;
            }
            
            // Test reading touchpad values
            Vector2 leftTouchpad = SteamVR_Actions.default_TouchpadPosition.GetAxis(SteamVR_Input_Sources.LeftHand);
            Vector2 rightTouchpad = SteamVR_Actions.default_TouchpadPosition.GetAxis(SteamVR_Input_Sources.RightHand);
            
            Debug.Log($"Touchpad validation - Left: {leftTouchpad}, Right: {rightTouchpad}");
            return true;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Touchpad validation failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// NEW: Attempt to recover touchpad input
    /// </summary>
    void AttemptInputRecovery()
    {
        if (inputRecoveryAttempts >= maxInputRecoveryAttempts)
        {
            Debug.LogWarning($"Max input recovery attempts ({maxInputRecoveryAttempts}) reached. Using alternative input.");
            isUsingAlternativeInput = true;
            return;
        }
        
        inputRecoveryAttempts++;
        Debug.Log($"Attempting input recovery {inputRecoveryAttempts}/{maxInputRecoveryAttempts}");
        
        try
        {
            // Force reinitialize SteamVR input
            if (SteamVR.enabled)
            {
                SteamVR_Input.Initialize(true);
                Debug.Log("Forced SteamVR input reinitialization");
            }
            
            // Wait a moment and revalidate
            StartCoroutine(DelayedInputValidation());
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Input recovery attempt failed: {ex.Message}");
            isUsingAlternativeInput = true;
        }
    }

    /// <summary>
    /// NEW: Delayed validation after recovery attempt
    /// </summary>
    System.Collections.IEnumerator DelayedInputValidation()
    {
        yield return new WaitForSeconds(0.3f);
        
        touchpadInputWorking = ValidateTouchpadInput();
        
        if (touchpadInputWorking)
        {
            Debug.Log("✅ Input recovery successful!");
            isUsingAlternativeInput = false;
            inputRecoveryAttempts = 0; // Reset on success
        }
        else
        {
            Debug.LogWarning($"❌ Input recovery attempt {inputRecoveryAttempts} failed");
            isUsingAlternativeInput = true;
        }
    }

    void Update()
    {
        // FIXED: Add null checking for SteamVR system to prevent errors during scene transitions
        if (!IsSteamVRReady())
        {
            return;
        }
        
        // NEW: Periodic input health check
        PeriodicInputHealthCheck();
        
        try
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
        catch (System.Exception ex)
        {
            Debug.LogWarning($"SteamVR input error in BackpropagationInputManager: {ex.Message}");
            
            // NEW: Trigger input recovery on errors
            if (enableInputRecovery && Time.time - lastInputRecoveryCheck > 2.0f)
            {
                lastInputRecoveryCheck = Time.time;
                touchpadInputWorking = false;
                AttemptInputRecovery();
            }
        }
    }

    /// <summary>
    /// NEW: Periodic check for input system health
    /// </summary>
    void PeriodicInputHealthCheck()
    {
        // Only check every few seconds to avoid performance impact
        if (Time.time - lastInputRecoveryCheck < 3.0f) return;
        lastInputRecoveryCheck = Time.time;
        
        if (!enableInputRecovery) return;
        
        // Quick health check
        bool currentlyWorking = ValidateTouchpadInput();
        
        if (touchpadInputWorking && !currentlyWorking)
        {
            Debug.LogWarning("Touchpad input corruption detected during gameplay! Attempting recovery...");
            touchpadInputWorking = false;
            AttemptInputRecovery();
        }
        else if (!touchpadInputWorking && currentlyWorking)
        {
            Debug.Log("✅ Touchpad input recovered!");
            touchpadInputWorking = true;
            isUsingAlternativeInput = false;
            inputRecoveryAttempts = 0;
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

    void HandleVRMovement()
    {
        HandleTeleportation();
        HandleTeleportToBall();
    }

    void HandleTeleportation()
    {
        if (rightHandController == null) return;
        
        // FIXED: Use SteamVR_Actions.default_* instead of manually initialized actions
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
        // FIXED: Use SteamVR_Actions.default_GrabGrip instead of manually initialized gripAction
        if (SteamVR_Actions.default_GrabGrip.GetStateDown(SteamVR_Input_Sources.LeftHand))
        {
            TeleportToBall();
        }
    }

    void HandleParameterPlotSwipes()
    {
        if (Time.time - lastSwipeTime < swipeCooldown) return;
        if (leftHandController == null) return;
        
        // NEW: Check input method and route accordingly
        if (touchpadInputWorking && !isUsingAlternativeInput)
        {
            HandleTouchpadParameterSwipes();
        }
        else if (enableAlternativeInput)
        {
            HandleAlternativeParameterInput();
        }
    }

    /// <summary>
    /// NEW: Handle parameter switching via touchpad (original method)
    /// </summary>
    void HandleTouchpadParameterSwipes()
    {
        try
        {
            // FIXED: Use SteamVR_Actions.default_TouchpadPosition instead of manually initialized touchpadAction
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
            Debug.LogWarning($"Touchpad parameter swipe error: {ex.Message}");
            
            // Mark touchpad as non-working and switch to alternative input
            touchpadInputWorking = false;
            isUsingAlternativeInput = true;
            
            Debug.LogWarning("Switching to alternative input due to touchpad error");
        }
    }

    /// <summary>
    /// NEW: Handle parameter switching via alternative input methods
    /// </summary>
    void HandleAlternativeParameterInput()
    {
        if (Time.time - lastAlternativeInputTime < alternativeInputCooldown) return;
        if (rightHandController == null) return;
        
        try
        {
            // Alternative method: Use right hand trigger + left/right hand position
            bool rightTrigger = SteamVR_Actions.default_InteractUI.GetStateDown(SteamVR_Input_Sources.RightHand);
            
            if (rightTrigger)
            {
                // Determine direction based on right hand position relative to left hand
                if (leftHandController != null)
                {
                    Vector3 rightPos = rightHandController.transform.position;
                    Vector3 leftPos = leftHandController.transform.position;
                    Vector3 relativePos = rightPos - leftPos;
                    
                    // Check if right hand is to the left or right of left hand
                    float horizontalOffset = Vector3.Dot(relativePos, rightHandController.transform.right);
                    
                    if (Mathf.Abs(horizontalOffset) > 0.1f) // Minimum threshold for direction detection
                    {
                        if (horizontalOffset > 0)
                        {
                            OnSwipeRight();
                            Debug.Log("Alternative input: RIGHT (trigger + right hand position)");
                        }
                        else
                        {
                            OnSwipeLeft();
                            Debug.Log("Alternative input: LEFT (trigger + right hand position)");
                        }
                        
                        lastAlternativeInputTime = Time.time;
                    }
                }
                else
                {
                    // Fallback: Just cycle through parameters
                    OnSwipeRight();
                    Debug.Log("Alternative input: NEXT (trigger - cycling through parameters)");
                    lastAlternativeInputTime = Time.time;
                }
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"Alternative parameter input error: {ex.Message}");
        }
    }

    // Handle swipe right
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

    // Handle swipe left
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

    // Context menu debugging methods for parameter switching
    [ContextMenu("Test Parameter Switch Right")]
    public void TestParameterSwitchRight()
    {
        Debug.Log("=== TESTING PARAMETER SWITCH RIGHT ===");
        OnSwipeRight();
    }
    
    [ContextMenu("Test Parameter Switch Left")]
    public void TestParameterSwitchLeft()
    {
        Debug.Log("=== TESTING PARAMETER SWITCH LEFT ===");
        OnSwipeLeft();
    }
    
    [ContextMenu("Debug Input State")]
    public void DebugInputState()
    {
        Debug.Log("=== INPUT STATE DEBUG ===");
        Debug.Log($"enableParameterPlotSwipe: {enableParameterPlotSwipe}");
        Debug.Log($"handCanvasUI found: {handCanvasUI != null}");
        Debug.Log($"leftHandController: {leftHandController != null}");
        Debug.Log($"enableVRMovement: {enableVRMovement}");
        Debug.Log($"rightHandController: {rightHandController != null}");
        
        if (enableParameterPlotSwipe)
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

    [ContextMenu("Debug SteamVR Input System")]
    public void DebugSteamVRInputSystem()
    {
        Debug.Log("=== STEAMVR INPUT SYSTEM STATUS ===");
        Debug.Log($"SteamVR Ready: {IsSteamVRReady()}");
        Debug.Log($"TouchpadPosition available: {SteamVR_Actions.default_TouchpadPosition != null}");
        Debug.Log($"InteractUI available: {SteamVR_Actions.default_InteractUI != null}");
        Debug.Log($"GrabGrip available: {SteamVR_Actions.default_GrabGrip != null}");
        Debug.Log($"BackpropagationInputManager enabled: {enabled}");
        Debug.Log($"Left controller found: {leftHandController != null}");
        Debug.Log($"Right controller found: {rightHandController != null}");
        Debug.Log($"HandCanvasUI found: {handCanvasUI != null}");
        Debug.Log($"VR Movement enabled: {enableVRMovement}");
        Debug.Log($"Parameter plot swipe enabled: {enableParameterPlotSwipe}");
        
        // NEW: Enhanced debug info
        Debug.Log($"--- ENHANCED INPUT RECOVERY STATUS ---");
        Debug.Log($"Input recovery enabled: {enableInputRecovery}");
        Debug.Log($"Touchpad input working: {touchpadInputWorking}");
        Debug.Log($"Using alternative input: {isUsingAlternativeInput}");
        Debug.Log($"Alternative input enabled: {enableAlternativeInput}");
        Debug.Log($"Input recovery attempts: {inputRecoveryAttempts}/{maxInputRecoveryAttempts}");
        
        if (IsSteamVRReady())
        {
            try
            {
                Vector2 leftTouchpad = SteamVR_Actions.default_TouchpadPosition.GetAxis(SteamVR_Input_Sources.LeftHand);
                Vector2 rightTouchpad = SteamVR_Actions.default_TouchpadPosition.GetAxis(SteamVR_Input_Sources.RightHand);
                Debug.Log($"Current touchpad values - Left: {leftTouchpad}, Right: {rightTouchpad}");
                
                // NEW: Test input method status
                bool touchpadTest = ValidateTouchpadInput();
                Debug.Log($"Real-time touchpad validation: {touchpadTest}");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Error reading SteamVR input: {ex.Message}");
            }
        }
        
        Debug.Log("=== END STEAMVR INPUT SYSTEM STATUS ===");
    }

    /// <summary>
    /// NEW: Force switch to alternative input for testing
    /// </summary>
    [ContextMenu("Force Alternative Input Mode")]
    public void ForceAlternativeInputMode()
    {
        Debug.Log("=== FORCING ALTERNATIVE INPUT MODE ===");
        touchpadInputWorking = false;
        isUsingAlternativeInput = true;
        Debug.Log("Alternative input mode enabled for testing. Use right trigger + hand positioning to switch parameters.");
    }

    /// <summary>
    /// NEW: Force switch back to touchpad input for testing
    /// </summary>
    [ContextMenu("Force Touchpad Input Mode")]
    public void ForceTouchpadInputMode()
    {
        Debug.Log("=== FORCING TOUCHPAD INPUT MODE ===");
        touchpadInputWorking = true;
        isUsingAlternativeInput = false;
        inputRecoveryAttempts = 0;
        Debug.Log("Touchpad input mode enabled for testing. Use left touchpad swipes to switch parameters.");
    }

    /// <summary>
    /// NEW: Test input recovery system
    /// </summary>
    [ContextMenu("Test Input Recovery System")]
    public void TestInputRecoverySystem()
    {
        Debug.Log("=== TESTING INPUT RECOVERY SYSTEM ===");
        Debug.Log("Simulating input corruption...");
        
        touchpadInputWorking = false;
        inputRecoveryAttempts = 0;
        
        AttemptInputRecovery();
        
        Debug.Log("Input recovery test initiated. Check console for recovery progress.");
    }

    /// <summary>
    /// NEW: Debug input state with enhanced information
    /// </summary>
    [ContextMenu("Debug Enhanced Input State")]
    public void DebugEnhancedInputState()
    {
        Debug.Log("=== ENHANCED INPUT STATE DEBUG ===");
        Debug.Log($"enableParameterPlotSwipe: {enableParameterPlotSwipe}");
        Debug.Log($"enableInputRecovery: {enableInputRecovery}");
        Debug.Log($"enableAlternativeInput: {enableAlternativeInput}");
        Debug.Log($"touchpadInputWorking: {touchpadInputWorking}");
        Debug.Log($"isUsingAlternativeInput: {isUsingAlternativeInput}");
        Debug.Log($"handCanvasUI found: {handCanvasUI != null}");
        Debug.Log($"leftHandController: {leftHandController != null}");
        Debug.Log($"rightHandController: {rightHandController != null}");
        Debug.Log($"enableVRMovement: {enableVRMovement}");
        
        if (enableParameterPlotSwipe)
        {
            try
            {
                Vector2 touchpadValue = SteamVR_Actions.default_TouchpadPosition.GetAxis(SteamVR_Input_Sources.LeftHand);
                Debug.Log($"Current left touchpad value: {touchpadValue}");
                Debug.Log($"Swipe threshold: {swipeThreshold}");
                Debug.Log($"Time since last swipe: {Time.time - lastSwipeTime:F2}s");
                Debug.Log($"Time since last alternative input: {Time.time - lastAlternativeInputTime:F2}s");
                
                // Test input validation
                bool inputValid = ValidateTouchpadInput();
                Debug.Log($"Real-time input validation: {inputValid}");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Error reading input state: {ex.Message}");
            }
        }
    }

    void OnDestroy()
    {
        // Clean up teleportation system
        CleanupTeleportSystem();
    }
}
