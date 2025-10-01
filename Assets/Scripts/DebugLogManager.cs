using UnityEngine;

/// <summary>
/// Simple Debug Log Manager to control verbose logging
/// Use this to centrally enable/disable specific categories of debug logs
/// </summary>
public static class DebugLogManager
{
    // Debug categories - set to false to disable all logs in that category
    public static bool EnableParameterLogs = false;      // Parameter changes, weight updates
    public static bool EnablePositionLogs = false;       // Point positions, world coordinates
    public static bool EnableAnimationLogs = false;      // Animation progress, point movement
    public static bool EnableUILogs = false;             // UI interactions, plot updates
    public static bool EnableVRInputLogs = false;        // VR input, hand tracking
    public static bool EnablePerformanceLogs = false;    // Frame-based performance logs
    
    // Conditional logging methods
    public static void LogParameter(string message)
    {
        if (EnableParameterLogs) Debug.Log($"[PARAM] {message}");
    }
    
    public static void LogPosition(string message)
    {
        if (EnablePositionLogs) Debug.Log($"[POS] {message}");
    }
    
    public static void LogAnimation(string message)
    {
        if (EnableAnimationLogs) Debug.Log($"[ANIM] {message}");
    }
    
    public static void LogUI(string message)
    {
        if (EnableUILogs) Debug.Log($"[UI] {message}");
    }
    
    public static void LogVRInput(string message)
    {
        if (EnableVRInputLogs) Debug.Log($"[VR] {message}");
    }
    
    public static void LogPerformance(string message)
    {
        if (EnablePerformanceLogs) Debug.Log($"[PERF] {message}");
    }
    
    // Quick enable methods for debugging specific systems
    [RuntimeInitializeOnLoadMethod]
    public static void Initialize()
    {
        // Uncomment these lines to enable specific debug categories:
        // EnableParameterLogs = true;
        // EnablePositionLogs = true;
        // EnableAnimationLogs = true;
        // EnableUILogs = true;
        // EnableVRInputLogs = true;
        // EnablePerformanceLogs = true;
    }
} 