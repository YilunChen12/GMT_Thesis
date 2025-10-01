using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class NeuralNetwork : MonoBehaviour
{
    [Header("Network Config")]
    public int inputSize = 2;
    public int outputSize = 1;
    public int hiddenLayers = 1;
    public int neuronsPerLayer = 4;
    public float learningRate = 0.1f;
    public float updateInterval = 0.1f;
    public NeuralNetworkDataManager dataManager;

    [Header("Adaptive Learning Rate Settings")]
    [Tooltip("Enable adaptive learning rate scheduling")]
    public bool useAdaptiveLearningRate = true;
    [Tooltip("Initial learning rate")]
    public float initialLearningRate = 0.1f;
    [Tooltip("Current learning rate (updated during training)")]
    public float currentLearningRate;
    [Tooltip("Minimum learning rate to prevent too small updates")]
    public float minLearningRate = 0.001f;
    [Tooltip("Maximum learning rate to prevent too large updates")]
    public float maxLearningRate = 0.5f;
    [Tooltip("Decay factor for learning rate reduction")]
    public float decayRate = 0.95f;
    [Tooltip("Epochs between learning rate updates")]
    public int decayEpochs = 10;
    [Tooltip("Loss improvement threshold for learning rate adjustment")]
    public float lossImprovementThreshold = 0.01f;
    [Tooltip("Previous epoch loss for comparison")]
    private float previousEpochLoss = float.MaxValue;
    
    [Header("Player Reward Learning Rate Settings")]
    [Tooltip("Enable player reward-based learning rate increases")]
    public bool usePlayerRewardSystem = true;
    [Tooltip("Distance threshold for considering player choice 'good' (closer to optimal)")]
    public float goodChoiceThreshold = 0.5f;
    [Tooltip("Distance threshold for considering player choice 'excellent' (very close to optimal)")]
    public float excellentChoiceThreshold = 0.2f;
    [Tooltip("Learning rate increase for good choices")]
    public float goodChoiceReward = 1.1f; // 10% increase
    [Tooltip("Learning rate increase for excellent choices")]
    public float excellentChoiceReward = 1.2f; // 20% increase
    [Tooltip("Maximum learning rate increase from rewards")]
    public float maxRewardLearningRate = 0.8f;

    public enum ActivationType { ReLU, Sigmoid, Tanh, Softplus }
    [Header("Activation Function")]
    public ActivationType activationType = ActivationType.ReLU;

    [Header("Data")]
    public TextAsset trainingDataFile;

    [Header("Data Tracking")]
    public List<Vector2> inputOutputPairs = new List<Vector2>();

    // SINGLETON PATTERN
    public static NeuralNetwork Instance { get; private set; }

    [System.Serializable]
    public class TrainingData
    {
        public double[] inputs;
        public double[] targets;

        public TrainingData(double[] i, double[] t)
        {
            inputs = i;
            targets = t;
        }
    }

    [HideInInspector] public List<TrainingData> trainingData = new List<TrainingData>();
    [HideInInspector] public int[] layerSizes;
    [HideInInspector] public double[][] weights;
    [HideInInspector] public double[][] biases;
    [HideInInspector] public double[][] activations;
    [HideInInspector] public double[][] zValues;

    // Store original initialized values before training
    [HideInInspector] public double[][] originalWeights;
    [HideInInspector] public double[][] originalBiases;

    // Store pre-epoch values (parameters before current epoch's training)
    [HideInInspector] public double[][] preEpochWeights;
    [HideInInspector] public double[][] preEpochBiases;

    public event Action OnNetworkUpdated;
    public event Action OnWeightsUpdated;
    public event Action<float> OnLearningRateChanged; // New event for learning rate changes

    public int currentEpoch = 0;
    public int totalEpochs = 0;

    // === Gameplay-driven learning score metrics (per VR run/epoch) ===
    [Header("Gameplay Learning Score Metrics")]
    [Tooltip("Number of epochs completed this run (forward 3 neurons + backprop targets)")]
    public int epochsCompleted = 0;
    [Tooltip("Forward neurons completed in current epoch")] 
    public int neuronsCompletedThisEpoch = 0; // up to 3
    [Tooltip("Number of forward neurons with >= 10 datapoints collected in current epoch")] 
    public int forwardNeuronSuccessesThisEpoch = 0;
    [Tooltip("Total datapoints collected across neurons in current epoch")] 
    public int datapointsCollectedThisEpoch = 0;
    [Tooltip("Total datapoints generated across neurons in current epoch")] 
    public int datapointsGeneratedThisEpoch = 0;
    [Tooltip("Backprop steps used this epoch (first 6 are free)")] 
    public int backpropStepsThisEpoch = 0;
    [Tooltip("Backprop targets reached this epoch (0-3)")] 
    public int targetsReachedThisEpoch = 0;

    // Configuration constants for gameplay rules
    [Header("Gameplay Learning Score Rules")]
    public int datapointsPerNeuron = 20; // fixed by design
    public int neuronsPerEpoch = 3;
    public int freeBackpropSteps = 6;
    public int penaltyStepsBucket = 3; // every 3 extra -> -0.01
    public float perNeuronSuccessBonus = 0.05f;
    public float perNeuronFailPenalty = -0.05f;
    public float perTargetBonus = 0.05f; // up to 0.15 per epoch
    public float stepPenaltyPerBucket = 0.01f;
    public float baseLearningScore = 0.01f;

    void Awake()
    {
        // Singleton pattern implementation
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            Debug.Log($"NeuralNetwork singleton initialized: {gameObject.name}");
        }
        else if (Instance != this)
        {
            Debug.Log($"Destroying duplicate NeuralNetwork: {gameObject.name}, keeping singleton: {Instance.gameObject.name}");
            Destroy(gameObject);
            return;
        }
        
        // Initialize when needed (only for the singleton instance)
    }

    public void TrackDataPoint(double[] input, double output)
    {
        inputOutputPairs.Add(new Vector2(
            (float)input[0],
            (float)output
        ));
    }

    public void InitializeNetwork()
    {
        // Create layer structure
        layerSizes = new int[hiddenLayers + 2];
        layerSizes[0] = inputSize;
        for (int i = 1; i <= hiddenLayers; i++) layerSizes[i] = neuronsPerLayer;
        layerSizes[hiddenLayers + 1] = outputSize;

        // Initialize arrays
        weights = new double[layerSizes.Length - 1][];
        biases = new double[layerSizes.Length - 1][];
        activations = new double[layerSizes.Length][];
        zValues = new double[layerSizes.Length][];
        
        // Initialize arrays to store original values
        originalWeights = new double[layerSizes.Length - 1][];
        originalBiases = new double[layerSizes.Length - 1][];
        
        // Initialize arrays to store pre-epoch values
        preEpochWeights = new double[layerSizes.Length - 1][];
        preEpochBiases = new double[layerSizes.Length - 1][];

        System.Random rand = new System.Random();
        for (int i = 0; i < weights.Length; i++)
        {
            int rows = layerSizes[i + 1];
            int cols = layerSizes[i];

            weights[i] = new double[rows * cols];
            biases[i] = new double[rows];
            activations[i] = new double[layerSizes[i]];
            zValues[i] = new double[layerSizes[i]];
            
            // Initialize original value arrays
            originalWeights[i] = new double[rows * cols];
            originalBiases[i] = new double[rows];
            
            // Initialize pre-epoch value arrays
            preEpochWeights[i] = new double[rows * cols];
            preEpochBiases[i] = new double[rows];

            // Xavier initialization for better convergence
            double scale = Math.Sqrt(6.0 / (cols + rows));
            for (int j = 0; j < weights[i].Length; j++)
            {
                weights[i][j] = (rand.NextDouble() * 2 - 1) * scale;
                originalWeights[i][j] = weights[i][j]; // Store original value
                preEpochWeights[i][j] = weights[i][j]; // Initialize pre-epoch with same value
            }

            for (int j = 0; j < biases[i].Length; j++)
            {
                biases[i][j] = 0; // Initialize biases to zero
                originalBiases[i][j] = biases[i][j]; // Store original value
                preEpochBiases[i][j] = biases[i][j]; // Initialize pre-epoch with same value
            }
        }

        activations[^1] = new double[layerSizes[^1]];
        OnNetworkUpdated?.Invoke();
        
        // Initialize adaptive learning rate
        if (useAdaptiveLearningRate)
        {
            currentLearningRate = initialLearningRate;
            previousEpochLoss = float.MaxValue;
            Debug.Log($"Adaptive learning rate initialized: {currentLearningRate:F4}");
        }
        else
        {
            currentLearningRate = learningRate;
            Debug.Log($"Fixed learning rate set: {currentLearningRate:F4}");
        }

        // Reset gameplay metrics at (re)initialization
        ResetEpochMetrics();
    }

    /// <summary>
    /// Begin a new gameplay epoch (3 forward neurons + backprop stage)
    /// Call this when the player starts the forward stage for the first neuron of an epoch.
    /// </summary>
    public void BeginGameplayEpoch()
    {
        ResetEpochMetrics();
        Debug.Log("=== GAMEPLAY EPOCH STARTED ===");
    }

    /// <summary>
    /// Register the result of one forward neuron collection stage.
    /// </summary>
    /// <param name="collected">Datapoints collected by player</param>
    /// <param name="generated">Datapoints generated (defaults to 20)</param>
    public void RegisterForwardNeuronResult(int collected, int generated = -1)
    {
        if (generated <= 0) generated = datapointsPerNeuron;

        neuronsCompletedThisEpoch = Mathf.Clamp(neuronsCompletedThisEpoch + 1, 0, neuronsPerEpoch);
        datapointsCollectedThisEpoch += Mathf.Max(0, collected);
        datapointsGeneratedThisEpoch += Mathf.Max(0, generated);

        bool success = collected >= Mathf.CeilToInt(datapointsPerNeuron * 0.5f); // >=10 of 20
        if (success) forwardNeuronSuccessesThisEpoch++;

        float hitPct = generated > 0 ? (float)collected / generated * 100f : 0f;
        Debug.Log($"[Gameplay] Forward neuron result: collected={collected}, generated={generated}, hit={hitPct:F1}% → {(success ? "+0.05" : "-0.05")}");
    }

    /// <summary>
    /// Increment backprop step count (call on each slingshot launch during backprop scene).
    /// </summary>
    public void ReportBackpropStep()
    {
        backpropStepsThisEpoch++;
        // Occasionally log to avoid spam
        if (backpropStepsThisEpoch % 3 == 0)
        {
            Debug.Log($"[Gameplay] Backprop steps so far: {backpropStepsThisEpoch}");
        }
    }

    /// <summary>
    /// Report that a backprop target (parameter update goal) was reached. Up to 3 per epoch.
    /// </summary>
    public void ReportBackpropTargetReached()
    {
        targetsReachedThisEpoch = Mathf.Clamp(targetsReachedThisEpoch + 1, 0, 3);
        Debug.Log($"[Gameplay] Target reached: {targetsReachedThisEpoch}/3");
    }

    /// <summary>
    /// Finalize the epoch, compute learning score by rules, and update learning rate accordingly.
    /// Call this when returning to forward propagation after backpropagation finishes.
    /// </summary>
    public void FinalizeEpochAndApplyLearningRate()
    {
        // Forward contribution: each neuron success +0.05, each fail -0.05 (max +0.15)
        int neuronsFailed = Mathf.Max(0, neuronsCompletedThisEpoch - forwardNeuronSuccessesThisEpoch);
        float forwardContribution = forwardNeuronSuccessesThisEpoch * perNeuronSuccessBonus + neuronsFailed * perNeuronFailPenalty;
        forwardContribution = Mathf.Clamp(forwardContribution, -0.15f, +0.15f);

        // Backprop contribution: targets reached +0.05 each (max +0.15)
        float targetsContribution = Mathf.Clamp(targetsReachedThisEpoch, 0, 3) * perTargetBonus;
        targetsContribution = Mathf.Clamp(targetsContribution, 0f, 0.15f);

        // Step penalties: first 6 free; every 3 extra → -0.01
        int extraSteps = Mathf.Max(0, backpropStepsThisEpoch - freeBackpropSteps);
        int penaltyBuckets = extraSteps / penaltyStepsBucket;
        float stepPenalty = penaltyBuckets * stepPenaltyPerBucket;

        float finalLearningScore = baseLearningScore + forwardContribution + targetsContribution - stepPenalty;

        Debug.Log("=== GAMEPLAY LEARNING SCORE SUMMARY ===");
        Debug.Log($"Epoch neurons: {neuronsCompletedThisEpoch} (successes: {forwardNeuronSuccessesThisEpoch})");
        Debug.Log($"Datapoints: collected={datapointsCollectedThisEpoch}, generated={datapointsGeneratedThisEpoch}");
        Debug.Log($"Backprop: steps={backpropStepsThisEpoch} (extra={extraSteps}), targets={targetsReachedThisEpoch}");
        Debug.Log($"Contrib → Forward: {forwardContribution:+0.000;-0.000}, Targets: {targetsContribution:+0.000;-0.000}, Steps: -{stepPenalty:0.000}");
        Debug.Log($"Final learning score (delta LR): {finalLearningScore:+0.000;-0.000}");

        // Apply to learning rate
        float oldRate = currentLearningRate;
        currentLearningRate = Mathf.Clamp(currentLearningRate + finalLearningScore, minLearningRate, maxLearningRate);
        epochsCompleted++;

        Debug.Log($"Learning rate updated by gameplay: {oldRate:F4} → {currentLearningRate:F4} (epoch #{epochsCompleted})");

        OnLearningRateChanged?.Invoke(currentLearningRate);
        OnWeightsUpdated?.Invoke();

        // Prepare for next epoch
        ResetEpochMetrics();
    }

    private void ResetEpochMetrics()
    {
        neuronsCompletedThisEpoch = 0;
        forwardNeuronSuccessesThisEpoch = 0;
        datapointsCollectedThisEpoch = 0;
        datapointsGeneratedThisEpoch = 0;
        backpropStepsThisEpoch = 0;
        targetsReachedThisEpoch = 0;
    }
    
    /// <summary>
    /// Update learning rate based on training progress and loss improvement
    /// </summary>
    public void UpdateLearningRate()
    {
        if (!useAdaptiveLearningRate) return;
        
        float currentLoss = (float)CalculateLoss();
        float lossImprovement = previousEpochLoss - currentLoss;
        
        Debug.Log($"=== LEARNING RATE UPDATE FOR EPOCH {currentEpoch} ===");
        Debug.Log($"Previous loss: {previousEpochLoss:F6}");
        Debug.Log($"Current loss: {currentLoss:F6}");
        Debug.Log($"Loss improvement: {lossImprovement:F6}");
        Debug.Log($"Current learning rate: {currentLearningRate:F4}");
        
        // Check if we should update learning rate based on epoch count
        bool shouldUpdateByEpoch = currentEpoch > 0 && currentEpoch % decayEpochs == 0;
        
        // Check if loss improvement is below threshold
        bool shouldUpdateByLoss = lossImprovement < lossImprovementThreshold;
        
        if (shouldUpdateByEpoch || shouldUpdateByLoss)
        {
            float oldLearningRate = currentLearningRate;
            
            // Reduce learning rate
            currentLearningRate *= decayRate;
            
            // Clamp to valid range
            currentLearningRate = Mathf.Clamp(currentLearningRate, minLearningRate, maxLearningRate);
            
            Debug.Log($"Learning rate updated:");
            Debug.Log($"  Old rate: {oldLearningRate:F4}");
            Debug.Log($"  New rate: {currentLearningRate:F4}");
            Debug.Log($"  Reason: {(shouldUpdateByEpoch ? "Epoch-based decay" : "Loss-based decay")}");
            
            // Notify components that learning rate changed
            OnWeightsUpdated?.Invoke();
            OnLearningRateChanged?.Invoke(currentLearningRate); // Trigger the new event
        }
        else
        {
            Debug.Log($"Learning rate unchanged: {currentLearningRate:F4} (no decay conditions met)");
        }
        
        // Store current loss for next epoch comparison
        previousEpochLoss = currentLoss;
    }
    
    /// <summary>
    /// Get the current learning rate for external components (like SlingshotController)
    /// </summary>
    public float GetCurrentLearningRate()
    {
        return useAdaptiveLearningRate ? currentLearningRate : learningRate;
    }
    
    /// <summary>
    /// Reset learning rate to initial value (useful for new training sessions)
    /// </summary>
    public void ResetLearningRate()
    {
        if (useAdaptiveLearningRate)
        {
            currentLearningRate = initialLearningRate;
            previousEpochLoss = float.MaxValue;
            Debug.Log($"Learning rate reset to initial value: {currentLearningRate:F4}");
        }
    }
    
    /// <summary>
    /// Evaluate player's parameter choice and reward with learning rate increase if good
    /// Call this when player finishes backpropagation scene and returns to forward propagation
    /// </summary>
    /// <param name="playerW3">Player's chosen W3 parameter</param>
    /// <param name="playerW4">Player's chosen W4 parameter</param>
    /// <param name="playerB5">Player's chosen B5 parameter</param>
    /// <param name="optimalW3">Optimal W3 parameter</param>
    /// <param name="optimalW4">Optimal W4 parameter</param>
    /// <param name="optimalB5">Optimal B5 parameter</param>
    public void EvaluatePlayerChoice(float playerW3, float playerW4, float playerB5, 
                                   float optimalW3, float optimalW4, float optimalB5)
    {
        if (!usePlayerRewardSystem) return;
        
        // Calculate distance between player choice and optimal parameters
        float distanceToOptimal = Vector3.Distance(
            new Vector3(playerW3, playerW4, playerB5),
            new Vector3(optimalW3, optimalW4, optimalB5)
        );
        
        Debug.Log($"=== PLAYER CHOICE EVALUATION ===");
        Debug.Log($"Player choice: W3={playerW3:F3}, W4={playerW4:F3}, B5={playerB5:F3}");
        Debug.Log($"Optimal parameters: W3={optimalW3:F3}, W4={optimalW4:F3}, B5={optimalB5:F3}");
        Debug.Log($"Distance to optimal: {distanceToOptimal:F3}");
        Debug.Log($"Good choice threshold: {goodChoiceThreshold:F3}");
        Debug.Log($"Excellent choice threshold: {excellentChoiceThreshold:F3}");
        
        float oldLearningRate = currentLearningRate;
        float rewardMultiplier = 1.0f;
        string rewardReason = "No reward";
        
        // Determine reward based on distance to optimal
        if (distanceToOptimal <= excellentChoiceThreshold)
        {
            rewardMultiplier = excellentChoiceReward;
            rewardReason = "EXCELLENT choice - very close to optimal!";
        }
        else if (distanceToOptimal <= goodChoiceThreshold)
        {
            rewardMultiplier = goodChoiceReward;
            rewardReason = "GOOD choice - close to optimal";
        }
        
        // Apply reward if earned
        if (rewardMultiplier > 1.0f)
        {
            currentLearningRate *= rewardMultiplier;
            
            // Clamp to maximum reward learning rate
            if (currentLearningRate > maxRewardLearningRate)
            {
                currentLearningRate = maxRewardLearningRate;
            }
            
            Debug.Log($"🎉 PLAYER REWARD EARNED! 🎉");
            Debug.Log($"Reason: {rewardReason}");
            Debug.Log($"Learning rate increased: {oldLearningRate:F4} → {currentLearningRate:F4}");
            Debug.Log($"Reward multiplier: {rewardMultiplier:F2}x");
            Debug.Log($"Ball will now travel further in parameter space!");
            
            // Notify components of learning rate change
            OnLearningRateChanged?.Invoke(currentLearningRate);
        }
        else
        {
            Debug.Log($"No reward earned - distance {distanceToOptimal:F3} is above threshold {goodChoiceThreshold:F3}");
            Debug.Log($"Learning rate unchanged: {currentLearningRate:F4}");
        }
    }
    
    /// <summary>
    /// Get reward information for UI display
    /// </summary>
    public string GetPlayerRewardInfo()
    {
        if (!usePlayerRewardSystem) return "Player rewards disabled";
        
        return $"LR: {currentLearningRate:F3} | Good: {goodChoiceThreshold:F2} | Excellent: {excellentChoiceThreshold:F2}";
    }

    public double[] Forward(double[] input, bool isTraining = false)
    {
        Array.Copy(input, activations[0], input.Length);

        for (int i = 0; i < weights.Length; i++)
        {
            int currentSize = layerSizes[i];
            int nextSize = layerSizes[i + 1];

            activations[i + 1] = new double[nextSize];
            zValues[i + 1] = new double[nextSize];

            for (int j = 0; j < nextSize; j++)
            {
                zValues[i + 1][j] = biases[i][j];
                for (int k = 0; k < currentSize; k++)
                {
                    zValues[i + 1][j] += activations[i][k] * weights[i][j * currentSize + k];
                }

                // Use the GetActivationFunction method
                activations[i + 1][j] = GetActivationFunction(zValues[i + 1][j], i + 1);
            }
        }

        // Only track data points when not training (for visualization)
        if (!isTraining)
        {
            TrackDataPoint(input, activations[^1][0]);
        }

        return activations[^1];
    }

    public void Backward(double[] targets)
    {
        double[][] deltas = new double[layerSizes.Length][];
        for (int i = 0; i < layerSizes.Length; i++)
            deltas[i] = new double[layerSizes[i]];

        // Output layer delta - derivative of loss w.r.t. output
        for (int i = 0; i < layerSizes[^1]; i++)
            deltas[^1][i] = (activations[^1][i] - targets[i]);

        // Backpropagate through hidden layers
        for (int layer = weights.Length - 1; layer >= 0; layer--)
        {
            int currentSize = layerSizes[layer];
            int nextSize = layerSizes[layer + 1];

            // Calculate deltas for current layer
            for (int j = 0; j < currentSize; j++)
            {
                double error = 0;
                for (int k = 0; k < nextSize; k++)
                {
                    error += deltas[layer + 1][k] * weights[layer][k * currentSize + j];
                }

                // Use the correct activation derivative for the current layer
                double activationDerivative = (layer == 0) ? 1 : GetActivationDerivative(zValues[layer][j], layer);
                deltas[layer][j] = error * activationDerivative;
            }

            // Update weights and biases for this layer
            for (int j = 0; j < nextSize; j++)
            {
                // Update weights
                for (int k = 0; k < currentSize; k++)
                {
                    double gradient = deltas[layer + 1][j] * activations[layer][k];
                    weights[layer][j * currentSize + k] -= currentLearningRate * gradient;
                }

                // Update bias
                biases[layer][j] -= currentLearningRate * deltas[layer + 1][j];
            }
        }
    }

    public void NotifyWeightsUpdated()
    {
        OnWeightsUpdated?.Invoke();
    }

    public void NextEpoch()
    {
        inputOutputPairs.Clear();
    }

    public void RunEpoch()
    {
        // Clear previous epoch data
        inputOutputPairs.Clear();

        // Store old weights and biases for change tracking
        double[][] oldWeights = new double[weights.Length][];
        double[][] oldBiases = new double[biases.Length][];

        for (int i = 0; i < weights.Length; i++)
        {
            oldWeights[i] = new double[weights[i].Length];
            oldBiases[i] = new double[biases[i].Length];
            Array.Copy(weights[i], oldWeights[i], weights[i].Length);
            Array.Copy(biases[i], oldBiases[i], biases[i].Length);
        }

        // Run training epoch
        foreach (var data in trainingData)
        {
            Forward(data.inputs, isTraining: true);
            Backward(data.targets);
        }

        currentEpoch++;

        // Update learning rate based on training results
        UpdateLearningRate();

        // Capture changes for visualization
        if (dataManager != null)
        {
            dataManager.CaptureBackpropChanges(oldWeights, oldBiases);
        }

        OnWeightsUpdated?.Invoke();
    }

    public void SetTrainingData(List<TrainingData> data)
    {
        trainingData = data;
        Debug.Log($"Training data set with {data.Count} samples");
    }

    // Helper method to evaluate network on test data (for visualization)
    public double[] Evaluate(double[] input)
    {
        return Forward(input, isTraining: false);
    }

    // Calculate current loss for monitoring
    public double CalculateLoss()
    {
        if (trainingData.Count == 0) return 0;

        double totalLoss = 0;
        foreach (var data in trainingData)
        {
            double[] output = Forward(data.inputs, isTraining: true);
            for (int i = 0; i < output.Length; i++)
            {
                double error = output[i] - data.targets[i];
                totalLoss += 0.5 * error * error;
            }
        }
        return totalLoss / trainingData.Count;
    }

    // Get activation function for a specific layer
    public double GetActivationFunction(double x, int layerIndex)
    {
        // If it's the output layer (last layer)
        if (layerIndex >= layerSizes.Length - 1)
        {
            return x; // Linear activation for output layer
        }
        
        // For hidden layers, use selected activation function
        switch (activationType)
        {
            case ActivationType.ReLU:
                return Math.Max(0, x);
            case ActivationType.Sigmoid:
                return 1.0 / (1.0 + Math.Exp(-x));
            case ActivationType.Tanh:
                return Math.Tanh(x);
            case ActivationType.Softplus:
                return Math.Log(1.0 + Math.Exp(x));
            default:
                return Math.Max(0, x);
        }
    }

    // Get activation function derivative for a specific layer
    public double GetActivationDerivative(double x, int layerIndex)
    {
        // If it's the output layer (last layer)
        if (layerIndex >= layerSizes.Length - 1)
        {
            return 1.0; // Linear activation derivative is 1
        }
        
        // For hidden layers, use selected activation function derivative
        switch (activationType)
        {
            case ActivationType.ReLU:
                return x > 0 ? 1.0 : 0.0;
            case ActivationType.Sigmoid:
                double sigmoid = 1.0 / (1.0 + Math.Exp(-x));
                return sigmoid * (1.0 - sigmoid);
            case ActivationType.Tanh:
                double tanh = Math.Tanh(x);
                return 1.0 - tanh * tanh;
            case ActivationType.Softplus:
                return 1.0 / (1.0 + Math.Exp(-x)); // Sigmoid function
            default:
                return x > 0 ? 1.0 : 0.0;
        }
    }

    // Add methods to access neuron states
    public double GetNeuronActivation(int layerIndex, int neuronIndex)
    {
        if (layerIndex < 0 || layerIndex >= activations.Length || 
            neuronIndex < 0 || neuronIndex >= activations[layerIndex].Length)
            return 0;
        return activations[layerIndex][neuronIndex];
    }

    public double GetNeuronWeightedInput(int layerIndex, int neuronIndex)
    {
        if (layerIndex < 0 || layerIndex >= zValues.Length || 
            neuronIndex < 0 || neuronIndex >= zValues[layerIndex].Length)
            return 0;
        return zValues[layerIndex][neuronIndex];
    }

    public double GetNeuronBias(int layerIndex, int neuronIndex)
    {
        if (layerIndex < 0 || layerIndex >= biases.Length || 
            neuronIndex < 0 || neuronIndex >= biases[layerIndex].Length)
            return 0;
        return biases[layerIndex][neuronIndex];
    }

    public double[] GetNeuronWeights(int layerIndex, int neuronIndex)
    {
        if (layerIndex < 0 || layerIndex >= weights.Length || 
            neuronIndex < 0 || neuronIndex >= layerSizes[layerIndex + 1])
            return new double[0];

        int prevLayerSize = layerSizes[layerIndex];
        double[] neuronWeights = new double[prevLayerSize];
        
        for (int i = 0; i < prevLayerSize; i++)
        {
            neuronWeights[i] = weights[layerIndex][neuronIndex * prevLayerSize + i];
        }
        
        return neuronWeights;
    }

    // Add method to get input for a specific neuron
    public double GetNeuronInput(int layerIndex, int neuronIndex)
    {
        if (layerIndex == 0)
        {
            // For input layer, return the raw input
            return activations[0][neuronIndex];
        }
        else
        {
            // For other layers, return the activation from previous layer
            // For first hidden layer, we need to get the input from the input layer
            if (layerIndex == 1)
            {
                // Get the input from the input layer (assuming single input)
                return activations[0][0];
            }
            else
            {
                // For other hidden layers, get activation from previous layer
                return activations[layerIndex - 1][neuronIndex];
            }
        }
    }

    // Add method to calculate activation for a specific neuron
    public double CalculateNeuronActivation(int layerIndex, int neuronIndex, double input)
    {
        if (layerIndex >= layerSizes.Length - 1)
        {
            return input; // Linear activation for output layer
        }
        
        // For hidden layers, use selected activation function
        switch (activationType)
        {
            case ActivationType.ReLU:
                return Math.Max(0, input);
            case ActivationType.Sigmoid:
                return 1.0 / (1.0 + Math.Exp(-input));
            case ActivationType.Tanh:
                return Math.Tanh(input);
            case ActivationType.Softplus:
                return Math.Log(1.0 + Math.Exp(input));
            default:
                return Math.Max(0, input);
        }
    }
    
    /// <summary>
    /// Get the initialized (pre-training) parameters from the last layer
    /// Returns the actual weight and bias values before any training occurs
    /// </summary>
    public void GetInitializedLastLayerParameters(out float w3, out float w4, out float b5)
    {
        if (originalWeights == null || originalBiases == null)
        {
            Debug.LogWarning("Neural network not initialized yet! Using default values.");
            w3 = 0f;
            w4 = 0f;
            b5 = 0f;
            return;
        }
        
        int lastLayerIndex = originalWeights.Length - 1;
        
        // Get the original last layer weights (W3, W4) and bias (B5) from before training
        // Assuming the last layer has 2 inputs (from hidden layer) and 1 output
        w3 = (float)originalWeights[lastLayerIndex][0]; // Weight from first hidden neuron to output
        w4 = (float)originalWeights[lastLayerIndex][1]; // Weight from second hidden neuron to output
        b5 = (float)originalBiases[lastLayerIndex][0];  // Bias for output neuron
    }
    
    /// <summary>
    /// Get the current (potentially trained) parameters from the last layer
    /// </summary>
    public void GetCurrentLastLayerParameters(out float w3, out float w4, out float b5)
    {
        if (weights == null || biases == null)
        {
            Debug.LogWarning("Neural network not initialized yet! Using default values.");
            w3 = 0f;
            w4 = 0f;
            b5 = 0f;
            return;
        }
        
        int lastLayerIndex = weights.Length - 1;
        
        // Get the current last layer weights and bias
        w3 = (float)weights[lastLayerIndex][0]; // Weight from first hidden neuron to output
        w4 = (float)weights[lastLayerIndex][1]; // Weight from second hidden neuron to output
        b5 = (float)biases[lastLayerIndex][0];  // Bias for output neuron
    }
    
    /// <summary>
    /// Capture current parameters as pre-epoch parameters (call this before training each epoch)
    /// </summary>
    public void CapturePreEpochParameters()
    {
        if (weights == null || biases == null || preEpochWeights == null || preEpochBiases == null)
        {
            Debug.LogWarning("Cannot capture pre-epoch parameters - arrays not initialized!");
            return;
        }
        
        // Log current parameters before capturing
        int lastLayerIndex = weights.Length - 1;
        float currentW3 = (float)weights[lastLayerIndex][0];
        float currentW4 = (float)weights[lastLayerIndex][1];
        float currentB5 = (float)biases[lastLayerIndex][0];
        
        for (int i = 0; i < weights.Length; i++)
        {
            Array.Copy(weights[i], preEpochWeights[i], weights[i].Length);
            Array.Copy(biases[i], preEpochBiases[i], biases[i].Length);
        }
        
        Debug.Log($"=== PRE-EPOCH PARAMETERS CAPTURED FOR EPOCH {currentEpoch} ===");
        Debug.Log($"Captured last layer: W3={currentW3:F3}, W4={currentW4:F3}, B5={currentB5:F3}");
        Debug.Log("These will be used as ball starting position in backpropagation scene");
    }
    
    /// <summary>
    /// Get the pre-epoch parameters from the last layer (parameters before current epoch's training)
    /// </summary>
    public void GetPreEpochLastLayerParameters(out float w3, out float w4, out float b5)
    {
        if (preEpochWeights == null || preEpochBiases == null)
        {
            Debug.LogWarning("Pre-epoch parameters not captured yet! Using current values.");
            GetCurrentLastLayerParameters(out w3, out w4, out b5);
            return;
        }
        
        int lastLayerIndex = preEpochWeights.Length - 1;
        
        // Get the pre-epoch last layer weights and bias
        w3 = (float)preEpochWeights[lastLayerIndex][0]; // Weight from first hidden neuron to output
        w4 = (float)preEpochWeights[lastLayerIndex][1]; // Weight from second hidden neuron to output
        b5 = (float)preEpochBiases[lastLayerIndex][0];  // Bias for output neuron
    }
    
    /// <summary>
    /// Apply chosen parameters to the last layer and notify all visualization components
    /// </summary>
    public void ApplyLastLayerParameters(float w3, float w4, float b5, bool notifyVisualizers = true)
    {
        if (weights == null || biases == null)
        {
            Debug.LogError("Cannot apply parameters - network not initialized!");
            return;
        }
        
        int lastLayerIndex = weights.Length - 1;
        
        // Store old values for comparison
        float oldW3 = (float)weights[lastLayerIndex][0];
        float oldW4 = (float)weights[lastLayerIndex][1];
        float oldB5 = (float)biases[lastLayerIndex][0];
        
        // Apply new parameters
        weights[lastLayerIndex][0] = w3;
        weights[lastLayerIndex][1] = w4;
        biases[lastLayerIndex][0] = b5;
        
        Debug.Log($"=== APPLIED LAST LAYER PARAMETERS ===");
        Debug.Log($"Old: W3={oldW3:F3}, W4={oldW4:F3}, B5={oldB5:F3}");
        Debug.Log($"New: W3={w3:F3}, W4={w4:F3}, B5={b5:F3}");
        Debug.Log($"Changes: ΔW3={w3-oldW3:F3}, ΔW4={w4-oldW4:F3}, ΔB5={b5-oldB5:F3}");
        
        // Notify visualization components if requested
        if (notifyVisualizers)
        {
            Debug.Log("Triggering OnWeightsUpdated event for visualization updates...");
            OnWeightsUpdated?.Invoke();
        }
    }
    
    /// <summary>
    /// Get pre-epoch weights for a specific neuron (before current epoch's training)
    /// </summary>
    public double[] GetPreEpochNeuronWeights(int layerIndex, int neuronIndex)
    {
        if (preEpochWeights == null || layerIndex < 0 || layerIndex >= preEpochWeights.Length || 
            neuronIndex < 0 || neuronIndex >= layerSizes[layerIndex + 1])
            return new double[0];

        int prevLayerSize = layerSizes[layerIndex];
        double[] neuronWeights = new double[prevLayerSize];
        
        for (int i = 0; i < prevLayerSize; i++)
        {
            neuronWeights[i] = preEpochWeights[layerIndex][neuronIndex * prevLayerSize + i];
        }
        
        return neuronWeights;
    }
    
    /// <summary>
    /// Get pre-epoch bias for a specific neuron (before current epoch's training)
    /// </summary>
    public double GetPreEpochNeuronBias(int layerIndex, int neuronIndex)
    {
        if (preEpochBiases == null || layerIndex < 0 || layerIndex >= preEpochBiases.Length || 
            neuronIndex < 0 || neuronIndex >= preEpochBiases[layerIndex].Length)
            return 0;
        return preEpochBiases[layerIndex][neuronIndex];
    }
}