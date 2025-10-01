using UnityEngine;
using Valve.VR;
using System.Linq; // Added for .Where()

/// <summary>
/// Input manager for Scene 1 (Forward Propagation / Neural Network Visualization)
/// Handles neuron interaction, bias adjustment, and canvas toggles
/// </summary>
public class ForwardPropagationInputManager : MonoBehaviour
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

    private bool isVisualizationVisible = true;
    private GameObject lastHoveredNeuron = null;

    void Start()
    {
        Debug.Log("ForwardPropagationInputManager: Initialized for neural network scene");
        
        // Auto-detect components if not assigned
        if (leftHandController == null || rightHandController == null)
        {
            AutoDetectControllers();
        }
        
        if (leftHandCanvas == null)
        {
            leftHandCanvas = FindObjectOfType<Canvas>();
        }
        
        if (networkVisualization == null)
        {
            networkVisualization = FindObjectOfType<NetworkVis>();
        }
        
        if (activationVisualizer == null)
        {
            activationVisualizer = FindObjectOfType<AFVisualizer>();
        }
        
        // FIXED: Verify Scene 1 setup and SteamVR state
        VerifyScene1Setup();
    }

    /// <summary>
    /// Verify that Scene 1 is properly set up and SteamVR is not corrupted
    /// </summary>
    void VerifyScene1Setup()
    {
        Debug.Log("=== SCENE 1 SETUP VERIFICATION ===");
        Debug.Log($"ForwardPropagationInputManager enabled: {enabled}");
        Debug.Log($"SteamVR Ready: {IsSteamVRReady()}");
        Debug.Log($"Left controller: {leftHandController?.name ?? "NULL"}");
        Debug.Log($"Right controller: {rightHandController?.name ?? "NULL"}");
        Debug.Log($"Left hand canvas: {leftHandCanvas?.name ?? "NULL"}");
        Debug.Log($"Network visualization: {networkVisualization?.name ?? "NULL"}");
        Debug.Log($"Activation visualizer: {activationVisualizer?.name ?? "NULL"}");
        Debug.Log($"Canvas toggle enabled: {enableCanvasToggle}");
        
        // Check for potential conflicts
        var allInputManagers = FindObjectsOfType<MonoBehaviour>().Where(mb => 
            mb.GetType().Name.Contains("InputManager")).ToArray();
        
        Debug.Log($"Found {allInputManagers.Length} InputManager components in scene:");
        foreach (var manager in allInputManagers)
        {
            Debug.Log($"  - {manager.GetType().Name} on {manager.name} (enabled: {manager.enabled})");
        }
        
        // Verify NeuralNetwork singleton
        var neuralNetwork = NeuralNetwork.Instance;
        Debug.Log($"NeuralNetwork singleton: {(neuralNetwork != null ? neuralNetwork.name : "NULL")}");
        
        if (!IsSteamVRReady())
        {
            Debug.LogError("⚠️ SteamVR NOT READY in Scene 1 - this may cause issues on next scene transition!");
        }
        else
        {
            Debug.Log("✅ Scene 1 setup verification complete - SteamVR is ready");
        }
        
        Debug.Log("=== END SCENE 1 SETUP VERIFICATION ===");
    }

    void AutoDetectControllers()
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

    void Update()
    {
        // FIXED: Add null checking for SteamVR system to prevent errors during scene transitions
        if (!IsSteamVRReady())
        {
            return;
        }
        
        try
        {
            // Handle canvas toggle
            HandleControllerInputs();
            
            // Handle neuron interaction
            HandleNeuronInteraction();
            
            // Handle bias adjustment
            HandleBiasAdjustment();
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"SteamVR input error in ForwardPropagationInputManager: {ex.Message}");
        }
    }

    /// <summary>
    /// Check if SteamVR system is ready for input
    /// </summary>
    bool IsSteamVRReady()
    {
        // Check if SteamVR is initialized and actions are available
        return SteamVR_Actions.default_TouchpadPosition != null && 
               SteamVR_Actions.default_InteractUI != null;
    }

    private void HandleControllerInputs()
    {
        // FIXED: Only handle canvas toggle if not over UI to prevent conflicts
        if (enableCanvasToggle && !IsPointerOverUI())
        {
            if (SteamVR_Actions.default_InteractUI.GetStateDown(canvasToggleHand))
            {
                ToggleVisualization();
            }
        }
    }

    private void HandleNeuronInteraction()
    {
        if (!isVisualizationVisible) return;

        // FIXED: Don't handle neuron interaction if pointer is over UI
        if (IsPointerOverUI()) return;

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

            // Handle click - only if not over UI
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

    /// <summary>
    /// Check if the VR pointer is over a UI element to prevent conflicts
    /// </summary>
    bool IsPointerOverUI()
    {
        // Simple check - in VR, UI interactions are typically handled by SteamVR's UI system
        // This prevents our custom input from interfering with button presses
        return UnityEngine.EventSystems.EventSystem.current != null && 
               UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();
    }

    [ContextMenu("Debug SteamVR Input System")]
    public void DebugSteamVRInputSystem()
    {
        Debug.Log("=== STEAMVR INPUT SYSTEM STATUS (Forward Scene) ===");
        Debug.Log($"SteamVR Ready: {IsSteamVRReady()}");
        Debug.Log($"TouchpadPosition available: {SteamVR_Actions.default_TouchpadPosition != null}");
        Debug.Log($"InteractUI available: {SteamVR_Actions.default_InteractUI != null}");
        Debug.Log($"ForwardPropagationInputManager enabled: {enabled}");
        Debug.Log($"Left controller found: {leftHandController != null}");
        Debug.Log($"Right controller found: {rightHandController != null}");
        Debug.Log($"Canvas toggle enabled: {enableCanvasToggle}");
        Debug.Log($"Left hand canvas found: {leftHandCanvas != null}");
        Debug.Log($"Network visualization found: {networkVisualization != null}");
        Debug.Log($"Activation visualizer found: {activationVisualizer != null}");
        
        if (IsSteamVRReady())
        {
            try
            {
                Vector2 touchpadValue = SteamVR_Actions.default_TouchpadPosition.GetAxis(SteamVR_Input_Sources.RightHand);
                Debug.Log($"Current right touchpad value: {touchpadValue}");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Error reading SteamVR input: {ex.Message}");
            }
        }
        
        Debug.Log("=== END STEAMVR INPUT SYSTEM STATUS (Forward Scene) ===");
    }

    public bool IsRightTriggerPressed() {
        return SteamVR_Actions.default_InteractUI.GetStateDown(SteamVR_Input_Sources.RightHand);
    }
} 