using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Manages 3D parameter box visualization with optimized loss calculation
/// Replaces the 2D loss surface with proper 3D parameter space
/// </summary>
public class ParameterBoxManager : MonoBehaviour
{
    [Header("Parameter Box Settings")]
    public Vector3 boxSize = new Vector3(10f, 6f, 10f); // w3, b5, w4
    public Material boxMaterial;
    
    // Public property to access box size
    public Vector3 BoxSize => boxSize;
    
    [Header("Loss Grid Optimization")]
    [Tooltip("Resolution of the pre-computed loss grid")]
    public int gridResolution = 50;
    [Tooltip("Points to calculate per frame to avoid lag")]
    public int pointsPerFrame = 1000;
    [Tooltip("Update interval for background grid updates")]
    public float backgroundUpdateInterval = 0.5f;
    
    [Header("Adaptive LOD")]
    [Tooltip("High detail radius around ball")]
    public float highDetailRadius = 2f;
    [Tooltip("High detail grid resolution")]
    public int highDetailResolution = 25;
    
    [Header("Ball Color Coding")]
    public Gradient lossGradient;
    public float maxLossForColoring = 3f;
    
    [Header("Landmark System")]
    [Tooltip("Landmark prefab for optimal value indicators")]
    public GameObject landmarkPrefab;
    [Tooltip("Material for optimal value landmarks")]
    public Material optimalLandmarkMaterial;
    [Tooltip("Size of landmark indicators")]
    public float landmarkSize = 0.5f;
    [Tooltip("Distance threshold for ball to snap to landmark")]
    public float landmarkSnapDistance = 0.3f;
    [Tooltip("Whether landmarks are currently active")]
    public bool landmarksActive = true;
    
    // Progressive landmark game system
    private GameObject progressiveLandmark;
    private int currentLandmarkStage = 1; // 1, 2, or 3
    private int firstOptimizedParameter; // 0=w3, 1=w4, 2=b5
    private float[] landmarkParameters = new float[3]; // w3, w4, b5 for current landmark position
    private Vector3 progressiveLandmarkPosition;
    private bool landmarkGameCompleted = false;
    
    // Events for landmark progression
    public System.Action<int> OnLandmarkStageChanged; // Fired when landmark moves to next stage
    public System.Action OnLandmarkGameCompleted; // Fired when all parameters are optimized
    
    // 3D Loss Grid Cache
    private float[,,] lossGrid;
    private bool[,,] gridCalculated;
    private Vector3Int gridSize;
    
    // High-detail adaptive grid around ball
    private float[,,] highDetailGrid;
    private Vector3 lastHighDetailCenter;
    private bool highDetailGridValid = false;
    
    // References
    private BackpropagationManager backpropManager;
    private GameObject parameterBox;
    private LineRenderer[] boxEdges;
    
    // Landmark system
    private List<GameObject> landmarks = new List<GameObject>();
    private GameObject optimalLandmark;
    private GameObject w3OptimalLandmark;
    private GameObject w4OptimalLandmark;
    private GameObject b5OptimalLandmark;
    private Vector3 optimalWorldPosition;
    private bool optimalPositionSet = false;
    
    // Performance tracking
    private Coroutine backgroundUpdateCoroutine;
    private Queue<Vector3Int> updateQueue = new Queue<Vector3Int>();
    private bool isCalculating = false;
    
    // Events
    public System.Action<float> OnLossCalculated;
    
    void Awake()
    {
        SetupDefaultGradient();
        InitializeGrids();
    }
    
    void SetupDefaultGradient()
    {
        if (lossGradient.colorKeys.Length == 0)
        {
            var colorKeys = new GradientColorKey[3];
            colorKeys[0] = new GradientColorKey(Color.green, 0.0f);    // Low loss
            colorKeys[1] = new GradientColorKey(Color.yellow, 0.5f);   // Medium loss
            colorKeys[2] = new GradientColorKey(Color.red, 1.0f);      // High loss
            
            var alphaKeys = new GradientAlphaKey[2];
            alphaKeys[0] = new GradientAlphaKey(1f, 0.0f);
            alphaKeys[1] = new GradientAlphaKey(1f, 1.0f);
            
            lossGradient.SetKeys(colorKeys, alphaKeys);
        }
    }
    
    void InitializeGrids()
    {
        gridSize = new Vector3Int(gridResolution, gridResolution, gridResolution);
        
        // Main loss grid
        lossGrid = new float[gridSize.x, gridSize.y, gridSize.z];
        gridCalculated = new bool[gridSize.x, gridSize.y, gridSize.z];
        
        // High detail grid
        highDetailGrid = new float[highDetailResolution, highDetailResolution, highDetailResolution];
        
        Debug.Log($"Initialized parameter box grids: {gridSize.x}x{gridSize.y}x{gridSize.z}");
    }
    
    public void Initialize(BackpropagationManager manager)
    {
        backpropManager = manager;
        CreateParameterBox();
        
        // Create landmarks for navigation
        CreateLandmarks();
        
        // Start background grid calculation
        if (backgroundUpdateCoroutine != null) StopCoroutine(backgroundUpdateCoroutine);
        backgroundUpdateCoroutine = StartCoroutine(BackgroundGridUpdate());
        
        Debug.Log("ParameterBoxManager initialized");
    }
    
    void CreateParameterBox()
    {
        // Create wireframe box to show parameter space bounds
        parameterBox = new GameObject("ParameterBox");
        parameterBox.transform.SetParent(transform);
        
        // Create teleportable ground surface
        CreateTeleportableSurfaces();
        
        // Create 12 edges of the box using LineRenderers
        boxEdges = new LineRenderer[12];
        
        Vector3[] corners = GetBoxCorners();
        int[,] edges = GetBoxEdgeIndices();
        
        for (int i = 0; i < 12; i++)
        {
            GameObject edgeObj = new GameObject($"BoxEdge_{i}");
            edgeObj.transform.SetParent(parameterBox.transform);
            
            LineRenderer lr = edgeObj.AddComponent<LineRenderer>();
            lr.material = boxMaterial ?? CreateDefaultLineMaterial();
            lr.startWidth = 0.05f;
            lr.endWidth = 0.05f;
            lr.positionCount = 2;
            lr.useWorldSpace = true;
            
            int startCorner = edges[i, 0];
            int endCorner = edges[i, 1];
            
            lr.SetPosition(0, transform.TransformPoint(corners[startCorner]));
            lr.SetPosition(1, transform.TransformPoint(corners[endCorner]));
            
            boxEdges[i] = lr;
        }
        
        Debug.Log("Parameter box wireframe created");
    }
    
    Vector3[] GetBoxCorners()
    {
        Vector3 halfSize = boxSize * 0.5f;
        return new Vector3[]
        {
            new Vector3(-halfSize.x, -halfSize.y, -halfSize.z), // 0: min corner
            new Vector3( halfSize.x, -halfSize.y, -halfSize.z), // 1
            new Vector3( halfSize.x,  halfSize.y, -halfSize.z), // 2
            new Vector3(-halfSize.x,  halfSize.y, -halfSize.z), // 3
            new Vector3(-halfSize.x, -halfSize.y,  halfSize.z), // 4
            new Vector3( halfSize.x, -halfSize.y,  halfSize.z), // 5
            new Vector3( halfSize.x,  halfSize.y,  halfSize.z), // 6: max corner
            new Vector3(-halfSize.x,  halfSize.y,  halfSize.z)  // 7
        };
    }
    
    int[,] GetBoxEdgeIndices()
    {
        return new int[,]
        {
            {0,1}, {1,2}, {2,3}, {3,0}, // Bottom face
            {4,5}, {5,6}, {6,7}, {7,4}, // Top face
            {0,4}, {1,5}, {2,6}, {3,7}  // Vertical edges
        };
    }
    
    Material CreateDefaultLineMaterial()
    {
        Material mat = new Material(Shader.Find("Sprites/Default"));
        mat.color = Color.white;
        return mat;
    }
    
    void CreateTeleportableSurfaces()
    {
        // Create ground plane for teleportation
        GameObject ground = new GameObject("TeleportableGround");
        ground.transform.SetParent(parameterBox.transform);
        ground.layer = LayerMask.NameToLayer("Teleportable") != -1 ? LayerMask.NameToLayer("Teleportable") : 0;
        
        // Position at bottom of parameter box
        ground.transform.localPosition = new Vector3(0, -boxSize.y * 0.5f, 0);
        ground.transform.localScale = new Vector3(boxSize.x * 0.1f, 1f, boxSize.z * 0.1f); // Unity plane is 10x10 units
        
        // Add mesh components
        MeshFilter meshFilter = ground.AddComponent<MeshFilter>();
        meshFilter.mesh = Resources.GetBuiltinResource<Mesh>("Plane.fbx");
        
        MeshRenderer meshRenderer = ground.AddComponent<MeshRenderer>();
        meshRenderer.material = CreateGroundMaterial();
        
        // Add collider for teleportation
        MeshCollider meshCollider = ground.AddComponent<MeshCollider>();
        meshCollider.convex = false;
        
        Debug.Log($"Created teleportable ground at layer {ground.layer}");
        
        // Optional: Create additional surfaces (walls) for more teleportation options
        CreateWallSurfaces();
    }
    
    void CreateWallSurfaces()
    {
        // Create invisible wall colliders around the parameter box for teleportation
        Vector3[] wallPositions = {
            new Vector3(boxSize.x * 0.5f, 0, 0),    // Right wall
            new Vector3(-boxSize.x * 0.5f, 0, 0),   // Left wall
            new Vector3(0, 0, boxSize.z * 0.5f),    // Front wall
            new Vector3(0, 0, -boxSize.z * 0.5f)    // Back wall
        };
        
        Vector3[] wallRotations = {
            new Vector3(0, 0, 90),   // Right wall
            new Vector3(0, 0, -90),  // Left wall
            new Vector3(90, 0, 0),   // Front wall
            new Vector3(-90, 0, 0)   // Back wall
        };
        
        for (int i = 0; i < wallPositions.Length; i++)
        {
            GameObject wall = new GameObject($"TeleportableWall_{i}");
            wall.transform.SetParent(parameterBox.transform);
            wall.layer = LayerMask.NameToLayer("Teleportable") != -1 ? LayerMask.NameToLayer("Teleportable") : 0;
            
            wall.transform.localPosition = wallPositions[i];
            wall.transform.localRotation = Quaternion.Euler(wallRotations[i]);
            
            // Scale to match box dimensions
            float wallWidth = (i < 2) ? boxSize.z : boxSize.x;
            wall.transform.localScale = new Vector3(wallWidth * 0.1f, 1f, boxSize.y * 0.1f);
            
            // Add invisible mesh for teleportation
            MeshFilter meshFilter = wall.AddComponent<MeshFilter>();
            meshFilter.mesh = Resources.GetBuiltinResource<Mesh>("Plane.fbx");
            
            // Make walls invisible but keep colliders
            MeshRenderer meshRenderer = wall.AddComponent<MeshRenderer>();
            Material invisibleMat = new Material(Shader.Find("Sprites/Default"));
            invisibleMat.color = new Color(0, 0, 0, 0); // Transparent
            meshRenderer.material = invisibleMat;
            meshRenderer.enabled = false; // Completely hide walls
            
            // Add collider for teleportation
            MeshCollider meshCollider = wall.AddComponent<MeshCollider>();
            meshCollider.convex = false;
        }
        
        Debug.Log("Created wall surfaces for teleportation");
    }
    
    Material CreateGroundMaterial()
    {
        Material mat = new Material(Shader.Find("Standard"));
        mat.color = new Color(0.3f, 0.3f, 0.3f, 0.5f); // Semi-transparent gray
        mat.SetFloat("_Mode", 2); // Set to Fade mode for transparency
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.EnableKeyword("_ALPHABLEND_ON");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.renderQueue = 3000;
        return mat;
    }
    
    /// <summary>
    /// Get optimized loss value for given parameters using grid interpolation
    /// </summary>
    public float GetLossAt(float w3, float w4, float b5)
    {
        // Check if we should use high-detail grid
        Vector3 worldPos = ParametersToWorldPosition(w3, w4, b5);
        Vector3 ballPos = worldPos; // Assume ball is at this position
        
        if (Vector3.Distance(ballPos, lastHighDetailCenter) <= highDetailRadius && highDetailGridValid)
        {
            return SampleHighDetailGrid(w3, w4, b5);
        }
        else
        {
            return SampleMainGrid(w3, w4, b5);
        }
    }
    
    float SampleMainGrid(float w3, float w4, float b5)
    {
        // Convert parameters to grid coordinates
        Vector3 gridPos = ParametersToGridCoordinates(w3, w4, b5);
        
        // Trilinear interpolation
        return TrilinearInterpolate(lossGrid, gridPos);
    }
    
    float SampleHighDetailGrid(float w3, float w4, float b5)
    {
        // Convert to high-detail grid coordinates relative to center
        Vector3 localParams = new Vector3(w3, w4, b5) - lastHighDetailCenter;
        Vector3 gridStep = new Vector3(boxSize.x / highDetailResolution, boxSize.y / highDetailResolution, boxSize.z / highDetailResolution);
        Vector3 gridPos = new Vector3(localParams.x / gridStep.x, localParams.y / gridStep.y, localParams.z / gridStep.z) + Vector3.one * (highDetailResolution / 2f);
        
        return TrilinearInterpolate(highDetailGrid, gridPos);
    }
    
    float TrilinearInterpolate(float[,,] grid, Vector3 pos)
    {
        // Clamp to grid bounds
        pos.x = Mathf.Clamp(pos.x, 0, grid.GetLength(0) - 1.001f);
        pos.y = Mathf.Clamp(pos.y, 0, grid.GetLength(1) - 1.001f);
        pos.z = Mathf.Clamp(pos.z, 0, grid.GetLength(2) - 1.001f);
        
        // Get integer parts
        int x0 = Mathf.FloorToInt(pos.x), x1 = Mathf.Min(x0 + 1, grid.GetLength(0) - 1);
        int y0 = Mathf.FloorToInt(pos.y), y1 = Mathf.Min(y0 + 1, grid.GetLength(1) - 1);
        int z0 = Mathf.FloorToInt(pos.z), z1 = Mathf.Min(z0 + 1, grid.GetLength(2) - 1);
        
        // Get fractional parts
        float fx = pos.x - x0, fy = pos.y - y0, fz = pos.z - z0;
        
        // Trilinear interpolation
        float c000 = grid[x0, y0, z0], c001 = grid[x0, y0, z1];
        float c010 = grid[x0, y1, z0], c011 = grid[x0, y1, z1];
        float c100 = grid[x1, y0, z0], c101 = grid[x1, y0, z1];
        float c110 = grid[x1, y1, z0], c111 = grid[x1, y1, z1];
        
        float c00 = Mathf.Lerp(c000, c001, fz);
        float c01 = Mathf.Lerp(c010, c011, fz);
        float c10 = Mathf.Lerp(c100, c101, fz);
        float c11 = Mathf.Lerp(c110, c111, fz);
        
        float c0 = Mathf.Lerp(c00, c01, fy);
        float c1 = Mathf.Lerp(c10, c11, fy);
        
        return Mathf.Lerp(c0, c1, fx);
    }
    
    Vector3 ParametersToGridCoordinates(float w3, float w4, float b5)
    {
        // Normalize parameters to [0,1] range
        float normalizedW3 = Mathf.InverseLerp(backpropManager.WeightRange.x, backpropManager.WeightRange.y, w3);
        float normalizedW4 = Mathf.InverseLerp(backpropManager.WeightRange.x, backpropManager.WeightRange.y, w4);
        float normalizedB5 = Mathf.InverseLerp(backpropManager.BiasRange.x, backpropManager.BiasRange.y, b5);
        
        // Convert to grid coordinates
        return new Vector3(
            normalizedW3 * (gridSize.x - 1),
            normalizedB5 * (gridSize.y - 1),
            normalizedW4 * (gridSize.z - 1)
        );
    }
    
    public Vector3 ParametersToWorldPosition(float w3, float w4, float b5)
    {
        // Normalize parameters
        float normalizedW3 = Mathf.InverseLerp(backpropManager.WeightRange.x, backpropManager.WeightRange.y, w3);
        float normalizedW4 = Mathf.InverseLerp(backpropManager.WeightRange.x, backpropManager.WeightRange.y, w4);
        float normalizedB5 = Mathf.InverseLerp(backpropManager.BiasRange.x, backpropManager.BiasRange.y, b5);
        
        // Convert to world position within box
        Vector3 localPos = new Vector3(
            (normalizedW3 - 0.5f) * boxSize.x, // w3 -> X axis
            (normalizedB5 - 0.5f) * boxSize.y, // b5 -> Y axis  
            (normalizedW4 - 0.5f) * boxSize.z  // w4 -> Z axis
        );
        
        return transform.TransformPoint(localPos);
    }
    
    public Vector3 WorldPositionToParameters(Vector3 worldPos)
    {
        Vector3 localPos = transform.InverseTransformPoint(worldPos);
        
        float normalizedW3 = (localPos.x / boxSize.x) + 0.5f;
        float normalizedB5 = (localPos.y / boxSize.y) + 0.5f;
        float normalizedW4 = (localPos.z / boxSize.z) + 0.5f;
        
        float w3 = Mathf.Lerp(backpropManager.WeightRange.x, backpropManager.WeightRange.y, normalizedW3);
        float w4 = Mathf.Lerp(backpropManager.WeightRange.x, backpropManager.WeightRange.y, normalizedW4);
        float b5 = Mathf.Lerp(backpropManager.BiasRange.x, backpropManager.BiasRange.y, normalizedB5);
        
        return new Vector3(w3, w4, b5);
    }
    
    /// <summary>
    /// Get ball color based on loss value
    /// </summary>
    public Color GetBallColorForLoss(float loss)
    {
        // Auto-adjust maxLossForColoring if it seems inappropriate for current loss ranges
        AutoAdjustLossColoringRange();
        
        float normalizedLoss = Mathf.Clamp01(loss / maxLossForColoring);
        Color resultColor = lossGradient.Evaluate(normalizedLoss);
        
        // Debug color calculation occasionally (every 60 frames to avoid spam)
        if (Time.frameCount % 60 == 0)
        {
            Debug.Log($"=== BALL COLOR CALCULATION ===");
            Debug.Log($"Raw loss: {loss:F6}");
            Debug.Log($"Max loss for coloring: {maxLossForColoring:F3}");
            Debug.Log($"Normalized loss: {normalizedLoss:F3} ({normalizedLoss * 100:F1}%)");
            Debug.Log($"Result color: {resultColor} (R:{resultColor.r:F2}, G:{resultColor.g:F2}, B:{resultColor.b:F2})");
            
            if (normalizedLoss < 0.1f)
            {
                Debug.Log("✅ Low loss - should be green/near-green");
            }
            else if (normalizedLoss > 0.9f)
            {
                Debug.Log("❌ High loss - should be red/near-red");
            }
            else
            {
                Debug.Log("⚠️ Medium loss - should be yellow/orange");
            }
        }
        
        return resultColor;
    }
    
    /// <summary>
    /// Automatically adjust maxLossForColoring based on current parameter ranges and loss distribution
    /// </summary>
    void AutoAdjustLossColoringRange()
    {
        // Only auto-adjust occasionally to avoid performance impact
        if (Time.frameCount % 300 != 0) return; // Every 5 seconds at 60fps
        
        if (backpropManager == null) return;
        
        try
        {
            Debug.Log("=== AUTO-ADJUSTING LOSS COLORING RANGE ===");
            
            // Test loss at optimal position (should be minimum)
            float optimalLoss = 0f;
            if (backpropManager.HasOptimalParameters)
            {
                optimalLoss = backpropManager.CalculateLoss(
                    backpropManager.OptimalW3,
                    backpropManager.OptimalW4,
                    backpropManager.OptimalB5
                );
            }
            
            // Test loss at extreme parameter positions (should be maximum)
            Vector2 weightRange = backpropManager.WeightRange;
            Vector2 biasRange = backpropManager.BiasRange;
            
            // Sample several extreme positions to find reasonable max loss
            float[] testLosses = new float[]
            {
                backpropManager.CalculateLoss(weightRange.x, weightRange.x, biasRange.x), // Min corner
                backpropManager.CalculateLoss(weightRange.y, weightRange.y, biasRange.y), // Max corner
                backpropManager.CalculateLoss(weightRange.x, weightRange.y, biasRange.x), // Mixed corners
                backpropManager.CalculateLoss(weightRange.y, weightRange.x, biasRange.y),
                backpropManager.CalculateLoss(0f, 0f, 0f) // Center position
            };
            
            float maxTestLoss = Mathf.Max(testLosses);
            float minTestLoss = Mathf.Min(testLosses);
            
            Debug.Log($"Optimal loss: {optimalLoss:F6}");
            Debug.Log($"Test loss range: {minTestLoss:F6} to {maxTestLoss:F6}");
            Debug.Log($"Current maxLossForColoring: {maxLossForColoring:F3}");
            
            // Calculate appropriate maxLossForColoring
            // Use maxTestLoss with some padding, but ensure it's reasonable
            float suggestedMax = maxTestLoss * 1.2f; // 20% padding
            
            // Only update if the current value seems inappropriate
            bool shouldUpdate = false;
            
            if (maxLossForColoring < maxTestLoss * 0.8f)
            {
                Debug.Log("⚠️ maxLossForColoring too low - high losses will all appear red");
                shouldUpdate = true;
            }
            else if (maxLossForColoring > maxTestLoss * 3f)
            {
                Debug.Log("⚠️ maxLossForColoring too high - most losses will appear green");
                shouldUpdate = true;
            }
            else if (optimalLoss > maxLossForColoring * 0.3f)
            {
                Debug.Log("⚠️ Optimal loss too high relative to maxLossForColoring - optimal position won't be green");
                shouldUpdate = true;
            }
            
            if (shouldUpdate)
            {
                float oldMax = maxLossForColoring;
                maxLossForColoring = Mathf.Max(suggestedMax, 0.1f); // Ensure minimum value
                Debug.Log($"✅ Updated maxLossForColoring: {oldMax:F3} → {maxLossForColoring:F3}");
                
                // Test optimal loss normalization with new value
                float optimalNormalized = optimalLoss / maxLossForColoring;
                Debug.Log($"Optimal loss normalized: {optimalNormalized:F3} (should be close to 0 for green color)");
            }
            else
            {
                Debug.Log("✅ maxLossForColoring appears appropriate, no adjustment needed");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error during loss coloring range adjustment: {e.Message}");
        }
    }
    
    /// <summary>
    /// Update high-detail grid around ball position
    /// </summary>
    public void UpdateHighDetailGrid(Vector3 ballWorldPos)
    {
        Vector3 ballParams = WorldPositionToParameters(ballWorldPos);
        
        if (Vector3.Distance(ballParams, lastHighDetailCenter) > highDetailRadius * 0.5f)
        {
            StartCoroutine(CalculateHighDetailGrid(ballParams));
        }
    }
    
    IEnumerator CalculateHighDetailGrid(Vector3 centerParams)
    {
        Debug.Log($"Calculating high-detail grid around {centerParams}");
        
        lastHighDetailCenter = centerParams;
        highDetailGridValid = false;
        
        Vector3 halfRange = Vector3.one * (highDetailRadius / highDetailResolution);
        int calculationsThisFrame = 0;
        
        for (int x = 0; x < highDetailResolution; x++)
        {
            for (int y = 0; y < highDetailResolution; y++)
            {
                for (int z = 0; z < highDetailResolution; z++)
                {
                    Vector3 gridPos = new Vector3(x, y, z) / (highDetailResolution - 1f) - Vector3.one * 0.5f;
                    Vector3 paramValues = centerParams + gridPos * (highDetailRadius * 2f);
                    
                    // Clamp to valid parameter ranges
                    paramValues.x = Mathf.Clamp(paramValues.x, backpropManager.WeightRange.x, backpropManager.WeightRange.y);
                    paramValues.y = Mathf.Clamp(paramValues.y, backpropManager.WeightRange.x, backpropManager.WeightRange.y);
                    paramValues.z = Mathf.Clamp(paramValues.z, backpropManager.BiasRange.x, backpropManager.BiasRange.y);
                    
                    highDetailGrid[x, y, z] = backpropManager.CalculateLoss(paramValues.x, paramValues.y, paramValues.z);
                    
                    calculationsThisFrame++;
                    if (calculationsThisFrame >= pointsPerFrame / 4) // Use quarter of main grid budget
                    {
                        calculationsThisFrame = 0;
                        yield return null;
                    }
                }
            }
        }
        
        highDetailGridValid = true;
        Debug.Log("High-detail grid calculation complete");
    }
    
    /// <summary>
    /// Background grid calculation coroutine
    /// </summary>
    IEnumerator BackgroundGridUpdate()
    {
        while (true)
        {
            if (!isCalculating && backpropManager != null)
            {
                yield return StartCoroutine(CalculateGridSection());
            }
            
            yield return new WaitForSeconds(backgroundUpdateInterval);
        }
    }
    
    IEnumerator CalculateGridSection()
    {
        isCalculating = true;
        
        // Find uncalculated grid points
        List<Vector3Int> uncalculatedPoints = new List<Vector3Int>();
        
        for (int x = 0; x < gridSize.x && uncalculatedPoints.Count < pointsPerFrame * 5; x++)
        {
            for (int y = 0; y < gridSize.y && uncalculatedPoints.Count < pointsPerFrame * 5; y++)
            {
                for (int z = 0; z < gridSize.z && uncalculatedPoints.Count < pointsPerFrame * 5; z++)
                {
                    if (!gridCalculated[x, y, z])
                    {
                        uncalculatedPoints.Add(new Vector3Int(x, y, z));
                    }
                }
            }
        }
        
        // Calculate points in batches
        int calculationsThisFrame = 0;
        foreach (var point in uncalculatedPoints)
        {
            // Convert grid coordinates to parameters
            Vector3 normalizedPos = new Vector3(
                (float)point.x / (gridSize.x - 1),
                (float)point.y / (gridSize.y - 1),
                (float)point.z / (gridSize.z - 1)
            );
            
            float w3 = Mathf.Lerp(backpropManager.WeightRange.x, backpropManager.WeightRange.y, normalizedPos.x);
            float w4 = Mathf.Lerp(backpropManager.WeightRange.x, backpropManager.WeightRange.y, normalizedPos.z);
            float b5 = Mathf.Lerp(backpropManager.BiasRange.x, backpropManager.BiasRange.y, normalizedPos.y);
            
            // Calculate and store loss
            lossGrid[point.x, point.y, point.z] = backpropManager.CalculateLoss(w3, w4, b5);
            gridCalculated[point.x, point.y, point.z] = true;
            
            calculationsThisFrame++;
            if (calculationsThisFrame >= pointsPerFrame)
            {
                calculationsThisFrame = 0;
                yield return null;
            }
        }
        
        isCalculating = false;
        
        if (uncalculatedPoints.Count > 0)
        {
            float progress = (GetCalculatedPoints() / (float)(gridSize.x * gridSize.y * gridSize.z)) * 100f;
            Debug.Log($"Grid calculation progress: {progress:F1}%");
        }
    }
    
    int GetCalculatedPoints()
    {
        int count = 0;
        for (int x = 0; x < gridSize.x; x++)
            for (int y = 0; y < gridSize.y; y++)
                for (int z = 0; z < gridSize.z; z++)
                    if (gridCalculated[x, y, z]) count++;
        return count;
    }
    
    /// <summary>
    /// Force recalculation of entire grid (when network changes significantly)
    /// </summary>
    public void InvalidateGrid()
    {
        for (int x = 0; x < gridSize.x; x++)
            for (int y = 0; y < gridSize.y; y++)
                for (int z = 0; z < gridSize.z; z++)
                    gridCalculated[x, y, z] = false;
        
        highDetailGridValid = false;
        Debug.Log("Grid invalidated - recalculation will start");
    }
    
    void OnDrawGizmos()
    {
        // Draw parameter box bounds
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(transform.position, boxSize);
        
        // Draw coordinate system
        Gizmos.color = Color.red;   // w3 -> X
        Gizmos.DrawLine(transform.position, transform.position + transform.right * boxSize.x * 0.6f);
        
        Gizmos.color = Color.green; // b5 -> Y  
        Gizmos.DrawLine(transform.position, transform.position + transform.up * boxSize.y * 0.6f);
        
        Gizmos.color = Color.blue;  // w4 -> Z
        Gizmos.DrawLine(transform.position, transform.position + transform.forward * boxSize.z * 0.6f);
    }
    
    /// <summary>
    /// Create progressive landmark for the landmark game
    /// </summary>
    void CreateLandmarks()
    {
        if (!landmarksActive || backpropManager == null) return;
        
        Debug.Log("=== CREATING PROGRESSIVE LANDMARK GAME ===");
        
        try
        {
            // Clear existing landmarks
            ClearLandmarks();
            
            // Initialize progressive landmark game
            InitializeProgressiveLandmarkGame();
            
            Debug.Log($"Progressive landmark game initialized at stage {currentLandmarkStage}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error creating progressive landmark: {e.Message}");
            Debug.LogError("Landmarks will be disabled to prevent scene issues");
            landmarksActive = false;
        }
    }
    
    /// <summary>
    /// Initialize the progressive landmark game
    /// </summary>
    void InitializeProgressiveLandmarkGame()
    {
        if (!backpropManager.HasOptimalParameters) 
        {
            Debug.LogWarning("Cannot create progressive landmark - optimal parameters not available");
            return;
        }
        
        // Reset game state
        currentLandmarkStage = 1;
        landmarkGameCompleted = false;
        
        // Randomly select which parameter to optimize first (0=w3, 1=w4, 2=b5)
        firstOptimizedParameter = UnityEngine.Random.Range(0, 3);
        
        Debug.Log($"Progressive landmark game started with parameter {GetParameterName(firstOptimizedParameter)} optimized first");
        
        // Create the first stage landmark
        CreateProgressiveLandmarkAtStage(1);
    }
    
    /// <summary>
    /// Create progressive landmark at specified stage
    /// </summary>
    void CreateProgressiveLandmarkAtStage(int stage)
    {
        currentLandmarkStage = stage;
        
        // Calculate parameters for this stage
        CalculateLandmarkParametersForStage(stage);
        
        // Convert to world position
        progressiveLandmarkPosition = ParametersToWorldPosition(
            landmarkParameters[0], // w3
            landmarkParameters[1], // w4
            landmarkParameters[2]  // b5
        );
        
        // Destroy existing progressive landmark if it exists
        if (progressiveLandmark != null)
        {
            landmarks.Remove(progressiveLandmark);
            DestroyImmediate(progressiveLandmark);
        }
        
        // Create new landmark
        Color stageColor = GetStageColor(stage);
        string stageLabel = GetStageLabel(stage);
        
        progressiveLandmark = CreateLandmarkObject($"ProgressiveLandmark_Stage{stage}", progressiveLandmarkPosition, stageColor);
        landmarks.Add(progressiveLandmark);
        
        // Set landmark type and stage
        LandmarkIndicator landmarkComponent = progressiveLandmark.GetComponent<LandmarkIndicator>();
        if (landmarkComponent != null)
        {
            landmarkComponent.landmarkType = LandmarkIndicator.LandmarkType.Progressive;
            landmarkComponent.parameterPosition = progressiveLandmarkPosition;
            landmarkComponent.landmarkDescription = $"Progressive Stage {stage}: {GetStageLabel(stage)}";
        }
        
        // Add visual
        AddLandmarkVisual(progressiveLandmark, stageColor, stageLabel);
        
        Debug.Log($"=== PROGRESSIVE LANDMARK STAGE {stage} ===");
        Debug.Log($"Parameters: W3={landmarkParameters[0]:F3}, W4={landmarkParameters[1]:F3}, B5={landmarkParameters[2]:F3}");
        Debug.Log($"Position: {progressiveLandmarkPosition}");
        Debug.Log($"Label: {stageLabel}");
        
        // Don't fire event here - it's handled in OnLandmarkTouched() when player reaches the landmark
    }
    
    /// <summary>
    /// Calculate landmark parameters for given stage
    /// </summary>
    void CalculateLandmarkParametersForStage(int stage)
    {
        // Get optimal values
        float optimalW3 = backpropManager.OptimalW3;
        float optimalW4 = backpropManager.OptimalW4;
        float optimalB5 = backpropManager.OptimalB5;
        
        // Initialize with random values
        landmarkParameters[0] = UnityEngine.Random.Range(backpropManager.WeightRange.x, backpropManager.WeightRange.y); // w3
        landmarkParameters[1] = UnityEngine.Random.Range(backpropManager.WeightRange.x, backpropManager.WeightRange.y); // w4
        landmarkParameters[2] = UnityEngine.Random.Range(backpropManager.BiasRange.x, backpropManager.BiasRange.y); // b5
        
        // Apply optimal values based on stage and first optimized parameter
        switch (stage)
        {
            case 1: // One parameter optimized
                ApplyOptimalParameter(firstOptimizedParameter, optimalW3, optimalW4, optimalB5);
                break;
                
            case 2: // Two parameters optimized
                ApplyOptimalParameter(firstOptimizedParameter, optimalW3, optimalW4, optimalB5);
                int secondParameter = GetNextParameterToOptimize(firstOptimizedParameter);
                ApplyOptimalParameter(secondParameter, optimalW3, optimalW4, optimalB5);
                break;
                
            case 3: // All parameters optimized
                landmarkParameters[0] = optimalW3;
                landmarkParameters[1] = optimalW4;
                landmarkParameters[2] = optimalB5;
                break;
        }
        
        Debug.Log($"Stage {stage} parameters calculated:");
        Debug.Log($"  W3: {landmarkParameters[0]:F3} {(IsParameterOptimal(0, optimalW3, optimalW4, optimalB5) ? "(optimal)" : "(random)")}");
        Debug.Log($"  W4: {landmarkParameters[1]:F3} {(IsParameterOptimal(1, optimalW3, optimalW4, optimalB5) ? "(optimal)" : "(random)")}");
        Debug.Log($"  B5: {landmarkParameters[2]:F3} {(IsParameterOptimal(2, optimalW3, optimalW4, optimalB5) ? "(optimal)" : "(random)")}");
    }
    
    /// <summary>
    /// Apply optimal value to specified parameter
    /// </summary>
    void ApplyOptimalParameter(int paramIndex, float optimalW3, float optimalW4, float optimalB5)
    {
        switch (paramIndex)
        {
            case 0: landmarkParameters[0] = optimalW3; break;
            case 1: landmarkParameters[1] = optimalW4; break;
            case 2: landmarkParameters[2] = optimalB5; break;
        }
    }
    
    /// <summary>
    /// Get the next parameter to optimize after the first one
    /// </summary>
    int GetNextParameterToOptimize(int firstParam)
    {
        // Simple sequence: if first is 0, next is 1; if first is 1, next is 2; if first is 2, next is 0
        return (firstParam + 1) % 3;
    }
    
    /// <summary>
    /// Check if parameter at index is using optimal value
    /// </summary>
    bool IsParameterOptimal(int paramIndex, float optimalW3, float optimalW4, float optimalB5)
    {
        float tolerance = 0.001f;
        switch (paramIndex)
        {
            case 0: return Mathf.Abs(landmarkParameters[0] - optimalW3) < tolerance;
            case 1: return Mathf.Abs(landmarkParameters[1] - optimalW4) < tolerance;
            case 2: return Mathf.Abs(landmarkParameters[2] - optimalB5) < tolerance;
            default: return false;
        }
    }
    
    /// <summary>
    /// Get parameter name for debugging
    /// </summary>
    string GetParameterName(int paramIndex)
    {
        switch (paramIndex)
        {
            case 0: return "W3";
            case 1: return "W4";
            case 2: return "B5";
            default: return "Unknown";
        }
    }
    
    /// <summary>
    /// Get color for landmark stage
    /// </summary>
    Color GetStageColor(int stage)
    {
        switch (stage)
        {
            case 1: return Color.blue;      // First stage - blue
            case 2: return Color.yellow;    // Second stage - yellow
            case 3: return Color.green;     // Final stage - green
            default: return Color.white;
        }
    }
    
    /// <summary>
    /// Get label for landmark stage
    /// </summary>
    string GetStageLabel(int stage)
    {
        switch (stage)
        {
            case 1: return $"STEP 1\n{GetParameterName(firstOptimizedParameter)} OPTIMAL";
            case 2: 
                int secondParam = GetNextParameterToOptimize(firstOptimizedParameter);
                return $"STEP 2\n{GetParameterName(firstOptimizedParameter)} + {GetParameterName(secondParam)}";
            case 3: return "FINAL\nALL OPTIMAL";
            default: return "UNKNOWN";
        }
    }
    
    /// <summary>
    /// Handle landmark being touched by ball
    /// </summary>
    public void OnLandmarkTouched()
    {
        if (landmarkGameCompleted || progressiveLandmark == null) return;
        
        Debug.Log($"=== LANDMARK TOUCHED AT STAGE {currentLandmarkStage} ===");
        
        // First, fire the event for the current stage that was just reached
        Debug.Log($"Player reached stage {currentLandmarkStage} - firing stage changed event");
        OnLandmarkStageChanged?.Invoke(currentLandmarkStage);
        
        if (currentLandmarkStage < 3)
        {
            // Move to next stage
            int nextStage = currentLandmarkStage + 1;
            Debug.Log($"Moving landmark from stage {currentLandmarkStage} to stage {nextStage}");
            CreateProgressiveLandmarkAtStage(nextStage);
            // Gameplay: reaching a stage implies a target achieved in backprop
            if (NeuralNetwork.Instance != null)
            {
                NeuralNetwork.Instance.ReportBackpropTargetReached();
            }
        }
        else
        {
            // Game completed!
            Debug.Log("🎉 PROGRESSIVE LANDMARK GAME COMPLETED! All parameters optimized!");
            landmarkGameCompleted = true;
            OnLandmarkGameCompleted?.Invoke();
            // Final target reached
            if (NeuralNetwork.Instance != null)
            {
                NeuralNetwork.Instance.ReportBackpropTargetReached();
            }
        }
    }
    
    /// <summary>
    /// Create the optimal value landmark (all parameters optimized) - LEGACY METHOD
    /// </summary>
    void CreateOptimalLandmark()
    {
        if (!backpropManager.HasOptimalParameters) 
        {
            Debug.LogWarning("Cannot create optimal landmark - optimal parameters not available");
            return;
        }
        
        // Get optimal parameters
        float optimalW3 = backpropManager.OptimalW3;
        float optimalW4 = backpropManager.OptimalW4;
        float optimalB5 = backpropManager.OptimalB5;
        
        // Convert to world position
        optimalWorldPosition = ParametersToWorldPosition(optimalW3, optimalW4, optimalB5);
        optimalPositionSet = true;
        
        Debug.Log($"Optimal parameters (all optimized): W3={optimalW3:F3}, W4={optimalW4:F3}, B5={optimalB5:F3}");
        Debug.Log($"Optimal world position: {optimalWorldPosition}");
        
        // Create landmark GameObject
        GameObject landmark = CreateLandmarkObject("OptimalLandmark", optimalWorldPosition, Color.green);
        optimalLandmark = landmark;
        landmarks.Add(landmark);
        
        // Set landmark type
        LandmarkIndicator landmarkComponent = landmark.GetComponent<LandmarkIndicator>();
        if (landmarkComponent != null)
        {
            landmarkComponent.landmarkType = LandmarkIndicator.LandmarkType.Optimal;
        }
        
        // Add visual indicator (sphere or arrow)
        AddLandmarkVisual(landmark, Color.green, "ALL OPTIMAL");
        
        Debug.Log("Optimal landmark (all parameters) created successfully");
    }
    
    /// <summary>
    /// Create landmarks for single parameter optimization
    /// </summary>
    void CreateSingleParameterLandmarks()
    {
        if (!backpropManager.HasOptimalParameters) 
        {
            Debug.LogWarning("Cannot create single parameter landmarks - optimal parameters not available");
            return;
        }
        
        // Get optimal parameters
        float optimalW3 = backpropManager.OptimalW3;
        float optimalW4 = backpropManager.OptimalW4;
        float optimalB5 = backpropManager.OptimalB5;
        
        Debug.Log($"Optimal parameters: W3={optimalW3:F3}, W4={optimalW4:F3}, B5={optimalB5:F3}");
        Debug.Log($"Using random values for non-optimized parameters within ranges: W3/W4=[{backpropManager.WeightRange.x:F1},{backpropManager.WeightRange.y:F1}], B5=[{backpropManager.BiasRange.x:F1},{backpropManager.BiasRange.y:F1}]");
        
        // Create W3 optimal landmark (only W3 optimized, others random)
        CreateW3OptimalLandmark(optimalW3);
        
        // Create W4 optimal landmark (only W4 optimized, others random)
        CreateW4OptimalLandmark(optimalW4);
        
        // Create B5 optimal landmark (only B5 optimized, others random)
        CreateB5OptimalLandmark(optimalB5);
    }
    
    /// <summary>
    /// Create landmark showing optimal W3 with other parameters random
    /// </summary>
    void CreateW3OptimalLandmark(float optimalW3)
    {
        // Generate random values for W4 and B5
        float randomW4 = UnityEngine.Random.Range(backpropManager.WeightRange.x, backpropManager.WeightRange.y);
        float randomB5 = UnityEngine.Random.Range(backpropManager.BiasRange.x, backpropManager.BiasRange.y);
        
        Vector3 landmarkPosition = ParametersToWorldPosition(optimalW3, randomW4, randomB5);
        
        Debug.Log($"W3 Optimal Landmark: W3={optimalW3:F3} (optimal), W4={randomW4:F3} (random), B5={randomB5:F3} (random)");
        Debug.Log($"W3 Optimal world position: {landmarkPosition}");
        
        GameObject landmark = CreateLandmarkObject("W3OptimalLandmark", landmarkPosition, Color.blue);
        w3OptimalLandmark = landmark;
        landmarks.Add(landmark);
        
        // Set landmark type
        LandmarkIndicator landmarkComponent = landmark.GetComponent<LandmarkIndicator>();
        if (landmarkComponent != null)
        {
            landmarkComponent.landmarkType = LandmarkIndicator.LandmarkType.W3Optimal;
        }
        
        AddLandmarkVisual(landmark, Color.blue, "W3 OPTIMAL");
        
        Debug.Log("W3 Optimal landmark created successfully");
    }
    
    /// <summary>
    /// Create landmark showing optimal W4 with other parameters random
    /// </summary>
    void CreateW4OptimalLandmark(float optimalW4)
    {
        // Generate random values for W3 and B5
        float randomW3 = UnityEngine.Random.Range(backpropManager.WeightRange.x, backpropManager.WeightRange.y);
        float randomB5 = UnityEngine.Random.Range(backpropManager.BiasRange.x, backpropManager.BiasRange.y);
        
        Vector3 landmarkPosition = ParametersToWorldPosition(randomW3, optimalW4, randomB5);
        
        Debug.Log($"W4 Optimal Landmark: W3={randomW3:F3} (random), W4={optimalW4:F3} (optimal), B5={randomB5:F3} (random)");
        Debug.Log($"W4 Optimal world position: {landmarkPosition}");
        
        GameObject landmark = CreateLandmarkObject("W4OptimalLandmark", landmarkPosition, Color.cyan);
        w4OptimalLandmark = landmark;
        landmarks.Add(landmark);
        
        // Set landmark type
        LandmarkIndicator landmarkComponent = landmark.GetComponent<LandmarkIndicator>();
        if (landmarkComponent != null)
        {
            landmarkComponent.landmarkType = LandmarkIndicator.LandmarkType.W4Optimal;
        }
        
        AddLandmarkVisual(landmark, Color.cyan, "W4 OPTIMAL");
        
        Debug.Log("W4 Optimal landmark created successfully");
    }
    
    /// <summary>
    /// Create landmark showing optimal B5 with other parameters random
    /// </summary>
    void CreateB5OptimalLandmark(float optimalB5)
    {
        // Generate random values for W3 and W4
        float randomW3 = UnityEngine.Random.Range(backpropManager.WeightRange.x, backpropManager.WeightRange.y);
        float randomW4 = UnityEngine.Random.Range(backpropManager.WeightRange.x, backpropManager.WeightRange.y);
        
        Vector3 landmarkPosition = ParametersToWorldPosition(randomW3, randomW4, optimalB5);
        
        Debug.Log($"B5 Optimal Landmark: W3={randomW3:F3} (random), W4={randomW4:F3} (random), B5={optimalB5:F3} (optimal)");
        Debug.Log($"B5 Optimal world position: {landmarkPosition}");
        
        GameObject landmark = CreateLandmarkObject("B5OptimalLandmark", landmarkPosition, Color.magenta);
        b5OptimalLandmark = landmark;
        landmarks.Add(landmark);
        
        // Set landmark type
        LandmarkIndicator landmarkComponent = landmark.GetComponent<LandmarkIndicator>();
        if (landmarkComponent != null)
        {
            landmarkComponent.landmarkType = LandmarkIndicator.LandmarkType.B5Optimal;
        }
        
        AddLandmarkVisual(landmark, Color.magenta, "B5 OPTIMAL");
        
        Debug.Log("B5 Optimal landmark created successfully");
    }
    
    /// <summary>
    /// Create a landmark GameObject at the specified position
    /// </summary>
    GameObject CreateLandmarkObject(string name, Vector3 position, Color color)
    {
        GameObject landmark = new GameObject(name);
        landmark.transform.SetParent(transform);
        landmark.transform.position = position;
        
        // Add collider for interaction
        SphereCollider collider = landmark.AddComponent<SphereCollider>();
        collider.radius = landmarkSize * 0.5f;
        collider.isTrigger = true;
        
        // Add landmark component for identification
        LandmarkIndicator landmarkComponent = landmark.AddComponent<LandmarkIndicator>();
        landmarkComponent.parameterPosition = position;
        
        return landmark;
    }
    
    /// <summary>
    /// Add visual representation to a landmark
    /// </summary>
    void AddLandmarkVisual(GameObject landmark, Color color, string label)
    {
        try
        {
            // Create visual sphere
            GameObject visualSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            visualSphere.transform.SetParent(landmark.transform);
            visualSphere.transform.localPosition = Vector3.zero;
            visualSphere.transform.localScale = Vector3.one * landmarkSize;
            
            // Remove the collider from the visual sphere (keep only the parent's trigger collider)
            DestroyImmediate(visualSphere.GetComponent<Collider>());
            
            // Set material
            MeshRenderer renderer = visualSphere.GetComponent<MeshRenderer>();
            if (optimalLandmarkMaterial != null)
            {
                renderer.material = optimalLandmarkMaterial;
            }
            else
            {
                // Create default material
                Material mat = new Material(Shader.Find("Standard"));
                mat.color = color;
                mat.SetFloat("_Metallic", 0.8f);
                mat.SetFloat("_Smoothness", 0.6f);
                renderer.material = mat;
            }
            
            // Add text label (with error handling)
            try
            {
                CreateLandmarkLabel(landmark, label, color);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"Failed to create landmark label: {e.Message}. Landmark will be created without text.");
            }
            
            // Add pulsing animation
            StartCoroutine(PulseLandmark(landmark));
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error creating landmark visual: {e.Message}");
            throw; // Re-throw to be caught by CreateLandmarks
        }
    }
    
    /// <summary>
    /// Create a text label for the landmark
    /// </summary>
    void CreateLandmarkLabel(GameObject landmark, string text, Color color)
    {
        // Create canvas for text
        GameObject canvasObj = new GameObject("LandmarkCanvas");
        canvasObj.transform.SetParent(landmark.transform);
        canvasObj.transform.localPosition = Vector3.up * (landmarkSize * 0.8f);
        canvasObj.transform.localRotation = Quaternion.identity;
        
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = Camera.main;
        
        // Create text
        GameObject textObj = new GameObject("Label");
        textObj.transform.SetParent(canvasObj.transform);
        textObj.transform.localPosition = Vector3.zero;
        textObj.transform.localScale = Vector3.one * 0.01f; // Scale down for world space
        
        Text textComponent = textObj.AddComponent<Text>();
        textComponent.text = text;
        
        // Try to get the built-in font with fallback
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null)
        {
            // Fallback to any available font
            font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        }
        if (font == null)
        {
            // Last resort - use default font
            font = Font.CreateDynamicFontFromOSFont("Arial", 24);
        }
        
        textComponent.font = font;
        textComponent.fontSize = 24;
        textComponent.color = color;
        textComponent.alignment = TextAnchor.MiddleCenter;
        
        // Add background
        GameObject background = new GameObject("Background");
        background.transform.SetParent(textObj.transform);
        background.transform.localPosition = Vector3.zero;
        background.transform.localScale = Vector3.one;
        
        Image bgImage = background.AddComponent<Image>();
        bgImage.color = new Color(0, 0, 0, 0.7f);
        
        // Set text rect transform
        RectTransform textRect = textComponent.GetComponent<RectTransform>();
        textRect.sizeDelta = new Vector2(200, 50);
        
        RectTransform bgRect = bgImage.GetComponent<RectTransform>();
        bgRect.sizeDelta = new Vector2(200, 50);
    }
    
    /// <summary>
    /// Animate landmark with pulsing effect
    /// </summary>
    IEnumerator PulseLandmark(GameObject landmark)
    {
        Vector3 originalScale = landmark.transform.localScale;
        float pulseSpeed = 2f;
        float pulseAmount = 0.2f;
        
        while (landmark != null)
        {
            float pulse = 1f + Mathf.Sin(Time.time * pulseSpeed) * pulseAmount;
            landmark.transform.localScale = originalScale * pulse;
            yield return null;
        }
    }
    
    /// <summary>
    /// Clear all existing landmarks
    /// </summary>
    void ClearLandmarks()
    {
        // Clear legacy landmarks
        foreach (GameObject landmark in landmarks)
        {
            if (landmark != null)
            {
                DestroyImmediate(landmark);
            }
        }
        landmarks.Clear();
        
        // Clear progressive landmark
        if (progressiveLandmark != null)
        {
            DestroyImmediate(progressiveLandmark);
            progressiveLandmark = null;
        }
        
        // Reset legacy landmark references
        optimalLandmark = null;
        w3OptimalLandmark = null;
        w4OptimalLandmark = null;
        b5OptimalLandmark = null;
        optimalPositionSet = false;
        
        // Reset progressive landmark game state
        currentLandmarkStage = 1;
        landmarkGameCompleted = false;
        firstOptimizedParameter = 0;
        landmarkParameters = new float[3];
    }
    
    /// <summary>
    /// Check if ball is near the progressive landmark and handle touch/snap
    /// </summary>
    public bool CheckLandmarkSnap(Vector3 ballPosition, out Vector3 snappedPosition)
    {
        snappedPosition = ballPosition;
        
        if (!landmarksActive || progressiveLandmark == null || landmarkGameCompleted) return false;
        
        float distance = Vector3.Distance(ballPosition, progressiveLandmark.transform.position);
        if (distance <= landmarkSnapDistance)
        {
            snappedPosition = progressiveLandmark.transform.position;
            Debug.Log($"Ball touched progressive landmark at stage {currentLandmarkStage}, distance: {distance:F3}");
            
            // Handle landmark progression
            OnLandmarkTouched();
            
            return true;
        }
        
        return false;
    }
    
    /// <summary>
    /// Get the optimal landmark position
    /// </summary>
    public Vector3 GetOptimalLandmarkPosition()
    {
        return optimalPositionSet ? optimalWorldPosition : Vector3.zero;
    }
    
    /// <summary>
    /// Toggle landmarks on/off
    /// </summary>
    public void ToggleLandmarks()
    {
        landmarksActive = !landmarksActive;
        
        foreach (GameObject landmark in landmarks)
        {
            if (landmark != null)
            {
                landmark.SetActive(landmarksActive);
            }
        }
        
        Debug.Log($"Landmarks {(landmarksActive ? "enabled" : "disabled")}");
    }
    
    /// <summary>
    /// Refresh landmarks with new optimal parameters
    /// </summary>
    public void RefreshLandmarks()
    {
        if (landmarksActive)
        {
            Debug.Log("Refreshing landmarks with current optimal parameters and ball position");
            CreateLandmarks();
        }
    }
    
    /// <summary>
    /// Refresh single-parameter landmarks with new random positions
    /// </summary>
    public void RefreshSingleParameterLandmarks()
    {
        if (!landmarksActive || backpropManager == null) return;
        
        Debug.Log("Refreshing single-parameter landmarks with new random positions");
        
        // Remove old single-parameter landmarks
        if (w3OptimalLandmark != null) 
        {
            landmarks.Remove(w3OptimalLandmark);
            DestroyImmediate(w3OptimalLandmark);
        }
        if (w4OptimalLandmark != null) 
        {
            landmarks.Remove(w4OptimalLandmark);
            DestroyImmediate(w4OptimalLandmark);
        }
        if (b5OptimalLandmark != null) 
        {
            landmarks.Remove(b5OptimalLandmark);
            DestroyImmediate(b5OptimalLandmark);
        }
        
        // Create new single-parameter landmarks with new random positions
        CreateSingleParameterLandmarks();
        
        Debug.Log($"Refreshed single-parameter landmarks with new random positions. Total landmarks: {landmarks.Count}");
    }
    
    /// <summary>
    /// Get the number of active landmarks
    /// </summary>
    public int GetLandmarkCount()
    {
        return progressiveLandmark != null ? 1 : 0;
    }
    
    /// <summary>
    /// Check if progressive landmark exists and is active
    /// </summary>
    public bool HasOptimalLandmark()
    {
        return progressiveLandmark != null && progressiveLandmark.activeInHierarchy;
    }
    
    /// <summary>
    /// Get the current stage of the progressive landmark game
    /// </summary>
    public int GetCurrentLandmarkStage()
    {
        return currentLandmarkStage;
    }
    
    /// <summary>
    /// Check if the progressive landmark game is completed
    /// </summary>
    public bool IsLandmarkGameCompleted()
    {
        return landmarkGameCompleted;
    }
    
    /// <summary>
    /// Get the position of the current progressive landmark
    /// </summary>
    public Vector3 GetProgressiveLandmarkPosition()
    {
        return progressiveLandmark != null ? progressiveLandmark.transform.position : Vector3.zero;
    }
    
    /// <summary>
    /// Get the parameters of the current progressive landmark
    /// </summary>
    public Vector3 GetProgressiveLandmarkParameters()
    {
        return new Vector3(landmarkParameters[0], landmarkParameters[1], landmarkParameters[2]);
    }
    
    /// <summary>
    /// Reset the progressive landmark game
    /// </summary>
    public void ResetProgressiveLandmarkGame()
    {
        Debug.Log("=== RESETTING PROGRESSIVE LANDMARK GAME ===");
        ClearLandmarks();
        if (landmarksActive && backpropManager != null)
        {
            InitializeProgressiveLandmarkGame();
        }
    }

    /// <summary>
    /// Get the specific parameter names that are optimized at the given stage
    /// </summary>
    public List<string> GetOptimizedParameterNames(int stage)
    {
        List<string> optimizedParams = new List<string>();
        
        if (stage < 1 || stage > 3) return optimizedParams;

        // Get parameter names based on stage and first optimized parameter
        switch (stage)
        {
            case 1:
                // One parameter optimal
                optimizedParams.Add(GetParameterName(firstOptimizedParameter));
                break;
                
            case 2:
                // Two parameters optimal
                optimizedParams.Add(GetParameterName(firstOptimizedParameter));
                int secondParameter = GetNextParameterToOptimize(firstOptimizedParameter);
                optimizedParams.Add(GetParameterName(secondParameter));
                break;
                
            case 3:
                // All parameters optimal
                optimizedParams.Add("W3");
                optimizedParams.Add("W4");
                optimizedParams.Add("B5");
                break;
        }
        
        return optimizedParams;
    }

    /// <summary>
    /// Get formatted text describing which parameters are optimized at the given stage
    /// </summary>
    public string GetOptimizedParametersText(int stage)
    {
        List<string> optimizedParams = GetOptimizedParameterNames(stage);
        
        if (optimizedParams.Count == 0)
        {
            return "No parameters optimal yet";
        }
        else if (optimizedParams.Count == 1)
        {
            return $"{optimizedParams[0]} is now OPTIMAL!";
        }
        else if (optimizedParams.Count == 2)
        {
            return $"{optimizedParams[0]} and {optimizedParams[1]}\nare now OPTIMAL!";
        }
        else
        {
            return $"{string.Join(", ", optimizedParams.ToArray())}\nare now OPTIMAL!";
        }
    }

    /// <summary>
    /// Get descriptive text for the current landmark stage
    /// </summary>
    public string GetCurrentStageDescription()
    {
        switch (currentLandmarkStage)
        {
            case 1:
                return $"Step 1: Optimize {GetParameterName(firstOptimizedParameter)}";
            case 2:
                int secondParam = GetNextParameterToOptimize(firstOptimizedParameter);
                return $"Step 2: Optimize {GetParameterName(firstOptimizedParameter)} + {GetParameterName(secondParam)}";
            case 3:
                return "Step 3: All parameters optimal!";
            default:
                return "Unknown stage";
        }
    }

    /// <summary>
    /// Get the first optimized parameter index (for external access)
    /// </summary>
    public int GetFirstOptimizedParameter()
    {
        return firstOptimizedParameter;
    }

    /// <summary>
    /// Get the current landmark stage with additional info
    /// </summary>
    public (int stage, string description, List<string> optimizedParams) GetCurrentLandmarkInfo()
    {
        return (
            currentLandmarkStage,
            GetCurrentStageDescription(),
            GetOptimizedParameterNames(currentLandmarkStage)
        );
    }
    
    /// <summary>
    /// Debug method to test progressive landmark creation
    /// </summary>
    [ContextMenu("Test Progressive Landmark Creation")]
    public void TestLandmarkCreation()
    {
        Debug.Log("=== TESTING PROGRESSIVE LANDMARK CREATION ===");
        
        if (backpropManager == null)
        {
            Debug.LogError("BackpropagationManager is null - cannot test progressive landmarks");
            return;
        }
        
        if (!backpropManager.HasOptimalParameters)
        {
            Debug.LogError("No optimal parameters available - cannot create progressive landmark");
            return;
        }
        
        Debug.Log($"Optimal parameters: W3={backpropManager.OptimalW3:F3}, W4={backpropManager.OptimalW4:F3}, B5={backpropManager.OptimalB5:F3}");
        
        // Force create progressive landmark
        landmarksActive = true;
        CreateLandmarks();
        
        Debug.Log($"Progressive landmark test completed.");
        Debug.Log($"Progressive landmark exists: {HasOptimalLandmark()}");
        Debug.Log($"Current stage: {GetCurrentLandmarkStage()}");
        Debug.Log($"First optimized parameter: {GetParameterName(firstOptimizedParameter)}");
        
        if (HasOptimalLandmark())
        {
            Debug.Log($"Progressive landmark position: {progressiveLandmark.transform.position}");
            Debug.Log($"Progressive landmark parameters: {GetProgressiveLandmarkParameters()}");
        }
    }
    
    /// <summary>
    /// Debug method to test progressive landmark progression
    /// </summary>
    [ContextMenu("Test Progressive Landmark Progression")]
    public void TestProgressiveLandmarkProgression()
    {
        Debug.Log("=== TESTING PROGRESSIVE LANDMARK PROGRESSION ===");
        
        if (progressiveLandmark == null)
        {
            Debug.LogError("No progressive landmark exists - create one first");
            return;
        }
        
        Debug.Log($"Before progression: Stage {currentLandmarkStage}");
        Debug.Log($"Current parameters: {GetProgressiveLandmarkParameters()}");
        
        // Simulate landmark touch
        OnLandmarkTouched();
        
        Debug.Log($"After progression: Stage {currentLandmarkStage}");
        Debug.Log($"New parameters: {GetProgressiveLandmarkParameters()}");
        Debug.Log($"Game completed: {IsLandmarkGameCompleted()}");
    }
    
    /// <summary>
    /// Debug method to test landmark snapping
    /// </summary>
    [ContextMenu("Test Landmark Snapping")]
    public void TestLandmarkSnapping()
    {
        Debug.Log("=== TESTING LANDMARK SNAPPING ===");
        
        if (!HasOptimalLandmark())
        {
            Debug.LogError("No optimal landmark exists - cannot test snapping");
            return;
        }
        
        Vector3 landmarkPos = optimalLandmark.transform.position;
        Debug.Log($"Optimal landmark position: {landmarkPos}");
        
        // Test positions at different distances
        Vector3[] testPositions = {
            landmarkPos + Vector3.forward * 0.1f,  // Close
            landmarkPos + Vector3.forward * 0.2f,  // Medium
            landmarkPos + Vector3.forward * 0.4f,  // Far
            landmarkPos + Vector3.forward * 0.6f   // Very far
        };
        
        foreach (Vector3 testPos in testPositions)
        {
            Vector3 snappedPos;
            bool shouldSnap = CheckLandmarkSnap(testPos, out snappedPos);
            float distance = Vector3.Distance(testPos, landmarkPos);
            
            Debug.Log($"Test position: {testPos} (distance: {distance:F3}) -> Should snap: {shouldSnap}, Snapped to: {snappedPos}");
        }
    }
    
    /// <summary>
    /// Manually recalculate and adjust maxLossForColoring for current parameter ranges
    /// </summary>
    [ContextMenu("Recalculate Loss Coloring Range")]
    public void RecalculateLossColoringRange()
    {
        Debug.Log("=== MANUAL LOSS COLORING RANGE RECALCULATION ===");
        
        if (backpropManager == null)
        {
            Debug.LogError("BackpropagationManager is null - cannot recalculate loss range");
            return;
        }
        
        // Force auto-adjustment by temporarily resetting the frame check
        int originalFrame = Time.frameCount;
        
        // Temporarily modify frame count to trigger auto-adjustment
        var frameField = typeof(Time).GetField("frameCount", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        if (frameField == null)
        {
            // Fallback: directly call the auto-adjustment logic
            try
            {
                Debug.Log("Testing loss values across parameter space...");
                
                // Test loss at optimal position
                float optimalLoss = 0f;
                if (backpropManager.HasOptimalParameters)
                {
                    optimalLoss = backpropManager.CalculateLoss(
                        backpropManager.OptimalW3,
                        backpropManager.OptimalW4,
                        backpropManager.OptimalB5
                    );
                }
                
                // Test loss at current position
                float currentLoss = backpropManager.CalculateLoss(
                    backpropManager.CurrentW3,
                    backpropManager.CurrentW4,
                    backpropManager.CurrentB5
                );
                
                // Test extreme positions
                Vector2 weightRange = backpropManager.WeightRange;
                Vector2 biasRange = backpropManager.BiasRange;
                
                float[] extremeLosses = new float[]
                {
                    backpropManager.CalculateLoss(weightRange.x, weightRange.x, biasRange.x),
                    backpropManager.CalculateLoss(weightRange.y, weightRange.y, biasRange.y),
                    backpropManager.CalculateLoss(weightRange.x, weightRange.y, biasRange.x),
                    backpropManager.CalculateLoss(weightRange.y, weightRange.x, biasRange.y),
                    backpropManager.CalculateLoss(0f, 0f, 0f)
                };
                
                float maxLoss = Mathf.Max(extremeLosses);
                float minLoss = Mathf.Min(extremeLosses);
                
                Debug.Log($"=== LOSS ANALYSIS ===");
                Debug.Log($"Optimal loss: {optimalLoss:F6}");
                Debug.Log($"Current ball loss: {currentLoss:F6}");
                Debug.Log($"Parameter range loss: {minLoss:F6} to {maxLoss:F6}");
                Debug.Log($"Current maxLossForColoring: {maxLossForColoring:F3}");
                
                // Calculate suggested new value
                float suggestedMax = maxLoss * 1.2f;
                float oldMax = maxLossForColoring;
                maxLossForColoring = Mathf.Max(suggestedMax, 0.1f);
                
                Debug.Log($"=== UPDATED COLORING RANGE ===");
                Debug.Log($"Old maxLossForColoring: {oldMax:F3}");
                Debug.Log($"New maxLossForColoring: {maxLossForColoring:F3}");
                
                // Test color mapping
                Color optimalColor = GetBallColorForLoss(optimalLoss);
                Color currentColor = GetBallColorForLoss(currentLoss);
                Color maxColor = GetBallColorForLoss(maxLoss);
                
                Debug.Log($"=== COLOR MAPPING TEST ===");
                Debug.Log($"Optimal position color: {optimalColor} (should be green)");
                Debug.Log($"Current ball color: {currentColor}");
                Debug.Log($"Maximum loss color: {maxColor} (should be red)");
                
                Debug.Log("✅ Loss coloring range recalculated successfully!");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error during manual loss range calculation: {e.Message}");
            }
        }
    }
    
    /// <summary>
    /// Debug method to test single-parameter landmarks
    /// </summary>
    [ContextMenu("Test Single Parameter Landmarks")]
    public void TestSingleParameterLandmarks()
    {
        Debug.Log("=== TESTING SINGLE PARAMETER LANDMARKS ===");
        
        if (backpropManager == null)
        {
            Debug.LogError("BackpropagationManager is null - cannot test single parameter landmarks");
            return;
        }
        
        Debug.Log($"Optimal parameters: W3={backpropManager.OptimalW3:F3}, W4={backpropManager.OptimalW4:F3}, B5={backpropManager.OptimalB5:F3}");
        Debug.Log($"Parameter ranges: W3/W4=[{backpropManager.WeightRange.x:F1},{backpropManager.WeightRange.y:F1}], B5=[{backpropManager.BiasRange.x:F1},{backpropManager.BiasRange.y:F1}]");
        
        // Test each single-parameter landmark
        if (w3OptimalLandmark != null)
        {
            Vector3 w3Pos = w3OptimalLandmark.transform.position;
            Vector3 w3Params = backpropManager.WorldPositionToParameters(w3Pos);
            Debug.Log($"W3 Optimal Landmark: Position={w3Pos}, Parameters=W3={w3Params.x:F3} (optimal), W4={w3Params.y:F3} (random), B5={w3Params.z:F3} (random)");
        }
        
        if (w4OptimalLandmark != null)
        {
            Vector3 w4Pos = w4OptimalLandmark.transform.position;
            Vector3 w4Params = backpropManager.WorldPositionToParameters(w4Pos);
            Debug.Log($"W4 Optimal Landmark: Position={w4Pos}, Parameters=W3={w4Params.x:F3} (random), W4={w4Params.y:F3} (optimal), B5={w4Params.z:F3} (random)");
        }
        
        if (b5OptimalLandmark != null)
        {
            Vector3 b5Pos = b5OptimalLandmark.transform.position;
            Vector3 b5Params = backpropManager.WorldPositionToParameters(b5Pos);
            Debug.Log($"B5 Optimal Landmark: Position={b5Pos}, Parameters=W3={b5Params.x:F3} (random), W4={b5Params.y:F3} (random), B5={b5Params.z:F3} (optimal)");
        }
        
        Debug.Log($"Total landmarks: {landmarks.Count}");
    }
    
    /// <summary>
    /// Test ball color at optimal position to verify green color
    /// </summary>
    [ContextMenu("Test Ball Color at Optimal Position")]
    public void TestBallColorAtOptimalPosition()
    {
        Debug.Log("=== TESTING BALL COLOR AT OPTIMAL POSITION ===");
        
        if (backpropManager == null || !backpropManager.HasOptimalParameters)
        {
            Debug.LogError("Cannot test - BackpropManager or optimal parameters not available");
            return;
        }
        
        // Get optimal parameters
        float optimalW3 = backpropManager.OptimalW3;
        float optimalW4 = backpropManager.OptimalW4;
        float optimalB5 = backpropManager.OptimalB5;
        
        // Calculate loss at optimal position
        float optimalLoss = backpropManager.CalculateLoss(optimalW3, optimalW4, optimalB5);
        
        // Get the color that would be used for optimal position
        Color optimalColor = GetBallColorForLoss(optimalLoss);
        
        Debug.Log($"Optimal parameters: W3={optimalW3:F3}, W4={optimalW4:F3}, B5={optimalB5:F3}");
        Debug.Log($"Optimal loss: {optimalLoss:F6}");
        Debug.Log($"maxLossForColoring: {maxLossForColoring:F3}");
        Debug.Log($"Normalized optimal loss: {(optimalLoss / maxLossForColoring):F4} (should be close to 0)");
        Debug.Log($"Optimal color: {optimalColor}");
        Debug.Log($"Color components: R={optimalColor.r:F3}, G={optimalColor.g:F3}, B={optimalColor.b:F3}");
        
        // Check if color is green-ish
        bool isGreenish = optimalColor.g > 0.7f && optimalColor.g > optimalColor.r && optimalColor.g > optimalColor.b;
        
        if (isGreenish)
        {
            Debug.Log("✅ SUCCESS: Optimal position produces green-ish color!");
        }
        else
        {
            Debug.LogWarning("❌ PROBLEM: Optimal position does NOT produce green color!");
            Debug.LogWarning("Consider running 'Recalculate Loss Coloring Range' to fix this.");
        }
        
        // Also test current ball position for comparison
        float currentLoss = backpropManager.CalculateLoss(
            backpropManager.CurrentW3,
            backpropManager.CurrentW4,
            backpropManager.CurrentB5
        );
        Color currentColor = GetBallColorForLoss(currentLoss);
        
        Debug.Log($"=== CURRENT BALL POSITION COMPARISON ===");
        Debug.Log($"Current parameters: W3={backpropManager.CurrentW3:F3}, W4={backpropManager.CurrentW4:F3}, B5={backpropManager.CurrentB5:F3}");
        Debug.Log($"Current loss: {currentLoss:F6}");
        Debug.Log($"Current color: {currentColor}");
        Debug.Log($"Current color components: R={currentColor.r:F3}, G={currentColor.g:F3}, B={currentColor.b:F3}");
    }
    
    void OnDestroy()
    {
        if (backgroundUpdateCoroutine != null)
        {
            StopCoroutine(backgroundUpdateCoroutine);
        }
    }
    
    /// <summary>
    /// Constrain a world position to stay within the parameter box bounds
    /// </summary>
    /// <param name="worldPosition">The world position to constrain</param>
    /// <returns>The constrained world position that is guaranteed to be within the parameter box</returns>
    public Vector3 ConstrainWorldPositionToBox(Vector3 worldPosition)
    {
        if (backpropManager == null)
        {
            Debug.LogWarning("Cannot constrain position - BackpropagationManager not available");
            return worldPosition;
        }
        
        // Convert world position to parameters
        Vector3 parameters = WorldPositionToParameters(worldPosition);
        
        // Get valid parameter ranges
        Vector2 weightRange = backpropManager.WeightRange;
        Vector2 biasRange = backpropManager.BiasRange;
        
        // Track if any constraint was applied
        bool wasConstrained = false;
        Vector3 originalParameters = parameters;
        
        // Clamp parameters to valid ranges
        if (parameters.x < weightRange.x || parameters.x > weightRange.y)
        {
            parameters.x = Mathf.Clamp(parameters.x, weightRange.x, weightRange.y);
            wasConstrained = true;
        }
        
        if (parameters.y < weightRange.x || parameters.y > weightRange.y)
        {
            parameters.y = Mathf.Clamp(parameters.y, weightRange.x, weightRange.y);
            wasConstrained = true;
        }
        
        if (parameters.z < biasRange.x || parameters.z > biasRange.y)
        {
            parameters.z = Mathf.Clamp(parameters.z, biasRange.x, biasRange.y);
            wasConstrained = true;
        }
        
        // Convert back to world position
        Vector3 constrainedWorldPosition = ParametersToWorldPosition(parameters.x, parameters.y, parameters.z);
        
        // Debug constraint application
        if (wasConstrained)
        {
            Debug.Log($"Position constrained to parameter box:");
            Debug.Log($"  Original parameters: W3={originalParameters.x:F3}, W4={originalParameters.y:F3}, B5={originalParameters.z:F3}");
            Debug.Log($"  Constrained parameters: W3={parameters.x:F3}, W4={parameters.y:F3}, B5={parameters.z:F3}");
            Debug.Log($"  Original world pos: {worldPosition}");
            Debug.Log($"  Constrained world pos: {constrainedWorldPosition}");
        }
        
        return constrainedWorldPosition;
    }
    
    /// <summary>
    /// Check if a world position is within the parameter box bounds
    /// </summary>
    /// <param name="worldPosition">The world position to check</param>
    /// <returns>True if the position is within bounds, false otherwise</returns>
    public bool IsWorldPositionInBounds(Vector3 worldPosition)
    {
        if (backpropManager == null) return true; // Can't check bounds without backprop manager
        
        // Convert to parameters and check ranges
        Vector3 parameters = WorldPositionToParameters(worldPosition);
        Vector2 weightRange = backpropManager.WeightRange;
        Vector2 biasRange = backpropManager.BiasRange;
        
        bool w3InBounds = parameters.x >= weightRange.x && parameters.x <= weightRange.y;
        bool w4InBounds = parameters.y >= weightRange.x && parameters.y <= weightRange.y;
        bool b5InBounds = parameters.z >= biasRange.x && parameters.z <= biasRange.y;
        
        return w3InBounds && w4InBounds && b5InBounds;
    }
    
    /// <summary>
    /// Constrain a ball GameObject to stay within the parameter box bounds
    /// </summary>
    /// <param name="ballObject">The ball GameObject to constrain</param>
    /// <param name="stopMovement">Whether to stop the ball's movement when constrained</param>
    /// <returns>True if the ball was constrained (moved), false if it was already in bounds</returns>
    public bool ConstrainBallToBox(GameObject ballObject, bool stopMovement = true)
    {
        if (ballObject == null) return false;
        
        Vector3 originalPosition = ballObject.transform.position;
        Vector3 constrainedPosition = ConstrainWorldPositionToBox(originalPosition);
        
        // Check if constraint was applied
        bool wasConstrained = Vector3.Distance(originalPosition, constrainedPosition) > 0.001f;
        
        if (wasConstrained)
        {
            // Move ball to constrained position
            ballObject.transform.position = constrainedPosition;
            
            // Stop ball movement if requested
            if (stopMovement)
            {
                Rigidbody ballRigidbody = ballObject.GetComponent<Rigidbody>();
                if (ballRigidbody != null && !ballRigidbody.isKinematic)
                {
                    ballRigidbody.velocity = Vector3.zero;
                    ballRigidbody.angularVelocity = Vector3.zero;
                }
            }
            
            Debug.Log($"Ball constrained to parameter box bounds at position: {constrainedPosition}");
            
            // Update parameters with constrained values
            if (backpropManager != null)
            {
                Vector3 constrainedParameters = WorldPositionToParameters(constrainedPosition);
                backpropManager.UpdateParameters(constrainedParameters.x, constrainedParameters.y, constrainedParameters.z);
            }
        }
        
        return wasConstrained;
    }
} 