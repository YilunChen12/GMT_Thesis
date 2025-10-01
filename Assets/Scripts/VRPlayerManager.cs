using UnityEngine;
using Valve.VR.InteractionSystem;

/// <summary>
/// Simple VR Player Manager for scene-specific VR player setup
/// Works with VRSceneManager to ensure each scene has its own VR player
/// </summary>
public class VRPlayerManager : MonoBehaviour
{
    [Header("VR Player Settings")]
    [Tooltip("VR player components for this scene")]
    public GameObject vrPlayerRoot;
    public Transform leftHand;
    public Transform rightHand;
    
    [Header("Scene Configuration")]
    [Tooltip("Is this the backpropagation scene VR player?")]
    public bool isBackpropagationVRPlayer = false;
    
    [Header("Debug")]
    public bool enableDebugLogs = true;
    
    public static VRPlayerManager Instance { get; private set; }
    
    void Awake()
    {
        // Simple singleton for current scene only (no DontDestroyOnLoad)
        if (Instance == null)
        {
            Instance = this;
            
            if (enableDebugLogs)
            {
                Debug.Log($"VRPlayerManager initialized for scene: {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}");
                Debug.Log($"Is backpropagation VR player: {isBackpropagationVRPlayer}");
            }
        }
        else
        {
            Debug.LogWarning($"Multiple VRPlayerManager instances detected in scene - destroying duplicate: {gameObject.name}");
            Destroy(gameObject);
            return;
        }
        
        // Auto-detect VR player components if not assigned
        if (vrPlayerRoot == null)
        {
            vrPlayerRoot = gameObject;
        }
        
        // Remove any DontDestroyOnLoad component to ensure this VR player stays in this scene
        RemoveDontDestroyOnLoad();
        
        // Auto-detect hands
        AutoDetectHands();
    }
    
    void Start()
    {
        if (enableDebugLogs)
        {
            LogVRPlayerConfiguration();
        }
        
        // FIXED: Automatically manage input managers to prevent conflicts
        ManageInputManagers();
    }
    
    /// <summary>
    /// Remove DontDestroyOnLoad component to ensure scene-specific VR players
    /// </summary>
    void RemoveDontDestroyOnLoad()
    {
        // Check the VR player root and all parent objects for DontDestroyOnLoad components
        Transform current = transform;
        while (current != null)
        {
            var dontDestroyComponent = current.GetComponent<Valve.VR.InteractionSystem.DontDestroyOnLoad>();
            if (dontDestroyComponent != null)
            {
                Debug.Log($"🔧 Removing DontDestroyOnLoad from {current.name} to make VR player scene-specific");
                DestroyImmediate(dontDestroyComponent);
            }
            current = current.parent;
        }
        
        // Also check the Player component
        Player player = GetComponentInParent<Player>();
        if (player != null)
        {
            var dontDestroyComponent = player.GetComponent<Valve.VR.InteractionSystem.DontDestroyOnLoad>();
            if (dontDestroyComponent != null)
            {
                Debug.Log($"🔧 Removing DontDestroyOnLoad from Player component: {player.name}");
                DestroyImmediate(dontDestroyComponent);
            }
        }
    }
    
    /// <summary>
    /// Auto-detect left and right hands if not manually assigned
    /// </summary>
    void AutoDetectHands()
    {
        if (leftHand == null || rightHand == null)
        {
            var hands = GetComponentsInChildren<Hand>();
            
            foreach (var hand in hands)
            {
                if (hand.handType == Valve.VR.SteamVR_Input_Sources.LeftHand && leftHand == null)
                {
                    leftHand = hand.transform;
                    if (enableDebugLogs) Debug.Log($"Auto-detected left hand: {hand.name}");
                }
                else if (hand.handType == Valve.VR.SteamVR_Input_Sources.RightHand && rightHand == null)
                {
                    rightHand = hand.transform;
                    if (enableDebugLogs) Debug.Log($"Auto-detected right hand: {hand.name}");
                }
            }
        }
    }
    
    /// <summary>
    /// Log current VR player configuration for debugging
    /// </summary>
    void LogVRPlayerConfiguration()
    {
        Debug.Log("=== VR PLAYER CONFIGURATION ===");
        Debug.Log($"Scene: {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}");
        Debug.Log($"VR Player Root: {vrPlayerRoot?.name}");
        Debug.Log($"Left Hand: {leftHand?.name}");
        Debug.Log($"Right Hand: {rightHand?.name}");
        Debug.Log($"Is Backprop VR Player: {isBackpropagationVRPlayer}");
        
        // List key components for the new input managers
        var forwardInputManager = GetComponent<ForwardPropagationInputManager>();
        var backpropInputManager = GetComponent<BackpropagationInputManager>();
        
        Debug.Log($"ForwardPropagationInputManager: {forwardInputManager != null}");
        Debug.Log($"BackpropagationInputManager: {backpropInputManager != null}");
        
        // Check for old PlayerInputManager
        var oldPlayerInputManager = GetComponent<PlayerInputManager>();
        if (oldPlayerInputManager != null)
        {
            Debug.LogWarning("⚠️ Old PlayerInputManager detected - should be replaced with scene-specific input manager");
        }
        
        if (isBackpropagationVRPlayer)
        {
            Debug.Log("This VR player is configured for backpropagation scene");
            if (backpropInputManager == null)
            {
                Debug.LogWarning("⚠️ Backpropagation VR player missing BackpropagationInputManager component");
            }
        }
        else
        {
            Debug.Log("This VR player is configured for forward propagation scene");
            if (forwardInputManager == null)
            {
                Debug.LogWarning("⚠️ Forward propagation VR player missing ForwardPropagationInputManager component");
            }
        }
        
        // Check if this player has been properly cleaned of DontDestroyOnLoad
        Player player = GetComponentInParent<Player>();
        if (player != null)
        {
            var dontDestroyComponent = player.GetComponent<Valve.VR.InteractionSystem.DontDestroyOnLoad>();
            if (dontDestroyComponent != null)
            {
                Debug.LogWarning($"⚠️ VR Player still has DontDestroyOnLoad component: {player.name}");
            }
            else
            {
                Debug.Log("✅ VR Player properly configured as scene-specific (no DontDestroyOnLoad)");
            }
        }
        
        Debug.Log("=== END VR PLAYER CONFIGURATION ===");
    }
    
    /// <summary>
    /// Automatically disable conflicting input managers based on scene type
    /// </summary>
    void ManageInputManagers()
    {
        var forwardInputManager = GetComponent<ForwardPropagationInputManager>();
        var backpropInputManager = GetComponent<BackpropagationInputManager>();
        
        if (isBackpropagationVRPlayer)
        {
            // In backpropagation scene - keep BackpropagationInputManager, disable ForwardPropagationInputManager
            if (backpropInputManager != null)
            {
                backpropInputManager.enabled = true;
                Debug.Log("✅ Enabled BackpropagationInputManager for backprop scene");
            }
            else
            {
                Debug.LogWarning("⚠️ BackpropagationInputManager not found in backprop scene!");
            }
            
            if (forwardInputManager != null)
            {
                forwardInputManager.enabled = false;
                Debug.Log("🔧 Disabled ForwardPropagationInputManager in backprop scene");
            }
        }
        else
        {
            // In forward propagation scene - keep ForwardPropagationInputManager, disable BackpropagationInputManager
            if (forwardInputManager != null)
            {
                forwardInputManager.enabled = true;
                Debug.Log("✅ Enabled ForwardPropagationInputManager for forward scene");
            }
            else
            {
                Debug.LogWarning("⚠️ ForwardPropagationInputManager not found in forward scene!");
            }
            
            if (backpropInputManager != null)
            {
                backpropInputManager.enabled = false;
                Debug.Log("🔧 Disabled BackpropagationInputManager in forward scene");
            }
        }
        
        // Check for old PlayerInputManager (should not be used anymore)
        var oldPlayerInputManager = GetComponent<PlayerInputManager>();
        if (oldPlayerInputManager != null)
        {
            oldPlayerInputManager.enabled = false;
            Debug.LogWarning("🔧 Found and disabled old PlayerInputManager - should be removed from VR player prefab");
        }
    }
    
    /// <summary>
    /// Get the right hand transform for UI attachment
    /// </summary>
    public Transform GetRightHand()
    {
        if (rightHand == null)
        {
            AutoDetectHands();
        }
        return rightHand;
    }
    
    /// <summary>
    /// Get the left hand transform for UI attachment  
    /// </summary>
    public Transform GetLeftHand()
    {
        if (leftHand == null)
        {
            AutoDetectHands();
        }
        return leftHand;
    }
    
    /// <summary>
    /// Check if this is the backpropagation scene VR player
    /// </summary>
    public bool IsBackpropagationVRPlayer()
    {
        return isBackpropagationVRPlayer;
    }
    
    void OnDestroy()
    {
        // Clear the singleton when this instance is destroyed
        if (Instance == this)
        {
            Instance = null;
        }
        
        if (enableDebugLogs)
        {
            Debug.Log($"VRPlayerManager destroyed for scene: {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}");
        }
    }
} 