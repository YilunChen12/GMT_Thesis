using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System;
using System.Linq;
using Valve.VR;

/// <summary>
/// Manages different instruction panels throughout the VR Neural Network game
/// Supports skip functionality and specialized layouts for different learning moments
/// Also manages the right-hand instruction canvas visibility via right controller trigger
/// UPDATED: Now supports both forward and backpropagation scenes with scene-specific panels
/// UPDATED: Added support for progressive landmark step panels
/// </summary>
public class InstructionUIManager : MonoBehaviour
{
    public static InstructionUIManager Instance { get; private set; }

    [Header("Forward Propagation Panels")]
    public GameObject gameStartPanel;
    public GameObject neuronSelectionPanel;
    public GameObject outputNeuronPanel;
    public GameObject backpropTransitionPanel;
    public GameObject afterAnimationPanel;

    [Header("Backpropagation Panels")]
    public GameObject backStartingPanel; // New panel for backprop scene start
    public GameObject step1Panel; // Panel for first landmark (1 parameter optimal)
    public GameObject step2Panel; // Panel for second landmark (2 parameters optimal)
    public GameObject step3Panel; // Panel for third landmark (all parameters optimal)

    [Header("Step Panel Parameter Display")]
    [Tooltip("Text component to show optimized parameter names in step panels")]
    public Text parameterDisplayText;
    [Tooltip("Alternative text component if different panels have different text objects")]
    public Text step1ParameterText;
    public Text step2ParameterText;
    public Text step3ParameterText;

    [Header("Canvas Control")]
    public Canvas RightHandCanvas; // Right-hand instruction canvas
    
    [Header("Scene Detection")]
    [SerializeField] private string forwardSceneName = "OpenningScene";
    [SerializeField] private string backpropSceneName = "BackpropagationScene";
    
    private GameObject currentActivePanel = null;
    private bool isInstructionCanvasVisible = true; // Canvas visibility state
    private bool isBackpropagationScene = false; // Current scene type
    public Action OnCurrentInstructionCompleted;

    // Landmark integration
    private ParameterBoxManager parameterBoxManager;
    private string[] parameterNames = { "W3", "W4", "B5" };

    public bool IsInstructionCanvasActive => isInstructionCanvasVisible;
    public bool IsBackpropagationScene => isBackpropagationScene;

    public event Action OnInstructionCanvasHidden;

    void Awake() { 
        // Don't destroy on load since we want scene-specific behavior
        // but still use singleton pattern for each scene
        if (Instance != null && Instance != this)
        {
            Debug.Log($"Destroying duplicate InstructionUIManager in scene: {SceneManager.GetActiveScene().name}");
            Destroy(gameObject);
            return;
        }
        
        Instance = this; 
        
        // Detect current scene
        DetectCurrentScene();
        
        // Ensure all panels are hidden at start
        HideAllPanels();
        
        // Show appropriate default panel based on scene
        ShowDefaultPanelForScene();
        
        // Ensure canvas is enabled to show the default panel
        if (RightHandCanvas != null) {
            RightHandCanvas.enabled = isInstructionCanvasVisible; // true by default
        }
        
        Debug.Log($"InstructionUIManager initialized for scene: {SceneManager.GetActiveScene().name}, isBackprop: {isBackpropagationScene}");
    }

    void Start()
    {
        // Connect to landmark system if in backpropagation scene
        if (isBackpropagationScene)
        {
            ConnectToLandmarkSystem();
        }
    }

    /// <summary>
    /// Connect to the landmark system to receive landmark events
    /// </summary>
    void ConnectToLandmarkSystem()
    {
        // Find ParameterBoxManager
        parameterBoxManager = FindObjectOfType<ParameterBoxManager>();
        
        if (parameterBoxManager != null)
        {
            // Subscribe to landmark events
            parameterBoxManager.OnLandmarkStageChanged += HandleLandmarkStageChanged;
            parameterBoxManager.OnLandmarkGameCompleted += HandleLandmarkGameCompleted;
            
            Debug.Log("Successfully connected to landmark system");
        }
        else
        {
            Debug.LogWarning("ParameterBoxManager not found - landmark step panels won't work");
            
            // Try to find it with a delay in case it's being initialized
            StartCoroutine(RetryConnectToLandmarkSystem());
        }
    }

    /// <summary>
    /// Retry connecting to landmark system with delay
    /// </summary>
    System.Collections.IEnumerator RetryConnectToLandmarkSystem()
    {
        float maxWaitTime = 3.0f;
        float elapsedTime = 0f;
        
        while (parameterBoxManager == null && elapsedTime < maxWaitTime)
        {
            yield return new WaitForSeconds(0.5f);
            elapsedTime += 0.5f;
            parameterBoxManager = FindObjectOfType<ParameterBoxManager>();
        }
        
        if (parameterBoxManager != null)
        {
            parameterBoxManager.OnLandmarkStageChanged += HandleLandmarkStageChanged;
            parameterBoxManager.OnLandmarkGameCompleted += HandleLandmarkGameCompleted;
            Debug.Log("Successfully connected to landmark system (delayed)");
        }
        else
        {
            Debug.LogWarning("Failed to find ParameterBoxManager after retry - landmark step panels disabled");
        }
    }

    /// <summary>
    /// Handle when player reaches a landmark and moves to next stage
    /// </summary>
    void HandleLandmarkStageChanged(int newStage)
    {
        Debug.Log($"Landmark stage changed to: {newStage}");
        
        // Show appropriate step panel based on stage
        ShowStepPanel(newStage);
    }

    /// <summary>
    /// Handle when the landmark game is completed (all parameters optimal)
    /// </summary>
    void HandleLandmarkGameCompleted()
    {
        Debug.Log("Landmark game completed - all parameters optimized!");
        
        // Show final completion panel or transition
        // You might want to add a completion panel or transition back to forward propagation
    }

    /// <summary>
    /// Show the appropriate step panel with parameter information
    /// </summary>
    public void ShowStepPanel(int stage)
    {
        if (!isBackpropagationScene || parameterBoxManager == null)
        {
            Debug.LogWarning("Cannot show step panel - not in backprop scene or no landmark manager");
            return;
        }

        GameObject panelToShow = null;
        Text textToUpdate = null;
        string optimizedParameters = "";

        // Determine which panel to show and get parameter information
        switch (stage)
        {
            case 1:
                panelToShow = step1Panel;
                textToUpdate = step1ParameterText ?? parameterDisplayText;
                optimizedParameters = GetOptimizedParametersText(1);
                break;
            case 2:
                panelToShow = step2Panel;
                textToUpdate = step2ParameterText ?? parameterDisplayText;
                optimizedParameters = GetOptimizedParametersText(2);
                break;
            case 3:
                panelToShow = step3Panel;
                textToUpdate = step3ParameterText ?? parameterDisplayText;
                optimizedParameters = GetOptimizedParametersText(3);
                break;
            default:
                Debug.LogWarning($"Unknown step stage: {stage}");
                return;
        }

        if (panelToShow == null)
        {
            Debug.LogWarning($"Step{stage}Panel is not assigned!");
            return;
        }

        // Update parameter text if available
        if (textToUpdate != null)
        {
            textToUpdate.text = optimizedParameters;
            Debug.Log($"Updated step {stage} panel text: {optimizedParameters}");
        }
        else
        {
            Debug.LogWarning($"No text component found for step {stage} panel");
        }

        // Show the panel and canvas
        ShowInstructionCanvas();
        ShowPanel(panelToShow);
        
        Debug.Log($"Showing step {stage} panel with optimized parameters: {optimizedParameters}");
    }

    /// <summary>
    /// Get the text describing which parameters are optimized for the given stage
    /// </summary>
    string GetOptimizedParametersText(int stage)
    {
        if (parameterBoxManager == null) return "Parameter info unavailable";

        try
        {
            // Use the new methods from ParameterBoxManager to get exact parameter names
            return parameterBoxManager.GetOptimizedParametersText(stage);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error getting optimized parameters text: {e.Message}");
            return "Parameter info error";
        }
    }

    /// <summary>
    /// Get the current landmark information for debugging
    /// </summary>
    public string GetCurrentLandmarkInfo()
    {
        if (parameterBoxManager == null) return "No landmark manager";

        var (stage, description, optimizedParams) = parameterBoxManager.GetCurrentLandmarkInfo();
        return $"Stage {stage}: {string.Join(", ", optimizedParams)} optimal";
    }

    /// <summary>
    /// Get list of optimized parameter names for the current stage
    /// </summary>
    public List<string> GetCurrentOptimizedParameters()
    {
        if (parameterBoxManager == null) return new List<string>();

        return parameterBoxManager.GetOptimizedParameterNames(parameterBoxManager.GetCurrentLandmarkStage());
    }

    /// <summary>
    /// Debug method to test step panel display
    /// </summary>
    [ContextMenu("Test Step Panel Display")]
    public void TestStepPanelDisplay()
    {
        if (!isBackpropagationScene)
        {
            Debug.LogWarning("Not in backpropagation scene - cannot test step panels");
            return;
        }

        if (parameterBoxManager == null)
        {
            Debug.LogWarning("No ParameterBoxManager found - cannot test step panels");
            return;
        }

        Debug.Log("=== TESTING STEP PANEL DISPLAY ===");

        for (int stage = 1; stage <= 3; stage++)
        {
            var optimizedParams = parameterBoxManager.GetOptimizedParameterNames(stage);
            var paramText = parameterBoxManager.GetOptimizedParametersText(stage);
            
            Debug.Log($"Stage {stage}: {string.Join(", ", optimizedParams)} -> \"{paramText}\"");
        }

        // Test showing each panel
        Debug.Log("Testing Step 1 Panel...");
        ShowStepPanel(1);
    }

    /// <summary>
    /// Public method to manually show a step panel with custom parameter text
    /// </summary>
    public void ShowStepPanelWithParameters(int stage, string parameterText)
    {
        GameObject panelToShow = null;
        Text textToUpdate = null;

        switch (stage)
        {
            case 1:
                panelToShow = step1Panel;
                textToUpdate = step1ParameterText ?? parameterDisplayText;
                break;
            case 2:
                panelToShow = step2Panel;
                textToUpdate = step2ParameterText ?? parameterDisplayText;
                break;
            case 3:
                panelToShow = step3Panel;
                textToUpdate = step3ParameterText ?? parameterDisplayText;
                break;
            default:
                Debug.LogWarning($"Invalid step stage: {stage}");
                return;
        }

        if (panelToShow == null)
        {
            Debug.LogWarning($"Step{stage}Panel is not assigned!");
            return;
        }

        // Update parameter text
        if (textToUpdate != null)
        {
            textToUpdate.text = parameterText;
        }

        // Show the panel
        ShowInstructionCanvas();
        ShowPanel(panelToShow);
        
        Debug.Log($"Showing step {stage} panel with custom text: {parameterText}");
    }

    /// <summary>
    /// Detect which scene we're currently in
    /// </summary>
    void DetectCurrentScene()
    {
        string currentSceneName = SceneManager.GetActiveScene().name;
        isBackpropagationScene = (currentSceneName == backpropSceneName);
        
        Debug.Log($"Scene detected: {currentSceneName}, isBackpropagationScene: {isBackpropagationScene}");
    }

    /// <summary>
    /// Show the appropriate default panel for the current scene
    /// </summary>
    void ShowDefaultPanelForScene()
    {
        if (isBackpropagationScene)
        {
            // Backpropagation scene - show back starting panel
            if (backStartingPanel != null)
            {
                currentActivePanel = backStartingPanel;
                backStartingPanel.SetActive(true);
                Debug.Log("Back starting panel shown as default for backpropagation scene");
            }
            else
            {
                Debug.LogWarning("backStartingPanel is not assigned for backpropagation scene!");
            }
        }
        else
        {
            // Forward propagation scene - show game start panel
            if (gameStartPanel != null)
            {
                currentActivePanel = gameStartPanel;
                gameStartPanel.SetActive(true);
                Debug.Log("Game start panel shown as default for forward propagation scene");
            }
            else
            {
                Debug.LogWarning("gameStartPanel is not assigned for forward propagation scene!");
            }
        }
    }

    void Update() {
        // Handle right controller trigger for canvas toggle
        //HandleCanvasToggle(); // 注释掉：不再用右手扳机控制Canvas
        
        // Handle panel skip functionality (keep existing logic)
        // if (inputManager != null && inputManager.IsRightTriggerPressed()) {
        //     HideCurrentPanel();
        //     OnCurrentInstructionCompleted?.Invoke();
        // }
    }

    /// <summary>
    /// Handle right controller trigger input for toggling instruction canvas
    /// </summary>
    private void HandleCanvasToggle() {
        // Check if SteamVR is ready to prevent errors
        //if (!IsSteamVRReady()) return; // 注释掉
        
        //try {
        //    // Listen for right controller trigger press to toggle canvas
        //    if (SteamVR_Actions.default_InteractUI.GetStateDown(SteamVR_Input_Sources.RightHand)) {
        //        ToggleInstructionCanvas();
        //    }
        //}
        //catch (System.Exception ex) {
        //    Debug.LogWarning($"SteamVR input error in InstructionUIManager: {ex.Message}");
        //}
    }

    /// <summary>
    /// Check if SteamVR system is ready for input
    /// </summary>
    private bool IsSteamVRReady() {
        return SteamVR_Actions.default_InteractUI != null;
    }

    /// <summary>
    /// Toggle the visibility of the right-hand instruction canvas
    /// </summary>
    public void ToggleInstructionCanvas() {
        isInstructionCanvasVisible = !isInstructionCanvasVisible;
        
        if (RightHandCanvas != null) {
            RightHandCanvas.enabled = isInstructionCanvasVisible;
            
            if (isInstructionCanvasVisible) {
                // When showing canvas, only activate the current active panel
                HideAllPanels(); // First hide all panels
                if (currentActivePanel != null) {
                    currentActivePanel.SetActive(true);
                    Debug.Log($"Instruction Canvas shown with panel: {currentActivePanel.name}");
                } else {
                    Debug.Log("Instruction Canvas shown but no active panel to display");
                }
            } else {
                // When hiding canvas, hide all panels
                HideAllPanels();
                Debug.Log("Instruction Canvas hidden");
            }
        }
        else {
            Debug.LogWarning("RightHandCanvas is not assigned in InstructionUIManager");
        }
        if (!isInstructionCanvasVisible) {
            OnInstructionCanvasHidden?.Invoke(); // fire event when hidden
        }
    }

    /// <summary>
    /// Show the instruction canvas
    /// </summary>
    public void ShowInstructionCanvas() {
        isInstructionCanvasVisible = true;
        
        if (RightHandCanvas != null) {
            RightHandCanvas.enabled = true;
            
            // Only show the current active panel, not all children
            HideAllPanels(); // First hide all panels
            if (currentActivePanel != null) {
                currentActivePanel.SetActive(true);
                Debug.Log($"Instruction Canvas shown with panel: {currentActivePanel.name}");
            } else {
                Debug.Log("Instruction Canvas shown but no active panel to display");
            }
        }
    }

    /// <summary>
    /// Hide the instruction canvas
    /// </summary>
    public void HideInstructionCanvas() {
        isInstructionCanvasVisible = false;
        
        if (RightHandCanvas != null) {
            RightHandCanvas.enabled = false;
            
            // Hide all panels when hiding canvas
            HideAllPanels();
            
            Debug.Log("Instruction Canvas hidden");
        }
        OnInstructionCanvasHidden?.Invoke(); // fire event when hidden
    }

    /// <summary>
    /// Show a specific panel with optional completion callback
    /// Now validates panel compatibility with current scene
    /// </summary>
    public void ShowPanel(GameObject panel, Action onComplete = null) {
        // Validate panel compatibility with current scene
        if (!IsPanelValidForCurrentScene(panel))
        {
            Debug.LogWarning($"Panel {panel?.name} is not valid for current scene ({SceneManager.GetActiveScene().name}). Skipping panel show.");
            return;
        }
        
        Debug.Log($"Switching from panel: {(currentActivePanel != null ? currentActivePanel.name : "none")} to panel: {(panel != null ? panel.name : "none")}");
        HideAllPanels();
        currentActivePanel = panel;
        if (currentActivePanel != null) {
            currentActivePanel.SetActive(true);
            Debug.Log($"Panel {currentActivePanel.name} is now active");
        }
        OnCurrentInstructionCompleted = onComplete;
    }

    /// <summary>
    /// Check if a panel is valid for the current scene
    /// </summary>
    bool IsPanelValidForCurrentScene(GameObject panel)
    {
        if (panel == null) return false;
        
        if (isBackpropagationScene)
        {
            // In backpropagation scene - allow backprop panels and transition panels
            return panel == backStartingPanel || 
                   panel == step1Panel ||
                   panel == step2Panel ||
                   panel == step3Panel ||
                   panel == backpropTransitionPanel; // Allow transition panel in both scenes
        }
        else
        {
            // In forward propagation scene - allow forward panels and transition panels
            return panel == gameStartPanel || 
                   panel == neuronSelectionPanel || 
                   panel == outputNeuronPanel || 
                   panel == backpropTransitionPanel || // Allow transition panel in both scenes
                   panel == afterAnimationPanel;
        }
    }

    public void HideCurrentPanel() {
        if (currentActivePanel != null) {
            Debug.Log($"Hiding current panel: {currentActivePanel.name}");
            currentActivePanel.SetActive(false);
        }
        currentActivePanel = null;
        Debug.Log("No active panel set");
    }

    public void HideAllPanels() {
        // Forward propagation panels
        if (gameStartPanel != null) gameStartPanel.SetActive(false);
        if (neuronSelectionPanel != null) neuronSelectionPanel.SetActive(false);
        if (outputNeuronPanel != null) outputNeuronPanel.SetActive(false);
        if (backpropTransitionPanel != null) backpropTransitionPanel.SetActive(false);
        if (afterAnimationPanel != null) afterAnimationPanel.SetActive(false);
        
        // Backpropagation panels
        if (backStartingPanel != null) backStartingPanel.SetActive(false);
        if (step1Panel != null) step1Panel.SetActive(false);
        if (step2Panel != null) step2Panel.SetActive(false);
        if (step3Panel != null) step3Panel.SetActive(false);
        
        // Note: Don't reset currentActivePanel here, let ShowPanel manage it
    }

    /// <summary>
    /// Show the backpropagation starting panel when entering backprop scene
    /// </summary>
    public void ShowBackpropagationStartPanel(Action onComplete = null)
    {
        if (isBackpropagationScene && backStartingPanel != null)
        {
            ShowPanel(backStartingPanel, onComplete);
        }
        else
        {
            Debug.LogWarning("Cannot show backprop start panel - either not in backprop scene or panel not assigned");
        }
    }

    /// <summary>
    /// Convenience method to show forward propagation game start panel
    /// </summary>
    public void ShowForwardPropagationStartPanel(Action onComplete = null)
    {
        if (!isBackpropagationScene && gameStartPanel != null)
        {
            ShowPanel(gameStartPanel, onComplete);
        }
        else
        {
            Debug.LogWarning("Cannot show forward prop start panel - either not in forward scene or panel not assigned");
        }
    }

    bool IsRightTriggerPressed() {
        // SteamVR标准右手扳机动作
        return SteamVR_Actions.default_InteractUI.GetStateDown(SteamVR_Input_Sources.RightHand);
        // 或者你可以通过inputManager暴露的接口来判断
    }

    public void OnToggleInstructionCanvasButtonPressed() {
        ToggleInstructionCanvas();
    }

    /// <summary>
    /// Called when transitioning to backpropagation scene
    /// Can be called by BackpropagationManager when scene loads
    /// </summary>
    public void OnBackpropagationSceneEntered()
    {
        Debug.Log("InstructionUIManager: Entered backpropagation scene");
        DetectCurrentScene(); // Re-detect scene
        
        // Show backprop starting panel
        if (backStartingPanel != null)
        {
            ShowPanel(backStartingPanel);
        }
    }

    /// <summary>
    /// Called when transitioning to forward propagation scene
    /// Can be called by scene managers when scene loads
    /// </summary>
    public void OnForwardPropagationSceneEntered()
    {
        Debug.Log("InstructionUIManager: Entered forward propagation scene");
        DetectCurrentScene(); // Re-detect scene
        
        // Show game start panel (but check if we're resuming from backprop)
        bool isResumingFromBackprop = BackpropagationManager.IsReturningFromBackpropagation();
        
        if (!isResumingFromBackprop && gameStartPanel != null)
        {
            ShowPanel(gameStartPanel);
        }
        else
        {
            Debug.Log("Resuming from backpropagation - not showing game start panel");
        }
    }

    void OnDestroy()
    {
        // Unsubscribe from landmark events to prevent memory leaks
        if (parameterBoxManager != null)
        {
            parameterBoxManager.OnLandmarkStageChanged -= HandleLandmarkStageChanged;
            parameterBoxManager.OnLandmarkGameCompleted -= HandleLandmarkGameCompleted;
        }
    }

} 