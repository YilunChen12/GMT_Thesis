using UnityEngine;

/// <summary>
/// Component to identify and manage landmarks in the parameter space
/// </summary>
public class LandmarkIndicator : MonoBehaviour
{
    public enum LandmarkType
    {
        Optimal,        // All parameters optimized (post-training)
        W3Optimal,      // Only W3 optimized, others at ball position
        W4Optimal,      // Only W4 optimized, others at ball position
        B5Optimal,      // Only B5 optimized, others at ball position
        Progressive,    // Progressive landmark that moves through stages
        LocalMin,       // Local minimum (for educational purposes)
        SaddlePoint,    // Saddle point (for educational purposes)
        Random          // Random position (for educational purposes)
    }
    
    [Header("Landmark Properties")]
    public LandmarkType landmarkType = LandmarkType.Optimal;
    public Vector3 parameterPosition; // World position in parameter space
    public string landmarkDescription = "";
    
    [Header("Interaction")]
    public bool isSnappable = true;
    public float snapRadius = 0.3f;
    
    [Header("Visual")]
    public Color landmarkColor = Color.green;
    public string displayLabel = "";
    
    // Events
    public System.Action<LandmarkIndicator> OnLandmarkReached;
    
    void Start()
    {
        // Set display label based on type if not already set
        if (string.IsNullOrEmpty(displayLabel))
        {
            switch (landmarkType)
            {
                case LandmarkType.Optimal:
                    displayLabel = "ALL OPTIMAL";
                    landmarkColor = Color.green;
                    break;
                case LandmarkType.W3Optimal:
                    displayLabel = "W3 OPTIMAL";
                    landmarkColor = Color.blue;
                    break;
                case LandmarkType.W4Optimal:
                    displayLabel = "W4 OPTIMAL";
                    landmarkColor = Color.cyan;
                    break;
                case LandmarkType.B5Optimal:
                    displayLabel = "B5 OPTIMAL";
                    landmarkColor = Color.magenta;
                    break;
                case LandmarkType.LocalMin:
                    displayLabel = "LOCAL MIN";
                    landmarkColor = Color.yellow;
                    break;
                case LandmarkType.SaddlePoint:
                    displayLabel = "SADDLE";
                    landmarkColor = Color.black;
                    break;
                case LandmarkType.Random:
                    displayLabel = "RANDOM";
                    landmarkColor = Color.red;
                    break;
            }
        }
    }
    
    /// <summary>
    /// Check if a position is within snap range of this landmark
    /// </summary>
    public bool IsWithinSnapRange(Vector3 position)
    {
        if (!isSnappable) return false;
        
        float distance = Vector3.Distance(position, transform.position);
        return distance <= snapRadius;
    }
    
    /// <summary>
    /// Get the parameter values at this landmark position
    /// </summary>
    public Vector3 GetParameterValues()
    {
        // This would need to be implemented based on your parameter space mapping
        // For now, return the world position as a placeholder
        return parameterPosition;
    }

    /// <summary>
    /// Trigger when ball reaches this landmark
    /// </summary>
    public void TriggerLandmarkReached()
    {
        Debug.Log($"Landmark reached: {landmarkType} at {transform.position}");
        OnLandmarkReached?.Invoke(this);
    }

    void OnDrawGizmos()
    {
        // Draw snap radius
        Gizmos.color = landmarkColor;
        Gizmos.DrawWireSphere(transform.position, snapRadius);
        
        // Draw landmark type indicator
        Gizmos.color = landmarkColor;
        Gizmos.DrawSphere(transform.position, 0.1f);
    }
} 