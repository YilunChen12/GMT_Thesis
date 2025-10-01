using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class LossSurfaceGenerator : MonoBehaviour
{
    [Header("Surface Configuration")]
    public Vector3 surfaceSize = new Vector3(20f, 5f, 20f); // X=W3, Y=Height, Z=W4
    public Material surfaceMaterial;
    
    [Header("Loss Visualization")]
    public Gradient lossColorGradient;
    public float maxLossForColoring = 2.0f; // Max loss value for color mapping
    public bool useLogarithmicScale = true;
    
    [Header("Performance")]
    public bool enableRealTimeUpdates = true;
    public float updateInterval = 0.1f;
    
    private BackpropagationManager backpropManager;
    private Mesh surfaceMesh;
    private Vector3[] vertices;
    private Color[] colors;
    private int[] triangles;
    private int resolution;
    private float[,] lossGrid;
    
    private float lastUpdateTime;
    
    public Vector3 SurfaceSize => surfaceSize;
    public float MaxHeight => surfaceSize.y;
    
    void Awake()
    {
        // Set up default gradient if none provided
        if (lossColorGradient.colorKeys.Length == 0)
        {
            SetupDefaultGradient();
        }
    }
    
    private void SetupDefaultGradient()
    {
        var colorKeys = new GradientColorKey[3];
        colorKeys[0] = new GradientColorKey(Color.green, 0.0f);    // Low loss
        colorKeys[1] = new GradientColorKey(Color.yellow, 0.5f);   // Medium loss
        colorKeys[2] = new GradientColorKey(Color.red, 1.0f);      // High loss
        
        var alphaKeys = new GradientAlphaKey[2];
        alphaKeys[0] = new GradientAlphaKey(0.8f, 0.0f);
        alphaKeys[1] = new GradientAlphaKey(0.8f, 1.0f);
        
        lossColorGradient.SetKeys(colorKeys, alphaKeys);
    }
    
    public void Initialize(BackpropagationManager manager, Vector2 weightRange, Vector2 biasRange, int meshResolution)
    {
        backpropManager = manager;
        resolution = meshResolution;
        
        Debug.Log($"Initializing surface with resolution {resolution}x{resolution}");
        
        CreateMesh();
        CalculateInitialLossGrid();
        UpdateMeshColors();
        
        Debug.Log("Surface initialization complete");
    }
    
    public void GenerateLossSurface()
    {
        if (backpropManager == null)
        {
            Debug.LogError("BackpropagationManager not set!");
            return;
        }
        
        Debug.Log("Generating initial loss surface...");
        
        // Pre-calculate loss values for the entire surface
        lossGrid = new float[resolution, resolution];
        Vector2 weightRange = backpropManager.WeightRange;
        
        float minLoss = float.MaxValue;
        float maxLoss = float.MinValue;
        
        // Calculate loss for each grid point
        for (int x = 0; x < resolution; x++)
        {
            for (int z = 0; z < resolution; z++)
            {
                // Convert grid coordinates to parameter values
                float w3 = Mathf.Lerp(weightRange.x, weightRange.y, (float)x / (resolution - 1));
                float w4 = Mathf.Lerp(weightRange.x, weightRange.y, (float)z / (resolution - 1));
                float b5 = backpropManager.CurrentB5; // Start with current bias value
                
                float loss = backpropManager.CalculateLoss(w3, w4, b5);
                lossGrid[x, z] = loss;
                
                minLoss = Mathf.Min(minLoss, loss);
                maxLoss = Mathf.Max(maxLoss, loss);
            }
        }
        
        // Update surface visualization
        UpdateMeshColors();
        
        Debug.Log($"Loss surface generated. Loss range: {minLoss:F3} to {maxLoss:F3}");
    }
    
    void CreateMesh()
    {
        surfaceMesh = new Mesh();
        GetComponent<MeshFilter>().mesh = surfaceMesh;
        
        // Create vertices
        int vertexCount = resolution * resolution;
        vertices = new Vector3[vertexCount];
        colors = new Color[vertexCount];
        
        for (int x = 0; x < resolution; x++)
        {
            for (int z = 0; z < resolution; z++)
            {
                int index = x * resolution + z;
                
                // Map grid coordinates to world coordinates
                float xPos = (float)x / (resolution - 1) * surfaceSize.x - surfaceSize.x * 0.5f;
                float zPos = (float)z / (resolution - 1) * surfaceSize.z - surfaceSize.z * 0.5f;
                
                vertices[index] = new Vector3(xPos, 0f, zPos);
                colors[index] = Color.white; // Will be updated by loss calculation
            }
        }
        
        // Create triangles
        int triangleCount = (resolution - 1) * (resolution - 1) * 2;
        triangles = new int[triangleCount * 3];
        int triangleIndex = 0;
        
        for (int x = 0; x < resolution - 1; x++)
        {
            for (int z = 0; z < resolution - 1; z++)
            {
                int bottomLeft = x * resolution + z;
                int bottomRight = bottomLeft + 1;
                int topLeft = (x + 1) * resolution + z;
                int topRight = topLeft + 1;
                
                // First triangle
                triangles[triangleIndex] = bottomLeft;
                triangles[triangleIndex + 1] = topLeft;
                triangles[triangleIndex + 2] = bottomRight;
                
                // Second triangle
                triangles[triangleIndex + 3] = bottomRight;
                triangles[triangleIndex + 4] = topLeft;
                triangles[triangleIndex + 5] = topRight;
                
                triangleIndex += 6;
            }
        }
        
        // Apply mesh data
        surfaceMesh.vertices = vertices;
        surfaceMesh.triangles = triangles;
        surfaceMesh.colors = colors;
        surfaceMesh.RecalculateNormals();
        surfaceMesh.RecalculateBounds();
        
        Debug.Log($"Created mesh with {vertices.Length} vertices and {triangles.Length / 3} triangles");
    }
    
    void CalculateInitialLossGrid()
    {
        if (lossGrid == null)
        {
            lossGrid = new float[resolution, resolution];
        }
        
        Vector2 weightRange = backpropManager.WeightRange;
        
        // Calculate loss for current bias level
        for (int x = 0; x < resolution; x++)
        {
            for (int z = 0; z < resolution; z++)
            {
                float w3 = Mathf.Lerp(weightRange.x, weightRange.y, (float)x / (resolution - 1));
                float w4 = Mathf.Lerp(weightRange.x, weightRange.y, (float)z / (resolution - 1));
                float b5 = backpropManager.CurrentB5;
                
                lossGrid[x, z] = backpropManager.CalculateLoss(w3, w4, b5);
            }
        }
    }
    
    public void UpdateSurfaceColoring(float currentW3, float currentW4, float currentB5)
    {
        if (!enableRealTimeUpdates) return;
        if (Time.time - lastUpdateTime < updateInterval) return;
        
        // Recalculate loss grid for new bias value
        Vector2 weightRange = backpropManager.WeightRange;
        
        for (int x = 0; x < resolution; x++)
        {
            for (int z = 0; z < resolution; z++)
            {
                float w3 = Mathf.Lerp(weightRange.x, weightRange.y, (float)x / (resolution - 1));
                float w4 = Mathf.Lerp(weightRange.x, weightRange.y, (float)z / (resolution - 1));
                
                lossGrid[x, z] = backpropManager.CalculateLoss(w3, w4, currentB5);
            }
        }
        
        UpdateMeshColors();
        lastUpdateTime = Time.time;
    }
    
    void UpdateMeshColors()
    {
        if (lossGrid == null || colors == null) return;
        
        // Find min and max loss for normalization
        float minLoss = float.MaxValue;
        float maxLoss = float.MinValue;
        
        for (int x = 0; x < resolution; x++)
        {
            for (int z = 0; z < resolution; z++)
            {
                minLoss = Mathf.Min(minLoss, lossGrid[x, z]);
                maxLoss = Mathf.Max(maxLoss, lossGrid[x, z]);
            }
        }
        
        // Update vertex colors based on loss values
        for (int x = 0; x < resolution; x++)
        {
            for (int z = 0; z < resolution; z++)
            {
                int index = x * resolution + z;
                float loss = lossGrid[x, z];
                
                float normalizedLoss;
                if (useLogarithmicScale && maxLoss > minLoss)
                {
                    // Logarithmic scale for better visualization of loss valleys
                    normalizedLoss = Mathf.Log(1 + (loss - minLoss) / (maxLoss - minLoss) * 9) / Mathf.Log(10);
                }
                else
                {
                    // Linear scale
                    normalizedLoss = (maxLoss > minLoss) ? (loss - minLoss) / (maxLoss - minLoss) : 0f;
                }
                
                normalizedLoss = Mathf.Clamp01(normalizedLoss);
                colors[index] = lossColorGradient.Evaluate(normalizedLoss);
            }
        }
        
        // Apply updated colors to mesh
        surfaceMesh.colors = colors;
        
        Debug.Log($"Updated surface colors. Loss range: {minLoss:F3} to {maxLoss:F3}");
    }
    
    /// <summary>
    /// Get the world position on the surface for given parameters
    /// </summary>
    public Vector3 GetSurfacePosition(float w3, float w4, float b5)
    {
        Vector2 weightRange = backpropManager.WeightRange;
        Vector2 biasRange = backpropManager.BiasRange;
        
        // Convert parameters to surface coordinates
        float x = Mathf.InverseLerp(weightRange.x, weightRange.y, w3) * surfaceSize.x - surfaceSize.x * 0.5f;
        float z = Mathf.InverseLerp(weightRange.x, weightRange.y, w4) * surfaceSize.z - surfaceSize.z * 0.5f;
        float y = Mathf.InverseLerp(biasRange.x, biasRange.y, b5) * surfaceSize.y;
        
        return transform.TransformPoint(new Vector3(x, y, z));
    }
    
    /// <summary>
    /// Sample the loss at a given point on the surface
    /// </summary>
    public float SampleLossAtPoint(Vector3 worldPosition)
    {
        Vector3 localPos = transform.InverseTransformPoint(worldPosition);
        
        // Convert world position to grid coordinates
        float xNorm = (localPos.x + surfaceSize.x * 0.5f) / surfaceSize.x;
        float zNorm = (localPos.z + surfaceSize.z * 0.5f) / surfaceSize.z;
        
        int x = Mathf.FloorToInt(xNorm * (resolution - 1));
        int z = Mathf.FloorToInt(zNorm * (resolution - 1));
        
        x = Mathf.Clamp(x, 0, resolution - 1);
        z = Mathf.Clamp(z, 0, resolution - 1);
        
        return lossGrid[x, z];
    }
    
    /// <summary>
    /// Highlight the current ball position on the surface
    /// </summary>
    public void HighlightPosition(Vector3 worldPosition, Color highlightColor, float radius = 1f)
    {
        // TODO: Add visual highlighting effect for ball position
        // This could be implemented with a shader or additional visual elements
    }
    
    void OnDrawGizmos()
    {
        // Draw surface bounds
        Gizmos.color = Color.white;
        Gizmos.DrawWireCube(transform.position, surfaceSize);
        
        // Draw coordinate system
        Gizmos.color = Color.red;
        Gizmos.DrawLine(transform.position - Vector3.right * surfaceSize.x * 0.6f, 
                       transform.position + Vector3.right * surfaceSize.x * 0.6f);
        
        Gizmos.color = Color.blue;
        Gizmos.DrawLine(transform.position - Vector3.forward * surfaceSize.z * 0.6f, 
                       transform.position + Vector3.forward * surfaceSize.z * 0.6f);
        
        Gizmos.color = Color.green;
        Gizmos.DrawLine(transform.position, 
                       transform.position + Vector3.up * surfaceSize.y);
    }
} 