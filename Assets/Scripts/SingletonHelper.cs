using UnityEngine;

/// <summary>
/// Helper component that automatically finds and assigns singleton references
/// Attach this to any GameObject that needs NeuralNetwork or BackpropagationManager references
/// </summary>
public class SingletonHelper : MonoBehaviour
{
    [Header("Automatic Singleton References")]
    [SerializeField] private NeuralNetwork neuralNetwork;
    [SerializeField] private BackpropagationManager backpropManager;
    
    [Header("Auto-Update Settings")]
    [Tooltip("Check for singletons every frame (for real-time updates)")]
    public bool updateEveryFrame = false;
    [Tooltip("Check for singletons periodically")]
    public float updateInterval = 1f;
    
    // Public getters for other scripts to use
    public NeuralNetwork NeuralNetwork => neuralNetwork;
    public BackpropagationManager BackpropManager => backpropManager;
    
    private float lastUpdateTime;
    
    void Start()
    {
        UpdateSingletonReferences();
    }
    
    void Update()
    {
        if (updateEveryFrame)
        {
            UpdateSingletonReferences();
        }
        else if (Time.time - lastUpdateTime > updateInterval)
        {
            UpdateSingletonReferences();
            lastUpdateTime = Time.time;
        }
    }
    
    /// <summary>
    /// Manually update singleton references - call this when needed
    /// </summary>
    public void UpdateSingletonReferences()
    {
        bool updated = false;
        
        // Update NeuralNetwork reference
        if (neuralNetwork != NeuralNetwork.Instance)
        {
            neuralNetwork = NeuralNetwork.Instance;
            if (neuralNetwork != null)
            {
                Debug.Log($"SingletonHelper: Updated NeuralNetwork reference to {neuralNetwork.name}");
                updated = true;
            }
        }
        
        // Update BackpropagationManager reference
        if (backpropManager != BackpropagationManager.Instance)
        {
            backpropManager = BackpropagationManager.Instance;
            if (backpropManager != null)
            {
                Debug.Log($"SingletonHelper: Updated BackpropagationManager reference to {backpropManager.name}");
                updated = true;
            }
        }
        
        // Notify other components on this GameObject that references have been updated
        if (updated)
        {
            NotifyComponentsOfUpdate();
        }
    }
    
    /// <summary>
    /// Notify other components that singleton references have been updated
    /// </summary>
    void NotifyComponentsOfUpdate()
    {
        // Find components that might need updating and call their refresh methods
        var componentsToUpdate = GetComponents<MonoBehaviour>();
        
        foreach (var component in componentsToUpdate)
        {
            if (component == this) continue; // Skip self
            
            // Try to call common refresh method names
            try
            {
                component.SendMessage("OnSingletonReferencesUpdated", SendMessageOptions.DontRequireReceiver);
            }
            catch (System.Exception e)
            {
                // Ignore errors - not all components will have this method
            }
        }
    }
    
    /// <summary>
    /// Get NeuralNetwork singleton - use this in other scripts
    /// </summary>
    public static NeuralNetwork GetNeuralNetwork()
    {
        return NeuralNetwork.Instance;
    }
    
    /// <summary>
    /// Get BackpropagationManager singleton - use this in other scripts
    /// </summary>
    public static BackpropagationManager GetBackpropagationManager()
    {
        return BackpropagationManager.Instance;
    }
    
    /// <summary>
    /// Check if both singletons are available
    /// </summary>
    public bool AreAllSingletonsAvailable()
    {
        return NeuralNetwork.Instance != null && BackpropagationManager.Instance != null;
    }
    
    /// <summary>
    /// Wait for singletons to be available (coroutine)
    /// </summary>
    public System.Collections.IEnumerator WaitForSingletons()
    {
        Debug.Log("Waiting for singletons to become available...");
        
        while (!AreAllSingletonsAvailable())
        {
            yield return new WaitForSeconds(0.1f);
            UpdateSingletonReferences();
        }
        
        Debug.Log("All singletons are now available!");
    }
    
    // Inspector helper - shows current singleton status
    void OnValidate()
    {
        if (Application.isPlaying)
        {
            UpdateSingletonReferences();
        }
    }
} 