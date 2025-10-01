/* 
 * SETUP INSTRUCTIONS FOR INPUT/OUTPUT PLOT:
 * 
 * 1. CREATE UI HIERARCHY:
 *    - Create a Canvas (World Space) with HandCanvasUI script
 *    - Under Canvas, create:
 *      - OutputPlotPanel (RectTransform/Panel) 
 *      - SurfaceOverviewPanel (RectTransform/Panel)
 *      - ParameterDisplayPanel (RectTransform/Panel)
 *      - ParameterPlotPanel (RectTransform/Panel)
 *    
 * 2. OUTPUT PLOT SETUP:
 *    - Under OutputPlotPanel, create:
 *      - PlotArea (Empty GameObject with RectTransform) - this defines the plot bounds
 *      - OutputPlotTitle (Text component)
 *      - Three GameObjects with LineRenderer components:
 *        - OutputPlotLine (for current network output - blue)
 *        - OptimalPlotLine (for optimal network output - green)
 *        - TargetPlotLine (for training targets - red)
 *    
 * 3. PARAMETER DISPLAY SETUP:
 *    - Under ParameterDisplayPanel, create:
 *      - W3Value, W4Value, B5Value (Text components for parameter values)
 *      - LossValue (Text component for loss display)
 *      - LossIndicator (Slider component for visual loss indicator)
 *      - ParameterLossLabel (Text component to show which parameter loss is displayed)
 *    
 * 4. ASSIGN REFERENCES IN INSPECTOR:
 *    - Drag OutputPlotPanel to outputPlotPanel field
 *    - Drag PlotArea to plotArea field  
 *    - Drag LineRenderers to outputPlotLine, optimalPlotLine, and targetPlotLine fields
 *    - Drag Text components to their respective fields (w3ValueText, lossValueText, parameterLossLabel, etc.)
 *    - Drag Slider to lossIndicator field
 *    
 * 5. LINERENDERER SETTINGS:
 *    - Material: Use Sprites/Default or UI/Default
 *    - Width: 0.003f
 *    - Use World Space: false
 *    - Colors will be set by script
 *    
 * 6. CANVAS SETTINGS:
 *    - Render Mode: World Space
 *    - Event Camera: Main Camera
 *    - Sorting Layer: UI (or higher)
 *    
 * The script will auto-create missing components if references are null,
 * but manual setup gives better control over positioning and appearance.
 * 
 * NEW FEATURE: Parameter Loss Syncing
 * - When syncLossWithParameterPlot is enabled, the loss display will show 
 *   parameter-specific loss corresponding to the currently selected parameter
 *   in the ParameterPlotPanel (W3, W4, or B5)
 * - Player can swipe on left controller touchpad to switch between parameters
 * - Loss slider and text will update to show loss for the selected parameter
 * 
 * UPDATED: SteamVR Input Corruption Handling
 * - Added robust input validation and recovery for touchpad actions
 * - Handles input corruption during rapid scene transitions  
 * - Provides fallback input methods when touchpad actions fail
 * - Auto-detects and recovers from SteamVR input system corruption
 */

using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Linq;
using System.Collections;
using Valve.VR.InteractionSystem;
using Valve.VR;

public class HandCanvasUI : MonoBehaviour
{
    [Header("Canvas Setup")]
    public Canvas handCanvas;
    public Transform leftHandTransform;
    public Vector3 canvasOffset = new Vector3(0f, 0.1f, 0.15f);
    public Vector3 canvasRotation = new Vector3(-45f, 0f, 0f);
    public float canvasScale = 0.001f;
    
    [Header("UI Panels")]
    public RectTransform outputPlotPanel;
    public RectTransform surfaceOverviewPanel;
    public RectTransform parameterDisplayPanel;
    public RectTransform parameterPlotPanel; // NEW: Parameter plot panel
    
    [Header("Output Plot")]
    public LineRenderer outputPlotLine;
    public LineRenderer optimalPlotLine;
    public LineRenderer targetPlotLine;
    public RectTransform plotArea;
    public Text outputPlotTitle;
    
    [Header("Parameter Plot")] // NEW: Parameter plot settings
    public LineRenderer parameterPlotLine;
    public LineRenderer currentPositionMarker;
    public RectTransform parameterPlotArea;
    public Text parameterPlotTitle;
    public Text parameterInstructionText;
    
    [Header("Surface Overview")]
    public RawImage surfaceOverviewImage;
    public RectTransform ballIndicator;
    public Text surfaceTitle;
    
    [Header("Parameter Display")]
    public Text w3ValueText;
    public Text w4ValueText;
    public Text b5ValueText;
    public Text lossValueText;
    public Slider lossIndicator;
    // NEW: Parameter-specific loss display
    public Text parameterLossLabel; // Shows which parameter loss is being displayed
    public bool syncLossWithParameterPlot = true; // Enable/disable syncing
    
    [Header("Plot Settings")]
    public int plotResolution = 100;
    public Vector2 inputRange = new Vector2(-5f, 5f);
    public Vector2 outputRange = new Vector2(-2f, 2f);
    public Color currentOutputColor = Color.blue;
    public Color optimalOutputColor = Color.green;
    public Color targetOutputColor = Color.red;
    public Color parameterPlotColor = Color.cyan; // NEW: Parameter plot color
    public Color currentPositionColor = Color.red; // NEW: Current position marker color
    public float currentPositionMarkerSize = 0.02f; // NEW: Marker size
    
    [Header("SteamVR Input Recovery")] // NEW: Input corruption handling
    public float inputRecoveryDelay = 1.0f; // Delay before enabling input after scene load
    public int maxInputRecoveryAttempts = 5; // Max attempts to recover corrupted input
    public float inputRecoveryRetryInterval = 0.5f; // Time between recovery attempts
    
    // NEW: Parameter plot enum and state
    public enum PlotParameter
    {
        W3 = 0,
        W4 = 1,
        B5 = 2
    }
    
    private BackpropagationManager backpropManager;
    private List<Vector2> currentOutputData = new List<Vector2>();
    private List<Vector2> optimalOutputData = new List<Vector2>();
    private List<Vector2> targetOutputData = new List<Vector2>();
    private List<Vector2> parameterPlotData = new List<Vector2>(); // NEW: Parameter plot data
    private RenderTexture surfaceRenderTexture;
    private Camera surfaceCamera;
    
    // Store optimal parameters (after training)
    private float optimalW3, optimalW4, optimalB5;
    private bool optimalParametersLoaded = false;
    
    // NEW: Parameter plot state
    private PlotParameter currentPlotParameter = PlotParameter.W3;
    private bool showParameterPlot = false;
    private readonly string[] parameterNames = { "W3", "W4", "B5" };
    private readonly string[] parameterDescriptions = { 
        "Weight from Hidden Neuron 1 to Output",
        "Weight from Hidden Neuron 2 to Output", 
        "Bias of Output Neuron"
    };

    [Header("Return Transition")]
    public GameObject returnButton;
    public Text returnButtonText;
    public float buttonTriggerDistance = 0.2f; // Distance for collision detection
    [Tooltip("Only enable the return button when the final (third) landmark is reached and Step 3 panel is shown")]
    public bool restrictReturnUntilFinalStage = true;
    
    // VR controller references for button interaction
    private Hand rightHand;
    
    // Landmark gating
    private ParameterBoxManager parameterBoxManager;
    private bool finalStageReached = false; // true when landmark stage >= 3 or game completed
    private float lastVisibilityCheckTime = 0f;
    private const float visibilityCheckInterval = 0.2f;
    
    // NEW: SteamVR Input Recovery System
    private bool steamVRInputReady = false;
    private bool inputRecoveryInProgress = false;
    private int inputRecoveryAttempts = 0;
    private float lastInputValidationTime = 0f;
    private bool useAlternativeInput = false; // Fallback to button-based input when touchpad fails
    
    void Awake()
    {
        SetupCanvas();
        SetupSurfaceCamera();
        SetupParameterPlot(); // NEW: Setup parameter plot
        SetupReturnButton(); // NEW: Setup return transition button
        FindVRHand(); // NEW: Find VR controller for button interaction
        
        // NEW: Start input recovery system
        StartCoroutine(InitializeSteamVRInputWithRecovery());

        // Connect to ParameterBoxManager to gate the return button by landmark progress
        TryConnectToParameterBoxManager();
        // Start hidden until conditions are met
        if (restrictReturnUntilFinalStage)
        {
            SetReturnButtonVisible(false);
        }
    }

    void TryConnectToParameterBoxManager()
    {
        parameterBoxManager = FindObjectOfType<ParameterBoxManager>();
        if (parameterBoxManager != null)
        {
            parameterBoxManager.OnLandmarkStageChanged += HandleLandmarkStageChangedForReturn;
            parameterBoxManager.OnLandmarkGameCompleted += HandleLandmarkGameCompletedForReturn;
            // Initialize current state
            finalStageReached = parameterBoxManager.GetCurrentLandmarkStage() >= 3 || parameterBoxManager.IsLandmarkGameCompleted();
        }
    }

    void HandleLandmarkStageChangedForReturn(int newStage)
    {
        finalStageReached = newStage >= 3;
        UpdateReturnButtonVisibility();
    }

    void HandleLandmarkGameCompletedForReturn()
    {
        finalStageReached = true;
        UpdateReturnButtonVisibility();
    }

    bool IsStep3PanelVisible()
    {
        var ui = InstructionUIManager.Instance;
        if (ui == null) return false;
        var step3 = ui != null ? ui.step3Panel : null;
        return ui.IsInstructionCanvasActive && step3 != null && step3.activeInHierarchy;
    }

    void UpdateReturnButtonVisibility()
    {
        if (!restrictReturnUntilFinalStage)
        {
            SetReturnButtonVisible(true);
            return;
        }

        bool shouldShow = finalStageReached && IsStep3PanelVisible();
        SetReturnButtonVisible(shouldShow);
    }
    
    /// <summary>
    /// NEW: Initialize SteamVR input with corruption recovery
    /// </summary>
    System.Collections.IEnumerator InitializeSteamVRInputWithRecovery()
    {
        Debug.Log("=== INITIALIZING STEAMVR INPUT WITH RECOVERY ===");
        
        // Wait for initial scene load to complete
        yield return new WaitForSeconds(inputRecoveryDelay);
        
        // Attempt to validate and recover SteamVR input
        steamVRInputReady = false;
        inputRecoveryAttempts = 0;
        
        while (!steamVRInputReady && inputRecoveryAttempts < maxInputRecoveryAttempts)
        {
            inputRecoveryAttempts++;
            Debug.Log($"SteamVR input recovery attempt {inputRecoveryAttempts}/{maxInputRecoveryAttempts}");
            
            // Test touchpad action validity
            bool touchpadValid = ValidateTouchpadActions();
            
            if (touchpadValid)
            {
                steamVRInputReady = true;
                useAlternativeInput = false;
                Debug.Log($"✅ SteamVR touchpad input validated successfully on attempt {inputRecoveryAttempts}");
                break;
            }
            else
            {
                Debug.LogWarning($"⚠️ SteamVR touchpad input validation failed on attempt {inputRecoveryAttempts}");
                
                // Try to force reinitialize SteamVR input
                ForceReinitializeSteamVRInput();
                
                // Wait before next attempt
                yield return new WaitForSeconds(inputRecoveryRetryInterval);
            }
        }
        
        if (!steamVRInputReady)
        {
            Debug.LogError($"❌ FAILED TO RECOVER STEAMVR TOUCHPAD INPUT after {maxInputRecoveryAttempts} attempts! Enabling alternative input methods.");
            useAlternativeInput = true;
            
            // Show warning to user
            if (parameterInstructionText != null)
            {
                parameterInstructionText.text = "⚠️ Touchpad input not working! Use trigger button on right controller to switch parameters.";
                parameterInstructionText.color = Color.yellow;
            }
        }
        else
        {
            // Success - restore normal instruction text
            if (parameterInstructionText != null)
            {
                parameterInstructionText.text = "Swipe left/right on left controller touchpad to switch between W3, W4, and B5 SSR plots.";
                parameterInstructionText.color = Color.white;
            }
        }
        
        inputRecoveryInProgress = false;
        Debug.Log($"SteamVR input initialization complete. Ready: {steamVRInputReady}, Alternative: {useAlternativeInput}");
    }
    
    /// <summary>
    /// NEW: Validate touchpad actions for corruption
    /// </summary>
    bool ValidateTouchpadActions()
    {
        try
        {
            // Test if SteamVR_Actions are available
            if (SteamVR_Actions.default_TouchpadPosition == null)
            {
                Debug.LogWarning("SteamVR_Actions.default_TouchpadPosition is null");
                return false;
            }
            
            // Test if we can read touchpad values without errors
            Vector2 leftTouchpad = SteamVR_Actions.default_TouchpadPosition.GetAxis(SteamVR_Input_Sources.LeftHand);
            Vector2 rightTouchpad = SteamVR_Actions.default_TouchpadPosition.GetAxis(SteamVR_Input_Sources.RightHand);
            
            Debug.Log($"Touchpad validation successful - Left: {leftTouchpad}, Right: {rightTouchpad}");
            return true;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Touchpad validation failed: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// NEW: Force reinitialize SteamVR input system
    /// </summary>
    void ForceReinitializeSteamVRInput()
    {
        try
        {
            Debug.Log("Attempting to force reinitialize SteamVR input system...");
            
            // Force SteamVR to reinitialize (this might help with corruption)
            if (SteamVR.enabled)
            {
                // Try to refresh SteamVR input
                SteamVR_Input.Initialize(true); // Force reinitialize
                Debug.Log("SteamVR_Input.Initialize(true) called");
            }
            
            // Small delay to let SteamVR stabilize
            StartCoroutine(DelayedInputValidation());
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Failed to reinitialize SteamVR input: {ex.Message}");
        }
    }
    
    /// <summary>
    /// NEW: Delayed input validation after reinitialize attempt
    /// </summary>
    System.Collections.IEnumerator DelayedInputValidation()
    {
        yield return new WaitForSeconds(0.2f); // Brief delay for SteamVR to stabilize
        
        Debug.Log("Testing input after reinitialize attempt...");
        bool isValid = ValidateTouchpadActions();
        Debug.Log($"Input validation after reinitialize: {isValid}");
    }
    
    /// <summary>
    /// NEW: Periodic input validation during gameplay
    /// </summary>
    void PeriodicInputValidation()
    {
        // Only validate every few seconds to avoid performance impact
        if (Time.time - lastInputValidationTime < 3.0f) return;
        lastInputValidationTime = Time.time;
        
        if (!steamVRInputReady || useAlternativeInput) return;
        
        // Quick validation check
        bool isValid = ValidateTouchpadActions();
        
        if (!isValid && !inputRecoveryInProgress)
        {
            Debug.LogWarning("Touchpad input corruption detected during gameplay! Starting recovery...");
            StartCoroutine(RecoverFromInputCorruption());
        }
    }
    
    /// <summary>
    /// NEW: Recover from input corruption detected during gameplay
    /// </summary>
    System.Collections.IEnumerator RecoverFromInputCorruption()
    {
        inputRecoveryInProgress = true;
        steamVRInputReady = false;
        
        Debug.Log("Attempting to recover from input corruption...");
        
        ForceReinitializeSteamVRInput();
        yield return new WaitForSeconds(inputRecoveryRetryInterval);
        
        bool recovered = ValidateTouchpadActions();
        
        if (recovered)
        {
            steamVRInputReady = true;
            useAlternativeInput = false;
            Debug.Log("✅ Successfully recovered from input corruption!");
            
            if (parameterInstructionText != null)
            {
                parameterInstructionText.text = "Swipe left/right on left controller touchpad to switch between W3, W4, and B5 SSR plots.";
                parameterInstructionText.color = Color.white;
            }
        }
        else
        {
            useAlternativeInput = true;
            Debug.LogWarning("❌ Could not recover from input corruption - using alternative input");
            
            if (parameterInstructionText != null)
            {
                parameterInstructionText.text = "⚠️ Touchpad input corrupted! Use trigger button on right controller to switch parameters.";
                parameterInstructionText.color = Color.yellow;
            }
        }
        
        inputRecoveryInProgress = false;
    }
    
    void SetupCanvas()
    {
        if (handCanvas == null)
        {
            handCanvas = GetComponent<Canvas>();
            if (handCanvas == null)
            {
                handCanvas = gameObject.AddComponent<Canvas>();
            }
        }
        
        handCanvas.renderMode = RenderMode.WorldSpace;
        handCanvas.worldCamera = Camera.main;
        
        // Set canvas scale
        transform.localScale = Vector3.one * canvasScale;
    }
    
    void SetupSurfaceCamera()
    {
        // Create a camera for rendering the surface overview
        GameObject cameraGO = new GameObject("SurfaceOverviewCamera");
        surfaceCamera = cameraGO.AddComponent<Camera>();
        
        surfaceCamera.clearFlags = CameraClearFlags.SolidColor;
        surfaceCamera.backgroundColor = Color.black;
        surfaceCamera.cullingMask = LayerMask.GetMask("Surface"); // Assume surface is on "Surface" layer
        surfaceCamera.orthographic = true;
        surfaceCamera.orthographicSize = 15f;
        surfaceCamera.enabled = false; // Only render on demand
        
        // Position camera above surface
        surfaceCamera.transform.position = Vector3.up * 20f;
        surfaceCamera.transform.rotation = Quaternion.LookRotation(Vector3.down);
        
        // Create render texture
        surfaceRenderTexture = new RenderTexture(256, 256, 16);
        surfaceCamera.targetTexture = surfaceRenderTexture;
        
        if (surfaceOverviewImage != null)
        {
            surfaceOverviewImage.texture = surfaceRenderTexture;
        }
    }

    void SetupParameterPlot()
    {
        if (parameterPlotPanel == null)
        {
            Debug.LogError("ParameterPlotPanel is not assigned in the inspector!");
            return;
        }

        if (parameterPlotLine == null)
        {
            Debug.Log("Creating parameterPlotLine LineRenderer...");
            GameObject plotLineObj = new GameObject("ParameterPlotLine");
            plotLineObj.transform.SetParent(parameterPlotPanel);
            parameterPlotLine = plotLineObj.AddComponent<LineRenderer>();
        }

        if (parameterPlotLine != null)
        {
            ConfigureLineRenderer(parameterPlotLine, parameterPlotColor, "Parameter Plot Line");
        }

        if (currentPositionMarker == null)
        {
            Debug.Log("Creating currentPositionMarker LineRenderer...");
            GameObject markerObj = new GameObject("CurrentPositionMarker");
            markerObj.transform.SetParent(parameterPlotPanel);
            currentPositionMarker = markerObj.AddComponent<LineRenderer>();
        }

        if (currentPositionMarker != null)
        {
            ConfigureLineRenderer(currentPositionMarker, currentPositionColor, "Current Position Marker");
        }

        if (parameterPlotArea == null && parameterPlotPanel != null)
        {
            Debug.Log("Creating parameter plot area...");
            GameObject plotAreaObj = new GameObject("ParameterPlotArea");
            plotAreaObj.transform.SetParent(parameterPlotPanel);
            parameterPlotArea = plotAreaObj.AddComponent<RectTransform>();
            
            // Set plot area to fill the panel with some padding
            parameterPlotArea.anchorMin = new Vector2(0.1f, 0.1f);
            parameterPlotArea.anchorMax = new Vector2(0.9f, 0.9f);
            parameterPlotArea.offsetMin = Vector2.zero;
            parameterPlotArea.offsetMax = Vector2.zero;
        }

        if (parameterPlotTitle != null)
        {
            parameterPlotTitle.text = $"{parameterNames[(int)currentPlotParameter]} vs SSR";
        }

        if (parameterInstructionText != null)
        {
            parameterInstructionText.text = $"Swipe left/right on left controller touchpad to switch between W3, W4, and B5 SSR plots.";
        }

        Debug.Log($"Parameter Plot UI Setup Complete - Line: {parameterPlotLine != null}, Marker: {currentPositionMarker != null}, Area: {parameterPlotArea != null}");
    }
    
    public void Initialize(BackpropagationManager manager)
    {
        backpropManager = manager;
        
        // Load optimal parameters from the manager
        LoadOptimalParameters();
        
        // Check if optimal parameters are within bounds
        bool boundsOK = AreOptimalParametersWithinBounds();
        if (!boundsOK)
        {
            Debug.LogWarning("⚠️ OPTIMAL PARAMETERS ARE OUTSIDE PARAMETER BOX BOUNDS! Ball movement should be restricted to prevent issues.");
        }
        
        SetupUIElements();
        GenerateTargetOutputData();
        GenerateOptimalOutputData();
        UpdateDisplays(manager.CurrentW3, manager.CurrentW4, manager.CurrentB5);
        
        // VERIFY: Check that curves are different
        VerifyCurveDifferences();
        
        Debug.Log("Hand Canvas UI initialized");
    }
    
    void LoadOptimalParameters()
    {
        // The optimal parameters are now accessible via BackpropagationManager getters
        if (backpropManager != null && backpropManager.HasOptimalParameters)
        {
            optimalW3 = backpropManager.OptimalW3;
            optimalW4 = backpropManager.OptimalW4;
            optimalB5 = backpropManager.OptimalB5;
            optimalParametersLoaded = true;
            
            Debug.Log($"Loaded optimal parameters: W3={optimalW3:F3}, W4={optimalW4:F3}, B5={optimalB5:F3}");
        }
        else
        {
            Debug.LogWarning("Cannot load optimal parameters - BackpropagationManager not available or has no optimal parameters");
            optimalParametersLoaded = false;
        }
    }
    
    void SetupUIElements()
    {
        Debug.Log("=== SETTING UP UI ELEMENTS ===");
        
        // Setup output plot line renderers
        if (outputPlotLine == null)
        {
            Debug.Log("Creating outputPlotLine LineRenderer...");
            GameObject plotLineObj = new GameObject("OutputPlotLine");
            plotLineObj.transform.SetParent(outputPlotPanel != null ? outputPlotPanel : transform);
            outputPlotLine = plotLineObj.AddComponent<LineRenderer>();
        }
        
        if (outputPlotLine != null)
        {
            ConfigureLineRenderer(outputPlotLine, currentOutputColor, "Current Output Plot Line");
        }
        
        // Setup optimal plot line renderer
        if (optimalPlotLine == null)
        {
            Debug.Log("Creating optimalPlotLine LineRenderer...");
            GameObject optimalLineObj = new GameObject("OptimalPlotLine");
            optimalLineObj.transform.SetParent(outputPlotPanel != null ? outputPlotPanel : transform);
            optimalPlotLine = optimalLineObj.AddComponent<LineRenderer>();
        }
        
        if (optimalPlotLine != null)
        {
            ConfigureLineRenderer(optimalPlotLine, optimalOutputColor, "Optimal Output Plot Line");
        }
        
        if (targetPlotLine == null)
        {
            Debug.Log("Creating targetPlotLine LineRenderer...");
            GameObject targetLineObj = new GameObject("TargetPlotLine");
            targetLineObj.transform.SetParent(outputPlotPanel != null ? outputPlotPanel : transform);
            targetPlotLine = targetLineObj.AddComponent<LineRenderer>();
        }
        
        if (targetPlotLine != null)
        {
            ConfigureLineRenderer(targetPlotLine, targetOutputColor, "Target Plot Line");
        }
        
        // Create plot area if not assigned
        if (plotArea == null && outputPlotPanel != null)
        {
            Debug.Log("Creating plot area...");
            GameObject plotAreaObj = new GameObject("PlotArea");
            plotAreaObj.transform.SetParent(outputPlotPanel);
            plotArea = plotAreaObj.AddComponent<RectTransform>();
            
            // Set plot area to fill the panel with some padding
            plotArea.anchorMin = new Vector2(0.1f, 0.1f);
            plotArea.anchorMax = new Vector2(0.9f, 0.9f);
            plotArea.offsetMin = Vector2.zero;
            plotArea.offsetMax = Vector2.zero;
        }
        
        // Setup titles
        if (outputPlotTitle != null)
        {
            outputPlotTitle.text = "Network Output vs Input\nBlue: Current | Green: Optimal | Red: Target";
        }
        
        if (surfaceTitle != null)
        {
            surfaceTitle.text = "Loss Surface (w3, w4)";
        }
        
        Debug.Log($"UI Setup Complete - OutputLine: {outputPlotLine != null}, OptimalLine: {optimalPlotLine != null}, TargetLine: {targetPlotLine != null}, PlotArea: {plotArea != null}");
    }
    
    void ConfigureLineRenderer(LineRenderer lr, Color color, string name)
    {
        Debug.Log($"Configuring LineRenderer: {name} with color {color}");
        
        // Basic setup
        lr.material = CreateLineMaterial(color);
        lr.startColor = color;
        lr.endColor = color;
        lr.startWidth = 0.003f;  // Very thin for UI plots
        lr.endWidth = 0.003f;
        lr.useWorldSpace = false;  // FIXED: Use local space by default so plots follow canvas
        lr.sortingOrder = 1;     // Render on top
        
        // Smooth curves
        lr.numCornerVertices = 4;
        lr.numCapVertices = 4;
        
        // No shadows for UI elements
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        
        // Verify the material color was set correctly
        if (lr.material != null)
        {
            Debug.Log($"LineRenderer {name} material color set to: {lr.material.color}");
        }
        else
        {
            Debug.LogError($"Failed to create material for LineRenderer {name}");
        }
        
        Debug.Log($"LineRenderer {name} configured - StartColor: {lr.startColor}, EndColor: {lr.endColor}, Material: {lr.material?.name}");
    }
    
    Material CreateLineMaterial(Color color)
    {
        Material mat = new Material(Shader.Find("Sprites/Default"));
        mat.color = color;
        mat.renderQueue = 3000; // Render on top
        Debug.Log($"Created line material with color {color}");
        return mat;
    }
    
    /// <summary>
    /// Check if optimal parameters are within the parameter box bounds
    /// </summary>
    public bool AreOptimalParametersWithinBounds()
    {
        if (!optimalParametersLoaded || backpropManager == null)
        {
            Debug.LogWarning("Cannot check bounds: Optimal parameters not loaded or BackpropManager missing");
            return false;
        }
        
        Vector2 weightRange = backpropManager.WeightRange;
        Vector2 biasRange = backpropManager.BiasRange;
        
        bool w3InBounds = optimalW3 >= weightRange.x && optimalW3 <= weightRange.y;
        bool w4InBounds = optimalW4 >= weightRange.x && optimalW4 <= weightRange.y;
        bool b5InBounds = optimalB5 >= biasRange.x && optimalB5 <= biasRange.y;
        
        Debug.Log($"=== OPTIMAL PARAMETER BOUNDS CHECK ===");
        Debug.Log($"Weight range: [{weightRange.x:F3}, {weightRange.y:F3}]");
        Debug.Log($"Bias range: [{biasRange.x:F3}, {biasRange.y:F3}]");
        Debug.Log($"Optimal W3: {optimalW3:F3} - {(w3InBounds ? "WITHIN BOUNDS" : "OUT OF BOUNDS")}");
        Debug.Log($"Optimal W4: {optimalW4:F3} - {(w4InBounds ? "WITHIN BOUNDS" : "OUT OF BOUNDS")}");
        Debug.Log($"Optimal B5: {optimalB5:F3} - {(b5InBounds ? "WITHIN BOUNDS" : "OUT OF BOUNDS")}");
        
        bool allInBounds = w3InBounds && w4InBounds && b5InBounds;
        Debug.Log($"All optimal parameters within bounds: {allInBounds}");
        
        return allInBounds;
    }
    
    void GenerateTargetOutputData()
    {
        if (backpropManager?.TrainingData == null) 
        {
            Debug.LogWarning("Cannot generate target output data: No training data available");
            return;
        }
        
        // Generate target curve from training data
        targetOutputData.Clear();
        
        // Sort training data by input for smooth curve
        var sortedData = backpropManager.TrainingData
            .OrderBy(data => data.inputs[0])
            .ToList();
        
        if (sortedData.Count == 0)
        {
            Debug.LogWarning("No training data available for target curve");
            return;
        }
        
        // Debug: Print training data structure
        Debug.Log("=== TRAINING DATA ANALYSIS ===");
        Debug.Log($"Training data count: {sortedData.Count}");
        
        float minInput = float.MaxValue, maxInput = float.MinValue;
        float minTarget = float.MaxValue, maxTarget = float.MinValue;
        
        foreach (var data in sortedData)
        {
            float input = (float)data.inputs[0];
            float target = (float)data.targets[0];
            
            minInput = Mathf.Min(minInput, input);
            maxInput = Mathf.Max(maxInput, input);
            minTarget = Mathf.Min(minTarget, target);
            maxTarget = Mathf.Max(maxTarget, target);
            
            targetOutputData.Add(new Vector2(input, target));
        }
        
        Debug.Log($"Input range: [{minInput:F3}, {maxInput:F3}]");
        Debug.Log($"Target range: [{minTarget:F3}, {maxTarget:F3}]");
        Debug.Log($"Plot input range: [{inputRange.x:F3}, {inputRange.y:F3}]");
        Debug.Log($"Plot output range: [{outputRange.x:F3}, {outputRange.y:F3}]");
        
        // Check if training data is binary
        bool isBinary = true;
        foreach (var data in sortedData)
        {
            float target = (float)data.targets[0];
            if (target != 0f && target != 1f)
            {
                isBinary = false;
                break;
            }
        }
        
        Debug.Log($"Training data appears to be: {(isBinary ? "BINARY (0/1)" : "CONTINUOUS")}");
        Debug.Log($"Generated {targetOutputData.Count} target data points for red curve");
        
        // Print first and last few samples
        Debug.Log("First 3 samples:");
        for (int i = 0; i < Mathf.Min(3, sortedData.Count); i++)
        {
            Debug.Log($"  Input: {sortedData[i].inputs[0]:F3}, Target: {sortedData[i].targets[0]:F3}");
        }
        
        Debug.Log("Last 3 samples:");
        for (int i = Mathf.Max(0, sortedData.Count - 3); i < sortedData.Count; i++)
        {
            Debug.Log($"  Input: {sortedData[i].inputs[0]:F3}, Target: {sortedData[i].targets[0]:F3}");
        }
        
        UpdateTargetPlot();
    }
    
    void GenerateOptimalOutputData()
    {
        if (backpropManager?.neuralNetwork == null || !optimalParametersLoaded) 
        {
            Debug.LogWarning("Cannot generate optimal output data: Network or optimal parameters not available");
            return;
        }
        
        Debug.Log($"=== GENERATING OPTIMAL OUTPUT CURVE (GREEN) ===");
        Debug.Log($"Using optimal parameters (post-training): W3={optimalW3:F3}, W4={optimalW4:F3}, B5={optimalB5:F3}");
        
        optimalOutputData.Clear();
        
        // Get network references
        var network = backpropManager.neuralNetwork;
        int lastLayerIndex = network.weights.Length - 1;
        
        // Store original parameters BEFORE starting the loop
        double originalW3 = network.weights[lastLayerIndex][0];
        double originalW4 = network.weights[lastLayerIndex][1];
        double originalB5 = network.biases[lastLayerIndex][0];
        
        try
        {
            // Generate optimal network output curve
            for (int i = 0; i < plotResolution; i++)
            {
                float input = Mathf.Lerp(inputRange.x, inputRange.y, (float)i / (plotResolution - 1));
                
                // Set the optimal parameters for this plot point
                network.weights[lastLayerIndex][0] = optimalW3;
                network.weights[lastLayerIndex][1] = optimalW4;
                network.biases[lastLayerIndex][0] = optimalB5;
                
                // Forward pass
                double[] networkInput = { input };
                double[] output = network.Forward(networkInput, isTraining: false);
                
                optimalOutputData.Add(new Vector2(input, (float)output[0]));
            }
            
            Debug.Log($"Generated {optimalOutputData.Count} optimal plot points for green curve. Sample: ({optimalOutputData[0].x:F2}, {optimalOutputData[0].y:F2}) to ({optimalOutputData[optimalOutputData.Count-1].x:F2}, {optimalOutputData[optimalOutputData.Count-1].y:F2})");
            
            // Update the visual plot
            UpdateOptimalPlot();
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error during optimal plot generation: {e.Message}");
        }
        finally
        {
            // ALWAYS restore original parameters, even if an error occurred
            network.weights[lastLayerIndex][0] = originalW3;
            network.weights[lastLayerIndex][1] = originalW4;
            network.biases[lastLayerIndex][0] = originalB5;
            
            Debug.Log($"Restored original parameters after optimal curve generation");
        }
    }

    void GenerateParameterPlotData()
    {
        if (backpropManager?.neuralNetwork == null || !optimalParametersLoaded)
        {
            Debug.LogWarning("Cannot generate parameter plot data: Network or optimal parameters not available");
            return;
        }

        Debug.Log($"=== GENERATING {parameterNames[(int)currentPlotParameter]} PLOT CURVE ===");
        Debug.Log($"Generating plot for parameter: {parameterNames[(int)currentPlotParameter]}");

        parameterPlotData.Clear();

        // Get network references
        var network = backpropManager.neuralNetwork;
        int lastLayerIndex = network.weights.Length - 1;

        // Store original parameters BEFORE starting the loop
        double originalW3 = network.weights[lastLayerIndex][0];
        double originalW4 = network.weights[lastLayerIndex][1];
        double originalB5 = network.biases[lastLayerIndex][0];

        try
        {
            // Generate parameter plot curve
            for (int i = 0; i < plotResolution; i++)
            {
                float input = Mathf.Lerp(inputRange.x, inputRange.y, (float)i / (plotResolution - 1));

                // Set the desired parameter for this plot point
                network.weights[lastLayerIndex][0] = currentPlotParameter == PlotParameter.W3 ? optimalW3 : originalW3;
                network.weights[lastLayerIndex][1] = currentPlotParameter == PlotParameter.W4 ? optimalW4 : originalW4;
                network.biases[lastLayerIndex][0] = currentPlotParameter == PlotParameter.B5 ? optimalB5 : originalB5;

                // Forward pass
                double[] networkInput = { input };
                double[] output = network.Forward(networkInput, isTraining: false);

                parameterPlotData.Add(new Vector2(input, (float)output[0]));
            }

            Debug.Log($"Generated {parameterPlotData.Count} parameter plot points for {parameterNames[(int)currentPlotParameter]} curve. Sample: ({parameterPlotData[0].x:F2}, {parameterPlotData[0].y:F2}) to ({parameterPlotData[parameterPlotData.Count-1].x:F2}, {parameterPlotData[parameterPlotData.Count-1].y:F2})");

            // Update the visual plot
            UpdateParameterPlot();
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error during parameter plot generation: {e.Message}");
        }
        finally
        {
            // ALWAYS restore original parameters, even if an error occurred
            network.weights[lastLayerIndex][0] = originalW3;
            network.weights[lastLayerIndex][1] = originalW4;
            network.biases[lastLayerIndex][0] = originalB5;
            
            Debug.Log($"Restored original parameters after parameter curve generation");
        }
    }
    
    public void UpdateDisplays(float w3, float w4, float b5)
    {
        UpdateParameterDisplay(w3, w4, b5);
        UpdateOutputPlot(w3, w4, b5);
        UpdateSurfaceOverview(w3, w4);
        UpdateLossDisplay(w3, w4, b5);
        
        // NEW: Update parameter plot if visible
        if (showParameterPlot)
        {
            UpdateParameterSSRPlot();
            
            // NEW: Also update parameter-specific loss if syncing is enabled
            if (syncLossWithParameterPlot)
            {
                UpdateParameterSpecificLossDisplay();
            }
        }
    }
    
    // NEW: Switch to next parameter (called by PlayerInputManager for swipe right)
    public void SwitchToNextParameter()
    {
        // Auto-enable parameter plot if not already enabled (without affecting output plot)
        if (!showParameterPlot)
        {
            showParameterPlot = true;
            if (parameterPlotPanel != null)
            {
                parameterPlotPanel.gameObject.SetActive(true);
            }
        }
        
        currentPlotParameter = (PlotParameter)(((int)currentPlotParameter + 1) % 3);
        UpdateParameterPlotDisplay();
        
        // NEW: Update parameter display loss when syncing is enabled
        if (syncLossWithParameterPlot && backpropManager != null)
        {
            UpdateParameterSpecificLossDisplay();
        }
        
        Debug.Log($"Switched to parameter: {parameterNames[(int)currentPlotParameter]}");
    }
    
    // NEW: Switch to previous parameter (called by PlayerInputManager for swipe left)
    public void SwitchToPreviousParameter()
    {
        // Auto-enable parameter plot if not already enabled (without affecting output plot)
        if (!showParameterPlot)
        {
            showParameterPlot = true;
            if (parameterPlotPanel != null)
            {
                parameterPlotPanel.gameObject.SetActive(true);
            }
        }
        
        currentPlotParameter = (PlotParameter)(((int)currentPlotParameter + 2) % 3);
        UpdateParameterPlotDisplay();
        
        // NEW: Update parameter display loss when syncing is enabled
        if (syncLossWithParameterPlot && backpropManager != null)
        {
            UpdateParameterSpecificLossDisplay();
        }
        
        Debug.Log($"Switched to parameter: {parameterNames[(int)currentPlotParameter]}");
    }
    
    // NEW: Update parameter plot display after switching
    void UpdateParameterPlotDisplay()
    {
        if (parameterPlotTitle != null)
        {
            parameterPlotTitle.text = $"{parameterNames[(int)currentPlotParameter]} vs SSR";
        }
        
        if (parameterInstructionText != null)
        {
            string paramName = parameterNames[(int)currentPlotParameter];
            string description = parameterDescriptions[(int)currentPlotParameter];
            
            // Update instruction text based on input method availability
            if (steamVRInputReady && !useAlternativeInput)
            {
                parameterInstructionText.text = $"Current: {paramName}\n{description}\nSwipe left/right on left touchpad to switch parameters";
                parameterInstructionText.color = Color.white;
            }
            else
            {
                parameterInstructionText.text = $"Current: {paramName}\n{description}\n⚠️ Use trigger button on right controller to switch parameters";
                parameterInstructionText.color = Color.yellow;
            }
        }
        
        // Generate new SSR plot for the selected parameter
        StartCoroutine(GenerateParameterSSRPlot());
    }
    
    // NEW: Generate SSR plot for current parameter vs its value
    System.Collections.IEnumerator GenerateParameterSSRPlot()
    {
        if (backpropManager == null)
        {
            Debug.LogWarning("Cannot generate SSR plot - BackpropagationManager not available");
            yield break;
        }
        
        parameterPlotData.Clear();
        
        Debug.Log($"Generating SSR plot for {parameterNames[(int)currentPlotParameter]}");
        
        // Get current ball position parameters
        float currentW3 = backpropManager.CurrentW3;
        float currentW4 = backpropManager.CurrentW4;
        float currentB5 = backpropManager.CurrentB5;
        
        // Get parameter range for current parameter
        Vector2 paramRange = GetParameterRange(currentPlotParameter);
        
        // Generate SSR plot data
        for (int i = 0; i < plotResolution; i++)
        {
            float t = (float)i / (plotResolution - 1);
            float paramValue = Mathf.Lerp(paramRange.x, paramRange.y, t);
            
            // Calculate SSR with current parameter varied, others fixed at ball position
            float w3 = currentPlotParameter == PlotParameter.W3 ? paramValue : currentW3;
            float w4 = currentPlotParameter == PlotParameter.W4 ? paramValue : currentW4;
            float b5 = currentPlotParameter == PlotParameter.B5 ? paramValue : currentB5;
            
            float ssr = backpropManager.CalculateLoss(w3, w4, b5);
            
            parameterPlotData.Add(new Vector2(paramValue, ssr));
            
            // Yield occasionally to prevent frame drops
            if (i % 10 == 0)
            {
                yield return null;
            }
        }
        
        Debug.Log($"Generated {parameterPlotData.Count} SSR plot points for {parameterNames[(int)currentPlotParameter]}");
        
        // Update plot visualization
        UpdateParameterSSRVisualization();
        UpdateCurrentPositionMarker();
    }
    
    // NEW: Update SSR plot visualization
    void UpdateParameterSSRVisualization()
    {
        if (parameterPlotLine == null || parameterPlotArea == null || parameterPlotData.Count == 0) return;
        
        Debug.Log($"Updating SSR plot visualization for {parameterNames[(int)currentPlotParameter]}");
        
        // Ensure proper setup
        parameterPlotLine.useWorldSpace = false;
        if (parameterPlotLine.transform.parent != parameterPlotArea)
        {
            parameterPlotLine.transform.SetParent(parameterPlotArea, false);
            parameterPlotLine.transform.localPosition = Vector3.zero;
            parameterPlotLine.transform.localRotation = Quaternion.identity;
            parameterPlotLine.transform.localScale = Vector3.one;
        }
        
        // Find plot bounds
        float minParam = float.MaxValue;
        float maxParam = float.MinValue;
        float minSSR = float.MaxValue;
        float maxSSR = float.MinValue;
        
        foreach (var point in parameterPlotData)
        {
            minParam = Mathf.Min(minParam, point.x);
            maxParam = Mathf.Max(maxParam, point.x);
            minSSR = Mathf.Min(minSSR, point.y);
            maxSSR = Mathf.Max(maxSSR, point.y);
        }
        
        // Add padding to SSR range
        float ssrPadding = (maxSSR - minSSR) * 0.1f;
        minSSR -= ssrPadding;
        maxSSR += ssrPadding;
        
        // Convert to plot coordinates
        Vector3[] plotPoints = new Vector3[parameterPlotData.Count];
        
        for (int i = 0; i < parameterPlotData.Count; i++)
        {
            float x = Mathf.InverseLerp(minParam, maxParam, parameterPlotData[i].x);
            float y = Mathf.InverseLerp(minSSR, maxSSR, parameterPlotData[i].y);
            
            // Scale to plot area
            Vector3 plotPos = new Vector3(
                (x - 0.5f) * parameterPlotArea.rect.width,
                (y - 0.5f) * parameterPlotArea.rect.height,
                0f
            );
            plotPoints[i] = plotPos;
        }
        
        // Update line renderer
        parameterPlotLine.positionCount = plotPoints.Length;
        parameterPlotLine.SetPositions(plotPoints);
        parameterPlotLine.enabled = true;
        parameterPlotLine.gameObject.SetActive(true);
        parameterPlotLine.sortingOrder = 10;
        
        Debug.Log($"SSR plot updated with {plotPoints.Length} points. SSR range: {minSSR:F4} to {maxSSR:F4}");
    }
    
    // NEW: Update current position marker on SSR plot
    void UpdateCurrentPositionMarker()
    {
        if (currentPositionMarker == null || backpropManager == null || !showParameterPlot) return;
        
        // Get current parameter value
        float currentValue = GetCurrentParameterValue();
        
        if (parameterPlotData.Count > 0 && parameterPlotArea != null)
        {
            // FIXED: Calculate SSR using THE SAME LOGIC as the plot curve generation
            // The plot curve uses ball position for non-selected parameters, so marker must too
            float w3ForSSR = currentPlotParameter == PlotParameter.W3 ? currentValue : backpropManager.CurrentW3;
            float w4ForSSR = currentPlotParameter == PlotParameter.W4 ? currentValue : backpropManager.CurrentW4;
            float b5ForSSR = currentPlotParameter == PlotParameter.B5 ? currentValue : backpropManager.CurrentB5;
            
            float currentSSR = backpropManager.CalculateLoss(w3ForSSR, w4ForSSR, b5ForSSR);
            
            // Debug logging for marker position calculation
            if (Time.frameCount % 60 == 0) // Log every 60 frames to reduce spam
            {
                Debug.Log($"=== SSR MARKER POSITION CALCULATION ===");
                Debug.Log($"Selected parameter: {parameterNames[(int)currentPlotParameter]}");
                Debug.Log($"Current {parameterNames[(int)currentPlotParameter]} value: {currentValue:F3}");
                Debug.Log($"SSR calculation parameters: W3={w3ForSSR:F3}, W4={w4ForSSR:F3}, B5={b5ForSSR:F3}");
                Debug.Log($"Calculated SSR: {currentSSR:F6}");
            }
            
            // Find plot bounds (same calculation as in visualization)
            float minParam = float.MaxValue;
            float maxParam = float.MinValue;
            float minSSR = float.MaxValue;
            float maxSSR = float.MinValue;
            
            foreach (var point in parameterPlotData)
            {
                minParam = Mathf.Min(minParam, point.x);
                maxParam = Mathf.Max(maxParam, point.x);
                minSSR = Mathf.Min(minSSR, point.y);
                maxSSR = Mathf.Max(maxSSR, point.y);
            }
            
            float ssrPadding = (maxSSR - minSSR) * 0.1f;
            minSSR -= ssrPadding;
            maxSSR += ssrPadding;
            
            // Convert to plot coordinates
            float x = Mathf.InverseLerp(minParam, maxParam, currentValue);
            float y = Mathf.InverseLerp(minSSR, maxSSR, currentSSR);
            
            Vector3 markerPos = new Vector3(
                (x - 0.5f) * parameterPlotArea.rect.width,
                (y - 0.5f) * parameterPlotArea.rect.height,
                0f
            );
            
            // Create cross marker
            CreateCrossMarker(markerPos);
        }
    }
    
    // NEW: Create cross marker at specified position
    void CreateCrossMarker(Vector3 position)
    {
        if (currentPositionMarker == null) return;
        
        currentPositionMarker.useWorldSpace = false;
        if (currentPositionMarker.transform.parent != parameterPlotArea)
        {
            currentPositionMarker.transform.SetParent(parameterPlotArea, false);
            currentPositionMarker.transform.localPosition = Vector3.zero;
            currentPositionMarker.transform.localRotation = Quaternion.identity;
            currentPositionMarker.transform.localScale = Vector3.one;
        }
        
        // Create cross marker points
        float size = currentPositionMarkerSize * parameterPlotArea.rect.width;
        Vector3[] crossPoints = new Vector3[]
        {
            // Vertical line
            position + new Vector3(0, -size, 0),
            position + new Vector3(0, size, 0),
            position, // Center point to break line
            // Horizontal line
            position + new Vector3(-size, 0, 0),
            position + new Vector3(size, 0, 0)
        };
        
        currentPositionMarker.positionCount = crossPoints.Length;
        currentPositionMarker.SetPositions(crossPoints);
        currentPositionMarker.enabled = true;
        currentPositionMarker.gameObject.SetActive(true);
        currentPositionMarker.sortingOrder = 15; // Higher than plot line
    }
    
    // NEW: Get current parameter value based on selected parameter
    float GetCurrentParameterValue()
    {
        if (backpropManager == null) return 0f;
        
        switch (currentPlotParameter)
        {
            case PlotParameter.W3: return backpropManager.CurrentW3;
            case PlotParameter.W4: return backpropManager.CurrentW4;
            case PlotParameter.B5: return backpropManager.CurrentB5;
            default: return 0f;
        }
    }
    
    // NEW: Get parameter range based on parameter type
    Vector2 GetParameterRange(PlotParameter parameter)
    {
        if (backpropManager == null) return Vector2.zero;
        
        switch (parameter)
        {
            case PlotParameter.W3:
            case PlotParameter.W4:
                return backpropManager.WeightRange;
            case PlotParameter.B5:
                return backpropManager.BiasRange;
            default:
                return Vector2.zero;
        }
    }
    
    // NEW: Update parameter SSR plot (called from UpdateDisplays)
    void UpdateParameterSSRPlot()
    {
        if (showParameterPlot)
        {
            StartCoroutine(GenerateParameterSSRPlot());
        }
    }
    
    // NEW: Toggle between output plot and parameter plot
    public void ToggleToParameterPlot()
    {
        showParameterPlot = true;
        
        if (outputPlotPanel != null)
        {
            outputPlotPanel.gameObject.SetActive(false);
        }
        
        if (parameterPlotPanel != null)
        {
            parameterPlotPanel.gameObject.SetActive(true);
        }
        
        UpdateParameterPlotDisplay();
        
        Debug.Log($"Switched to Parameter Plot mode: {parameterNames[(int)currentPlotParameter]}");
    }
    
    // NEW: Toggle back to output plot
    public void ToggleToOutputPlot()
    {
        showParameterPlot = false;
        
        if (outputPlotPanel != null)
        {
            outputPlotPanel.gameObject.SetActive(true);
        }
        
        if (parameterPlotPanel != null)
        {
            parameterPlotPanel.gameObject.SetActive(false);
        }
        
        UpdateCurrentOutputPlot();
        
        Debug.Log("Switched to Output Plot mode");
    }
    
    /// <summary>
    /// Force refresh the output plot - useful for troubleshooting or manual updates
    /// </summary>
    [ContextMenu("Force Refresh Plot")]
    public void ForceRefreshPlot()
    {
        Debug.Log("=== FORCE REFRESHING OUTPUT PLOT ===");
        
        if (backpropManager != null)
        {
            UpdateDisplays(backpropManager.CurrentW3, backpropManager.CurrentW4, backpropManager.CurrentB5);
            Debug.Log("Plot refresh completed");
        }
        else
        {
            Debug.LogWarning("Cannot refresh plot: backpropManager is null");
        }
    }
    
    /// <summary>
    /// Adjust plot ranges based on training data to fix red curve scaling
    /// </summary>
    [ContextMenu("Auto-Adjust Plot Ranges")]
    public void AutoAdjustPlotRanges()
    {
        if (backpropManager?.TrainingData == null)
        {
            Debug.LogWarning("Cannot adjust plot ranges - no training data available");
            return;
        }
        
        Debug.Log("=== AUTO-ADJUSTING PLOT RANGES ===");
        
        // Analyze training data ranges
        float minInput = float.MaxValue, maxInput = float.MinValue;
        float minTarget = float.MaxValue, maxTarget = float.MinValue;
        
        foreach (var data in backpropManager.TrainingData)
        {
            float input = (float)data.inputs[0];
            float target = (float)data.targets[0];
            
            minInput = Mathf.Min(minInput, input);
            maxInput = Mathf.Max(maxInput, input);
            minTarget = Mathf.Min(minTarget, target);
            maxTarget = Mathf.Max(maxTarget, target);
        }
        
        // Add some padding to the ranges
        float inputPadding = (maxInput - minInput) * 0.1f;
        float targetPadding = (maxTarget - minTarget) * 0.1f;
        
        // Update plot ranges
        Vector2 oldInputRange = inputRange;
        Vector2 oldOutputRange = outputRange;
        
        inputRange = new Vector2(minInput - inputPadding, maxInput + inputPadding);
        outputRange = new Vector2(minTarget - targetPadding, maxTarget + targetPadding);
        
        Debug.Log($"Input range changed from [{oldInputRange.x:F3}, {oldInputRange.y:F3}] to [{inputRange.x:F3}, {inputRange.y:F3}]");
        Debug.Log($"Output range changed from [{oldOutputRange.x:F3}, {oldOutputRange.y:F3}] to [{outputRange.x:F3}, {outputRange.y:F3}]");
        
        // Regenerate all curves with new ranges
        GenerateTargetOutputData();
        GenerateOptimalOutputData();
        
        if (backpropManager != null)
        {
            UpdateDisplays(backpropManager.CurrentW3, backpropManager.CurrentW4, backpropManager.CurrentB5);
        }
        
        Debug.Log("Plot ranges auto-adjusted and curves regenerated");
    }
    
    /// <summary>
    /// Debug method to verify UI setup - call this from inspector or debug console
    /// </summary>
    [ContextMenu("Verify UI Setup")]
    public void VerifyUISetup()
    {
        Debug.Log("=== HAND CANVAS UI SETUP VERIFICATION ===");
        
        // Basic references
        Debug.Log($"handCanvas: {handCanvas != null}");
        Debug.Log($"backpropManager: {backpropManager != null}");
        
        // Output plot references
        Debug.Log($"outputPlotPanel: {outputPlotPanel != null}");
        Debug.Log($"outputPlotLine: {outputPlotLine != null}");
        Debug.Log($"optimalPlotLine: {optimalPlotLine != null}");
        Debug.Log($"targetPlotLine: {targetPlotLine != null}");
        Debug.Log($"plotArea: {plotArea != null}");
        
        // NEW: Parameter plot references
        Debug.Log($"parameterPlotPanel: {parameterPlotPanel != null}");
        Debug.Log($"parameterPlotLine: {parameterPlotLine != null}");
        Debug.Log($"currentPositionMarker: {currentPositionMarker != null}");
        Debug.Log($"parameterPlotArea: {parameterPlotArea != null}");
        Debug.Log($"parameterPlotTitle: {parameterPlotTitle != null}");
        Debug.Log($"parameterInstructionText: {parameterInstructionText != null}");
        
        // Parameter display references
        Debug.Log($"w3ValueText: {w3ValueText != null}");
        Debug.Log($"w4ValueText: {w4ValueText != null}");
        Debug.Log($"b5ValueText: {b5ValueText != null}");
        Debug.Log($"lossValueText: {lossValueText != null}");
        Debug.Log($"lossIndicator: {lossIndicator != null}");
        Debug.Log($"parameterLossLabel: {parameterLossLabel != null}"); // NEW
        
        // Surface overview references
        Debug.Log($"surfaceOverviewPanel: {surfaceOverviewPanel != null}");
        Debug.Log($"surfaceOverviewImage: {surfaceOverviewImage != null}");
        Debug.Log($"ballIndicator: {ballIndicator != null}");
        
        // Parameter plot state
        Debug.Log($"Current parameter: {parameterNames[(int)currentPlotParameter]}");
        Debug.Log($"Show parameter plot: {showParameterPlot}");
        Debug.Log($"Parameter plot data count: {parameterPlotData.Count}");
        Debug.Log($"Sync loss with parameter plot: {syncLossWithParameterPlot}"); // NEW
        
        // Plot data
        Debug.Log($"Current output data: {currentOutputData.Count} points");
        Debug.Log($"Optimal output data: {optimalOutputData.Count} points");
        Debug.Log($"Target output data: {targetOutputData.Count} points");
        
        Debug.Log("=== VERIFICATION COMPLETE ===");
    }
    
    // NEW: Context menu methods for parameter plot testing
    [ContextMenu("Test Switch to W3 Plot")]
    public void DebugSwitchToW3()
    {
        currentPlotParameter = PlotParameter.W3;
        if (!showParameterPlot) 
        {
            showParameterPlot = true;
            if (parameterPlotPanel != null)
            {
                parameterPlotPanel.gameObject.SetActive(true);
            }
        }
        UpdateParameterPlotDisplay();
    }
    
    [ContextMenu("Test Switch to W4 Plot")]
    public void DebugSwitchToW4()
    {
        currentPlotParameter = PlotParameter.W4;
        if (!showParameterPlot) 
        {
            showParameterPlot = true;
            if (parameterPlotPanel != null)
            {
                parameterPlotPanel.gameObject.SetActive(true);
            }
        }
        UpdateParameterPlotDisplay();
    }
    
    [ContextMenu("Test Switch to B5 Plot")]
    public void DebugSwitchToB5()
    {
        currentPlotParameter = PlotParameter.B5;
        if (!showParameterPlot) 
        {
            showParameterPlot = true;
            if (parameterPlotPanel != null)
            {
                parameterPlotPanel.gameObject.SetActive(true);
            }
        }
        UpdateParameterPlotDisplay();
    }
    
    [ContextMenu("Test Next Parameter")]
    public void DebugNextParameter()
    {
        SwitchToNextParameter();
    }
    
    [ContextMenu("Test Previous Parameter")]
    public void DebugPreviousParameter()
    {
        SwitchToPreviousParameter();
    }
    
    [ContextMenu("Force Generate SSR Plot")]
    public void DebugGenerateSSRPlot()
    {
        if (backpropManager == null)
        {
            Debug.LogError("Cannot generate SSR plot - BackpropagationManager not found");
            return;
        }
        
        Debug.Log($"Forcing SSR plot generation for {parameterNames[(int)currentPlotParameter]}");
        StartCoroutine(GenerateParameterSSRPlot());
    }
    
    [ContextMenu("Debug Current Parameter Values")]
    public void DebugCurrentParameterValues()
    {
        if (backpropManager == null)
        {
            Debug.LogError("BackpropagationManager not found");
            return;
        }
        
        Debug.Log("=== CURRENT PARAMETER VALUES ===");
        Debug.Log($"W3: {backpropManager.CurrentW3:F4}");
        Debug.Log($"W4: {backpropManager.CurrentW4:F4}");
        Debug.Log($"B5: {backpropManager.CurrentB5:F4}");
        Debug.Log($"Current Loss/SSR: {backpropManager.CalculateLoss(backpropManager.CurrentW3, backpropManager.CurrentW4, backpropManager.CurrentB5):F6}");
        Debug.Log($"Weight Range: [{backpropManager.WeightRange.x:F1}, {backpropManager.WeightRange.y:F1}]");
        Debug.Log($"Bias Range: [{backpropManager.BiasRange.x:F1}, {backpropManager.BiasRange.y:F1}]");
        Debug.Log($"Selected Parameter: {parameterNames[(int)currentPlotParameter]}");
        Debug.Log($"Current {parameterNames[(int)currentPlotParameter]} Value: {GetCurrentParameterValue():F4}");
        Debug.Log("=== END PARAMETER VALUES ===");
    }
    
    void UpdateParameterDisplay(float w3, float w4, float b5)
    {
        if (w3ValueText != null) w3ValueText.text = $"w3: {w3:F3}";
        if (w4ValueText != null) w4ValueText.text = $"w4: {w4:F3}";
        if (b5ValueText != null) b5ValueText.text = $"b5: {b5:F3}";
    }
    
    void UpdateOutputPlot(float w3, float w4, float b5)
    {
        Debug.Log($"=== UPDATING OUTPUT PLOT (BLUE CURVE): w3={w3:F3}, w4={w4:F3}, b5={b5:F3} ===");
        Debug.Log($"Using current ball position parameters: W3={w3:F3}, W4={w4:F3}, B5={b5:F3}");
        
        if (backpropManager?.neuralNetwork == null) 
        {
            Debug.LogError("Cannot update plot: backpropManager or neuralNetwork is null!");
            return;
        }
        
        if (outputPlotLine == null)
        {
            Debug.LogError("Cannot update plot: outputPlotLine is null!");
            return;
        }
        
        currentOutputData.Clear();
        
        // Get network references
        var network = backpropManager.neuralNetwork;
        int lastLayerIndex = network.weights.Length - 1;
        
        // Store original parameters BEFORE starting the loop
        double originalW3 = network.weights[lastLayerIndex][0];
        double originalW4 = network.weights[lastLayerIndex][1];
        double originalB5 = network.biases[lastLayerIndex][0];
        
        try
        {
            // Generate current network output curve
            for (int i = 0; i < plotResolution; i++)
            {
                float input = Mathf.Lerp(inputRange.x, inputRange.y, (float)i / (plotResolution - 1));
                
                // Set the desired parameters for this plot point
                network.weights[lastLayerIndex][0] = w3;
                network.weights[lastLayerIndex][1] = w4;
                network.biases[lastLayerIndex][0] = b5;
                
                // Forward pass
                double[] networkInput = { input };
                double[] output = network.Forward(networkInput, isTraining: false); // Use isTraining: false to avoid interference
                
                currentOutputData.Add(new Vector2(input, (float)output[0]));
            }
            
            Debug.Log($"Generated {currentOutputData.Count} plot points. Sample: ({currentOutputData[0].x:F2}, {currentOutputData[0].y:F2}) to ({currentOutputData[currentOutputData.Count-1].x:F2}, {currentOutputData[currentOutputData.Count-1].y:F2})");
            
            // Update the visual plot
            UpdateCurrentOutputPlot();
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error during plot generation: {e.Message}");
        }
        finally
        {
            // ALWAYS restore original parameters, even if an error occurred
            network.weights[lastLayerIndex][0] = originalW3;
            network.weights[lastLayerIndex][1] = originalW4;
            network.biases[lastLayerIndex][0] = originalB5;
            
            Debug.Log($"Restored original parameters: w3={originalW3:F3}, w4={originalW4:F3}, b5={originalB5:F3}");
        }
    }

    void UpdateParameterPlot()
    {
        if (parameterPlotLine == null || parameterPlotArea == null || parameterPlotData.Count == 0) return;

        Debug.Log("=== UPDATING PARAMETER PLOT ===");

        // Ensure parameterPlotLine uses local space and is properly parented
        parameterPlotLine.useWorldSpace = false;
        if (parameterPlotLine.transform.parent != parameterPlotArea)
        {
            Debug.Log("Reparenting parameter LineRenderer to parameterPlotArea for proper local space movement");
            parameterPlotLine.transform.SetParent(parameterPlotArea, false);
            parameterPlotLine.transform.localPosition = Vector3.zero;
            parameterPlotLine.transform.localRotation = Quaternion.identity;
            parameterPlotLine.transform.localScale = Vector3.one;
        }

        Vector3[] positions = new Vector3[parameterPlotData.Count];

        for (int i = 0; i < parameterPlotData.Count; i++)
        {
            Vector2 dataPoint = parameterPlotData[i];

            // Normalize to plot area coordinates
            float x = Mathf.InverseLerp(inputRange.x, inputRange.y, dataPoint.x);
            float y = Mathf.InverseLerp(outputRange.x, outputRange.y, dataPoint.y);

            // FIXED: Use local coordinates relative to plotArea instead of world coordinates
            Vector3 localPosition = new Vector3(
                (x - 0.5f) * parameterPlotArea.rect.width,
                (y - 0.5f) * parameterPlotArea.rect.height,
                0f
            );

            positions[i] = localPosition; // Direct local position, no transform needed
        }

        parameterPlotLine.positionCount = positions.Length;
        parameterPlotLine.SetPositions(positions);

        // Force enable and verify properties
        parameterPlotLine.enabled = true;
        parameterPlotLine.gameObject.SetActive(true);

        // Ensure proper rendering order
        parameterPlotLine.sortingOrder = 10; // High sorting order to render on top

        Debug.Log($"Parameter LineRenderer updated with {positions.Length} points. Enabled: {parameterPlotLine.enabled}");
    }
    
    void UpdateCurrentOutputPlot()
    {
        Debug.Log("=== UPDATING CURRENT OUTPUT PLOT ===");
        
        if (outputPlotLine == null)
        {
            Debug.LogError("Cannot update plot: outputPlotLine is null!");
            return;
        }
        
        if (plotArea == null)
        {
            Debug.LogError("Cannot update plot: plotArea is null!");
            return;
        }
        
        if (currentOutputData.Count == 0)
        {
            Debug.LogWarning("No output data to plot!");
            return;
        }
        
        // Ensure LineRenderer is properly configured before updating positions
        if (outputPlotLine.material == null)
        {
            Debug.LogWarning("LineRenderer material is null, reconfiguring...");
            ConfigureLineRenderer(outputPlotLine, currentOutputColor, "Current Output Plot Line");
        }
        
        // Ensure LineRenderer is enabled and properly set up
        outputPlotLine.enabled = true;
        outputPlotLine.useWorldSpace = false; // FIXED: Use local space so plot follows canvas
        
        // Ensure LineRenderer is properly parented to the plot area
        if (outputPlotLine.transform.parent != plotArea)
        {
            Debug.Log("Reparenting LineRenderer to plotArea for proper local space movement");
            outputPlotLine.transform.SetParent(plotArea, false);
            outputPlotLine.transform.localPosition = Vector3.zero;
            outputPlotLine.transform.localRotation = Quaternion.identity;
            outputPlotLine.transform.localScale = Vector3.one;
        }
        
        Vector3[] positions = new Vector3[currentOutputData.Count];
        
        Debug.Log($"PlotArea rect: {plotArea.rect}, Position: {plotArea.position}");
        
        for (int i = 0; i < currentOutputData.Count; i++)
        {
            Vector2 dataPoint = currentOutputData[i];
            
            // Normalize to plot area coordinates
            float x = Mathf.InverseLerp(inputRange.x, inputRange.y, dataPoint.x);
            float y = Mathf.InverseLerp(outputRange.x, outputRange.y, dataPoint.y);
            
            // FIXED: Use local coordinates relative to plotArea instead of world coordinates
            Vector3 localPosition = new Vector3(
                (x - 0.5f) * plotArea.rect.width,
                (y - 0.5f) * plotArea.rect.height,
                0f
            );
            
            positions[i] = localPosition; // Direct local position, no transform needed
            
            // Debug first and last few points
            if (i < 3 || i >= currentOutputData.Count - 3)
            {
                Debug.Log($"Point {i}: Data({dataPoint.x:F2}, {dataPoint.y:F2}) -> Normalized({x:F2}, {y:F2}) -> Local({positions[i].x:F2}, {positions[i].y:F2}, {positions[i].z:F2})");
            }
        }
        
        outputPlotLine.positionCount = positions.Length;
        outputPlotLine.SetPositions(positions);
        
        // Force enable and verify properties
        outputPlotLine.enabled = true;
        outputPlotLine.gameObject.SetActive(true);
        
        // Ensure proper rendering order
        outputPlotLine.sortingOrder = 10; // High sorting order to render on top
        
        Debug.Log($"LineRenderer updated with {positions.Length} points. Enabled: {outputPlotLine.enabled}, Active: {outputPlotLine.gameObject.activeInHierarchy}, Material: {outputPlotLine.material?.name}, UseWorldSpace: {outputPlotLine.useWorldSpace}");
        
        // Additional verification - check if the LineRenderer component is intact
        if (outputPlotLine.positionCount != positions.Length)
        {
            Debug.LogError($"LineRenderer position count mismatch! Expected: {positions.Length}, Actual: {outputPlotLine.positionCount}");
        }
    }
    
    void UpdateOptimalPlot()
    {
        if (optimalPlotLine == null || plotArea == null || optimalOutputData.Count == 0) return;
        
        Debug.Log("=== UPDATING OPTIMAL OUTPUT PLOT ===");
        
        // Ensure optimalPlotLine uses local space and is properly parented
        optimalPlotLine.useWorldSpace = false;
        if (optimalPlotLine.transform.parent != plotArea)
        {
            Debug.Log("Reparenting optimal LineRenderer to plotArea for proper local space movement");
            optimalPlotLine.transform.SetParent(plotArea, false);
            optimalPlotLine.transform.localPosition = Vector3.zero;
            optimalPlotLine.transform.localRotation = Quaternion.identity;
            optimalPlotLine.transform.localScale = Vector3.one;
        }
        
        Vector3[] positions = new Vector3[optimalOutputData.Count];
        
        for (int i = 0; i < optimalOutputData.Count; i++)
        {
            Vector2 dataPoint = optimalOutputData[i];
            
            // Normalize to plot area coordinates
            float x = Mathf.InverseLerp(inputRange.x, inputRange.y, dataPoint.x);
            float y = Mathf.InverseLerp(outputRange.x, outputRange.y, dataPoint.y);
            
            // FIXED: Use local coordinates relative to plotArea instead of world coordinates
            Vector3 localPosition = new Vector3(
                (x - 0.5f) * plotArea.rect.width,
                (y - 0.5f) * plotArea.rect.height,
                0f
            );
            
            positions[i] = localPosition; // Direct local position, no transform needed
        }
        
        optimalPlotLine.positionCount = positions.Length;
        optimalPlotLine.SetPositions(positions);
        
        // Force enable and verify properties
        optimalPlotLine.enabled = true;
        optimalPlotLine.gameObject.SetActive(true);
        
        // Ensure proper rendering order
        optimalPlotLine.sortingOrder = 11; // Slightly higher than current output
        
        Debug.Log($"Optimal LineRenderer updated with {positions.Length} points. Enabled: {optimalPlotLine.enabled}");
    }
    
    void UpdateTargetPlot()
    {
        if (targetPlotLine == null || plotArea == null || targetOutputData.Count == 0) return;
        
        // Ensure targetPlotLine uses local space and is properly parented
        targetPlotLine.useWorldSpace = false;
        if (targetPlotLine.transform.parent != plotArea)
        {
            Debug.Log("Reparenting target LineRenderer to plotArea for proper local space movement");
            targetPlotLine.transform.SetParent(plotArea, false);
            targetPlotLine.transform.localPosition = Vector3.zero;
            targetPlotLine.transform.localRotation = Quaternion.identity;
            targetPlotLine.transform.localScale = Vector3.one;
        }
        
        Vector3[] positions = new Vector3[targetOutputData.Count];
        
        for (int i = 0; i < targetOutputData.Count; i++)
        {
            Vector2 dataPoint = targetOutputData[i];
            
            // Normalize to plot area coordinates
            float x = Mathf.InverseLerp(inputRange.x, inputRange.y, dataPoint.x);
            float y = Mathf.InverseLerp(outputRange.x, outputRange.y, dataPoint.y);
            
            // FIXED: Use local coordinates relative to plotArea instead of world coordinates
            Vector3 localPosition = new Vector3(
                (x - 0.5f) * plotArea.rect.width,
                (y - 0.5f) * plotArea.rect.height,
                0f
            );
            
            positions[i] = localPosition; // Direct local position, no transform needed
        }
        
        targetPlotLine.positionCount = positions.Length;
        targetPlotLine.SetPositions(positions);
        
        // Force enable and verify properties
        targetPlotLine.enabled = true;
        targetPlotLine.gameObject.SetActive(true);
        
        // Ensure proper rendering order
        targetPlotLine.sortingOrder = 12; // Highest priority for target data
    }
    
    void UpdateSurfaceOverview(float w3, float w4)
    {
        if (surfaceCamera == null) return;
        
        // Render surface overview
        surfaceCamera.Render();
        
        // Update ball indicator position
        if (ballIndicator != null && backpropManager != null)
        {
            Vector2 weightRange = backpropManager.WeightRange;
            
            // Normalize w3, w4 to surface overview coordinates
            float normalizedW3 = Mathf.InverseLerp(weightRange.x, weightRange.y, w3);
            float normalizedW4 = Mathf.InverseLerp(weightRange.x, weightRange.y, w4);
            
            // Convert to UI coordinates
            RectTransform surfaceRect = surfaceOverviewImage.rectTransform;
            Vector2 ballPos = new Vector2(
                (normalizedW3 - 0.5f) * surfaceRect.rect.width,
                (normalizedW4 - 0.5f) * surfaceRect.rect.height
            );
            
            ballIndicator.anchoredPosition = ballPos;
        }
    }
    
    void UpdateLossDisplay(float w3, float w4, float b5)
    {
        if (backpropManager == null) return;
        
        // NEW: Check if we should use parameter-specific loss or total loss
        if (syncLossWithParameterPlot && showParameterPlot)
        {
            UpdateParameterSpecificLossDisplay();
        }
        else
        {
            // Original behavior: show total loss using all current parameters
            float currentLoss = backpropManager.CalculateLoss(w3, w4, b5);
            
            if (lossValueText != null)
            {
                lossValueText.text = $"Loss: {currentLoss:F4}";
            }
            
            if (lossIndicator != null)
            {
                // Normalize loss for slider (assuming max loss of 5.0)
                float normalizedLoss = Mathf.Clamp01(currentLoss / 5.0f);
                lossIndicator.value = normalizedLoss;
                
                // Color code the slider
                Image sliderFill = lossIndicator.fillRect?.GetComponent<Image>();
                if (sliderFill != null)
                {
                    sliderFill.color = Color.Lerp(Color.green, Color.red, normalizedLoss);
                }
            }
            
            // Update parameter loss label to show it's total loss
            if (parameterLossLabel != null)
            {
                parameterLossLabel.text = "Total Loss";
            }
        }
    }
    
    // NEW: Update loss display for the currently selected parameter
    void UpdateParameterSpecificLossDisplay()
    {
        if (backpropManager == null || !optimalParametersLoaded) 
        {
            Debug.LogWarning("Cannot update parameter-specific loss: BackpropManager or optimal parameters not available");
            return;
        }
        
        // Get current ball parameters
        float currentW3 = backpropManager.CurrentW3;
        float currentW4 = backpropManager.CurrentW4;
        float currentB5 = backpropManager.CurrentB5;
        
        // FIXED: Calculate loss with selected parameter at ball position, others at optimal values
        float w3ForLoss, w4ForLoss, b5ForLoss;
        
        switch (currentPlotParameter)
        {
            case PlotParameter.W3:
                w3ForLoss = currentW3;     // Selected parameter at ball position
                w4ForLoss = optimalW4;     // Others at optimal values
                b5ForLoss = optimalB5;
                break;
            case PlotParameter.W4:
                w3ForLoss = optimalW3;     // Others at optimal values
                w4ForLoss = currentW4;     // Selected parameter at ball position
                b5ForLoss = optimalB5;
                break;
            case PlotParameter.B5:
                w3ForLoss = optimalW3;     // Others at optimal values
                w4ForLoss = optimalW4;
                b5ForLoss = currentB5;     // Selected parameter at ball position
                break;
            default:
                // Fallback to current values
                w3ForLoss = currentW3;
                w4ForLoss = currentW4;
                b5ForLoss = currentB5;
                break;
        }
        
        float parameterSpecificLoss = backpropManager.CalculateLoss(w3ForLoss, w4ForLoss, b5ForLoss);
        
        // Update loss text
        if (lossValueText != null)
        {
            lossValueText.text = $"{parameterNames[(int)currentPlotParameter]} Loss: {parameterSpecificLoss:F4}";
        }
        
        // Update loss slider
        if (lossIndicator != null)
        {
            // Normalize loss for slider (assuming max loss of 5.0)
            float normalizedLoss = Mathf.Clamp01(parameterSpecificLoss / 5.0f);
            lossIndicator.value = normalizedLoss;
            
            // Color code the slider
            Image sliderFill = lossIndicator.fillRect?.GetComponent<Image>();
            if (sliderFill != null)
            {
                sliderFill.color = Color.Lerp(Color.green, Color.red, normalizedLoss);
            }
        }
        
        // Update parameter loss label
        if (parameterLossLabel != null)
        {
            string parameterDescription = parameterDescriptions[(int)currentPlotParameter];
            parameterLossLabel.text = $"{parameterNames[(int)currentPlotParameter]} Loss\n({parameterDescription})\nOthers at optimal";
        }
        
        Debug.Log($"Updated parameter-specific loss display for {parameterNames[(int)currentPlotParameter]}: {parameterSpecificLoss:F4} (Selected: {GetCurrentParameterValue():F3}, Others: optimal)");
    }

    void ToggleParameterPlot()
    {
        showParameterPlot = !showParameterPlot;
        if (showParameterPlot)
        {
            Debug.Log($"Showing {parameterNames[(int)currentPlotParameter]} Plot");
            GenerateParameterPlotData();
            UpdateParameterPlot();
            if (parameterPlotTitle != null) parameterPlotTitle.text = $"{parameterNames[(int)currentPlotParameter]} vs SSR";
            if (parameterInstructionText != null) parameterInstructionText.text = $"Click the button to toggle between {parameterNames[(int)currentPlotParameter]} and SSR plots.";
        }
        else
        {
            Debug.Log($"Showing Network Output Plot");
            UpdateCurrentOutputPlot();
            if (parameterPlotTitle != null) parameterPlotTitle.text = "Network Output vs Input";
            if (parameterInstructionText != null) parameterInstructionText.text = "Click the button to toggle between W3/W4/B5 and SSR plots.";
        }
    }
    
    void Update()
    {
        UpdateCanvasPosition();
        HandleReturnButtonInteraction(); // NEW: Handle button interaction
        
        // Periodically evaluate visibility conditions (handles when Step 3 panel becomes visible later)
        if (restrictReturnUntilFinalStage && Time.time - lastVisibilityCheckTime >= visibilityCheckInterval)
        {
            lastVisibilityCheckTime = Time.time;
            UpdateReturnButtonVisibility();
        }
        
        // Ensure plot remains visible and updated - add periodic refresh (DISABLED - too verbose)
        // if (backpropManager != null && Time.frameCount % 10 == 0) // Update every 10 frames for performance
        // {
        //     // Verify plot lines are still properly configured
        //     if (outputPlotLine != null && !outputPlotLine.enabled)
        //     {
        //         Debug.LogWarning("Output plot line was disabled, re-enabling...");
        //         outputPlotLine.enabled = true;
        //     }
        //     
        //     if (optimalPlotLine != null && !optimalPlotLine.enabled)
        //     {
        //         Debug.LogWarning("Optimal plot line was disabled, re-enabling...");
        //         optimalPlotLine.enabled = true;
        //     }

        //     if (parameterPlotLine != null && !parameterPlotLine.enabled)
        //     {
        //         Debug.LogWarning("Parameter plot line was disabled, re-enabling...");
        //         parameterPlotLine.enabled = true;
        //     }
        //     
        //     // Periodic refresh to maintain plot visibility
        //     if (outputPlotLine != null && currentOutputData.Count > 0 && outputPlotLine.positionCount == 0)
        //     {
        //         Debug.LogWarning("Output plot positions were lost, refreshing...");
        //         UpdateCurrentOutputPlot();
        //     }
        //     
        //     if (optimalPlotLine != null && optimalOutputData.Count > 0 && optimalPlotLine.positionCount == 0)
        //     {
        //         Debug.LogWarning("Optimal plot positions were lost, refreshing...");
        //         UpdateOptimalPlot();
        //     }
        // 
        //     if (parameterPlotLine != null && parameterPlotData.Count > 0 && parameterPlotLine.positionCount == 0)
        //     {
        //         Debug.LogWarning("Parameter plot positions were lost, refreshing...");
        //         GenerateParameterPlotData(); // Regenerate data if positions are lost
        //         UpdateParameterPlot();
        //     }
        // }
    }
    
    void UpdateCanvasPosition()
    {
        if (leftHandTransform == null) return;
        
        // Position canvas relative to left hand
        transform.position = leftHandTransform.position + leftHandTransform.TransformDirection(canvasOffset);
        transform.rotation = leftHandTransform.rotation * Quaternion.Euler(canvasRotation);
    }
    
    public void SetInteractable(bool interactable)
    {
        if (handCanvas != null)
        {
            GraphicRaycaster raycaster = handCanvas.GetComponent<GraphicRaycaster>();
            if (raycaster != null)
            {
                raycaster.enabled = interactable;
            }
        }
    }
    
    void OnDestroy()
    {
        if (parameterBoxManager != null)
        {
            parameterBoxManager.OnLandmarkStageChanged -= HandleLandmarkStageChangedForReturn;
            parameterBoxManager.OnLandmarkGameCompleted -= HandleLandmarkGameCompletedForReturn;
        }
        if (surfaceRenderTexture != null)
        {
            surfaceRenderTexture.Release();
        }
        
        if (surfaceCamera != null)
        {
            DestroyImmediate(surfaceCamera.gameObject);
        }
    }
    
    /// <summary>
    /// Attach the UI canvas to the appropriate VR hand
    /// </summary>
    public void AttachToHand()
    {
        Debug.Log("=== ATTACHING HAND CANVAS UI TO HAND ===");
        
        // Find left hand if not already assigned
        if (leftHandTransform == null)
        {
            FindAndAssignHand();
        }
        
        if (leftHandTransform != null)
        {
            Debug.Log($"Attaching UI canvas to hand: {leftHandTransform.name}");
            
            // The canvas should stay in world space but follow the hand
            // We'll update its position in UpdateCanvasPosition()
            Debug.Log("UI canvas will follow hand via UpdateCanvasPosition()");
        }
        else
        {
            Debug.LogWarning("Hand not found immediately, starting retry coroutine...");
            StartCoroutine(RetryHandAttachment());
        }
    }
    
    /// <summary>
    /// Retry hand attachment until VR hands are available
    /// </summary>
    System.Collections.IEnumerator RetryHandAttachment()
    {
        int retryCount = 0;
        int maxRetries = 10;
        
        while (leftHandTransform == null && retryCount < maxRetries)
        {
            retryCount++;
            Debug.Log($"Retry {retryCount}: Attempting to find VR hands...");
            
            yield return new WaitForSeconds(0.5f); // Wait half second between retries
            
            FindAndAssignHand();
            
            if (leftHandTransform != null)
            {
                Debug.Log($"SUCCESS: Found hand on retry {retryCount}: {leftHandTransform.name}");
                break;
            }
        }
        
        if (leftHandTransform == null)
        {
            Debug.LogError($"FAILED: Could not find VR hands after {maxRetries} retries!");
            ListAvailableHands();
        }
    }
    
    /// <summary>
    /// Find and assign the appropriate hand for the UI using VRPlayerManager
    /// </summary>
    void FindAndAssignHand()
    {
        Debug.Log("Searching for VR hands for UI attachment...");
        
        // Try to use VRPlayerManager first (scene-specific approach)
        VRPlayerManager vrPlayerManager = FindObjectOfType<VRPlayerManager>();
        if (vrPlayerManager != null)
        {
            Debug.Log($"Found VRPlayerManager: {vrPlayerManager.name}");
            
            // Use right hand for UI attachment (variable name is confusing but leftHandTransform holds UI hand)
            Transform rightHand = vrPlayerManager.GetRightHand();
            if (rightHand != null)
            {
                leftHandTransform = rightHand; // Note: variable name is leftHandTransform but holds right hand
                Debug.Log($"Assigned RIGHT hand for UI via VRPlayerManager: {rightHand.name}");
                return;
            }
            
            // Fallback to left hand if right not available
            Transform leftHand = vrPlayerManager.GetLeftHand();
            if (leftHand != null)
            {
                leftHandTransform = leftHand;
                Debug.Log($"Using left hand as fallback via VRPlayerManager: {leftHand.name}");
                return;
            }
        }
        
        // Fallback to manual detection if VRPlayerManager not available
        Debug.LogWarning("VRPlayerManager not found, falling back to manual hand detection...");
        
        // Get all hands in the scene
        var allHands = FindObjectsOfType<Valve.VR.InteractionSystem.Hand>();
        Debug.Log($"Found {allHands.Length} Hand components in scene");
        
        // Look for RIGHT hand first (prioritize right hand for UI)
        foreach (var hand in allHands)
        {
            Debug.Log($"Checking hand: {hand.name}, HandType: {hand.handType}");
            
            if (hand.handType == Valve.VR.SteamVR_Input_Sources.RightHand || 
                hand.name.ToLower().Contains("right"))
            {
                leftHandTransform = hand.transform; // Note: variable name is leftHandTransform but holds right hand
                Debug.Log($"Assigned RIGHT hand for UI (manual detection): {hand.name}");
                return;
            }
        }
        
        // If no right hand found, try left hand as fallback
        foreach (var hand in allHands)
        {
            if (hand.handType == Valve.VR.SteamVR_Input_Sources.LeftHand || 
                hand.name.ToLower().Contains("left"))
            {
                leftHandTransform = hand.transform;
                Debug.Log($"Using left hand as fallback (manual detection): {hand.name}");
                return;
            }
        }
        
        // If still no hand found, try any hand
        if (allHands.Length > 0)
        {
            leftHandTransform = allHands[0].transform;
            Debug.Log($"Using first available hand as fallback: {allHands[0].name}");
        }
        else
        {
            Debug.LogError("No VR hands found in scene for UI attachment!");
        }
    }
    
    /// <summary>
    /// List all available hands for debugging
    /// </summary>
    void ListAvailableHands()
    {
        Debug.LogWarning("Available hands in scene:");
        var allHands = FindObjectsOfType<Valve.VR.InteractionSystem.Hand>();
        foreach (var hand in allHands)
        {
            Debug.LogWarning($"  - {hand.name} (HandType: {hand.handType}, Active: {hand.gameObject.activeInHierarchy})");
        }
        
        if (allHands.Length == 0)
        {
            Debug.LogWarning("No Hand components found! Make sure VR player is properly loaded in scene.");
        }
    }
    
    // Debug visualization
    void OnDrawGizmos()
    {
        if (leftHandTransform != null)
        {
            Gizmos.color = Color.yellow;
            Vector3 canvasPos = leftHandTransform.position + leftHandTransform.TransformDirection(canvasOffset);
            Gizmos.DrawWireCube(canvasPos, Vector3.one * 0.1f);
        }
    }

    // [ContextMenu("Debug SSR Plot Generation")]
    // public void DebugSSRPlotGeneration()
    // {
    //     if (backpropManager == null)
    //     {
    //         Debug.LogError("BackpropagationManager is null!");
    //         return;
    //     }
        
    //     Debug.Log("=== SSR PLOT GENERATION DEBUG ===");
    //     Debug.Log($"showParameterPlot: {showParameterPlot}");
    //     Debug.Log($"currentPlotParameter: {currentPlotParameter} ({parameterNames[(int)currentPlotParameter]})");
    //     Debug.Log($"plotResolution: {plotResolution}");
        
    //     // Get current parameters
    //     float currentW3 = backpropManager.CurrentW3;
    //     float currentW4 = backpropManager.CurrentW4;
    //     float currentB5 = backpropManager.CurrentB5;
        
    //     Debug.Log($"Current ball parameters: W3={currentW3:F3}, W4={currentW4:F3}, B5={currentB5:F3}");
        
    //     // Get parameter range
    //     Vector2 paramRange = GetParameterRange(currentPlotParameter);
    //     Debug.Log($"Parameter range for {parameterNames[(int)currentPlotParameter]}: [{paramRange.x:F3}, {paramRange.y:F3}]");
        
    //     // Test SSR calculation at different parameter values
    //     Debug.Log("=== TESTING SSR CALCULATION ===");
    //     for (int i = 0; i < 5; i++)
    //     {
    //         float t = (float)i / 4f;
    //         float paramValue = Mathf.Lerp(paramRange.x, paramRange.y, t);
            
    //         float w3 = currentPlotParameter == PlotParameter.W3 ? paramValue : currentW3;
    //         float w4 = currentPlotParameter == PlotParameter.W4 ? paramValue : currentW4;
    //         float b5 = currentPlotParameter == PlotParameter.B5 ? paramValue : currentB5;
            
    //         float ssr = backpropManager.CalculateLoss(w3, w4, b5);
    //         Debug.Log($"  {parameterNames[(int)currentPlotParameter]}={paramValue:F3} -> SSR={ssr:F6}");
    //     }
        
    //     // Check if parameter plot is enabled
    //     Debug.Log($"Parameter plot enabled: {showParameterPlot}");
    //     Debug.Log($"Parameter plot data count: {parameterPlotData.Count}");
        
    //     // Force enable parameter plot and generate
    //     if (!showParameterPlot)
    //     {
    //         Debug.Log("Enabling parameter plot for debugging...");
    //         ToggleToParameterPlot();
    //     }
    //     else
    //     {
    //         Debug.Log("Forcing SSR plot generation...");
    //         StartCoroutine(GenerateParameterSSRPlot());
    //     }
    // }
    
    // [ContextMenu("Debug Current Plot State")]
    // public void DebugCurrentPlotState()
    // {
    //     Debug.Log("=== CURRENT PLOT STATE ===");
    //     Debug.Log($"showParameterPlot: {showParameterPlot}");
    //     Debug.Log($"currentPlotParameter: {currentPlotParameter} ({parameterNames[(int)currentPlotParameter]})");
    //     Debug.Log($"parameterPlotData.Count: {parameterPlotData.Count}");
        
    //     // Check UI components
    //     Debug.Log("=== UI COMPONENTS ===");
    //     Debug.Log($"parameterPlotPanel: {parameterPlotPanel != null} (active: {parameterPlotPanel?.gameObject.activeInHierarchy})");
    //     Debug.Log($"outputPlotPanel: {outputPlotPanel != null} (active: {outputPlotPanel?.gameObject.activeInHierarchy})");
    //     Debug.Log($"parameterPlotLine: {parameterPlotLine != null} (enabled: {parameterPlotLine?.enabled})");
    //     Debug.Log($"parameterPlotArea: {parameterPlotArea != null}");
        
    //     if (parameterPlotLine != null)
    //     {
    //         Debug.Log($"parameterPlotLine positionCount: {parameterPlotLine.positionCount}");
    //         Debug.Log($"parameterPlotLine useWorldSpace: {parameterPlotLine.useWorldSpace}");
    //         Debug.Log($"parameterPlotLine material: {parameterPlotLine.material?.name}");
    //     }
        
    //     // Print first few parameter plot data points
    //     if (parameterPlotData.Count > 0)
    //     {
    //         Debug.Log("=== PARAMETER PLOT DATA SAMPLE ===");
    //         for (int i = 0; i < Mathf.Min(5, parameterPlotData.Count); i++)
    //         {
    //             Debug.Log($"  Point {i}: ({parameterPlotData[i].x:F3}, {parameterPlotData[i].y:F6})");
    //         }
    //     }
    // }
    
    // [ContextMenu("Force Enable Parameter Plot")]
    // public void ForceEnableParameterPlot()
    // {
    //     Debug.Log("=== FORCING PARAMETER PLOT ENABLE ===");
        
    //     if (backpropManager == null)
    //     {
    //         Debug.LogError("BackpropagationManager is null!");
    //         return;
    //     }
        
    //     // Enable parameter plot
    //     showParameterPlot = true;
        
    //     // Hide output plot panel
    //     if (outputPlotPanel != null)
    //     {
    //         outputPlotPanel.gameObject.SetActive(false);
    //         Debug.Log("Disabled output plot panel");
    //     }
        
    //     // Show parameter plot panel
    //     if (parameterPlotPanel != null)
    //     {
    //         parameterPlotPanel.gameObject.SetActive(true);
    //         Debug.Log("Enabled parameter plot panel");
    //     }
        
    //     // Update display
    //     UpdateParameterPlotDisplay();
        
    //     Debug.Log($"Parameter plot enabled for: {parameterNames[(int)currentPlotParameter]}");
    // }
    
    // [ContextMenu("Test Parameter Plot System")]
    // public void TestParameterPlotSystem()
    // {
    //     Debug.Log("=== TESTING PARAMETER PLOT SYSTEM ===");
        
    //     if (backpropManager == null)
    //     {
    //         Debug.LogError("BackpropagationManager is null - cannot test parameter plot system!");
    //         return;
    //     }
        
    //     // Test enabling parameter plot
    //     Debug.Log("Testing parameter plot enable...");
    //     ToggleToParameterPlot();
        
    //     // Test parameter switching
    //     Debug.Log("Testing parameter switching...");
    //     SwitchToNextParameter();
    //     SwitchToNextParameter();
    //     SwitchToPreviousParameter();
        
    //     // Verify plot data generation
    //     Debug.Log($"Parameter plot data count: {parameterPlotData.Count}");
    //     Debug.Log($"Current parameter: {parameterNames[(int)currentPlotParameter]}");
    //     Debug.Log($"Show parameter plot: {showParameterPlot}");
        
    //     // Test SSR calculation
    //     if (parameterPlotData.Count > 0)
    //     {
    //         Debug.Log("Sample SSR data points:");
    //         for (int i = 0; i < Mathf.Min(3, parameterPlotData.Count); i++)
    //         {
    //             Debug.Log($"  Point {i}: Parameter={parameterPlotData[i].x:F3}, SSR={parameterPlotData[i].y:F6}");
    //         }
    //     }
        
    //     Debug.Log("=== PARAMETER PLOT SYSTEM TEST COMPLETE ===");
    // }
    
    // NEW: Methods to control parameter loss syncing
    /// <summary>
    /// Enable syncing of loss display with parameter plot selection
    /// </summary>
    public void EnableParameterLossSync()
    {
        syncLossWithParameterPlot = true;
        if (showParameterPlot && backpropManager != null)
        {
            UpdateParameterSpecificLossDisplay();
        }
        Debug.Log("Parameter loss syncing enabled");
    }
    
    /// <summary>
    /// Disable syncing of loss display with parameter plot selection (show total loss)
    /// </summary>
    public void DisableParameterLossSync()
    {
        syncLossWithParameterPlot = false;
        if (backpropManager != null)
        {
            // Revert to total loss display
            UpdateLossDisplay(backpropManager.CurrentW3, backpropManager.CurrentW4, backpropManager.CurrentB5);
        }
        Debug.Log("Parameter loss syncing disabled - showing total loss");
    }
    
    /// <summary>
    /// Toggle parameter loss syncing on/off
    /// </summary>
    public void ToggleParameterLossSync()
    {
        if (syncLossWithParameterPlot)
        {
            DisableParameterLossSync();
        }
        else
        {
            EnableParameterLossSync();
        }
    }
    
    [ContextMenu("Test Parameter Loss Sync")]
    public void DebugTestParameterLossSync()
    {
        Debug.Log("=== TESTING PARAMETER LOSS SYNC ===");
        Debug.Log($"syncLossWithParameterPlot: {syncLossWithParameterPlot}");
        Debug.Log($"showParameterPlot: {showParameterPlot}");
        Debug.Log($"currentPlotParameter: {currentPlotParameter} ({parameterNames[(int)currentPlotParameter]})");
        
        if (backpropManager == null)
        {
            Debug.LogError("BackpropagationManager is null!");
            return;
        }
        
        // Test parameter-specific loss calculation
        float currentW3 = backpropManager.CurrentW3;
        float currentW4 = backpropManager.CurrentW4;
        float currentB5 = backpropManager.CurrentB5;
        
        Debug.Log($"Current ball parameters: W3={currentW3:F3}, W4={currentW4:F3}, B5={currentB5:F3}");
        
        if (!optimalParametersLoaded)
        {
            Debug.LogWarning("Optimal parameters not loaded!");
            return;
        }
        
        Debug.Log($"Optimal parameters: W3={optimalW3:F3}, W4={optimalW4:F3}, B5={optimalB5:F3}");
        
        // Test CORRECTED parameter-specific losses:
        Debug.Log("Testing CORRECTED parameter-specific losses:");
        
        // W3 loss (W3 at ball, W4&B5 at optimal)
        float w3SpecificLoss = backpropManager.CalculateLoss(currentW3, optimalW4, optimalB5);
        Debug.Log($"  W3 Specific Loss: {w3SpecificLoss:F6} (W3={currentW3:F3} at ball, W4={optimalW4:F3} optimal, B5={optimalB5:F3} optimal)");
        
        // W4 loss (W4 at ball, W3&B5 at optimal) 
        float w4SpecificLoss = backpropManager.CalculateLoss(optimalW3, currentW4, optimalB5);
        Debug.Log($"  W4 Specific Loss: {w4SpecificLoss:F6} (W3={optimalW3:F3} optimal, W4={currentW4:F3} at ball, B5={optimalB5:F3} optimal)");
        
        // B5 loss (B5 at ball, W3&W4 at optimal)
        float b5SpecificLoss = backpropManager.CalculateLoss(optimalW3, optimalW4, currentB5);
        Debug.Log($"  B5 Specific Loss: {b5SpecificLoss:F6} (W3={optimalW3:F3} optimal, W4={optimalW4:F3} optimal, B5={currentB5:F3} at ball)");
        
        // Compare with total loss
        float totalLoss = backpropManager.CalculateLoss(currentW3, currentW4, currentB5);
        Debug.Log($"  Total Loss (all at ball): {totalLoss:F6}");
        
        // Force update parameter-specific loss display
        if (syncLossWithParameterPlot)
        {
            Debug.Log($"Forcing parameter-specific loss update for {parameterNames[(int)currentPlotParameter]}...");
            UpdateParameterSpecificLossDisplay();
        }
        
        Debug.Log("=== PARAMETER LOSS SYNC TEST COMPLETE ===");
    }
    
    // NEW: Quick test method to verify parameter loss calculation is working
    [ContextMenu("Quick Test Slider Values")]
    public void QuickTestSliderValues()
    {
        if (backpropManager == null || !optimalParametersLoaded)
        {
            Debug.LogError("Cannot test - BackpropManager or optimal parameters not available");
            return;
        }
        
        Debug.Log("=== QUICK SLIDER TEST ===");
        
        // Test each parameter and show what the slider should display
        for (int i = 0; i < 3; i++)
        {
            PlotParameter testParam = (PlotParameter)i;
            float currentW3 = backpropManager.CurrentW3;
            float currentW4 = backpropManager.CurrentW4;
            float currentB5 = backpropManager.CurrentB5;
            
            float w3ForLoss, w4ForLoss, b5ForLoss;
            switch (testParam)
            {
                case PlotParameter.W3:
                    w3ForLoss = currentW3; w4ForLoss = optimalW4; b5ForLoss = optimalB5;
                    break;
                case PlotParameter.W4:
                    w3ForLoss = optimalW3; w4ForLoss = currentW4; b5ForLoss = optimalB5;
                    break;
                case PlotParameter.B5:
                    w3ForLoss = optimalW3; w4ForLoss = optimalW4; b5ForLoss = currentB5;
                    break;
                default:
                    w3ForLoss = currentW3; w4ForLoss = currentW4; b5ForLoss = currentB5;
                    break;
            }
            
            float loss = backpropManager.CalculateLoss(w3ForLoss, w4ForLoss, b5ForLoss);
            float normalizedLoss = Mathf.Clamp01(loss / 5.0f);
            
            Debug.Log($"{parameterNames[i]} - Loss: {loss:F4}, Normalized: {normalizedLoss:F3}, Slider: {normalizedLoss * 100:F1}%");
        }
        
        Debug.Log("=== END SLIDER TEST ===");
    }
    
    /// <summary>
    /// Find VR controller for button interaction
    /// </summary>
    void FindVRHand()
    {
        Debug.Log("Searching for VR controllers for button interaction...");
        
        Hand[] allHands = FindObjectsOfType<Hand>();
        foreach (var hand in allHands)
        {
            if (hand.handType == Valve.VR.SteamVR_Input_Sources.RightHand || 
                hand.name.ToLower().Contains("right"))
            {
                rightHand = hand;
                Debug.Log($"Found right hand for button interaction: {hand.name}");
                break;
            }
        }
        
        if (rightHand == null)
        {
            Debug.LogWarning("Right hand not found for button interaction!");
        }
    }
    
    /// <summary>
    /// Setup return transition button
    /// </summary>
    void SetupReturnButton()
    {
        Debug.Log("Setting up return transition button...");
        
        // Create return button if not assigned
        if (returnButton == null)
        {
            returnButton = new GameObject("ReturnButton");
            returnButton.transform.SetParent(transform);
            
            // Position button at bottom of canvas
            RectTransform buttonRect = returnButton.AddComponent<RectTransform>();
            buttonRect.anchorMin = new Vector2(0.5f, 0.1f);
            buttonRect.anchorMax = new Vector2(0.5f, 0.1f);
            buttonRect.anchoredPosition = Vector2.zero;
            buttonRect.sizeDelta = new Vector2(200f, 60f);
            
            // Add visual components
            Image buttonImage = returnButton.AddComponent<Image>();
            buttonImage.color = new Color(0.2f, 0.6f, 0.2f, 0.8f); // Green background
            
            // Add text
            GameObject textObj = new GameObject("ButtonText");
            textObj.transform.SetParent(returnButton.transform);
            
            RectTransform textRect = textObj.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            
            returnButtonText = textObj.AddComponent<Text>();
            returnButtonText.text = "Return to Forward Propagation";
            returnButtonText.color = Color.white;
            returnButtonText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            returnButtonText.fontSize = 14;
            returnButtonText.alignment = TextAnchor.MiddleCenter;
            
            Debug.Log("Created return button UI");
        }
        
        // Add 3D collider for VR interaction
        if (returnButton.GetComponent<BoxCollider>() == null)
        {
            BoxCollider buttonCollider = returnButton.AddComponent<BoxCollider>();
            buttonCollider.isTrigger = true;
            buttonCollider.size = new Vector3(0.2f, 0.06f, 0.02f); // 3D collision box
            
            Debug.Log("Added 3D collider to return button");
        }
        
        // Ensure button text is set
        if (returnButtonText != null)
        {
            returnButtonText.text = "Return to Forward Propagation\n(Hit with controller + trigger)";
        }
        
        Debug.Log("Return button setup complete");
    }
    
    /// <summary>
    /// Handle VR controller interaction with return button
    /// </summary>
    void HandleReturnButtonInteraction()
    {
        if (rightHand == null || returnButton == null) return;
        // Do not process interactions when button is hidden or gating conditions are not met
        if (!returnButton.activeInHierarchy) return;
        if (restrictReturnUntilFinalStage && !(finalStageReached && IsStep3PanelVisible())) return;
        
        // Check if right controller is close to button
        Vector3 controllerPos = rightHand.transform.position;
        Vector3 buttonPos = returnButton.transform.position;
        float distance = Vector3.Distance(controllerPos, buttonPos);
        
        // Visual feedback - change button color when controller is close
        Image buttonImage = returnButton.GetComponent<Image>();
        if (buttonImage != null)
        {
            if (distance <= buttonTriggerDistance)
            {
                // Controller is close - brighten button
                buttonImage.color = new Color(0.3f, 0.8f, 0.3f, 0.9f);
                
                                // Check for trigger press
                // FIXED: Use SteamVR_Actions.default_InteractUI instead of manually initialized triggerAction
                if (SteamVR_Actions.default_InteractUI.GetStateDown(rightHand.handType))
                {
                    Debug.Log("Return button triggered by controller!");
                    OnReturnButtonPressed();
                }
            }
            else
            {
                // Controller is far - normal button color
                buttonImage.color = new Color(0.2f, 0.6f, 0.2f, 0.8f);
            }
        }
        
        // Debug distance occasionally (DISABLED - too verbose)
        // if (Time.frameCount % 60 == 0 && distance <= buttonTriggerDistance * 1.5f)
        // {
        //     Debug.Log($"Controller distance to return button: {distance:F3} (trigger distance: {buttonTriggerDistance:F3})");
        // }
    }
    
    /// <summary>
    /// Called when return button is pressed
    /// </summary>
    void OnReturnButtonPressed()
    {
        Debug.Log("=== RETURN BUTTON PRESSED ===");
        // Gate the action as well to avoid accidental activation
        if (restrictReturnUntilFinalStage && !(finalStageReached && IsStep3PanelVisible()))
        {
            Debug.LogWarning("Return button press ignored - final stage or Step3 panel not ready");
            return;
        }
        
        if (backpropManager == null)
        {
            Debug.LogError("Cannot return to forward scene - BackpropagationManager not found!");
            return;
        }
        
        // Provide visual feedback
        StartCoroutine(ButtonPressedFeedback());
        
        // Trigger return transition after brief delay for feedback
        StartCoroutine(DelayedReturnTransition());
    }
    
    /// <summary>
    /// Visual feedback when button is pressed
    /// </summary>
    System.Collections.IEnumerator ButtonPressedFeedback()
    {
        Image buttonImage = returnButton.GetComponent<Image>();
        if (buttonImage != null)
        {
            // Flash button white
            Color originalColor = buttonImage.color;
            buttonImage.color = Color.white;
            
            yield return new WaitForSeconds(0.1f);
            
            buttonImage.color = originalColor;
        }
        
        // Update button text
        if (returnButtonText != null)
        {
            returnButtonText.text = "Returning to Forward Propagation...";
        }
    }
    
    /// <summary>
    /// Delayed transition to give visual feedback time
    /// </summary>
    System.Collections.IEnumerator DelayedReturnTransition()
    {
        yield return new WaitForSeconds(0.5f); // Brief delay for feedback
        
        // Trigger the actual transition
        backpropManager.TransitionToForwardPropagation();
    }
    
    /// <summary>
    /// Enable/disable return button visibility
    /// </summary>
    public void SetReturnButtonVisible(bool visible)
    {
        if (returnButton != null)
        {
            returnButton.SetActive(visible);
            var col = returnButton.GetComponent<Collider>();
            if (col != null) col.enabled = visible;
            Debug.Log($"Return button visibility set to: {visible}");
        }
    }
    
    [ContextMenu("Test Return Button")]
    public void DebugTestReturnButton()
    {
        Debug.Log("Testing return button press...");
        OnReturnButtonPressed();
    }
    
    /// <summary>
    /// Verify that the curves are using different parameters and will not overlap
    /// </summary>
    [ContextMenu("Verify Curve Differences")]
    void VerifyCurveDifferences()
    {
        Debug.Log("=== VERIFYING CURVE DIFFERENCES ===");
        
        if (!optimalParametersLoaded || backpropManager == null)
        {
            Debug.LogWarning("Cannot verify curves - optimal parameters or backprop manager not available");
            return;
        }
        
        // Get current ball parameters (blue curve)
        float currentW3 = backpropManager.CurrentW3;
        float currentW4 = backpropManager.CurrentW4;
        float currentB5 = backpropManager.CurrentB5;
        
        Debug.Log($"Blue curve parameters (ball position): W3={currentW3:F3}, W4={currentW4:F3}, B5={currentB5:F3}");
        Debug.Log($"Green curve parameters (optimal): W3={optimalW3:F3}, W4={optimalW4:F3}, B5={optimalB5:F3}");
        
        // Check if parameters are different
        bool parametersAreDifferent = (Mathf.Abs(currentW3 - optimalW3) > 0.001f || 
                                     Mathf.Abs(currentW4 - optimalW4) > 0.001f || 
                                     Mathf.Abs(currentB5 - optimalB5) > 0.001f);
        
        if (parametersAreDifferent)
        {
            Debug.Log("✅ SUCCESS: Blue and green curves use different parameters - should not overlap!");
            
            // Calculate approximate difference magnitude
            float totalDifference = Mathf.Abs(currentW3 - optimalW3) + 
                                  Mathf.Abs(currentW4 - optimalW4) + 
                                  Mathf.Abs(currentB5 - optimalB5);
            Debug.Log($"Total parameter difference magnitude: {totalDifference:F4}");
        }
        else
        {
            Debug.LogError("❌ PROBLEM: Blue and green curves use identical parameters - they will overlap!");
            Debug.LogError("This means the training epoch was not effective or parameters weren't captured correctly.");
        }
        
        // Verify training data is available
        if (backpropManager.TrainingData != null && backpropManager.TrainingData.Count > 0)
        {
            Debug.Log($"Training data available: {backpropManager.TrainingData.Count} samples");
        }
        else
        {
            Debug.LogWarning("No training data available - this could explain why parameters didn't change");
        }
    }
    
    /// <summary>
    /// Update plot ranges adaptively based on parameter ranges from BackpropagationManager
    /// This ensures plots are properly scaled to show parameter differences clearly
    /// </summary>
    public void UpdatePlotRangesAdaptively()
    {
        if (backpropManager == null)
        {
            Debug.LogWarning("Cannot update plot ranges - BackpropagationManager not available");
            return;
        }
        
        Debug.Log("=== UPDATING PLOT RANGES ADAPTIVELY ===");
        
        // Store old ranges for comparison
        Vector2 oldInputRange = inputRange;
        Vector2 oldOutputRange = outputRange;
        
        // Get adaptive parameter ranges from BackpropagationManager
        Vector2 weightRange = backpropManager.WeightRange;
        Vector2 biasRange = backpropManager.BiasRange;
        
        Debug.Log($"Parameter ranges from BackpropManager: Weight={weightRange.x:F3} to {weightRange.y:F3}, Bias={biasRange.x:F3} to {biasRange.y:F3}");
        
        // Update input range to match the broader of weight or bias ranges
        // This ensures all parameter values are visible in plots
        float minParam = Mathf.Min(weightRange.x, biasRange.x);
        float maxParam = Mathf.Max(weightRange.y, biasRange.y);
        
        // Add small padding to input range for better visualization
        float inputPadding = (maxParam - minParam) * 0.1f;
        inputRange = new Vector2(minParam - inputPadding, maxParam + inputPadding);
        
        // Auto-scale output range based on actual training data if available
        if (backpropManager.TrainingData != null && backpropManager.TrainingData.Count > 0)
        {
            float minOutput = float.MaxValue;
            float maxOutput = float.MinValue;
            
            foreach (var data in backpropManager.TrainingData)
            {
                float output = (float)data.targets[0];
                minOutput = Mathf.Min(minOutput, output);
                maxOutput = Mathf.Max(maxOutput, output);
            }
            
            // Add padding to output range
            float outputPadding = (maxOutput - minOutput) * 0.15f;
            outputRange = new Vector2(minOutput - outputPadding, maxOutput + outputPadding);
            
            Debug.Log($"Output range auto-scaled based on training data: {minOutput:F3} to {maxOutput:F3}");
        }
        else
        {
            // Fallback: Use a reasonable range around 0-1 for typical neural network outputs
            outputRange = new Vector2(-0.2f, 1.2f);
            Debug.Log("Using fallback output range (no training data available)");
        }
        
        Debug.Log($"=== PLOT RANGE UPDATE RESULTS ===");
        Debug.Log($"Input range: {oldInputRange.x:F3} to {oldInputRange.y:F3} → {inputRange.x:F3} to {inputRange.y:F3}");
        Debug.Log($"Output range: {oldOutputRange.x:F3} to {oldOutputRange.y:F3} → {outputRange.x:F3} to {outputRange.y:F3}");
        
        // Calculate improvement factors
        float inputImprovement = (oldInputRange.y - oldInputRange.x) / (inputRange.y - inputRange.x);
        float outputImprovement = (oldOutputRange.y - oldOutputRange.x) / (outputRange.y - outputRange.x);
        
        Debug.Log($"Plot zoom factors: Input={inputImprovement:F1}x, Output={outputImprovement:F1}x");
        
        // Force regenerate all plot data with new ranges
        RegenerateAllPlotsWithNewRanges();
    }
    
    /// <summary>
    /// Regenerate all plot data with the new ranges for immediate visual update
    /// </summary>
    void RegenerateAllPlotsWithNewRanges()
    {
        Debug.Log("Regenerating all plots with new adaptive ranges...");
        
        try
        {
            // Regenerate target data (red curve)
            GenerateTargetOutputData();
            
            // Regenerate optimal data (green curve)  
            GenerateOptimalOutputData();
            
            // Regenerate current output data (blue curve)
            if (backpropManager != null)
            {
                UpdateOutputPlot(backpropManager.CurrentW3, backpropManager.CurrentW4, backpropManager.CurrentB5);
            }
            
            // Regenerate parameter plot if active
            if (showParameterPlot)
            {
                StartCoroutine(GenerateParameterSSRPlot());
            }
            
            Debug.Log("✅ All plots regenerated with adaptive ranges");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error regenerating plots: {e.Message}");
        }
    }
    
    /// <summary>
    /// Test method to manually verify adaptive plot ranges
    /// </summary>
    [ContextMenu("Test Adaptive Plot Ranges")]
    public void TestAdaptivePlotRanges()
    {
        Debug.Log("=== TESTING ADAPTIVE PLOT RANGES ===");
        
        if (backpropManager == null)
        {
            Debug.LogError("BackpropagationManager not available for testing");
            return;
        }
        
        Debug.Log($"Current input range: {inputRange.x:F3} to {inputRange.y:F3}");
        Debug.Log($"Current output range: {outputRange.x:F3} to {outputRange.y:F3}");
        Debug.Log($"BackpropManager weight range: {backpropManager.WeightRange.x:F3} to {backpropManager.WeightRange.y:F3}");
        Debug.Log($"BackpropManager bias range: {backpropManager.BiasRange.x:F3} to {backpropManager.BiasRange.y:F3}");
        
        // Test the update
        UpdatePlotRangesAdaptively();
        
        Debug.Log("Adaptive plot range test completed!");
    }
} 