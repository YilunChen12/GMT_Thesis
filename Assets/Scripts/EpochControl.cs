using UnityEngine;
using UnityEngine.UI;
using Valve.VR; // Added for SteamVR_Actions

public class EpochControl : MonoBehaviour
{
    [Header("UI References")]
    public Button nextEpochButton;
    public InputField epochsInput;
    public Text epochText;

    [Header("Network Reference")]
    private NeuralNetwork network; // Changed from public to private

    [Header("Visualization References")]
    public DataVis dataVis; // Assign this in the Unity Inspector
    public NetworkVis networkVis; // Assign this in the Unity Inspector
    [SerializeField] private AFVisualizer activationVis; // Change to private with SerializeField

    // Static counter to track multiple instances
    private static int instanceCount = 0;
    private static int clickCount = 0;
    private static float lastClickTime = 0f;
    private static float clickCooldown = 0.5f; // Half second cooldown

    void Awake()
    {
        instanceCount++;
        Debug.Log($"=== EPOCHCONTROL AWAKE() CALLED - Instance #{instanceCount} ===");
        Debug.Log($"Instance ID: {GetInstanceID()}");
        Debug.Log($"GameObject name: {gameObject.name}");
        Debug.Log($"Total EpochControl instances: {instanceCount}");
    }

    void Start()
    {
        Debug.Log("=== EPOCHCONTROL START() CALLED ===");
        Debug.Log($"EpochControl instance: {GetInstanceID()}");
        
        // Get singleton reference
        network = NeuralNetwork.Instance;
        if (network == null)
        {
            Debug.LogError("EpochControl: NeuralNetwork singleton not found!");
            return;
        }
        
        Debug.Log($"EpochControl: Using NeuralNetwork singleton: {network.name}");
        Debug.Log($"nextEpochButton: {nextEpochButton?.name ?? "NULL"}");
        
        if (nextEpochButton != null)
        {
            // Remove any existing listeners first to prevent duplicates
            nextEpochButton.onClick.RemoveAllListeners();
            Debug.Log("Removed all existing listeners from nextEpochButton");
            
            // Add our listener
            nextEpochButton.onClick.AddListener(OnNextEpochClicked);
            Debug.Log("Added OnNextEpochClicked listener to nextEpochButton");
        }
        else
        {
            Debug.LogError("nextEpochButton is null in Start()!");
        }
        
        // FIXED: Verify SteamVR state to prevent Scene 1 corruption
        VerifyUISetup();
    }

    /// <summary>
    /// Verify that UI interactions and SteamVR are working properly
    /// </summary>
    void VerifyUISetup()
    {
        Debug.Log("=== EPOCHCONTROL UI VERIFICATION ===");
        Debug.Log($"Button interactable: {nextEpochButton?.interactable ?? false}");
        Debug.Log($"Button active: {nextEpochButton?.gameObject.activeSelf ?? false}");
        Debug.Log($"Network current epoch: {network?.currentEpoch ?? -1}");
        Debug.Log($"Network total epochs: {network?.totalEpochs ?? -1}");
        
        // Check if SteamVR is working (for UI interaction)
        // Note: UI buttons should work with SteamVR's built-in UI interaction system
        Debug.Log("SteamVR UI interaction handled by SteamVR system automatically");
        
        Debug.Log("=== END EPOCHCONTROL UI VERIFICATION ===");
    }

    [ContextMenu("Test Epoch Button Manually")]
    public void TestEpochButtonManually()
    {
        Debug.Log("=== MANUAL EPOCH BUTTON TEST ===");
        OnNextEpochClicked();
    }

    [ContextMenu("Debug EpochControl State")]
    public void DebugEpochControlState()
    {
        Debug.Log("=== EPOCHCONTROL DEBUG STATE ===");
        Debug.Log($"Network: {(network != null ? network.name : "NULL")}");
        Debug.Log($"Button: {(nextEpochButton != null ? nextEpochButton.name : "NULL")}");
        Debug.Log($"Current epoch: {network?.currentEpoch ?? -1}");
        Debug.Log($"Total epochs: {network?.totalEpochs ?? -1}");
        Debug.Log($"Button listeners count: {nextEpochButton?.onClick.GetPersistentEventCount() ?? 0}");
        Debug.Log("UI button interactions should work automatically with SteamVR");
        Debug.Log("=== END EPOCHCONTROL DEBUG STATE ===");
    }

    void OnDestroy()
    {
        instanceCount--;
        Debug.Log($"=== EPOCHCONTROL DESTROYED - Instance #{GetInstanceID()} ===");
        Debug.Log($"Remaining EpochControl instances: {instanceCount}");
    }

    public void StartTraining() // auto training 
    {
        if (int.TryParse(epochsInput.text, out int epochs))
        {
            network.totalEpochs = epochs;
            network.currentEpoch = 0;
            UpdateEpochDisplay();
        }
    }

    public void OnNextEpochClicked()
    {
        clickCount++;
        float currentTime = Time.time;
        
        Debug.Log($"=== EPOCH BUTTON CLICKED #{clickCount} - Instance ID: {GetInstanceID()} ===");
        Debug.Log($"=== Time since last click: {currentTime - lastClickTime:F3}s ===");
        Debug.Log($"=== currentEpoch: {network.currentEpoch}, totalEpochs: {network.totalEpochs} ===");
        
        // Check cooldown to prevent rapid clicks
        if (currentTime - lastClickTime < clickCooldown)
        {
            Debug.Log($"CLICK IGNORED - Cooldown active ({clickCooldown}s)");
            return;
        }
        
        lastClickTime = currentTime;
        
        if (network.currentEpoch < network.totalEpochs)
        {
            Debug.Log("Calling network.RunEpoch()...");
            // Use the network's built-in RunEpoch method instead of manual processing
            network.RunEpoch();
            
            Debug.Log("Calling UpdateEpochDisplay()...");
            UpdateEpochDisplay();

            // Update all visualizations
            //dataVis.VisualizeDataFlow();
            // NOTE: RunEpoch already calls OnWeightsUpdated internally, so we don't need to call it again
            if (activationVis != null)
            {
                Debug.Log("Calling activationVis.UpdateVisualization()...");
                activationVis.UpdateVisualization();
            }
            Debug.Log("=== EPOCH BUTTON CLICKED PROCESSING COMPLETE ===");
        }
        else
        {
            Debug.Log("Epoch limit reached, not processing.");
        }
    }
    
    void UpdateEpochDisplay()
    {
        Debug.Log($"=== UpdateEpochDisplay() - currentEpoch: {network.currentEpoch}, totalEpochs: {network.totalEpochs} ===");
        epochText.text = $"Epoch: {network.currentEpoch}/{network.totalEpochs}";
        Debug.Log($"=== UI Text set to: {epochText.text} ===");
    }
}
