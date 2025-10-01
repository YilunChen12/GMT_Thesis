using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System.Collections;
using System; // Added for Array.Copy

public class BackpropagationManager : MonoBehaviour
{
    [Header("Scene Management")]
    public string backpropagationSceneName = "BackpropagationScene";
    public string forwardPropagationSceneName = "OpenningScene"; // NEW: Forward propagation scene name
    
    [Header("Network Data")]
    public NeuralNetwork neuralNetwork;
    public int targetLayerIndex = -1;
    public int targetNeuronIndex = -1;
    
    [Header("Golf System")]
    public ParameterBoxManager parameterBoxManager;
    public SlingshotController slingshotController;
    public HandCanvasUI handCanvasUI;
    
    [Header("Current Parameters")]
    [SerializeField] private float currentW3 = 0f;  // Weight from Hidden Neuron 1 to Output
    [SerializeField] private float currentW4 = 0f;  // Weight from Hidden Neuron 2 to Output  
    [SerializeField] private float currentB5 = 0f;  // Bias of Output Neuron
    
    [Header("Surface Settings")]
    public Vector2 weightRange = new Vector2(-3f, 3f);  // Range for w3 and w4
    public Vector2 biasRange = new Vector2(-2f, 2f);    // Range for b5
    public int surfaceResolution = 50;
    
    [Header("Adaptive Range Settings")]
    [Tooltip("Enable adaptive parameter ranges based on actual parameter values")]
    public bool useAdaptiveRanges = true;
    [Tooltip("Minimum expansion factor around parameter values (e.g., 3.0 means range will be at least 3x the parameter spread)")]
    public float minimumExpansionFactor = 3.0f;
    [Tooltip("Padding factor to add around parameters (as percentage of spread)")]
    public float paddingFactor = 0.5f;
    [Tooltip("Minimum range size to prevent too-small ranges")]
    public float minimumRangeSize = 0.2f;
    
    // Data persistence between scenes
    private static BackpropagationData s_PersistentData;
    
    [System.Serializable]
    public class BackpropagationData
    {
        public NeuralNetwork networkReference; // Direct reference to the network
        public int layerIndex;
        public int neuronIndex;
        
        // Optimal parameters (after training)
        public float optimalW3, optimalW4, optimalB5;
        
        // Pre-epoch parameters (starting position for ball - parameters before current epoch's training)
        public float preEpochW3, preEpochW4, preEpochB5;

        // Chosen parameters for continuing in Scene 1
        public float chosenW3, chosenW4, chosenB5;
        public int currentEpoch = 0;
        public bool isReturningFromBackprop = false;
        
        // CRITICAL FIX: Store ALL trained network parameters to preserve training progress
        [System.Serializable]
        public class CompleteNetworkState
        {
            public double[][] trainedWeights;  // All layer weights after training
            public double[][] trainedBiases;   // All layer biases after training
            public double[][] preEpochWeights; // All layer weights before current epoch
            public double[][] preEpochBiases;  // All layer biases before current epoch
        }
        
        public CompleteNetworkState networkState = new CompleteNetworkState();
    }
    
    public static BackpropagationManager Instance { get; private set; }
    
    void Awake()
    {
        // Singleton pattern implementation
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            
            // Subscribe to scene loaded events to detect when BackpropagationScene is loaded
            SceneManager.sceneLoaded += OnSceneLoaded;
            Debug.Log($"BackpropagationManager singleton initialized: {gameObject.name}");
        }
        else if (Instance != this)
        {
            Debug.Log($"Destroying duplicate BackpropagationManager: {gameObject.name}, keeping singleton: {Instance.gameObject.name}");
            Destroy(gameObject);
            return;
        }
    }
    
    void Start()
    {
        // Don't initialize here since we might be in scene 1
        // Wait for scene transition event instead
        Debug.Log($"BackpropagationManager Start() - Current scene: {SceneManager.GetActiveScene().name}");
        Debug.Log($"Has persistent data: {s_PersistentData != null}");
    }
    
    /// <summary>
    /// Called when any scene is loaded
    /// </summary>
    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Debug.Log($"=== SCENE LOADED: {scene.name} ===");
        Debug.Log($"Has persistent data: {s_PersistentData != null}");
        Debug.Log($"Is BackpropagationScene: {scene.name == backpropagationSceneName}");
        
        if (s_PersistentData != null)
        {
            Debug.Log($"Network reference exists: {s_PersistentData.networkReference != null}");
            if (s_PersistentData.networkReference != null)
            {
                Debug.Log($"Network name: {s_PersistentData.networkReference.name}");
                Debug.Log($"Network active: {s_PersistentData.networkReference.gameObject.activeInHierarchy}");
                Debug.Log($"Network has weights: {s_PersistentData.networkReference.weights != null}");
                Debug.Log($"Network has training data: {s_PersistentData.networkReference.trainingData?.Count ?? 0} samples");
            }
        }
        
        // Only initialize if we're in the backpropagation scene and have data from previous scene
        if (scene.name == backpropagationSceneName && s_PersistentData != null)
        {
            Debug.Log("BackpropagationScene detected with persistent data - starting delayed initialization...");
            
            // Use coroutine to ensure all objects in the new scene are fully loaded
            StartCoroutine(DelayedInitialization());
        }
    }
    
    /// <summary>
    /// Delayed initialization to ensure all scene objects are loaded
    /// </summary>
    System.Collections.IEnumerator DelayedInitialization()
    {
        Debug.Log("=== DELAYED INITIALIZATION START ===");
        
        // Wait a frame to ensure all scene objects are instantiated
        yield return new WaitForEndOfFrame();
        
        // Wait another frame to be extra sure
        yield return new WaitForEndOfFrame();
        
        Debug.Log("Scene objects should be loaded now, proceeding with initialization...");
        
        // Initialize the scene (network data will be loaded within this method)
        InitializeBackpropagationScene();
        
        // Show backpropagation starting instruction panel
        ShowBackpropagationStartingInstructions();
        
        Debug.Log("=== DELAYED INITIALIZATION COMPLETE ===");
    }

    /// <summary>
    /// Show the backpropagation starting instruction panel
    /// </summary>
    void ShowBackpropagationStartingInstructions()
    {
        // Wait a bit more to ensure InstructionUIManager is fully initialized
        StartCoroutine(DelayedInstructionDisplay());
    }

    /// <summary>
    /// Delayed instruction display to ensure InstructionUIManager is ready
    /// </summary>
    System.Collections.IEnumerator DelayedInstructionDisplay()
    {
        // Wait for InstructionUIManager to be ready
        float maxWaitTime = 3.0f;
        float elapsedTime = 0f;
        
        while (InstructionUIManager.Instance == null && elapsedTime < maxWaitTime)
        {
            yield return new WaitForSeconds(0.1f);
            elapsedTime += 0.1f;
        }
        
        if (InstructionUIManager.Instance != null)
        {
            Debug.Log("Showing backpropagation starting instructions...");
            
            // Ensure canvas is visible and show the backprop starting panel
            InstructionUIManager.Instance.ShowInstructionCanvas();
            InstructionUIManager.Instance.OnBackpropagationSceneEntered();
        }
        else
        {
            Debug.LogWarning("InstructionUIManager not found after waiting - cannot show backprop starting instructions");
        }
    }
    
    void OnDestroy()
    {
        // Unsubscribe from events to prevent memory leaks
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }
    
    /// <summary>
    /// Called from Stage1Manager when transitioning to backpropagation
    /// </summary>
    public static void TransitionToBackpropagation(NeuralNetwork network, int layerIndex, int neuronIndex)
    {
        Debug.Log("=== STARTING TRANSITION TO BACKPROPAGATION ===");
        Debug.Log($"Network: {network?.name}, Layer: {layerIndex}, Neuron: {neuronIndex}");
        
        // Use the singleton - no need to manage DontDestroyOnLoad manually
        if (NeuralNetwork.Instance == null)
        {
            Debug.LogError("Cannot transition: NeuralNetwork singleton not found!");
            return;
        }
        
        var neuralNetworkSingleton = NeuralNetwork.Instance;
        Debug.Log($"Using NeuralNetwork singleton: {neuralNetworkSingleton.name}");
        
        // Get current parameters (optimal - after training) 
        // NOTE: Training should have just completed in Stage1Manager before calling this method
        int lastLayerIndex = neuralNetworkSingleton.weights.Length - 1;
        float optimalW3 = (float)neuralNetworkSingleton.weights[lastLayerIndex][0];
        float optimalW4 = (float)neuralNetworkSingleton.weights[lastLayerIndex][1];
        float optimalB5 = (float)neuralNetworkSingleton.biases[lastLayerIndex][0];
        
        // FIXED: Get pre-epoch parameters (parameters before current epoch's training) for ball starting position
        float preEpochW3, preEpochW4, preEpochB5;
        neuralNetworkSingleton.GetPreEpochLastLayerParameters(out preEpochW3, out preEpochW4, out preEpochB5);

        Debug.Log($"=== PARAMETER COMPARISON FOR BACKPROPAGATION ===");
        Debug.Log($"Last layer index: {lastLayerIndex}");
        Debug.Log($"Pre-epoch parameters (ball start): W3={preEpochW3:F3}, W4={preEpochW4:F3}, B5={preEpochB5:F3}");
        Debug.Log($"Optimal parameters (green curve): W3={optimalW3:F3}, W4={optimalW4:F3}, B5={optimalB5:F3}");
        
        // Verify that the parameters are actually different
        bool parametersAreDifferent = (Mathf.Abs(preEpochW3 - optimalW3) > 0.001f || 
                                     Mathf.Abs(preEpochW4 - optimalW4) > 0.001f || 
                                     Mathf.Abs(preEpochB5 - optimalB5) > 0.001f);
        
        if (parametersAreDifferent)
        {
            Debug.Log("✅ GOOD: Pre-epoch and optimal parameters are different - curves should not overlap!");
        }
        else
        {
            Debug.LogWarning("⚠️ WARNING: Pre-epoch and optimal parameters are the same - curves will overlap!");
        }
        
        // CRITICAL FIX: Capture ALL network parameters to preserve training progress
        var completeNetworkState = new BackpropagationData.CompleteNetworkState();
        
        // Deep copy all trained weights and biases (after training)
        completeNetworkState.trainedWeights = new double[neuralNetworkSingleton.weights.Length][];
        completeNetworkState.trainedBiases = new double[neuralNetworkSingleton.biases.Length][];
        
        for (int i = 0; i < neuralNetworkSingleton.weights.Length; i++)
        {
            completeNetworkState.trainedWeights[i] = new double[neuralNetworkSingleton.weights[i].Length];
            Array.Copy(neuralNetworkSingleton.weights[i], completeNetworkState.trainedWeights[i], neuralNetworkSingleton.weights[i].Length);
            
            completeNetworkState.trainedBiases[i] = new double[neuralNetworkSingleton.biases[i].Length];
            Array.Copy(neuralNetworkSingleton.biases[i], completeNetworkState.trainedBiases[i], neuralNetworkSingleton.biases[i].Length);
        }
        
        // Deep copy all pre-epoch weights and biases (before training)
        completeNetworkState.preEpochWeights = new double[neuralNetworkSingleton.preEpochWeights.Length][];
        completeNetworkState.preEpochBiases = new double[neuralNetworkSingleton.preEpochBiases.Length][];
        
        for (int i = 0; i < neuralNetworkSingleton.preEpochWeights.Length; i++)
        {
            completeNetworkState.preEpochWeights[i] = new double[neuralNetworkSingleton.preEpochWeights[i].Length];
            Array.Copy(neuralNetworkSingleton.preEpochWeights[i], completeNetworkState.preEpochWeights[i], neuralNetworkSingleton.preEpochWeights[i].Length);
            
            completeNetworkState.preEpochBiases[i] = new double[neuralNetworkSingleton.preEpochBiases[i].Length];
            Array.Copy(neuralNetworkSingleton.preEpochBiases[i], completeNetworkState.preEpochBiases[i], neuralNetworkSingleton.preEpochBiases[i].Length);
        }
        
        Debug.Log($"=== CAPTURED COMPLETE NETWORK STATE ===");
        Debug.Log($"Layers captured: {completeNetworkState.trainedWeights.Length}");
        for (int i = 0; i < completeNetworkState.trainedWeights.Length; i++)
        {
            Debug.Log($"Layer {i}: {completeNetworkState.trainedWeights[i].Length} weights, {completeNetworkState.trainedBiases[i].Length} biases");
            
            // Log first few parameters for verification
            if (completeNetworkState.trainedWeights[i].Length > 0)
            {
                Debug.Log($"  Trained weights[0]: {completeNetworkState.trainedWeights[i][0]:F3}");
                Debug.Log($"  Pre-epoch weights[0]: {completeNetworkState.preEpochWeights[i][0]:F3}");
            }
            if (completeNetworkState.trainedBiases[i].Length > 0)
            {
                Debug.Log($"  Trained bias[0]: {completeNetworkState.trainedBiases[i][0]:F3}");
                Debug.Log($"  Pre-epoch bias[0]: {completeNetworkState.preEpochBiases[i][0]:F3}");
            }
        }
        
        // Preserve data for backpropagation parameters
        s_PersistentData = new BackpropagationData
        {
            networkReference = neuralNetworkSingleton, // Use singleton reference
            layerIndex = layerIndex,
            neuronIndex = neuronIndex,
            
            // Optimal parameters (after training) - for green curve
            optimalW3 = optimalW3,
            optimalW4 = optimalW4,
            optimalB5 = optimalB5,
            
            // Pre-epoch parameters (starting position) - for ball initial position
            preEpochW3 = preEpochW3,
            preEpochW4 = preEpochW4,
            preEpochB5 = preEpochB5,
            
            // Complete network state for preservation
            networkState = completeNetworkState
        };
        
        Debug.Log($"Optimal parameters (goal): W3={optimalW3:F3}, W4={optimalW4:F3}, B5={optimalB5:F3}");
        Debug.Log($"Pre-epoch parameters (start): W3={preEpochW3:F3}, W4={preEpochW4:F3}, B5={preEpochB5:F3}");
        
        // Load backpropagation scene
        SceneManager.LoadScene("BackpropagationScene");
    }
    
    private void InitializeBackpropagationScene()
    {
        Debug.Log("=== INITIALIZING BACKPROPAGATION SCENE ===");
        
        // Use the singleton neural network
        if (NeuralNetwork.Instance != null)
        {
            neuralNetwork = NeuralNetwork.Instance;
            Debug.Log($"Using NeuralNetwork singleton: {neuralNetwork.name}");
            
            // IMPORTANT: Set current parameters to PRE-EPOCH values (not optimal)
            // This is where the ball will start - at the parameters before current epoch's training
            if (s_PersistentData != null)
            {
                currentW3 = s_PersistentData.preEpochW3;
                currentW4 = s_PersistentData.preEpochW4;
                currentB5 = s_PersistentData.preEpochB5;
                
                if (neuralNetwork.weights != null && neuralNetwork.biases != null)
                {
                    int lastLayerIndex = neuralNetwork.weights.Length - 1;
                    neuralNetwork.weights[lastLayerIndex][0] = currentW3;
                    neuralNetwork.weights[lastLayerIndex][1] = currentW4;
                    neuralNetwork.biases[lastLayerIndex][0] = currentB5;
                    Debug.Log($"Applied pre-epoch parameters to neural network: W3={currentW3:F3}, W4={currentW4:F3}, B5={currentB5:F3}");
                }
                
                targetLayerIndex = s_PersistentData.layerIndex;
                targetNeuronIndex = s_PersistentData.neuronIndex;
                
                Debug.Log($"Ball starting parameters (pre-epoch): W3={currentW3:F3}, W4={currentW4:F3}, B5={currentB5:F3}");
                Debug.Log($"Optimal parameters (goal): W3={s_PersistentData.optimalW3:F3}, W4={s_PersistentData.optimalW4:F3}, B5={s_PersistentData.optimalB5:F3}");
                Debug.Log($"Training data count: {neuralNetwork.trainingData?.Count ?? 0}");
                
                // Calculate adaptive parameter ranges for better visual resolution
                CalculateAdaptiveParameterRanges();
            }
        }
        else
        {
            Debug.LogError("No NeuralNetwork singleton found!");
        }
        
        // Find components if not assigned (since we're in a new scene)
        Debug.Log("Step 1: Finding ParameterBoxManager...");
        if (parameterBoxManager == null)
        {
            parameterBoxManager = FindObjectOfType<ParameterBoxManager>();
            Debug.Log($"ParameterBoxManager search result: {(parameterBoxManager != null ? $"Found - {parameterBoxManager.name}" : "NOT FOUND")}");
        }
        else
        {
            Debug.Log("ParameterBoxManager already assigned");
        }
        
        Debug.Log("Step 2: Finding SlingshotController...");
        if (slingshotController == null)
        {
            slingshotController = FindObjectOfType<SlingshotController>();
            Debug.Log($"SlingshotController search result: {(slingshotController != null ? $"Found - {slingshotController.name}" : "NOT FOUND")}");
        }
        else
        {
            Debug.Log("SlingshotController already assigned");
        }
        
        Debug.Log("Step 3: Finding HandCanvasUI...");
        if (handCanvasUI == null)
        {
            handCanvasUI = FindObjectOfType<HandCanvasUI>();
            Debug.Log($"HandCanvasUI search result: {(handCanvasUI != null ? $"Found - {handCanvasUI.name}" : "NOT FOUND")}");
        }
        else
        {
            Debug.Log("HandCanvasUI already assigned");
        }
        
        // Initialize parameter box manager
        if (parameterBoxManager != null && neuralNetwork != null)
        {
            Debug.Log("Step 4: Initializing parameter box...");
            parameterBoxManager.Initialize(this);
            Debug.Log("Parameter box initialization completed");
        }
        else
        {
            Debug.LogError("CANNOT INITIALIZE PARAMETER BOX: Missing ParameterBoxManager or NeuralNetwork!");
        }
        
        // Initialize slingshot controller
        if (slingshotController != null)
        {
            Debug.Log("Step 5: Initializing slingshot controller...");
            slingshotController.Initialize(this);
            slingshotController.AttachToLeftHand();
            
            if (parameterBoxManager != null)
            {
                // IMPORTANT: Position ball at PRE-TRAINING parameters, not optimal
                Vector3 ballPosition = ParametersToWorldPosition(currentW3, currentW4, currentB5);
                slingshotController.SetBallPosition(ballPosition);
                Debug.Log($"Ball positioned at PRE-TRAINING parameters: {ballPosition}");
            }
            Debug.Log("Slingshot controller initialization completed");
        }
        else
        {
            Debug.LogError("CANNOT INITIALIZE: SlingshotController not found!");
        }
        
        // Initialize hand canvas UI and attach to hand (with delay)
        if (handCanvasUI != null)
        {
            Debug.Log("Step 6: Initializing hand canvas UI...");
            handCanvasUI.Initialize(this);
            
            // Update plot ranges adaptively to match the new parameter ranges
            handCanvasUI.UpdatePlotRangesAdaptively();
            
            // Delay hand attachment to ensure VR hands are fully loaded
            StartCoroutine(DelayedHandAttachment());
            Debug.Log("Hand canvas UI initialization completed");
        }
        
        // Notify SlingshotController about current learning rate
        NotifySlingshotControllerOfLearningRate();
    }
    
    /// <summary>
    /// Notify SlingshotController about current learning rate for movement calculations
    /// </summary>
    void NotifySlingshotControllerOfLearningRate()
    {
        if (slingshotController == null) return;
        
        NeuralNetwork neuralNetwork = NeuralNetwork.Instance;
        if (neuralNetwork != null)
        {
            float currentLearningRate = neuralNetwork.GetCurrentLearningRate();
            Debug.Log($"Notifying SlingshotController of current learning rate: {currentLearningRate:F4}");
            
            // The SlingshotController will automatically read the learning rate when needed
            // This is just for logging purposes
        }
    }
    
    /// <summary>
    /// Evaluate player's parameter choice and reward them with learning rate increase
    /// Call this when transitioning from backpropagation scene back to forward propagation
    /// </summary>
    public void EvaluatePlayerParameterChoice()
    {
        if (s_PersistentData == null)
        {
            Debug.LogWarning("Cannot evaluate player choice - no persistent data available");
            return;
        }
        
        NeuralNetwork neuralNetwork = NeuralNetwork.Instance;
        if (neuralNetwork == null)
        {
            Debug.LogWarning("Cannot evaluate player choice - NeuralNetwork not found");
            return;
        }
        
        // Get current parameters (player's final choice in backpropagation scene)
        float playerW3 = s_PersistentData.chosenW3;
        float playerW4 = s_PersistentData.chosenW4;
        float playerB5 = s_PersistentData.chosenB5;
        
        // Get optimal parameters
        float optimalW3 = s_PersistentData.optimalW3;
        float optimalW4 = s_PersistentData.optimalW4;
        float optimalB5 = s_PersistentData.optimalB5;
        
        Debug.Log($"=== EVALUATING PLAYER PARAMETER CHOICE ===");
        Debug.Log($"Player final choice: W3={playerW3:F3}, W4={playerW4:F3}, B5={playerB5:F3}");
        Debug.Log($"Optimal parameters: W3={optimalW3:F3}, W4={optimalW4:F3}, B5={optimalB5:F3}");
        
        // Evaluate player choice and potentially reward with learning rate increase
        neuralNetwork.EvaluatePlayerChoice(playerW3, playerW4, playerB5, optimalW3, optimalW4, optimalB5);
        
        // Update the neural network with player's chosen parameters
        neuralNetwork.ApplyLastLayerParameters(playerW3, playerW4, playerB5, true);
        
        Debug.Log($"Player choice evaluation complete - learning rate may have been increased!");
    }
    
    /// <summary>
    /// Get current reward system information for UI display
    /// </summary>
    public string GetPlayerRewardInfo()
    {
        NeuralNetwork neuralNetwork = NeuralNetwork.Instance;
        if (neuralNetwork == null) return "Neural Network not found";
        
        return neuralNetwork.GetPlayerRewardInfo();
    }
    
    /// <summary>
    /// Update network parameters and trigger surface/UI updates
    /// </summary>
    public void UpdateParameters(float w3, float w4, float b5)
    {
        currentW3 = Mathf.Clamp(w3, weightRange.x, weightRange.y);
        currentW4 = Mathf.Clamp(w4, weightRange.x, weightRange.y);
        currentB5 = Mathf.Clamp(b5, biasRange.x, biasRange.y);
        
        // Update actual network parameters
        if (neuralNetwork != null && neuralNetwork.weights != null && neuralNetwork.biases != null)
        {
            int lastLayerIndex = neuralNetwork.weights.Length - 1;
            neuralNetwork.weights[lastLayerIndex][0] = currentW3; // Weight from hidden neuron 1
            neuralNetwork.weights[lastLayerIndex][1] = currentW4; // Weight from hidden neuron 2
            neuralNetwork.biases[lastLayerIndex][0] = currentB5;  // Output bias
        }
        
        // Update parameter box visualization
        if (parameterBoxManager != null)
        {
            parameterBoxManager.UpdateHighDetailGrid(ParametersToWorldPosition(currentW3, currentW4, currentB5));
        }
        
        // Update UI displays
        if (handCanvasUI != null)
        {
            handCanvasUI.UpdateDisplays(currentW3, currentW4, currentB5);
        }
    }
    
    /// <summary>
    /// Delayed hand attachment for UI to ensure VR hands are fully loaded
    /// </summary>
    System.Collections.IEnumerator DelayedHandAttachment()
    {
        Debug.Log("Starting delayed hand attachment for UI...");
        
        // Wait a bit more for VR hands to be fully initialized
        yield return new WaitForSeconds(1f);
        
        if (handCanvasUI != null)
        {
            // Use scene-specific VR player manager to find hands
            VRPlayerManager vrPlayerManager = FindObjectOfType<VRPlayerManager>();
            if (vrPlayerManager != null && vrPlayerManager.IsBackpropagationVRPlayer())
            {
                Debug.Log("Found backpropagation scene VR player for UI attachment");
                handCanvasUI.AttachToHand();
            }
            else
            {
                Debug.LogWarning("VR Player Manager not found or not configured for backpropagation scene");
                handCanvasUI.AttachToHand(); // Try anyway with fallback detection
            }
        }
    }
    
    /// <summary>
    /// Calculate loss for given parameters
    /// </summary>
    public float CalculateLoss(float w3, float w4, float b5)
    {
        // Use singleton if local reference is null
        var networkToUse = neuralNetwork ?? NeuralNetwork.Instance;
        if (networkToUse == null || networkToUse.trainingData == null) return 0f;
        
        // Temporarily set network parameters
        double originalW3 = networkToUse.weights[networkToUse.weights.Length - 1][0];
        double originalW4 = networkToUse.weights[networkToUse.weights.Length - 1][1];
        double originalB5 = networkToUse.biases[networkToUse.biases.Length - 1][0];
        
        networkToUse.weights[networkToUse.weights.Length - 1][0] = w3;
        networkToUse.weights[networkToUse.weights.Length - 1][1] = w4;
        networkToUse.biases[networkToUse.biases.Length - 1][0] = b5;
        
        // Calculate loss
        float totalLoss = 0f;
        foreach (var data in networkToUse.trainingData)
        {
            double[] output = networkToUse.Forward(data.inputs, isTraining: true);
            for (int i = 0; i < output.Length; i++)
            {
                double error = output[i] - data.targets[i];
                totalLoss += (float)(0.5 * error * error);
            }
        }
        float avgLoss = totalLoss / networkToUse.trainingData.Count;
        
        // Restore original parameters
        networkToUse.weights[networkToUse.weights.Length - 1][0] = originalW3;
        networkToUse.weights[networkToUse.weights.Length - 1][1] = originalW4;
        networkToUse.biases[networkToUse.biases.Length - 1][0] = originalB5;
        
        return avgLoss;
    }
    
    /// <summary>
    /// Convert parameter values to world position in 3D parameter box
    /// </summary>
    public Vector3 ParametersToWorldPosition(float w3, float w4, float b5)
    {
        Debug.Log($"=== CONVERTING PARAMETERS TO WORLD POSITION ===");
        Debug.Log($"Input parameters: w3={w3:F3}, w4={w4:F3}, b5={b5:F3}");
        Debug.Log($"Weight range: {weightRange.x} to {weightRange.y}");
        Debug.Log($"Bias range: {biasRange.x} to {biasRange.y}");
        
        if (parameterBoxManager == null)
        {
            Debug.LogError("ParameterBoxManager is null! Using default position.");
            // Return a reasonable default position
            return new Vector3(0f, 2f, 0f);
        }
        
        Vector3 worldPos = parameterBoxManager.ParametersToWorldPosition(w3, w4, b5);
        
        Debug.Log($"Final world position: {worldPos}");
        
        return worldPos;
    }
    
    /// <summary>
    /// Convert world position to parameter values
    /// </summary>
    public Vector3 WorldPositionToParameters(Vector3 worldPos)
    {
        if (parameterBoxManager == null)
        {
            Debug.LogError("ParameterBoxManager is null! Cannot convert world position to parameters.");
            return Vector3.zero;
        }
        
        return parameterBoxManager.WorldPositionToParameters(worldPos);
    }
    
    // Helper methods removed - no longer needed with DontDestroyOnLoad approach
    
    // Getters for current state
    public float CurrentW3 => currentW3;
    public float CurrentW4 => currentW4;
    public float CurrentB5 => currentB5;
    public Vector2 WeightRange => weightRange;
    public Vector2 BiasRange => biasRange;
    public List<NeuralNetwork.TrainingData> TrainingData => neuralNetwork?.trainingData;
    
    // Getters for optimal parameters (for UI to access)
    public float OptimalW3 => s_PersistentData?.optimalW3 ?? 0f;
    public float OptimalW4 => s_PersistentData?.optimalW4 ?? 0f;
    public float OptimalB5 => s_PersistentData?.optimalB5 ?? 0f;
    public bool HasOptimalParameters => s_PersistentData != null;

    /// <summary>
    /// Check if we're returning from backpropagation scene
    /// </summary>
    public static bool IsReturningFromBackpropagation()
    {
        return s_PersistentData?.isReturningFromBackprop ?? false;
    }
    
    /// <summary>
    /// Get persistent network reference for Scene 1 to use
    /// DEPRECATED: Use NeuralNetwork.Instance instead
    /// </summary>
    public static NeuralNetwork GetPersistentNetwork()
    {
        Debug.LogWarning("GetPersistentNetwork() is deprecated. Use NeuralNetwork.Instance instead.");
        return NeuralNetwork.Instance;
    }
    
    /// <summary>
    /// Get chosen parameters and epoch info for continuing in Scene 1
    /// </summary>
    public static void GetChosenParametersForNextEpoch(out float w3, out float w4, out float b5, out int epoch)
    {
        if (s_PersistentData != null)
        {
            w3 = s_PersistentData.chosenW3;
            w4 = s_PersistentData.chosenW4;
            b5 = s_PersistentData.chosenB5;
            epoch = s_PersistentData.currentEpoch;
            
            // Clear the return flag
            s_PersistentData.isReturningFromBackprop = false;
            
            Debug.Log($"Retrieved chosen parameters for epoch {epoch}: W3={w3:F3}, W4={w4:F3}, B5={b5:F3}");
        }
        else
        {
            w3 = w4 = b5 = 0f;
            epoch = 0;
            Debug.LogWarning("No persistent data found - using default values");
        }
    }
    
    /// <summary>
    /// Apply chosen parameters to the persistent neural network
    /// </summary>
    void ApplyChosenParametersToNetwork(float w3, float w4, float b5)
    {
        if (NeuralNetwork.Instance != null)
        {
            var network = NeuralNetwork.Instance;
            int lastLayerIndex = network.weights.Length - 1;
            
            // Update network parameters
            network.weights[lastLayerIndex][0] = w3;
            network.weights[lastLayerIndex][1] = w4;
            network.biases[lastLayerIndex][0] = b5;
            
            Debug.Log($"Applied chosen parameters to NeuralNetwork singleton: W3={w3:F3}, W4={w4:F3}, B5={b5:F3}");
        }
        else
        {
            Debug.LogError("Cannot apply parameters - NeuralNetwork singleton not found!");
        }
    }
    
    /// <summary>
    /// Transition back to forward propagation scene with chosen parameters from ball position
    /// Called when player completes backpropagation (e.g., via return button)
    /// </summary>
    public void TransitionToForwardPropagation()
    {
        Debug.Log("=== STARTING TRANSITION TO FORWARD PROPAGATION ===");
        
        if (NeuralNetwork.Instance == null)
        {
            Debug.LogError("Cannot transition: No NeuralNetwork singleton found!");
            return;
        }
        
        // Get current ball position and convert to parameters
        Vector3 chosenParameters = GetChosenParametersFromBallPosition();
        
        Debug.Log($"Player chose parameters: W3={chosenParameters.x:F3}, W4={chosenParameters.y:F3}, B5={chosenParameters.z:F3}");
        
        // CRITICAL FIX: Restore ALL trained network parameters, then apply player's choice for last layer
        var network = NeuralNetwork.Instance;
        
        if (s_PersistentData?.networkState != null)
        {
            Debug.Log("=== RESTORING COMPLETE NETWORK STATE ===");
            
            // Restore ALL trained parameters (preserves training progress for W1, W2, bias1, bias2)
            for (int i = 0; i < s_PersistentData.networkState.trainedWeights.Length; i++)
            {
                // Restore weights for this layer
                Array.Copy(s_PersistentData.networkState.trainedWeights[i], network.weights[i], s_PersistentData.networkState.trainedWeights[i].Length);
                
                // Restore biases for this layer  
                Array.Copy(s_PersistentData.networkState.trainedBiases[i], network.biases[i], s_PersistentData.networkState.trainedBiases[i].Length);
                
                Debug.Log($"Restored Layer {i}: {s_PersistentData.networkState.trainedWeights[i].Length} weights, {s_PersistentData.networkState.trainedBiases[i].Length} biases");
                
                // Log restored parameters for verification
                if (s_PersistentData.networkState.trainedWeights[i].Length > 0)
                {
                    Debug.Log($"  Restored weights[0]: {network.weights[i][0]:F3}");
                }
                if (s_PersistentData.networkState.trainedBiases[i].Length > 0)
                {
                    Debug.Log($"  Restored bias[0]: {network.biases[i][0]:F3}");
                }
            }
            
            // Also restore pre-epoch parameters for future use
            for (int i = 0; i < s_PersistentData.networkState.preEpochWeights.Length; i++)
            {
                Array.Copy(s_PersistentData.networkState.preEpochWeights[i], network.preEpochWeights[i], s_PersistentData.networkState.preEpochWeights[i].Length);
                Array.Copy(s_PersistentData.networkState.preEpochBiases[i], network.preEpochBiases[i], s_PersistentData.networkState.preEpochBiases[i].Length);
            }
            
            Debug.Log("✅ ALL network parameters restored from training state");
        }
        else
        {
            Debug.LogWarning("⚠️ No complete network state found - only last layer will be updated");
        }
        
        // NOW apply the player's chosen last layer parameters (this overrides the trained values for last layer only)
        int lastLayerIndex = network.weights.Length - 1;
        
        Debug.Log($"=== APPLYING PLAYER'S CHOSEN LAST LAYER PARAMETERS ===");
        Debug.Log($"Before: W3={network.weights[lastLayerIndex][0]:F3}, W4={network.weights[lastLayerIndex][1]:F3}, B5={network.biases[lastLayerIndex][0]:F3}");
        
        network.weights[lastLayerIndex][0] = chosenParameters.x;
        network.weights[lastLayerIndex][1] = chosenParameters.y;
        network.biases[lastLayerIndex][0] = chosenParameters.z;
        
        Debug.Log($"After: W3={network.weights[lastLayerIndex][0]:F3}, W4={network.weights[lastLayerIndex][1]:F3}, B5={network.biases[lastLayerIndex][0]:F3}");
        
        // Update persistent data for next epoch
        if (s_PersistentData == null) s_PersistentData = new BackpropagationData();
        
        s_PersistentData.chosenW3 = chosenParameters.x;
        s_PersistentData.chosenW4 = chosenParameters.y;
        s_PersistentData.chosenB5 = chosenParameters.z;
        s_PersistentData.isReturningFromBackprop = true;
        s_PersistentData.currentEpoch++;
        
        Debug.Log($"Updated persistent data for epoch {s_PersistentData.currentEpoch}");
        Debug.Log($"Loading forward propagation scene: {forwardPropagationSceneName}");
        
        // Evaluate player's parameter choice and potentially reward with learning rate increase
        EvaluatePlayerParameterChoice();
        
        // Load forward propagation scene
        SceneManager.LoadScene(forwardPropagationSceneName);
    }
    
    /// <summary>
    /// Get chosen parameters from current ball position in parameter box
    /// </summary>
    Vector3 GetChosenParametersFromBallPosition()
    {
        // Try to get ball position from SlingshotController
        if (slingshotController?.CurrentBall != null)
        {
            Vector3 ballWorldPos = slingshotController.CurrentBall.transform.position;
            Vector3 parameters = WorldPositionToParameters(ballWorldPos);
            
            Debug.Log($"Ball position: {ballWorldPos} -> Parameters: W3={parameters.x:F3}, W4={parameters.y:F3}, B5={parameters.z:F3}");
            return parameters;
        }
        
        // Fallback to current parameters if ball not available
        Debug.LogWarning("Ball not found, using current parameters as fallback");
        return new Vector3(currentW3, currentW4, currentB5);
    }
    
    /// <summary>
    /// Calculate adaptive parameter ranges based on actual pre-epoch and optimal parameter values
    /// This makes visual differences more pronounced when parameters are close together
    /// </summary>
    void CalculateAdaptiveParameterRanges()
    {
        if (!useAdaptiveRanges || s_PersistentData == null)
        {
            Debug.Log("Using default parameter ranges (adaptive ranges disabled or no data)");
            return;
        }
        
        Debug.Log("=== CALCULATING ADAPTIVE PARAMETER RANGES ===");
        
        // Get pre-epoch and optimal parameter values
        float preW3 = s_PersistentData.preEpochW3;
        float preW4 = s_PersistentData.preEpochW4;
        float preB5 = s_PersistentData.preEpochB5;
        
        float optW3 = s_PersistentData.optimalW3;
        float optW4 = s_PersistentData.optimalW4;
        float optB5 = s_PersistentData.optimalB5;
        
        Debug.Log($"Pre-epoch: W3={preW3:F3}, W4={preW4:F3}, B5={preB5:F3}");
        Debug.Log($"Optimal: W3={optW3:F3}, W4={optW4:F3}, B5={optB5:F3}");
        
        // Calculate ranges for weights (W3 and W4)
        float minWeight = Mathf.Min(preW3, preW4, optW3, optW4);
        float maxWeight = Mathf.Max(preW3, preW4, optW3, optW4);
        float weightSpread = maxWeight - minWeight;
        float weightCenter = (minWeight + maxWeight) * 0.5f;
        
        // Calculate range for bias (B5)
        float minBias = Mathf.Min(preB5, optB5);
        float maxBias = Mathf.Max(preB5, optB5);
        float biasSpread = maxBias - minBias;
        float biasCenter = (minBias + maxBias) * 0.5f;
        
        Debug.Log($"Weight spread: {weightSpread:F4}, center: {weightCenter:F3}");
        Debug.Log($"Bias spread: {biasSpread:F4}, center: {biasCenter:F3}");
        
        // Ensure minimum range sizes
        float adaptiveWeightRange = Mathf.Max(weightSpread * minimumExpansionFactor, minimumRangeSize);
        float adaptiveBiasRange = Mathf.Max(biasSpread * minimumExpansionFactor, minimumRangeSize);
        
        // Add padding
        adaptiveWeightRange += adaptiveWeightRange * paddingFactor;
        adaptiveBiasRange += adaptiveBiasRange * paddingFactor;
        
        // Calculate new ranges centered around the parameters
        Vector2 oldWeightRange = weightRange;
        Vector2 oldBiasRange = biasRange;
        
        weightRange = new Vector2(
            weightCenter - adaptiveWeightRange * 0.5f,
            weightCenter + adaptiveWeightRange * 0.5f
        );
        
        biasRange = new Vector2(
            biasCenter - adaptiveBiasRange * 0.5f,
            biasCenter + adaptiveBiasRange * 0.5f
        );
        
        Debug.Log($"=== ADAPTIVE RANGE RESULTS ===");
        Debug.Log($"Weight range: {oldWeightRange.x:F3} to {oldWeightRange.y:F3} → {weightRange.x:F3} to {weightRange.y:F3}");
        Debug.Log($"Bias range: {oldBiasRange.x:F3} to {oldBiasRange.y:F3} → {biasRange.x:F3} to {biasRange.y:F3}");
        
        float weightRangeReduction = (oldWeightRange.y - oldWeightRange.x) / (weightRange.y - weightRange.x);
        float biasRangeReduction = (oldBiasRange.y - oldBiasRange.x) / (biasRange.y - biasRange.x);
        
        Debug.Log($"Range reduction factors: Weight={weightRangeReduction:F1}x, Bias={biasRangeReduction:F1}x");
        Debug.Log($"Visual differences should now be {weightRangeReduction:F1}x more pronounced!");
        
        // Update plot ranges in HandCanvasUI to match the new parameter ranges
        if (handCanvasUI != null)
        {
            Debug.Log("Updating HandCanvasUI plot ranges to match adaptive parameter ranges...");
            handCanvasUI.UpdatePlotRangesAdaptively();
        }
    }
    
    /// <summary>
    /// Refresh both adaptive parameter ranges and plot ranges for immediate visual update
    /// </summary>
    [ContextMenu("Refresh Adaptive Ranges")]
    public void RefreshAdaptiveRanges()
    {
        Debug.Log("=== REFRESHING ADAPTIVE RANGES ===");
        
        // Recalculate adaptive parameter ranges
        CalculateAdaptiveParameterRanges();
        
        Debug.Log("Adaptive ranges refresh completed!");
    }
    
    /// <summary>
    /// Test method to manually verify adaptive range calculation
    /// </summary>
    [ContextMenu("Test Adaptive Parameter Ranges")]
    public void TestAdaptiveParameterRanges()
    {
        Debug.Log("=== TESTING ADAPTIVE PARAMETER RANGES ===");
        
        if (s_PersistentData == null)
        {
            Debug.LogError("No persistent data available for testing");
            return;
        }
        
        Debug.Log($"useAdaptiveRanges: {useAdaptiveRanges}");
        Debug.Log($"minimumExpansionFactor: {minimumExpansionFactor}");
        Debug.Log($"paddingFactor: {paddingFactor}");
        Debug.Log($"minimumRangeSize: {minimumRangeSize}");
        
        Vector2 originalWeightRange = weightRange;
        Vector2 originalBiasRange = biasRange;
        
        // Test the calculation
        CalculateAdaptiveParameterRanges();
        
        Debug.Log($"=== COMPARISON RESULTS ===");
        Debug.Log($"Original weight range: [{originalWeightRange.x:F3}, {originalWeightRange.y:F3}] (size: {originalWeightRange.y - originalWeightRange.x:F3})");
        Debug.Log($"Adaptive weight range: [{weightRange.x:F3}, {weightRange.y:F3}] (size: {weightRange.y - weightRange.x:F3})");
        Debug.Log($"Original bias range: [{originalBiasRange.x:F3}, {originalBiasRange.y:F3}] (size: {originalBiasRange.y - originalBiasRange.x:F3})");
        Debug.Log($"Adaptive bias range: [{biasRange.x:F3}, {biasRange.y:F3}] (size: {biasRange.y - biasRange.x:F3})");
        
        // Calculate improvements
        float weightImprovement = (originalWeightRange.y - originalWeightRange.x) / (weightRange.y - weightRange.x);
        float biasImprovement = (originalBiasRange.y - originalBiasRange.x) / (biasRange.y - biasRange.x);
        
        Debug.Log($"Visual resolution improvement: Weight={weightImprovement:F1}x, Bias={biasImprovement:F1}x");
        
        // Test if both pre-epoch and optimal parameters are within new ranges
        bool preInBounds = (s_PersistentData.preEpochW3 >= weightRange.x && s_PersistentData.preEpochW3 <= weightRange.y) &&
                          (s_PersistentData.preEpochW4 >= weightRange.x && s_PersistentData.preEpochW4 <= weightRange.y) &&
                          (s_PersistentData.preEpochB5 >= biasRange.x && s_PersistentData.preEpochB5 <= biasRange.y);
        
        bool optInBounds = (s_PersistentData.optimalW3 >= weightRange.x && s_PersistentData.optimalW3 <= weightRange.y) &&
                          (s_PersistentData.optimalW4 >= weightRange.x && s_PersistentData.optimalW4 <= weightRange.y) &&
                          (s_PersistentData.optimalB5 >= biasRange.x && s_PersistentData.optimalB5 <= biasRange.y);
        
        Debug.Log($"Pre-epoch parameters in bounds: {preInBounds}");
        Debug.Log($"Optimal parameters in bounds: {optInBounds}");
        
        if (preInBounds && optInBounds)
        {
            Debug.Log("✅ SUCCESS: All parameters are within adaptive ranges!");
        }
        else
        {
            Debug.LogError("❌ ERROR: Some parameters are outside adaptive ranges!");
        }
    }

    /// <summary>
    /// Test the player reward system with different parameter choices
    /// </summary>
    [ContextMenu("Test Player Reward System")]
    public void TestPlayerRewardSystem()
    {
        Debug.Log("=== TESTING PLAYER REWARD SYSTEM ===");
        
        if (s_PersistentData == null)
        {
            Debug.LogError("Cannot test reward system - no persistent data available");
            return;
        }
        
        NeuralNetwork neuralNetwork = NeuralNetwork.Instance;
        if (neuralNetwork == null)
        {
            Debug.LogError("Cannot test reward system - NeuralNetwork not found");
            return;
        }
        
        float optimalW3 = s_PersistentData.optimalW3;
        float optimalW4 = s_PersistentData.optimalW4;
        float optimalB5 = s_PersistentData.optimalB5;
        
        Debug.Log($"Optimal parameters: W3={optimalW3:F3}, W4={optimalW4:F3}, B5={optimalB5:F3}");
        Debug.Log($"Current learning rate: {neuralNetwork.GetCurrentLearningRate():F4}");
        
        // Test different player choices
        float[] testDistances = { 0.1f, 0.3f, 0.6f, 1.0f };
        
        foreach (float testDistance in testDistances)
        {
            // Create test parameters at different distances from optimal
            float testW3 = optimalW3 + testDistance;
            float testW4 = optimalW4 + testDistance;
            float testB5 = optimalB5 + testDistance;
            
            float actualDistance = Vector3.Distance(
                new Vector3(testW3, testW4, testB5),
                new Vector3(optimalW3, optimalW4, optimalB5)
            );
            
            Debug.Log($"\n--- Testing distance {testDistance:F1} (actual: {actualDistance:F3}) ---");
            Debug.Log($"Test parameters: W3={testW3:F3}, W4={testW4:F3}, B5={testB5:F3}");
            
            // Temporarily store current learning rate
            float oldLearningRate = neuralNetwork.GetCurrentLearningRate();
            
            // Test the evaluation
            neuralNetwork.EvaluatePlayerChoice(testW3, testW4, testB5, optimalW3, optimalW4, optimalB5);
            
            float newLearningRate = neuralNetwork.GetCurrentLearningRate();
            float change = newLearningRate - oldLearningRate;
            
            Debug.Log($"Learning rate change: {oldLearningRate:F4} → {newLearningRate:F4} (Δ: {change:F4})");
            
            // Reset learning rate for next test
            if (change > 0)
            {
                neuralNetwork.currentLearningRate = oldLearningRate;
                Debug.Log("Reset learning rate for next test");
            }
        }
        
        Debug.Log("=== PLAYER REWARD SYSTEM TEST COMPLETE ===");
    }
} 