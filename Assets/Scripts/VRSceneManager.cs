using UnityEngine;
using UnityEngine.SceneManagement;
using Valve.VR.InteractionSystem;
using System.Collections;

/// <summary>
/// Manages VR players across scene transitions
/// Ensures each scene uses its own VR player by destroying persisted ones from previous scenes
/// </summary>
public class VRSceneManager : MonoBehaviour
{
    [Header("Scene Management")]
    [Tooltip("Enable debug logging for VR player management")]
    public bool enableDebugLogs = true;
    
    [Tooltip("Delay before cleaning up old VR players (in seconds)")]
    public float cleanupDelay = 0.5f;
    
    void Awake()
    {
        Debug.Log("=== VR SCENE MANAGER STARTING ===");
        
        // Start cleanup process to remove any VR players from previous scenes
        StartCoroutine(CleanupAndInitialize());
    }
    
    IEnumerator CleanupAndInitialize()
    {
        string currentSceneName = SceneManager.GetActiveScene().name;
        Debug.Log($"VRSceneManager: Managing VR players for scene '{currentSceneName}'");
        
        // Wait a brief moment for scene loading to complete
        yield return new WaitForSeconds(cleanupDelay);
        
        // Find all Player objects (SteamVR VR players)
        Player[] allPlayers = FindObjectsOfType<Player>();
        Debug.Log($"Found {allPlayers.Length} VR Player objects in scene");
        
        // Find all objects with DontDestroyOnLoad component
        var dontDestroyObjects = FindObjectsOfType<Valve.VR.InteractionSystem.DontDestroyOnLoad>();
        Debug.Log($"Found {dontDestroyObjects.Length} DontDestroyOnLoad objects");
        
        // Identify VR players from previous scenes (those with DontDestroyOnLoad that aren't supposed to be here)
        foreach (Player player in allPlayers)
        {
            bool isFromPreviousScene = false;
            bool hasDontDestroy = false;
            
            // Check if this player has DontDestroyOnLoad component
            var dontDestroyComponent = player.GetComponent<Valve.VR.InteractionSystem.DontDestroyOnLoad>();
            if (dontDestroyComponent != null)
            {
                hasDontDestroy = true;
                
                // Check if the player object is in DontDestroyOnLoad scene (indicating it's from previous scene)
                if (player.gameObject.scene.name == "DontDestroyOnLoad")
                {
                    isFromPreviousScene = true;
                }
            }
            
            if (enableDebugLogs)
            {
                Debug.Log($"VR Player: {player.name}");
                Debug.Log($"  - Scene: {player.gameObject.scene.name}");
                Debug.Log($"  - Has DontDestroyOnLoad: {hasDontDestroy}");
                Debug.Log($"  - From previous scene: {isFromPreviousScene}");
            }
            
            // Destroy VR players from previous scenes
            if (isFromPreviousScene)
            {
                Debug.Log($"🗑️ DESTROYING VR Player from previous scene: {player.name}");
                
                // First, try to properly cleanup the player
                CleanupVRPlayer(player);
                
                // Then destroy the entire game object
                DestroyImmediate(player.gameObject);
            }
            else
            {
                Debug.Log($"✅ KEEPING VR Player for current scene: {player.name}");
                
                // Remove DontDestroyOnLoad component from current scene's player to prevent it from persisting
                if (dontDestroyComponent != null)
                {
                    Debug.Log($"🔧 Removing DontDestroyOnLoad from current scene's VR player: {player.name}");
                    DestroyImmediate(dontDestroyComponent);
                }
            }
        }
        
        // Wait another moment for cleanup to complete
        yield return new WaitForSeconds(0.2f);
        
        // Verify the cleanup
        VerifyVRPlayerCleanup();
        
        Debug.Log("=== VR SCENE MANAGER INITIALIZATION COMPLETE ===");
    }
    
    void CleanupVRPlayer(Player player)
    {
        try
        {
            // Disable the player first to prevent any ongoing operations
            player.gameObject.SetActive(false);
            
            // Clear any hand references
            if (player.hands != null)
            {
                foreach (var hand in player.hands)
                {
                    if (hand != null)
                    {
                        hand.gameObject.SetActive(false);
                    }
                }
            }
            
            // Clean up any audio sources
            var audioSources = player.GetComponentsInChildren<AudioSource>();
            foreach (var audioSource in audioSources)
            {
                audioSource.Stop();
            }
            
            Debug.Log($"Cleaned up VR Player components for: {player.name}");
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"Error during VR Player cleanup: {ex.Message}");
        }
    }
    
    void VerifyVRPlayerCleanup()
    {
        Player[] remainingPlayers = FindObjectsOfType<Player>();
        Debug.Log($"After cleanup: {remainingPlayers.Length} VR Player(s) remaining");
        
        foreach (Player player in remainingPlayers)
        {
            bool hasDontDestroy = player.GetComponent<Valve.VR.InteractionSystem.DontDestroyOnLoad>() != null;
            Debug.Log($"  - {player.name} (Scene: {player.gameObject.scene.name}, DontDestroy: {hasDontDestroy})");
            
            if (hasDontDestroy)
            {
                Debug.LogWarning($"⚠️ VR Player still has DontDestroyOnLoad: {player.name}");
            }
        }
        
        // Also check for any remaining DontDestroyOnLoad objects
        var remainingDontDestroy = FindObjectsOfType<Valve.VR.InteractionSystem.DontDestroyOnLoad>();
        if (remainingDontDestroy.Length > 0)
        {
            Debug.Log($"Remaining DontDestroyOnLoad objects: {remainingDontDestroy.Length}");
            foreach (var obj in remainingDontDestroy)
            {
                Debug.Log($"  - {obj.name} on {obj.gameObject.scene.name}");
            }
        }
    }
    
    void OnDestroy()
    {
        if (enableDebugLogs)
        {
            Debug.Log("VRSceneManager destroyed");
        }
    }
} 