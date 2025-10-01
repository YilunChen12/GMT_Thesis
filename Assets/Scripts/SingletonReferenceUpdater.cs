using UnityEngine;

/// <summary>
/// Utility component that ensures singleton references are properly updated after scene transitions
/// Attach this to GameObjects that need to maintain references to singletons
/// </summary>
public class SingletonReferenceUpdater : MonoBehaviour
{
    [Header("Auto-Update Settings")]
    [Tooltip("Update references every frame (for debugging)")]
    public bool updateEveryFrame = false;
    [Tooltip("Update references periodically")]
    public float updateInterval = 1f;
    
    [Header("Debug")]
    public bool enableDebugLogs = false;
    
    private float lastUpdateTime;
    private bool hasUpdatedThisScene = false;
    
    void Start()
    {
        // Update immediately on start
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
    
    void OnEnable()
    {
        // Update when component is enabled (scene transitions)
        hasUpdatedThisScene = false;
        UpdateSingletonReferences();
    }
    
    /// <summary>
    /// Manually update singleton references - call this when needed
    /// </summary>
    public void UpdateSingletonReferences()
    {
        if (hasUpdatedThisScene && !updateEveryFrame) return;
        
        bool updated = false;
        
        // Update all components on this GameObject that might need singleton references
        var components = GetComponents<MonoBehaviour>();
        
        foreach (var component in components)
        {
            if (component == this) continue; // Skip self
            
            // Try to update neural network reference
            if (UpdateNeuralNetworkReference(component))
            {
                updated = true;
            }
            
            // Try to update backpropagation manager reference
            if (UpdateBackpropagationManagerReference(component))
            {
                updated = true;
            }
        }
        
        if (updated && enableDebugLogs)
        {
            Debug.Log($"SingletonReferenceUpdater: Updated singleton references for {gameObject.name}");
        }
        
        hasUpdatedThisScene = true;
    }
    
    /// <summary>
    /// Update neural network reference using reflection
    /// </summary>
    bool UpdateNeuralNetworkReference(MonoBehaviour component)
    {
        var type = component.GetType();
        var fields = type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        bool updated = false;
        
        foreach (var field in fields)
        {
            if (field.FieldType == typeof(NeuralNetwork))
            {
                var currentValue = (NeuralNetwork)field.GetValue(component);
                
                // Update if null or not the singleton
                if (currentValue != NeuralNetwork.Instance)
                {
                    field.SetValue(component, NeuralNetwork.Instance);
                    
                    if (enableDebugLogs)
                    {
                        Debug.Log($"Updated {type.Name}.{field.Name} to NeuralNetwork singleton");
                    }
                    
                    updated = true;
                }
            }
        }
        
        return updated;
    }
    
    /// <summary>
    /// Update backpropagation manager reference using reflection
    /// </summary>
    bool UpdateBackpropagationManagerReference(MonoBehaviour component)
    {
        var type = component.GetType();
        var fields = type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        bool updated = false;
        
        foreach (var field in fields)
        {
            if (field.FieldType == typeof(BackpropagationManager))
            {
                var currentValue = (BackpropagationManager)field.GetValue(component);
                
                // Update if null or not the singleton
                if (currentValue != BackpropagationManager.Instance)
                {
                    field.SetValue(component, BackpropagationManager.Instance);
                    
                    if (enableDebugLogs)
                    {
                        Debug.Log($"Updated {type.Name}.{field.Name} to BackpropagationManager singleton");
                    }
                    
                    updated = true;
                }
            }
        }
        
        return updated;
    }
    
    /// <summary>
    /// Check if all critical singletons are available
    /// </summary>
    public bool AreSingletonsAvailable()
    {
        return NeuralNetwork.Instance != null;
    }
    
    /// <summary>
    /// Wait for singletons to be available (coroutine)
    /// </summary>
    public System.Collections.IEnumerator WaitForSingletons()
    {
        Debug.Log("Waiting for singletons to become available...");
        
        while (!AreSingletonsAvailable())
        {
            yield return new WaitForSeconds(0.1f);
        }
        
        Debug.Log("All singletons are now available!");
        UpdateSingletonReferences();
    }
} 