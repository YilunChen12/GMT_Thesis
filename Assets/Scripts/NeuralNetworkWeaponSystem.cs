using UnityEngine;
using Valve.VR;
using TMPro;

public class NeuralNetworkWeaponSystem : MonoBehaviour
{
    [Header("Controller References")]
    public GameObject leftHandController;
    public GameObject rightHandController;
    public SteamVR_Behaviour_Pose leftHandPose;
    public SteamVR_Behaviour_Pose rightHandPose;
    
    [Header("Weapon References")]
    public GameObject weightBlade; // Left hand - weight blade
    public GameObject biasBlade;   // Right hand - bias blade
    public Material bladeMaterial; // Reference material to modify
    
    [Header("Weapon Selection")]
    public SteamVR_Action_Boolean selectNextWeight = SteamVR_Actions.default_GrabPinch;
    public SteamVR_Action_Boolean selectNextBias = SteamVR_Actions.default_GrabPinch;
    public SteamVR_Input_Sources leftHandType = SteamVR_Input_Sources.LeftHand;
    public SteamVR_Input_Sources rightHandType = SteamVR_Input_Sources.RightHand;
    
    [Header("Neural Network References")]
    public NeuralNetwork neuralNetwork;
    public NeuralNetworkDataManager dataManager;
    
    [Header("Visualization Settings")]
    [Range(0, 1)]
    public float maxBrightness = 1.0f;
    [Range(0, 1)]
    public float minBrightness = 0.1f;
    public Color positiveValueColor = Color.white;
    public Color negativeValueColor = Color.black;
    public float maxValueVisualization = 1.0f; // The absolute value that represents max brightness
    
    [Header("Weapon Selection")]
    [Tooltip("Currently selected layer for weight/bias")]
    public int selectedLayer = 0;
    [Tooltip("Currently selected neuron in the layer")]
    public int selectedNeuron = 0;
    [Tooltip("For weights, the currently selected input neuron")]
    public int selectedInputNeuron = 0;
    
    // Current values from the neural network
    private float currentWeightValue = 0f;
    private float currentBiasValue = 0f;
    
    // Material instances for the blades
    private Material weightBladeMaterial;
    private Material biasBladeMaterial;
    
    // Track if we need to select new neurons
    private bool selectingNextWeight = false;
    private bool selectingNextBias = false;

    [Header("Collision Detection")]
    public float collisionRadius = 0.1f;
    public LayerMask dataPointLayer;
    private DataPoint lastHitDataPoint;

    [Header("Weapon UI")]
    public TextMeshPro weightValueText;   // attach to weight blade
    public TextMeshPro biasValueText;     // attach to bias blade


    private void Start()
    {
        // Create material instances for the blades
        if (weightBlade != null && bladeMaterial != null)
        {
            Renderer weightRenderer = weightBlade.GetComponent<Renderer>();
            if (weightRenderer != null)
            {
                weightBladeMaterial = new Material(bladeMaterial);
                weightRenderer.material = weightBladeMaterial;
            }
        }
        
        if (biasBlade != null && bladeMaterial != null)
        {
            Renderer biasRenderer = biasBlade.GetComponent<Renderer>();
            if (biasRenderer != null)
            {
                biasBladeMaterial = new Material(bladeMaterial);
                biasRenderer.material = biasBladeMaterial;
            }
        }
        
        // Subscribe to neural network epoch change event if available
        if (neuralNetwork != null)
        {
            neuralNetwork.OnWeightsUpdated += UpdateWeaponValues;
        }
        
        // Initialize values
        UpdateWeaponValues();
    }
    
    private void Update()
    {
        // FIXED: Add SteamVR safety check to prevent corruption during scene transitions
        if (!IsSteamVRReady())
        {
            return;
        }
        
        // Handle weight selection (left hand)
        /*if (selectNextWeight.GetStateDown(leftHandType))
        {
            SelectNextWeight();
        }
        
        // Handle bias selection (right hand)
        if (selectNextBias.GetStateDown(rightHandType))
        {
            SelectNextBias();
        }*/
        
        // Update visualization based on current values
        UpdateBladeVisualization();

        // Check for collisions with data points
        CheckWeaponCollisions();
    }

    /// <summary>
    /// Check if SteamVR system is ready for input
    /// </summary>
    bool IsSteamVRReady()
    {
        // Check if SteamVR is initialized and actions are available
        return SteamVR_Actions.default_GrabPinch != null && 
               SteamVR_Actions.default_Haptic != null;
    }
    
    // Called when neural network updates weights/biases
    private void UpdateWeaponValues()
    {
        if (neuralNetwork == null) return;
        
        // Get the selected weight
        if (neuralNetwork.weights != null && 
            selectedLayer > 0 && selectedLayer < neuralNetwork.weights.Length + 1)
        {
            int layerIndex = selectedLayer - 1; // Adjust for 0-indexing and skipping input layer
            
            if (selectedNeuron < neuralNetwork.layerSizes[selectedLayer] &&
                selectedInputNeuron < neuralNetwork.layerSizes[selectedLayer - 1])
            {
                int weightIndex = selectedNeuron * neuralNetwork.layerSizes[selectedLayer - 1] + selectedInputNeuron;
                
                if (weightIndex < neuralNetwork.weights[layerIndex].Length)
                {
                    currentWeightValue = (float)neuralNetwork.weights[layerIndex][weightIndex];
                }
            }
        }
        
        // Get the selected bias
        if (neuralNetwork.biases != null && 
            selectedLayer > 0 && selectedLayer < neuralNetwork.biases.Length + 1)
        {
            int layerIndex = selectedLayer - 1; // Adjust for 0-indexing and skipping input layer
            
            if (selectedNeuron < neuralNetwork.biases[layerIndex].Length)
            {
                currentBiasValue = (float)neuralNetwork.biases[layerIndex][selectedNeuron];
            }
        }

        if (weightValueText != null)
        {
            weightValueText.text = $"W = {currentWeightValue:F2}";
        }
        if (biasValueText != null)
        {
            biasValueText.text = $"B = {currentBiasValue:F2}";
        }
    }
    
    // Select the next weight in the network
    public void SelectNextWeight()
    {
        if (neuralNetwork == null) return;
        
        // Increment the input neuron first
        selectedInputNeuron++;
        
        // If we reached the end of inputs for the current neuron
        if (selectedLayer > 0 && selectedInputNeuron >= neuralNetwork.layerSizes[selectedLayer - 1])
        {
            selectedInputNeuron = 0;
            selectedNeuron++;
            
            // If we reached the end of neurons in this layer
            if (selectedLayer < neuralNetwork.layerSizes.Length && 
                selectedNeuron >= neuralNetwork.layerSizes[selectedLayer])
            {
                selectedNeuron = 0;
                selectedLayer++;
                
                // If we reached the end of layers, wrap around to first hidden layer (skip input)
                if (selectedLayer >= neuralNetwork.layerSizes.Length)
                {
                    selectedLayer = 1; // First hidden layer
                }
            }
        }
        
        // Update the weapon value
        UpdateWeaponValues();
        
        Debug.Log($"Selected Weight: Layer {selectedLayer}, Neuron {selectedNeuron}, Input {selectedInputNeuron}, Value: {currentWeightValue}");
    }
    
    // Select the next bias in the network
    public void SelectNextBias()
    {
        if (neuralNetwork == null) return;
        
        // Increment the neuron first
        selectedNeuron++;
        
        // If we reached the end of neurons in this layer
        if (selectedLayer < neuralNetwork.layerSizes.Length && 
            selectedNeuron >= neuralNetwork.layerSizes[selectedLayer])
        {
            selectedNeuron = 0;
            selectedLayer++;
            
            // If we reached the end of layers, wrap around to first hidden layer (skip input)
            if (selectedLayer >= neuralNetwork.layerSizes.Length)
            {
                selectedLayer = 1; // First hidden layer
            }
        }
        
        // Update the weapon value
        UpdateWeaponValues();
        
        Debug.Log($"Selected Bias: Layer {selectedLayer}, Neuron {selectedNeuron}, Value: {currentBiasValue}");
    }
    
    private void UpdateBladeVisualization()
    {
        // Update weight blade visualization
        if (weightBladeMaterial != null)
        {
            UpdateMaterialColor(weightBladeMaterial, currentWeightValue);
        }
        
        // Update bias blade visualization
        if (biasBladeMaterial != null)
        {
            UpdateMaterialColor(biasBladeMaterial, currentBiasValue);
        }
    }
    
    private void UpdateMaterialColor(Material material, float value)
    {
        // Determine color based on value sign
        Color baseColor = value >= 0 ? positiveValueColor : negativeValueColor;
        
        // Calculate brightness based on absolute value
        float absValue = Mathf.Abs(value);
        float normalizedValue = Mathf.Clamp01(absValue / maxValueVisualization);
        float brightness;
        
        if (value >= 0)
        {
            // For positive values, higher value = brighter
            brightness = Mathf.Lerp(minBrightness, maxBrightness, normalizedValue);
        }
        else
        {
            // For negative values, more negative = darker
            brightness = Mathf.Lerp(maxBrightness, minBrightness, normalizedValue);
        }
        
        // Apply color and brightness to emission
        Color emissionColor = baseColor * brightness * 2.0f;
        material.SetColor("_EmissionColor", emissionColor);
        material.EnableKeyword("_EMISSION");
        
        // Also update the main color for blades without emission
        material.color = baseColor * brightness;
    }
    
    // This method allows manually setting the weapon values (for testing or UI control)
    public void SetWeaponValues(float weightValue, float biasValue)
    {
        currentWeightValue = weightValue;
        currentBiasValue = biasValue;
        UpdateBladeVisualization();
    }

    // Add haptic feedback method
    public void TriggerHapticFeedback(SteamVR_Input_Sources handType, float duration = 0.1f, float frequency = 100f, float amplitude = 0.5f)
    {
        try
        {
            if (handType == SteamVR_Input_Sources.LeftHand && leftHandPose != null)
            {
                SteamVR_Actions.default_Haptic.Execute(0, duration, frequency, amplitude, leftHandType);
            }
            else if (handType == SteamVR_Input_Sources.RightHand && rightHandPose != null)
            {
                SteamVR_Actions.default_Haptic.Execute(0, duration, frequency, amplitude, rightHandType);
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"Error triggering haptic feedback: {ex.Message}");
        }
    }

    private void CheckWeaponCollisions()
    {
        // Check weight blade collisions with data points
        if (weightBlade != null)
        {
            Collider[] weightColliders = Physics.OverlapSphere(weightBlade.transform.position, collisionRadius, dataPointLayer);
            foreach (Collider col in weightColliders)
            {
                DataPoint dataPoint = col.GetComponent<DataPoint>();
                if (dataPoint != null && !dataPoint.hasBeenHitByWeight)
                {
                    // Get blade direction
                    Vector3 bladeDirection = weightBlade.transform.forward;
                    dataPoint.HandleWeightBladeHit(currentWeightValue, bladeDirection);
                    TriggerHapticFeedback(leftHandType);
                }
            }
        }

        // Check bias blade collisions with data points
        if (biasBlade != null)
        {
            Collider[] biasColliders = Physics.OverlapSphere(biasBlade.transform.position, collisionRadius, dataPointLayer);
            foreach (Collider col in biasColliders)
            {
                DataPoint dataPoint = col.GetComponent<DataPoint>();
                if (dataPoint != null && !dataPoint.hasBeenHitByBias)
                {
                    // Get blade direction
                    Vector3 bladeDirection = biasBlade.transform.forward;
                    dataPoint.HandleBiasBladeHit(currentBiasValue, bladeDirection);
                    TriggerHapticFeedback(rightHandType);
                }
            }
        }

    }

    public void OnNeuronSelected(int layerIndex, int neuronIndex)
    {
        // Update the weapon system’s selected neuron
        selectedLayer = layerIndex;
        selectedNeuron = neuronIndex;
        selectedInputNeuron = 0; // start at first input, or allow cycling later

        UpdateWeaponValues(); // refresh currentWeightValue / currentBiasValue

        Debug.Log($"[WeaponSystem] Neuron selected: Layer {layerIndex}, Neuron {neuronIndex}, W={currentWeightValue:F2}, B={currentBiasValue:F2}");
    }

    private void OnEnable()
    {
        // Find the NetworkVis in the scene
        NetworkVis vis = FindObjectOfType<NetworkVis>();
        if (vis != null)
        {
            vis.OnNeuronSelected.AddListener(OnNeuronSelected);
            Debug.Log("[WeaponSystem] Subscribed to NetworkVis.OnNeuronSelected");
        }
    }

    private void OnDisable()
    {
        // Clean up to avoid duplicate subscriptions when reloading scenes
        NetworkVis vis = FindObjectOfType<NetworkVis>();
        if (vis != null)
        {
            vis.OnNeuronSelected.RemoveListener(OnNeuronSelected);
            Debug.Log("[WeaponSystem] Unsubscribed from NetworkVis.OnNeuronSelected");
        }
    }


} 