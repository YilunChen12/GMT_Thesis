using UnityEngine;
using UnityEngine.UI;

public class NetworkManager : MonoBehaviour
{
    [Header("References")]
    private NeuralNetwork network; // Changed from public to private
    public NeuralNetworkDataManager dataManager;
    public AFVisualizer activationVisualizer;
    public InputField hiddenLayersInput;
    public InputField neuronsInput;
    public Button trainButton;
    public Button epochButton;

    [Header("UI Elements")]
    public Text epochCountText;
    public Text lossText;

    void Start()
    {
        // Get singleton reference
        network = NeuralNetwork.Instance;
        if (network == null)
        {
            Debug.LogError("NetworkManager: NeuralNetwork singleton not found!");
            return;
        }
        
        Debug.Log($"NetworkManager: Using NeuralNetwork singleton: {network.name}");
        
        trainButton.onClick.AddListener(InitializeNetwork);

        // Commented out to prevent conflicts with EpochControl.cs
        // if (epochButton != null)
        // {
        //     epochButton.onClick.AddListener(RunNextEpoch);
        // }
        
        // FIXED: Verify NetworkManager setup to prevent Scene 1 issues
        VerifyNetworkManagerSetup();
    }

    /// <summary>
    /// Verify NetworkManager setup and detect potential issues
    /// </summary>
    void VerifyNetworkManagerSetup()
    {
        Debug.Log("=== NETWORKMANAGER SETUP VERIFICATION ===");
        Debug.Log($"NetworkManager enabled: {enabled}");
        Debug.Log($"Network reference: {(network != null ? network.name : "NULL")}");
        Debug.Log($"Data manager: {(dataManager != null ? dataManager.name : "NULL")}");
        Debug.Log($"Activation visualizer: {(activationVisualizer != null ? activationVisualizer.name : "NULL")}");
        Debug.Log($"Train button: {(trainButton != null ? trainButton.name : "NULL")}");
        Debug.Log($"Train button interactable: {trainButton?.interactable ?? false}");
        
        if (network != null)
        {
            Debug.Log($"Network current epoch: {network.currentEpoch}");
            Debug.Log($"Network total epochs: {network.totalEpochs}");
            Debug.Log($"Network training data count: {network.trainingData?.Count ?? 0}");
        }
        
        Debug.Log("=== END NETWORKMANAGER SETUP VERIFICATION ===");
    }

    [ContextMenu("Debug NetworkManager State")]
    public void DebugNetworkManagerState()
    {
        Debug.Log("=== NETWORKMANAGER DEBUG STATE ===");
        Debug.Log($"Network: {(network != null ? network.name : "NULL")}");
        Debug.Log($"DataManager: {(dataManager != null ? dataManager.name : "NULL")}");
        Debug.Log($"ActivationVisualizer: {(activationVisualizer != null ? activationVisualizer.name : "NULL")}");
        Debug.Log($"Current epoch: {network?.currentEpoch ?? -1}");
        Debug.Log($"Training data loaded: {network?.trainingData?.Count ?? 0} samples");
        Debug.Log("=== END NETWORKMANAGER DEBUG STATE ===");
    }

    [ContextMenu("Test Train Button Manually")]  
    public void TestTrainButtonManually()
    {
        Debug.Log("=== MANUAL TRAIN BUTTON TEST ===");
        InitializeNetwork();
    }

    void InitializeNetwork()
    {
        if (network == null || dataManager == null)
        {
            Debug.LogError("Network or DataManager reference is missing!");
            return;
        }

        network.hiddenLayers = int.Parse(hiddenLayersInput.text);
        network.neuronsPerLayer = int.Parse(neuronsInput.text);
        
        // Load training data using the data manager
        dataManager.LoadTrainingData();
        
        // Initialize the network
        network.InitializeNetwork();

        // Reset epoch counter
        network.currentEpoch = 0;

        // Clear visualization
        if (activationVisualizer != null)
        {
            activationVisualizer.OnNetworkInitialized();
        }

        // Update UI
        UpdateUI();
    }

    void RunNextEpoch()
    {
        if (network != null)
        {
            network.RunEpoch();
            
            // Clear visualization for new epoch
            if (activationVisualizer != null)
            {
                activationVisualizer.SetupForNewEpoch(network.trainingData.Count);
            }
            
            UpdateUI();
        }
    }

    void UpdateUI()
    {
        if (epochCountText != null)
        {
            epochCountText.text = $"Epoch: {network.currentEpoch}";
        }

        if (lossText != null && dataManager != null)
        {
            var latestState = dataManager.GetLatestState();
            if (latestState != null)
            {
                lossText.text = $"Loss: {latestState.loss:F4}";
            }
        }
    }

    // Method to load a specific epoch state for your game level
    public void LoadEpochForGameLevel(int epoch)
    {
        NetworkStateSnapshot state = dataManager.LoadNetworkState(epoch);

        if (state != null)
        {
            // Here you can use the state data to build your game level
            Debug.Log($"Loaded network state from epoch {epoch} with {state.layers.Count} layers");

            // Example of accessing data for your game level
            for (int layerIdx = 0; layerIdx < state.layers.Count; layerIdx++)
            {
                LayerData layer = state.layers[layerIdx];
                for (int neuronIdx = 0; neuronIdx < layer.neurons.Count; neuronIdx++)
                {
                    NeuronData neuron = layer.neurons[neuronIdx];

                    // You can use these values to generate your game level
                    float activation = (float)neuron.activation;
                    float weightedInput = (float)neuron.weightedInput;

                    // Example: Create game objects based on neuron values
                    // Instantiate(neuronPrefab, new Vector3(layerIdx * 2, neuronIdx * 2, 0), Quaternion.identity);
                }
            }
        }
    }
}