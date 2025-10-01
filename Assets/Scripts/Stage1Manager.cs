using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Events;
using System.Linq;
using System.Collections;

public class Stage1Manager : MonoBehaviour
{
    [Header("Tunnel Settings")]
    public float tunnelRadius = 5f;
    public float tunnelLength = 50f;
    public float spawnDistance = 30f; // Distance in front of player to spawn points
    public float destroyDistance = 5f; // Distance behind player before destroying points

    [Header("Datapoint Settings")]
    public GameObject validDatapointPrefab;
    public GameObject noiseDatapointPrefab;
    public float spawnInterval = 1f;
    public float pointSpeed = 5f;
    public float noiseRatio = 0.3f; // Ratio of noise points to total points
    public int pointsPerEpoch = 10; // Number of points to spawn per epoch

    [Header("Network Reference")]
    public NeuralNetwork neuralNetwork;
    public NetworkVis networkVisualization;

    [Header("Visualization Settings")]
    public Color validPointColor = Color.green;
    public Color noisePointColor = Color.red;
    public float pointSize = 0.2f;

    [Header("Visualization Reference")]
    public AFVisualizer afVisualizer;
    public ResultPlotVisualizer resultPlotVisualizer;
    public enum VisualizationPosition { TunnelEnd, Skybox }
    public VisualizationPosition visualizationPosition = VisualizationPosition.TunnelEnd;
    public float skyboxDistance = 100f; // Distance for skybox visualization
    
    [Header("Instruction System")]
    public bool showInstructionsOnStart = true;

    private Transform playerTransform;
    private float nextSpawnTime;
    private List<GameObject> activePoints = new List<GameObject>();
    private bool isGenerating = false;
    private int pointsSpawnedThisEpoch = 0;
    private List<NeuralNetwork.TrainingData> currentEpochData;
    
    // Track the remaining valid datapoints in the epoch
    private int remainingValidPointsInEpoch = 0;

    // Events
    public UnityEvent onEpochStarted;
    public UnityEvent onEpochCompleted;

    // Add current neuron tracking
    private int currentLayerIndex = -1;
    private int currentNeuronIndex = -1;
    public bool isStageActive = false;
    private bool hasStartedAnimation = false;

    private Dictionary<(int layer, int neuron), List<Vector2>> neuronResultPlots = new();

    private bool isPausedByInstruction = false;
    private int pendingLayerIndex = -1;
    private int pendingNeuronIndex = -1;
    private bool hasPendingNeuronSelection = false;

    private bool hasShownFirstTimeInstruction = false; // Track if first-time instruction has been shown
    private bool hasShownAfterAnimationInstruction = false; // Track if after-animation instruction has been shown
    
    [Header("Animation Timing")]
    public float animationDelayAfterSpawning = 20f; // 20 seconds delay after all points spawned
    private float allPointsSpawnedTime = -1f; // Time when all points were spawned
    private bool isWaitingForAfterAnimationInstruction = false; // Flag to indicate we're waiting for instruction

    public void PauseForInstruction() {
        isPausedByInstruction = true;
    }

    public void ResumeAfterInstruction() {
        isPausedByInstruction = false;
        
        // Check if we have a pending neuron selection to process
        if (hasPendingNeuronSelection) {
            Debug.Log($"Resuming with pending neuron selection: Layer {pendingLayerIndex}, Neuron {pendingNeuronIndex}");
            ProcessNeuronSelection(pendingLayerIndex, pendingNeuronIndex);
            hasPendingNeuronSelection = false;
            pendingLayerIndex = -1;
            pendingNeuronIndex = -1;
        }
        
        // Check if we were waiting for after-animation instruction and should now start animation
        if (isWaitingForAfterAnimationInstruction)
        {
            Debug.Log("After-animation instruction dismissed, starting animation now");
            hasShownAfterAnimationInstruction = true; // Mark as shown
            isWaitingForAfterAnimationInstruction = false;
            hasStartedAnimation = true;
            afVisualizer.StartActivationAnimation();
            StartCoroutine(WaitForAnimationAndProceed());
        }
    }
    
    private void ShowAfterAnimationInstruction()
    {
        Debug.Log("Showing after-animation instruction for first time");
        hasShownAfterAnimationInstruction = true; // Mark as shown
        
        // Show instruction canvas and after-animation panel
        InstructionUIManager.Instance.ShowInstructionCanvas();
        InstructionUIManager.Instance.ShowPanel(InstructionUIManager.Instance.afterAnimationPanel);
        
        // Listen for instruction canvas hidden event to continue with animation
        InstructionUIManager.Instance.OnInstructionCanvasHidden -= ResumeAfterInstruction;
        InstructionUIManager.Instance.OnInstructionCanvasHidden += ResumeAfterInstruction;
    }
    

    
    private IEnumerator WaitForInstructionDismissal()
    {
        bool instructionDismissed = false;
        
        // Create a local action to handle the dismissal
        System.Action onDismissed = () => instructionDismissed = true;
        
        // Subscribe to the instruction canvas hidden event
        InstructionUIManager.Instance.OnInstructionCanvasHidden += onDismissed;
        
        // Wait until the instruction is dismissed
        yield return new WaitUntil(() => instructionDismissed);
        
        // Unsubscribe from the event
        InstructionUIManager.Instance.OnInstructionCanvasHidden -= onDismissed;
        
        Debug.Log("Instruction panel dismissed by player");
    }
    
    //private IEnumerator WaitForInstructionDismissal()
    //{
    //    bool instructionDismissed = false;
        
    //    // Create a local action to handle the dismissal
    //    System.Action onDismissed = () => instructionDismissed = true;
        
    //    // Subscribe to the instruction canvas hidden event
    //    InstructionUIManager.Instance.OnInstructionCanvasHidden += onDismissed;
        
    //    // Wait until the instruction is dismissed
    //    yield return new WaitUntil(() => instructionDismissed);
        
    //    // Unsubscribe from the event
    //    InstructionUIManager.Instance.OnInstructionCanvasHidden -= onDismissed;
        
    //    Debug.Log("Instruction panel dismissed by player");
    //}

    private void Start()
    {
        playerTransform = Camera.main.transform;
        nextSpawnTime = Time.time;
        
        // Use the NeuralNetwork singleton - it handles persistence automatically
        if (NeuralNetwork.Instance != null)
        {
            neuralNetwork = NeuralNetwork.Instance;
            Debug.Log($"Using NeuralNetwork singleton: {neuralNetwork.name}");
            
            // Check if we're returning from backpropagation scene
            if (BackpropagationManager.IsReturningFromBackpropagation())
        {
                Debug.Log("=== RESUMING FROM BACKPROPAGATION ===");
                
                // Get chosen parameters and epoch info from backpropagation
                float chosenW3, chosenW4, chosenB5;
                int resumeEpoch;
                BackpropagationManager.GetChosenParametersForNextEpoch(
                    out chosenW3, out chosenW4, out chosenB5, out resumeEpoch);
                    
                Debug.Log($"Resuming with chosen parameters: W3={chosenW3:F3}, W4={chosenW4:F3}, B5={chosenB5:F3}");
                Debug.Log($"Starting epoch: {resumeEpoch}");
                
                // Apply chosen parameters to the network for the next epoch
                ApplyParametersToNetwork(chosenW3, chosenW4, chosenB5);
                
                // Update epoch count
                neuralNetwork.currentEpoch = resumeEpoch;
                
                // Finalize previous epoch by gameplay rules and apply LR, then begin new epoch metrics
                neuralNetwork.FinalizeEpochAndApplyLearningRate();
                neuralNetwork.BeginGameplayEpoch();

                Debug.Log($"Scene successfully resumed for epoch {resumeEpoch}");
                
                // Skip game start instruction when resuming from backpropagation
                showInstructionsOnStart = false;
                
                // Notify instruction manager about forward propagation scene entry
                if (InstructionUIManager.Instance != null)
                {
                    InstructionUIManager.Instance.OnForwardPropagationSceneEntered();
                }
            }
            else
            {
                // Normal game start (first time in scene)
                Debug.Log("=== STARTING NEW GAME ===");
                neuralNetwork.currentEpoch = 1; // Start with epoch 1
                neuralNetwork.BeginGameplayEpoch();
                
                // Notify instruction manager about forward propagation scene entry for fresh start
                if (InstructionUIManager.Instance != null)
                {
                    InstructionUIManager.Instance.OnForwardPropagationSceneEntered();
                }
            }
            
            // Update network visualization reference if needed
            if (networkVisualization != null)
            {
                // NetworkVis now handles its own singleton reference automatically
                Debug.Log("NetworkVis will use NeuralNetwork singleton automatically");
            }
            
            // TODO: Update visualizers to use singleton
            if (afVisualizer != null)
            {
                Debug.Log("AFVisualizer configured for NeuralNetwork singleton");
            }
        }
        else
        {
            Debug.LogError("NeuralNetwork singleton not found! Make sure a NeuralNetwork exists in the scene.");
        }

        // Subscribe to neuron selection events
        if (networkVisualization != null)
        {
            networkVisualization.OnNeuronSelected.AddListener(HandleNeuronSelection);
        }
        
        // Set up the visualization position
        SetupVisualizationPosition();

        if (afVisualizer != null)
        {
            afVisualizer.resultPlotVisualizer = resultPlotVisualizer;
        }
        
        // Initialize state flags to prevent early triggering
        isStageActive = false;
        hasStartedAnimation = false;
        allPointsSpawnedTime = -1f;
        isWaitingForAfterAnimationInstruction = false;
        pointsSpawnedThisEpoch = 0;
        remainingValidPointsInEpoch = 0;
        
    }
    
    void ApplyParametersToNetwork(float w3, float w4, float b5)
    {
        if (neuralNetwork == null)
        {
            Debug.LogError("Cannot apply parameters - neural network is null!");
            return;
        }
        
        Debug.Log($"=== STAGE1MANAGER: VERIFYING PARAMETERS FROM BACKPROPAGATION ===");
        Debug.Log($"Expected final parameters: W3={w3:F3}, W4={w4:F3}, B5={b5:F3}");
        
        // Verify that BackpropagationManager has already applied the complete network state
        int lastLayerIndex = neuralNetwork.weights.Length - 1;
        float actualW3 = (float)neuralNetwork.weights[lastLayerIndex][0];
        float actualW4 = (float)neuralNetwork.weights[lastLayerIndex][1];
        float actualB5 = (float)neuralNetwork.biases[lastLayerIndex][0];
        
        Debug.Log($"Actual network parameters: W3={actualW3:F3}, W4={actualW4:F3}, B5={actualB5:F3}");
        
        // Check if parameters match (they should already be applied by BackpropagationManager)
        bool parametersMatch = (Mathf.Abs(actualW3 - w3) < 0.001f && 
                              Mathf.Abs(actualW4 - w4) < 0.001f && 
                              Mathf.Abs(actualB5 - b5) < 0.001f);
        
        if (parametersMatch)
        {
            Debug.Log("✅ Parameters already correctly applied by BackpropagationManager");
        }
        else
        {
            Debug.LogWarning("⚠️ Parameter mismatch - applying correction...");
            neuralNetwork.ApplyLastLayerParameters(w3, w4, b5, notifyVisualizers: true);
        }
        
        // Log ALL network parameters to verify complete state restoration
        Debug.Log("=== COMPLETE NETWORK STATE VERIFICATION ===");
        for (int i = 0; i < neuralNetwork.weights.Length; i++)
        {
            Debug.Log($"Layer {i} → Layer {i+1}:");
            if (neuralNetwork.weights[i].Length > 0)
            {
                Debug.Log($"  First weight: {neuralNetwork.weights[i][0]:F3}");
            }
            if (neuralNetwork.biases[i].Length > 0)
            {
                Debug.Log($"  First bias: {neuralNetwork.biases[i][0]:F3}");
            }
        }
        
        // CRITICAL FIX: Capture the newly applied chosen parameters as pre-epoch parameters
        // This ensures the ball starts at the player's chosen position in the next backprop session
        Debug.Log("=== CAPTURING NEW PRE-EPOCH PARAMETERS ===");
        neuralNetwork.CapturePreEpochParameters();
        
        // Verify the capture worked by logging the pre-epoch parameters
        float capturedW3, capturedW4, capturedB5;
        neuralNetwork.GetPreEpochLastLayerParameters(out capturedW3, out capturedW4, out capturedB5);
        Debug.Log($"New pre-epoch parameters captured: W3={capturedW3:F3}, W4={capturedW4:F3}, B5={capturedB5:F3}");
        Debug.Log("These will be used as ball starting position in next backpropagation session");
        
        // Force refresh the activation function visualizer to reflect ALL parameter changes
        if (afVisualizer != null)
        {
            Debug.Log("Force refreshing AFVisualizer to reflect ALL network parameter changes...");
            afVisualizer.ForceRefreshVisualization();
        }
        
        // Trigger network update event to notify all visualization components
        Debug.Log("Triggering OnWeightsUpdated event for all visualization components...");
        neuralNetwork.NotifyWeightsUpdated();
        
        Debug.Log("Parameter verification, pre-epoch capture, and visualization update completed");
    }
    
    private void SetupVisualizationPosition()
    {
        if (afVisualizer == null) return;
        
        switch (visualizationPosition)
        {
            case VisualizationPosition.TunnelEnd:
                afVisualizer.PositionAtTunnelEnd(
                    playerTransform.position, 
                    playerTransform.forward, 
                    tunnelLength, 
                    tunnelRadius);
                break;
                
            case VisualizationPosition.Skybox:
                afVisualizer.PositionAsSkyboxElement(
                    playerTransform.position, 
                    playerTransform.forward, 
                    skyboxDistance);
                break;
        }
    }
    
    private void HandleNeuronSelection(int layerIndex, int neuronIndex)
    {
        // Check if this is an output layer neuron
        bool isOutputLayer = (layerIndex == neuralNetwork.layerSizes.Length - 1);
        
        // For first neuron selection OR output layer neurons, check if we need to show instruction canvas
        if ((!hasShownFirstTimeInstruction || isOutputLayer) && !InstructionUIManager.Instance.IsInstructionCanvasActive)
        {
            // Mark that we've shown the first-time instruction (only for first selection)
            if (!hasShownFirstTimeInstruction)
            {
                hasShownFirstTimeInstruction = true;
            }
            
            // 存储待处理的神经元选择
            pendingLayerIndex = layerIndex;
            pendingNeuronIndex = neuronIndex;
            hasPendingNeuronSelection = true;
            
            // Choose appropriate panel based on neuron type
            GameObject panelToShow = isOutputLayer ? 
                InstructionUIManager.Instance.outputNeuronPanel : 
                InstructionUIManager.Instance.neuronSelectionPanel;
            
            // 强制显示Instruction Canvas和相应面板
            InstructionUIManager.Instance.ShowInstructionCanvas();
            InstructionUIManager.Instance.ShowPanel(panelToShow);

            // 暂停小游戏
            PauseForInstruction();

            // 监听Instruction Canvas关闭事件，恢复小游戏
            InstructionUIManager.Instance.OnInstructionCanvasHidden -= ResumeAfterInstruction; // 防止重复注册
            InstructionUIManager.Instance.OnInstructionCanvasHidden += ResumeAfterInstruction;
            
            Debug.Log($"Auto-showing canvas for {(isOutputLayer ? "output" : "hidden")} layer neuron ({layerIndex}, {neuronIndex})");
            return; // 暂时不处理神经元选择，等Canvas关闭后再处理
        }
        
        // After first time for hidden layer, or Canvas already active, directly process neuron selection
        ProcessNeuronSelection(layerIndex, neuronIndex);
    }

    private void ProcessNeuronSelection(int layerIndex, int neuronIndex)
    {
        // Don't allow new selection if stage is active
        if (isStageActive)
        {
            Debug.Log("Cannot select new neuron while stage is active");
            return;
        }

        currentLayerIndex = layerIndex;
        currentNeuronIndex = neuronIndex;

        // Load data for the selected neuron
        LoadNeuronData();
    }

    private void LoadNeuronData()
    {
        if (neuralNetwork == null || neuralNetwork.trainingData == null)
        {
            Debug.LogError("No training data available");
            return;
        }

        // Filter training data for this neuron
        // Get all training data and shuffle it
        currentEpochData = neuralNetwork.trainingData.OrderBy(x => Random.value).ToList();
        
        // Count valid points for this epoch
        remainingValidPointsInEpoch = currentEpochData.Count(data => data.targets[0] >= 0.5f);
        
        //Debug.Log($"Starting stage for neuron {currentNeuronIndex} in layer {currentLayerIndex}");
        //Debug.Log($"Total training data available: {neuralNetwork.trainingData?.Length ?? 0}");
        //Debug.Log($"Loaded {currentEpochData.Count} data points, {remainingValidPointsInEpoch} valid points");
        
        // Debug: Print first few data points to see what we're working with
        for (int i = 0; i < Mathf.Min(5, currentEpochData.Count); i++)
        {
            var sample = currentEpochData[i];
            Debug.Log($"Sample {i}: Input={sample.inputs[0]}, Target={sample.targets[0]}");
        }

        // Start the stage
        StartNewEpoch();
        isStageActive = true;
    }

    public void StartNewEpoch()
    {
        ClearAllPoints();
        pointsSpawnedThisEpoch = 0;
        hasStartedAnimation = false; // Reset the animation flag
        allPointsSpawnedTime = -1f; // Reset the spawn timer
        isWaitingForAfterAnimationInstruction = false; // Reset instruction waiting flag
        
        if (currentEpochData != null && currentEpochData.Count > 0)
        {
            // Reset the activation visualizer for the new epoch
            if (afVisualizer != null)
            {
                afVisualizer.SetupForNewEpoch(remainingValidPointsInEpoch);
            }
        }
        else
        {
            Debug.LogWarning("No training data available for new epoch");
        }
        
        isGenerating = true;
        nextSpawnTime = Time.time;
        onEpochStarted?.Invoke();
    }

    private void SpawnDatapoints()
    {
        if (isPausedByInstruction) return;
        if (currentEpochData == null || currentEpochData.Count <= pointsSpawnedThisEpoch)
        {
            Debug.LogWarning("No more data points to spawn");
            return;
        }

        // Get the current sample
        var sample = currentEpochData[pointsSpawnedThisEpoch];
        
        // Calculate position based on input features
        Vector3 spawnPosition = GetPositionFromFeatures(sample.inputs);
        
        // Force all points to be valid
        bool isValid = true;
        GameObject pointPrefab = validDatapointPrefab;

        Debug.Log($"Spawning point {pointsSpawnedThisEpoch}: Target={sample.targets[0]}, IsValid={isValid}");

        // Instantiate the point
        GameObject point = Instantiate(pointPrefab, spawnPosition, Quaternion.identity);

        // Set up the DataPoint component
        DataPoint dataPoint = point.GetComponent<DataPoint>();
        if (dataPoint != null)
        {
            // Assign inputs and other properties
            dataPoint.inputs = sample.inputs;
            dataPoint.isValid = isValid;
            dataPoint.targetValue = sample.targets[0];
            
            // Assign the visualizer reference
            dataPoint.SetVisualizer(afVisualizer);
            
            // Set neural network reference
            dataPoint.SetNeuralNetwork(neuralNetwork);
            
            // Set neural network data and calculate target angles
            if (neuralNetwork != null)
            {
                // Forward pass to get z value and activation for the first hidden layer and this input
                float weightedInput = 0f;
                float activation = 0f;

                // Use the neural network to compute the weighted input (z) and activation
                double[] output = neuralNetwork.Forward(sample.inputs);
                if (output != null)
                {
                    // Validate layer and neuron indices
                    if (currentLayerIndex < 0 || currentLayerIndex >= neuralNetwork.layerSizes.Length ||
                        currentNeuronIndex < 0 || currentNeuronIndex >= neuralNetwork.layerSizes[currentLayerIndex])
                    {
                        Debug.LogError($"Invalid neuron selection: Layer {currentLayerIndex}, Neuron {currentNeuronIndex}. " +
                                     $"Network has {neuralNetwork.layerSizes.Length} layers with sizes: [{string.Join(",", neuralNetwork.layerSizes)}]");
                        return;
                    }
                    
                    // Ensure we have valid z-values and activations for the selected neuron
                    if (neuralNetwork.zValues != null && neuralNetwork.zValues.Length > currentLayerIndex &&
                        neuralNetwork.activations != null && neuralNetwork.activations.Length > currentLayerIndex &&
                        neuralNetwork.zValues[currentLayerIndex] != null && neuralNetwork.zValues[currentLayerIndex].Length > currentNeuronIndex &&
                        neuralNetwork.activations[currentLayerIndex] != null && neuralNetwork.activations[currentLayerIndex].Length > currentNeuronIndex)
                    {
                        // Use the current neuron index instead of calculating from pointsSpawnedThisEpoch
                        weightedInput = (float)neuralNetwork.zValues[currentLayerIndex][currentNeuronIndex];
                        activation = (float)neuralNetwork.activations[currentLayerIndex][currentNeuronIndex];
                        
                        Debug.Log($"Generated datapoint {pointsSpawnedThisEpoch} for neuron ({currentLayerIndex},{currentNeuronIndex}): " +
                                $"Input={sample.inputs[0]:F3}, WeightedInput={weightedInput:F3}, Activation={activation:F3}");

                        // Calculate target angles based on weights and biases
                        float weightAngle = CalculateWeightAngle(neuralNetwork.weights[currentLayerIndex - 1][currentNeuronIndex]);
                        float biasAngle = CalculateBiasAngle(neuralNetwork.biases[currentLayerIndex - 1][currentNeuronIndex]);
                        
                        // Set the target angles for the datapoint
                        dataPoint.SetTargetAngles(weightAngle, biasAngle);
                    }
                    else
                    {
                        Debug.LogError($"Cannot access neural network data for neuron ({currentLayerIndex},{currentNeuronIndex}). " +
                                     $"Z-values length: {neuralNetwork.zValues?.Length ?? 0}, " +
                                     $"Activations length: {neuralNetwork.activations?.Length ?? 0}");
                    }
                }
                
                // Set the neural network data in the datapoint
                dataPoint.SetNeuralNetworkData(weightedInput, activation);
            }
        }

        // Set point properties
        point.transform.localScale = Vector3.one * pointSize;
        Renderer renderer = point.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.material.color = validPointColor;
        }

        // Add to active points list
        activePoints.Add(point);
    }

    private float CalculateWeightAngle(double weight)
    {
        // Map weight value to angle range (e.g., -1 to 1 maps to -45 to 45 degrees)
        float normalizedWeight = Mathf.Clamp((float)weight, -1f, 1f);
        return normalizedWeight * 45f; // Scale to -45 to 45 degrees
    }

    private float CalculateBiasAngle(double bias)
    {
        // Map bias value to angle range (e.g., -1 to 1 maps to 135 to 225 degrees)
        float normalizedBias = Mathf.Clamp((float)bias, -1f, 1f);
        return 180f + (normalizedBias * 45f); // Scale to 135 to 225 degrees
    }

    private Vector3 GetPositionFromFeatures(double[] features)
    {
        // Map 1D feature to 3D tunnel space
        // Assuming feature is normalized between 0 and 1
        float x = (float)features[0] * tunnelRadius * 2 - tunnelRadius;
        float y = 0f; // No second input, so we'll keep y at 0
        
        // Calculate position in front of player
        Vector3 forward = playerTransform.forward;
        Vector3 right = playerTransform.right;
        Vector3 up = playerTransform.up;
        
        // Calculate final position
        Vector3 playerPosition = playerTransform.position;
        Vector3 spawnPosition = playerPosition + forward * spawnDistance + right * x + up * y;

        return spawnPosition;
    }

    private void MovePoints()
    {
        if (isPausedByInstruction) return;
        bool allPointsDestroyed = true;
        for (int i = activePoints.Count - 1; i >= 0; i--)
        {
            GameObject point = activePoints[i];
            if (point == null)
            {
                activePoints.RemoveAt(i);
                continue;
            }

            // Move point towards player
            Vector3 direction = (playerTransform.position - point.transform.position).normalized;
            point.transform.position += direction * pointSpeed * Time.deltaTime;

            // Calculate vector from player to point
            Vector3 playerToPoint = point.transform.position - playerTransform.position;
            
            // Project this vector onto player's forward direction to see if point is behind player
            float distanceBehindPlayer = Vector3.Dot(playerToPoint, -playerTransform.forward);

            // Remove points that have passed far behind the player
            if (distanceBehindPlayer > destroyDistance)
            {
                Debug.Log($"Destroying point at distance {distanceBehindPlayer}");
                Destroy(point);
                activePoints.RemoveAt(i);
            }
            else
            {
                allPointsDestroyed = false;
            }
        }

        // Check if animation should start based on two conditions:
        // 1. All points destroyed AND collected enough points (immediate start)
        // 2. OR 20 seconds passed since all points were spawned (automatic start)
        // Additional safety checks: must be in active stage and have spawned at least one point
        bool shouldStartAnimation = false;
        string startReason = "";

        // Safety check: only trigger animation if we're actually in an active game stage
        if (!isStageActive || pointsSpawnedThisEpoch == 0 || remainingValidPointsInEpoch == 0)
        {
            return; // Don't trigger animation if we're not in an active stage
        }

        if (allPointsDestroyed && 
            afVisualizer.GetCollectedPointsCount() >= remainingValidPointsInEpoch && 
            !hasStartedAnimation)
        {
            shouldStartAnimation = true;
            startReason = $"All points destroyed and collected {afVisualizer.GetCollectedPointsCount()} valid points";
        }
        else if (allPointsSpawnedTime > 0 && 
                 Time.time >= allPointsSpawnedTime + animationDelayAfterSpawning && 
                 !hasStartedAnimation)
        {
            shouldStartAnimation = true;
            startReason = $"20 seconds elapsed since all points spawned. Collected {afVisualizer.GetCollectedPointsCount()} points";
        }

        if (shouldStartAnimation)
        {
            Debug.Log($"Ready to start activation animation: {startReason}");
            
            // Check if this is an output layer neuron
            bool isOutputLayer = (currentLayerIndex == neuralNetwork.layerSizes.Length - 1);
            
            // Show appropriate instruction panel before animation based on neuron type
            // This is for the "after data collection" timing, not "after neuron selection"
            if (!isOutputLayer && !hasShownAfterAnimationInstruction && !isWaitingForAfterAnimationInstruction)
            {
                ShowAfterAnimationInstruction(); // Hidden layer instruction
                isWaitingForAfterAnimationInstruction = true;
                return; // Don't start animation yet, wait for instruction to be dismissed
            }
            
            // Start animation directly for output layer, or after instruction for hidden layer
            Debug.Log($"Starting activation animation: {startReason}");
            hasStartedAnimation = true; // Mark animation as started
            isWaitingForAfterAnimationInstruction = false; // Reset the waiting flag
            afVisualizer.StartActivationAnimation();
            StartCoroutine(WaitForAnimationAndProceed());
        }
    }

    public void ClearAllPoints()
    {
        foreach (GameObject point in activePoints)
        {
            if (point != null)
            {
                Destroy(point);
            }
        }
        activePoints.Clear();
    }

    private void Update()
    {
        if (isPausedByInstruction) return;
        // Move existing points towards player
        MovePoints();

        if (!isGenerating) return;

        // Spawn new points at intervals until we reach the available data count
        int maxPointsToSpawn = currentEpochData?.Count ?? pointsPerEpoch;
        if (Time.time >= nextSpawnTime && pointsSpawnedThisEpoch < maxPointsToSpawn)
        {
            SpawnDatapoints();
            nextSpawnTime = Time.time + spawnInterval;
            pointsSpawnedThisEpoch++;
            
            if (pointsSpawnedThisEpoch >= maxPointsToSpawn)
            {
                isGenerating = false;
                allPointsSpawnedTime = Time.time; // Record when all points were spawned
                Debug.Log($"All {maxPointsToSpawn} points spawned. 20-second timer started for automatic animation.");
                // Note: Don't mark as visited here - wait until animation completes
                
                onEpochCompleted?.Invoke();
            }
        }
    }

    private void OnDataPointCollected(float weightedInput, float activation)
    {
        // Add the data point to the visualization
        afVisualizer.AddCollectedDataPoint(weightedInput, activation);
    }

    private IEnumerator WaitForAnimationAndProceed()
    {
        Debug.Log("Waiting for animation to complete");
        // Wait for the activation animation to complete
        yield return new WaitForSeconds(afVisualizer.activationAnimationDuration + 0.5f); // Add a small buffer
        
        Debug.Log("Animation complete");
        
        // Save the result plot data for this neuron before clearing
        if (networkVisualization != null && resultPlotVisualizer != null && currentLayerIndex >= 0 && currentNeuronIndex >= 0)
        {
            // Check if this is an output layer neuron
            bool isOutputLayer = (currentLayerIndex == neuralNetwork.layerSizes.Length - 1);
            
            if (isOutputLayer && afVisualizer != null)
            {
                // Save output layer curve data
                OutputLayerCurveData curveData = afVisualizer.GetOutputLayerCurveData();
                if (curveData != null)
                {
                    resultPlotVisualizer.SaveOutputLayerCurveData(currentLayerIndex, currentNeuronIndex, curveData);
                    Debug.Log($"Saved output layer curve data for neuron ({currentLayerIndex}, {currentNeuronIndex})");
                }
                else
                {
                    Debug.LogWarning($"Failed to get output layer curve data for neuron ({currentLayerIndex}, {currentNeuronIndex})");
                }
            }
            else
            {
                // Regular neuron - save plot data as before
                List<Vector2> currentPlotData = GetCurrentResultPlotData();
                resultPlotVisualizer.SaveNeuronPlotData(currentLayerIndex, currentNeuronIndex, currentPlotData);
                Debug.Log($"Saved result plot data for neuron ({currentLayerIndex}, {currentNeuronIndex}) with {currentPlotData.Count} points");
            }
            
            // Mark the current neuron as visited NOW (after animation completes and plot is saved)
            networkVisualization.MarkNeuronAsVisited(currentLayerIndex, currentNeuronIndex);
            Debug.Log($"Marked neuron ({currentLayerIndex}, {currentNeuronIndex}) as visited");
            
            // Show appropriate panel after animation completion based on neuron type
            if (isOutputLayer)
            {
                Debug.Log("Output layer completed! Running training epoch before transitioning...");
                yield return new WaitForSeconds(2f); // Give player time to see completion
                
                // CRITICAL: Capture pre-epoch parameters before training (for backpropagation ball start position)
                Debug.Log("=== CAPTURING PRE-EPOCH PARAMETERS BEFORE TRAINING ===");
                neuralNetwork.CapturePreEpochParameters();
                
                // Log pre-epoch parameters for verification
                float preEpochW3, preEpochW4, preEpochB5;
                neuralNetwork.GetPreEpochLastLayerParameters(out preEpochW3, out preEpochW4, out preEpochB5);
                Debug.Log($"Pre-epoch parameters (ball start): W3={preEpochW3:F3}, W4={preEpochW4:F3}, B5={preEpochB5:F3}");
                
                // CRITICAL: Run the training epoch to get the actual optimal parameters
                // This ensures the green curve shows post-training results vs blue curve showing pre-training
                Debug.Log($"Current network parameters before training: W3={(float)neuralNetwork.weights[neuralNetwork.weights.Length-1][0]:F3}, W4={(float)neuralNetwork.weights[neuralNetwork.weights.Length-1][1]:F3}, B5={(float)neuralNetwork.biases[neuralNetwork.biases.Length-1][0]:F3}");
                
                neuralNetwork.RunEpoch();
                
                Debug.Log($"Network parameters after training: W3={(float)neuralNetwork.weights[neuralNetwork.weights.Length-1][0]:F3}, W4={(float)neuralNetwork.weights[neuralNetwork.weights.Length-1][1]:F3}, B5={(float)neuralNetwork.biases[neuralNetwork.biases.Length-1][0]:F3}");
                Debug.Log("Training epoch completed, showing backprop transition instruction...");

                // === GAMEPLAY METRICS: Forward-stage neuron completion ===
                // Count current neuron's datapoints: collected via AFVisualizer
                int collected = afVisualizer != null ? afVisualizer.GetCollectedPointsCount() : 0;
                int generated = pointsSpawnedThisEpoch; // Use actual spawned points instead of fixed 20
                if (neuralNetwork != null)
                {
                    neuralNetwork.RegisterForwardNeuronResult(collected, generated);
                }
                
                // Show backprop transition panel before scene transition - always show canvas for this
                InstructionUIManager.Instance.ShowInstructionCanvas();
                InstructionUIManager.Instance.ShowPanel(InstructionUIManager.Instance.backpropTransitionPanel);
                
                // Wait for player to dismiss the instruction panel
                yield return StartCoroutine(WaitForInstructionDismissal());
                
                Debug.Log("Instruction dismissed, transitioning to backpropagation scene...");
                
                // Transition to backpropagation
                BackpropagationManager.TransitionToBackpropagation(neuralNetwork, currentLayerIndex, currentNeuronIndex);
                yield break; // Stop coroutine execution here since we're transitioning scenes
            }
            else
            {
                // Hidden layer neuron: show after animation panel
                Debug.Log("Hidden layer animation completed, checking if we need to show after animation instruction...");
                
                // Only show canvas automatically for the first time
                if (!hasShownAfterAnimationInstruction)
                {
                    hasShownAfterAnimationInstruction = true; // Mark as shown
                    InstructionUIManager.Instance.ShowInstructionCanvas();
                    InstructionUIManager.Instance.ShowPanel(InstructionUIManager.Instance.afterAnimationPanel);
                    
                    // Wait for player to dismiss the instruction panel
                    yield return StartCoroutine(WaitForInstructionDismissal());
                    
                    Debug.Log("After animation instruction dismissed, continuing...");
                }
                else
                {
                    // For subsequent times, just set the panel without showing canvas
                    InstructionUIManager.Instance.ShowPanel(InstructionUIManager.Instance.afterAnimationPanel);
                    Debug.Log("After animation panel set (canvas not auto-shown), continuing...");
                }
                
                // === GAMEPLAY METRICS: Hidden layer neuron completion ===
                // Count current hidden layer neuron's datapoints: collected via AFVisualizer
                int collectedHidden = afVisualizer != null ? afVisualizer.GetCollectedPointsCount() : 0;
                int generatedHidden = pointsSpawnedThisEpoch; // Use actual spawned points instead of fixed 20
                if (neuralNetwork != null)
                {
                    neuralNetwork.RegisterForwardNeuronResult(collectedHidden, generatedHidden);
                }
            }
        }
        
        // DO NOT clear the visualization markers - we want the blue points to stay
        // afVisualizer.ClearDataPointMarkers();
        
        // Reset stage state but keep animation flag to prevent animation from restarting
        isStageActive = false;
        isGenerating = false;
        
        // Notify that the stage is complete
        onEpochCompleted?.Invoke();
    }

    // Get the current result plot data from the AFVisualizer
    private List<Vector2> GetCurrentResultPlotData()
    {
        if (afVisualizer != null)
        {
            // Get the result plot data directly from AFVisualizer
            // This is the same data that would be shown in the ResultPlotVisualizer
            return afVisualizer.GetResultPlotData();
        }
        
        return new List<Vector2>();
    }

    public void OnDataPointCreated(float inputValue, float label)
    {
        if (afVisualizer != null)
        {
            afVisualizer.AddDataPoint(inputValue, label);
        }
    }

    public void OnActivationComplete()
    {
        if (afVisualizer != null)
        {
            afVisualizer.UpdateActivationPlot();
        }
    }
}