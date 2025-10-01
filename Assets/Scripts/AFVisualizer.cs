using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using TMPro;


// Data structure for storing output layer curve information
[System.Serializable]
public class OutputLayerCurveData
{
    public List<Vector2> inputPoints;     // Original input points collected
    public List<Vector2> curve1Points;   // Points for first neuron's activation curve
    public List<Vector2> curve2Points;   // Points for second neuron's activation curve
    public List<Vector2> combinedPoints; // Points for final combined output curve
}

[RequireComponent(typeof(LineRenderer))]
public class AFVisualizer : MonoBehaviour
{
    [Header("Network Reference")]
    private NeuralNetwork neuralNetwork; // Changed from public to private
    public NeuralNetworkDataManager dataManager; // Add reference to data manager

    [Header("Visualization Settings")]
    public Material lineMaterial;
    public Material pointMaterial;   // Material for data points
    public float lineWidth = 0.1f;
    public float pointSize = 0.1f;   // Size of the points on the graph
    public Color defaultFunctionColor = Color.gray;
    public Color currentFunctionColor = Color.blue;
    public Color dataPointColor = Color.yellow; // Color for collected datapoints
    public Color networkDataColor = Color.yellow; // Color for network data line
    public int resolution = 100; // Number of points to plot
    public float xRange = 10f; // Range of x values to plot (-xRange to xRange)
    public float yRange = 2f; // Range of y values to plot
    public GameObject dataPointPrefab; // Prefab for visualizing collected data points

    [Header("Current Neuron")]
    public int currentLayerIndex = 0; // Track which layer we're visualizing
    public int currentNeuronIndex = 0; // Track which neuron we're visualizing
    
    [Header("Output Layer Visualization")]
    public bool isOutputLayer = false; // Flag to indicate if we're visualizing the output layer
    public Color curve1Color = Color.cyan; // Color for first neuron's curve
    public Color curve2Color = Color.magenta; // Color for second neuron's curve
    public Color combinedCurveColor = Color.red; // Color for the combined output curve
    private LineRenderer curve1Line; // Line renderer for first neuron's activation
    private LineRenderer curve2Line; // Line renderer for second neuron's activation
    private LineRenderer combinedLine; // Line renderer for combined output
    private List<GameObject> curve1Points = new List<GameObject>(); // Points from first curve
    private List<GameObject> curve2Points = new List<GameObject>(); // Points from second curve
    private List<Vector3> combinedTargetPositions = new List<Vector3>(); // Target positions for combined points

    [Header("Position Settings")]
    public Vector3 visualizationPosition = Vector3.zero; // World position where the graph will be drawn
    public Quaternion visualizationRotation = Quaternion.identity; // Rotation of the graph
    public float visualizationScale = 1f; // Scale of the entire visualization
    public bool flipXAxis = false; // Option to flip the X axis

    [Header("Label Settings")]
    public bool showPlotLabel = true; // Toggle for showing a simple label below the plot
    public string plotLabel = "Activation Plot"; // Text content of the label
    public Color labelColor = Color.white; // Label text color
    public int labelFontSize = 64; // Font size for the label
    public float labelMargin = 0.3f; // Distance below the plot (in local units)
    private TextMesh plotLabelText; // Runtime-created TextMesh for the label

    [Header("Data Collection")]
    private List<Vector2> collectedDataPoints = new List<Vector2>();
    private float currentBiasShift = 0f;
    public float biasShiftSpeed = 1f;
    public float maxBiasShift = 2f;

    [Header("Animation Settings")]
    public float activationAnimationDuration = 1.0f;
    private bool isAnimatingActivation = false;
    private float activationAnimationProgress = 0f;
    private List<GameObject> weightedInputPoints = new List<GameObject>();
    private List<Vector3> targetActivationPositions = new List<Vector3>();
    private bool animationCompleted = false; // New flag to track if animation has completed

    // Public property to check animation status
    public bool IsAnimating => isAnimatingActivation;

    private LineRenderer defaultFunctionLine;
    private LineRenderer currentFunctionLine;
    private LineRenderer networkDataLine; // Line renderer for network data
    private List<Vector2> currentEpochData = new List<Vector2>(); // weighted input vs activation output
    private GameObject graphContainer; // Container for the graph visualization
    private GameObject pointsContainer; // Container for datapoint markers
    
    private int totalDataPointsInEpoch = 0;
    private bool autoUpdateLine = true; // Controls whether line updates immediately or waits for all points

    [Header("Result Plot")]
    public ResultPlotVisualizer resultPlotVisualizer;
    private List<Vector2> resultDataPoints = new List<Vector2>();

    private void Awake()
    {
        // Create a container for the graph that we can position, rotate, and scale
        graphContainer = new GameObject("GraphContainer");
        graphContainer.transform.SetParent(transform);
        
        // Create a container for the datapoint markers
        pointsContainer = new GameObject("PointsContainer");
        pointsContainer.transform.SetParent(graphContainer.transform);
        pointsContainer.transform.localPosition = Vector3.zero;
        pointsContainer.transform.localRotation = Quaternion.identity;
        pointsContainer.transform.localScale = Vector3.one;
        
        // Create a simple world-space text label under the plot (optional)
        if (showPlotLabel)
        {
            GameObject labelObj = new GameObject("PlotLabel");
            labelObj.transform.SetParent(graphContainer.transform);
            plotLabelText = labelObj.AddComponent<TextMesh>();
            plotLabelText.text = plotLabel;
            plotLabelText.color = labelColor;
            plotLabelText.anchor = TextAnchor.UpperCenter;
            plotLabelText.alignment = TextAlignment.Center;
            plotLabelText.fontSize = labelFontSize;
            plotLabelText.characterSize = 0.1f; // Scales glyphs; keep small for finer control
            plotLabelText.richText = false;
        }

        // Create default function line
        GameObject defaultLineObj = new GameObject("DefaultFunction");
        defaultLineObj.transform.SetParent(graphContainer.transform);
        defaultFunctionLine = defaultLineObj.AddComponent<LineRenderer>();
        defaultFunctionLine.material = lineMaterial;
        defaultFunctionLine.startColor = defaultFunctionColor;
        defaultFunctionLine.endColor = defaultFunctionColor;
        defaultFunctionLine.startWidth = lineWidth;
        defaultFunctionLine.endWidth = lineWidth;
        defaultFunctionLine.positionCount = resolution;
        defaultFunctionLine.useWorldSpace = false;

        // Create current function line
        GameObject currentLineObj = new GameObject("CurrentFunction");
        currentLineObj.transform.SetParent(graphContainer.transform);
        currentFunctionLine = currentLineObj.AddComponent<LineRenderer>();
        currentFunctionLine.material = lineMaterial;
        currentFunctionLine.startColor = currentFunctionColor;
        currentFunctionLine.endColor = currentFunctionColor;
        currentFunctionLine.startWidth = lineWidth;
        currentFunctionLine.endWidth = lineWidth;
        currentFunctionLine.positionCount = resolution;
        currentFunctionLine.useWorldSpace = false;

        // Create network data line
        GameObject networkDataLineObj = new GameObject("NetworkDataLine");
        networkDataLineObj.transform.SetParent(graphContainer.transform);
        networkDataLine = networkDataLineObj.AddComponent<LineRenderer>();
        networkDataLine.material = lineMaterial;
        networkDataLine.startColor = networkDataColor;
        networkDataLine.endColor = networkDataColor;
        networkDataLine.startWidth = lineWidth;
        networkDataLine.endWidth = lineWidth;
        networkDataLine.positionCount = resolution;
        networkDataLine.useWorldSpace = false;
        
        // Initially hide the network data line
        networkDataLine.enabled = false;
        
        // Create additional line renderers for output layer visualization
        CreateOutputLayerLines();
        
        // Set the initial position, rotation, and scale of the graph container
        UpdateGraphTransform();
        
        // Initialize the visualization
        ResetVisualization();
    }

    private void Start()
    {
        // Get singleton reference
        neuralNetwork = NeuralNetwork.Instance;
        if (neuralNetwork == null)
        {
            Debug.LogError("AFVisualizer: NeuralNetwork singleton not found!");
            return;
        }
        
        Debug.Log($"AFVisualizer: Using NeuralNetwork singleton: {neuralNetwork.name}");
        
        // Initialize components
        LineRenderer lineRenderer = GetComponent<LineRenderer>();
        if (lineRenderer == null)
        {
            Debug.LogError("AFVisualizer requires a LineRenderer component!");
            return;
        }

        // Also get data manager singleton reference if available
        if (dataManager == null)
        {
            dataManager = FindObjectOfType<NeuralNetworkDataManager>();
            if (dataManager == null)
            {
                Debug.LogWarning("AFVisualizer: No NeuralNetworkDataManager found!");
            }
        }
        
        // Set initial material and settings
        lineRenderer.material = lineMaterial;
        lineRenderer.startWidth = lineWidth;
        lineRenderer.endWidth = lineWidth;
        lineRenderer.useWorldSpace = true;

        // Clear any existing data point markers
        ClearDataPointMarkers();
        
        // Subscribe to network events
        if (neuralNetwork != null)
        {
            neuralNetwork.OnWeightsUpdated += OnNetworkWeightsUpdated;
        }
        
        // Initialize visualization
        PlotDefaultFunction();
        resultDataPoints = new List<Vector2>();
    }

    private void OnDestroy()
    {
        if (neuralNetwork != null)
        {
            neuralNetwork.OnWeightsUpdated -= OnNetworkWeightsUpdated;
        }
    }

    private void OnNetworkWeightsUpdated()
    {
        // Add debug logging to track parameter changes
        Debug.Log("=== AFVisualizer: Network weights/bias updated ===");
        LogCurrentParameters();
        
        // Update visualization when network weights change
        UpdateVisualization();
    }
    
    /// <summary>
    /// Log current neuron parameters for debugging
    /// </summary>
    private void LogCurrentParameters()
    {
        if (neuralNetwork == null || currentLayerIndex <= 0) return;
        
        try
        {
            double[] weights = neuralNetwork.GetNeuronWeights(currentLayerIndex - 1, currentNeuronIndex);
            double bias = neuralNetwork.GetNeuronBias(currentLayerIndex - 1, currentNeuronIndex);
            
            Debug.Log($"=== AFVisualizer Parameters for Layer {currentLayerIndex}, Neuron {currentNeuronIndex} ===");
            Debug.Log($"  Weights: [{string.Join(", ", System.Array.ConvertAll(weights, w => w.ToString("F3")))}]");
            Debug.Log($"  Bias: {bias:F3}");
            Debug.Log($"  Activation Type: {neuralNetwork.activationType}");
            
            // ENHANCED: Add specific logging for different layer types
            if (currentLayerIndex == 1)
            {
                Debug.Log($"  → HIDDEN LAYER NEURON: This should use trained W1/W2 and bias1/bias2 from training");
                Debug.Log($"  → Input-to-Hidden weights affect how input data gets transformed");
                Debug.Log($"  → Bias shift: {bias:F3} should reflect training updates");
            }
            else if (currentLayerIndex == neuralNetwork.layerSizes.Length - 1)
            {
                Debug.Log($"  → OUTPUT LAYER NEURON: This should use player-chosen W3/W4 and B5");
                Debug.Log($"  → Hidden-to-Output weights from backpropagation scene");
            }
            
            // Test activation calculation with a sample input
            float testInput = 1.0f;
            float testActivation = CalculateActivation(testInput);
            Debug.Log($"  Test: input={testInput} → activation={testActivation:F3}");
            Debug.Log($"  Formula: activation = f({testInput:F3} * {weights[0]:F3} + {bias:F3}) = f({testInput * weights[0] + bias:F3}) = {testActivation:F3}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error logging parameters: {e.Message}");
        }
    }

    // Update the graph container's transform properties
    private void UpdateGraphTransform()
    {
        // Update the graph container's transform
        graphContainer.transform.position = visualizationPosition;
        graphContainer.transform.rotation = visualizationRotation;
        graphContainer.transform.localScale = Vector3.one * visualizationScale;
        
        // If we need to flip the X axis
        if (flipXAxis)
        {
            graphContainer.transform.localScale = new Vector3(-visualizationScale, visualizationScale, visualizationScale);
        }

        // Ensure label stays positioned just below the plotted area
        UpdateLabelPosition();
    }

    // Keep the label positioned just below the visible plot area
    private void UpdateLabelPosition()
    {
        if (plotLabelText == null) return;

        // Center under the plot, offset downward by yRange and margin in local space
        plotLabelText.text = plotLabel;
        plotLabelText.color = labelColor;
        plotLabelText.fontSize = labelFontSize;
        plotLabelText.gameObject.SetActive(showPlotLabel);

        // Local coordinates: x=0 centered, y just below the bottom of the plot
        Vector3 localPos = new Vector3(0f, -yRange - labelMargin, 0f);
        plotLabelText.transform.localPosition = localPos;
        plotLabelText.transform.localRotation = Quaternion.identity;
    }

    private void OnEnable()
    {
        if (neuralNetwork != null)
        {
            neuralNetwork.OnNetworkUpdated += ResetVisualization;
        }
    }

    private void OnDisable()
    {
        if (neuralNetwork != null)
        {
            neuralNetwork.OnNetworkUpdated -= ResetVisualization;
        }
    }

    private void PlotDefaultFunction()
    {
        // Plot the default activation function based on the neural network's current setting
        for (int i = 0; i < resolution; i++)
        {
            float x = Mathf.Lerp(-xRange, xRange, (float)i / (resolution - 1));
            float y = CalculateActivation(x);
            Vector3 position = new Vector3(x, y, 0);
            defaultFunctionLine.SetPosition(i, position);
        }
    }

    // Called when new epoch starts - resets collection state
    public void SetupForNewEpoch(int totalDataPoints)
    {
        Debug.Log("Setting up for new epoch");
        totalDataPointsInEpoch = totalDataPoints;
        ClearDataPointMarkers();
        
        // Reset current function line to default
        ResetVisualization();
    }
    
    // Add a datapoint when collected by player
    public void AddCollectedDataPoint(float weightedInput, float activation, float accuracy = 1f)
    {
        // Debug.Log($"Adding collected datapoint - weightedInput: {weightedInput}, activation: {activation}, accuracy: {accuracy}"); // DISABLED - too verbose
        
        // Add the data point to the collected points list
        collectedDataPoints.Add(new Vector2(weightedInput, 0)); // y=0 as it's on x-axis
        
        // Create a visual representation of the data point at the correct position on the plot
        Vector3 plotPosition = GetWorldPositionOnPlot(weightedInput, 0); // Place on x-axis
        // Debug.Log($"Created point at plot position: {plotPosition}"); // DISABLED - too verbose
        
        // Create the data point as a child of the graph container
        GameObject dataPoint = Instantiate(dataPointPrefab, plotPosition, Quaternion.identity, graphContainer.transform);
        dataPoint.transform.localScale = Vector3.one * pointSize;

        TextMeshPro tmp = dataPoint.GetComponentInChildren<TextMeshPro>();
        if (tmp != null)
        {
            tmp.text = $"x={weightedInput:F2}\ny={activation:F2}";
        }
        
        // Set the color based on accuracy (green for perfect hits, yellow for less accurate)
        Renderer renderer = dataPoint.GetComponent<Renderer>();
        if (renderer != null)
        {
            if (pointMaterial != null)
                renderer.material = pointMaterial;
            // Interpolate between green and yellow based on accuracy
            renderer.material.color = Color.Lerp(Color.yellow, Color.green, accuracy);
        }
        
        // Store reference to the point
        weightedInputPoints.Add(dataPoint);
        
        // Update result plot using weighted input
        if (resultPlotVisualizer != null)
        {
            List<Vector2> resultPoints = new List<Vector2>();
            for (int i = 0; i < collectedDataPoints.Count; i++)
            {
                float rawInput = collectedDataPoints[i].x;
                float weightedInputValue = CalculateWeightedInput(rawInput); // NEW: Use weighted input
                resultPoints.Add(new Vector2(weightedInputValue, 0)); // Start at y=0, x=weighted input
            }
            resultPlotVisualizer.UpdatePlot(resultPoints);
        }
        
        Debug.Log($"Total points collected: {collectedDataPoints.Count}");
    }
    
    // Create a visual marker for a datapoint on the graph
    private void CreateDataPointMarker(Vector2 point)
    {
        GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        marker.transform.SetParent(pointsContainer.transform);
        marker.transform.localPosition = new Vector3(point.x, point.y, -0.1f); // Slightly in front of the line
        marker.transform.localScale = Vector3.one * pointSize;
        
        // Set material and color
        Renderer renderer = marker.GetComponent<Renderer>();
        if (renderer != null)
        {
            if (pointMaterial != null)
                renderer.material = pointMaterial;
            renderer.material.color = dataPointColor;
        }
        
        // Remove collider to prevent interaction
        Collider collider = marker.GetComponent<Collider>();
        if (collider != null)
        {
            Destroy(collider);
        }
    }
    
    // Get the number of collected data points
    public int GetCollectedPointsCount()
    {
        return collectedDataPoints.Count;
    }

    // Helper to expose total generated datapoints for a neuron (fixed rule = 20)
    public int GetGeneratedPointsCountPerNeuron()
    {
        return 20;
    }

    // Clear all data point markers and collected points
    public void ClearDataPointMarkers()
    {
        Debug.Log("Clearing all data points");
        
        // Clear all points
        foreach (var point in weightedInputPoints)
        {
            if (point != null)
            {
                Destroy(point);
            }
        }
        weightedInputPoints.Clear();
        
        // Clear the collected data points list
        collectedDataPoints.Clear();
        targetActivationPositions.Clear();
        
        // Reset the animation state
        animationCompleted = false;
        isAnimatingActivation = false;
        activationAnimationProgress = 0f;
        
        // Hide network data line
        networkDataLine.enabled = false;
        
        // Reset the visualization
        ResetVisualization();
    }

    private void Update()
    {
        // If animation is completed, keep the network data line visible
        if (animationCompleted)
        {
            return;
        }

        if (isAnimatingActivation)
        {
            activationAnimationProgress += Time.deltaTime / activationAnimationDuration;
            float progress = Mathf.Clamp01(activationAnimationProgress);

            if (isOutputLayer)
            {
                AnimateOutputLayerPoints(progress);
            }
            else
            {
                AnimateRegularPoints(progress);
            }

            // When animation is complete
            if (progress >= 1f)
            {
                Debug.Log("Animation completed");
                isAnimatingActivation = false;
                activationAnimationProgress = 0f; // Reset progress
                animationCompleted = true; // Mark animation as completed
                
                if (isOutputLayer)
                {
                    CompleteOutputLayerAnimation();
                }
                else
                {
                    CompleteRegularAnimation();
                }
            }
        }

        // Update network data line every frame during animation
        if (isAnimatingActivation)
        {
            UpdateNetworkDataLine();
        }
    }

    // Complete regular animation for hidden layers
    private void CompleteRegularAnimation()
    {
        // Keep network data line visible
        networkDataLine.enabled = true;
        
        // FIXED: Show the red curve after animation completes - this gives the feeling that the moving points formed the curve
        currentFunctionLine.enabled = true;
        Debug.Log("Animation completed - red activation curve now visible");
        
        // Update the points' colors to indicate they've gone through activation
        foreach (GameObject point in weightedInputPoints)
        {
            if (point != null)
            {
                Renderer renderer = point.GetComponent<Renderer>();
                if (renderer != null)
                {
                    renderer.material.color = Color.blue; // Change color to indicate activation
                }
            }
        }
        
        // Make sure all points are at their final positions
        for (int i = 0; i < weightedInputPoints.Count; i++)
        {
            if (weightedInputPoints[i] != null && i < targetActivationPositions.Count)
            {
                weightedInputPoints[i].transform.position = targetActivationPositions[i]; // Set final position
                
                // Lock the point by removing any component that might move it
                DataPoint dataPoint = weightedInputPoints[i].GetComponent<DataPoint>();
                if (dataPoint != null)
                {
                    dataPoint.enabled = false; // Disable the data point component
                }
            }
        }

        // Update result plot with final positions using weighted input
        if (resultPlotVisualizer != null)
        {
            List<Vector2> resultPoints = new List<Vector2>();
            for (int i = 0; i < collectedDataPoints.Count; i++)
            {
                float rawInput = collectedDataPoints[i].x;
                float weightedInput = CalculateWeightedInput(rawInput); // NEW: Use weighted input
                float activation = CalculateActivation(rawInput);
                resultPoints.Add(new Vector2(weightedInput, activation)); // X = weighted input
            }
            resultPlotVisualizer.UpdatePlot(resultPoints);
        }
    }

    // Complete output layer animation
    private void CompleteOutputLayerAnimation()
    {
        // Keep all three curves visible
        curve1Line.enabled = true;
        curve2Line.enabled = true;
        combinedLine.enabled = true;
        
        // FIXED: Do NOT show red activation curve for output layer - it's meaningless (linear y=x)
        currentFunctionLine.enabled = false;
        Debug.Log("Output layer animation completed - combined curve visible, red activation curve stays hidden");
        
        // FIXED: Destroy intermediate curve points - only keep the final combined result
        Debug.Log($"Destroying {curve1Points.Count + curve2Points.Count} intermediate points, keeping only final combined result");
        
        // Destroy curve1 points (first hidden neuron activation points)
        foreach (GameObject point in curve1Points)
        {
            if (point != null)
            {
                Destroy(point);
            }
        }
        curve1Points.Clear();
        
        // Destroy curve2 points (second hidden neuron activation points)  
        foreach (GameObject point in curve2Points)
        {
            if (point != null)
            {
                Destroy(point);
            }
        }
        curve2Points.Clear();
        
        // Create single set of final combined points at the combined positions
        for (int i = 0; i < combinedTargetPositions.Count; i++)
        {
            Vector3 finalPos = combinedTargetPositions[i];
            GameObject finalPoint = Instantiate(dataPointPrefab, finalPos, Quaternion.identity, graphContainer.transform);
            finalPoint.transform.localScale = Vector3.one * pointSize;
            
            Renderer renderer = finalPoint.GetComponent<Renderer>();
            if (renderer != null)
            {
                if (pointMaterial != null) renderer.material = pointMaterial;
                renderer.material.color = combinedCurveColor; // Final combined color
            }
            
            // Disable DataPoint component to prevent interaction
            DataPoint dataPointComponent = finalPoint.GetComponent<DataPoint>();
            if (dataPointComponent != null)
            {
                dataPointComponent.enabled = false;
            }
            
            // Add to curve1Points list to maintain compatibility with other systems
            curve1Points.Add(finalPoint);
        }
        
        Debug.Log($"Created {curve1Points.Count} final combined points representing the sum result");

        // Update result plot with final combined positions using weighted input
        if (resultPlotVisualizer != null)
        {
            List<Vector2> resultPoints = new List<Vector2>();
            for (int i = 0; i < collectedDataPoints.Count; i++)
            {
                float rawInput = collectedDataPoints[i].x;
                float weightedInput = CalculateWeightedInput(rawInput); // NEW: Use weighted input
                float combinedActivation = CalculateOutputLayerActivation(rawInput);
                resultPoints.Add(new Vector2(weightedInput, combinedActivation)); // X = weighted input
            }
            resultPlotVisualizer.UpdatePlot(resultPoints);
        }
    }

    // Calculate weighted input for a given raw input (separate from activation calculation)
    private float CalculateWeightedInput(float rawInput)
    {
        if (neuralNetwork == null || currentLayerIndex <= 0) return rawInput;

        try
        {
            // For non-input layers, apply weights and bias
            double[] weights = neuralNetwork.GetNeuronWeights(currentLayerIndex - 1, currentNeuronIndex);
            double bias = neuralNetwork.GetNeuronBias(currentLayerIndex - 1, currentNeuronIndex);
            
            if (weights == null || weights.Length == 0) return rawInput;
            
            // Calculate weighted input: w*x + b
            double weightedInput = rawInput * weights[0] + bias; // Simplified for single input
            return (float)weightedInput;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error calculating weighted input: {e.Message}");
            return rawInput;
        }
    }

    // Calculate activation using current neuron's function
    private float CalculateActivation(float x)
    {
        if (neuralNetwork == null) return Mathf.Max(0, x);

        // Calculate weighted input
        double weightedInput = x;
        if (currentLayerIndex > 0)
        {
            // For non-input layers, apply weights and bias
            double[] weights = neuralNetwork.GetNeuronWeights(currentLayerIndex - 1, currentNeuronIndex);
            double bias = neuralNetwork.GetNeuronBias(currentLayerIndex - 1, currentNeuronIndex);
            
            // ENHANCED DEBUG: Log parameters being used for calculation (DISABLED - too verbose)
            // if (Time.frameCount % 60 == 0) // Log once per second at 60fps
            // {
            //     Debug.Log($"CalculateActivation: Using weights[0]={weights[0]:F3}, bias={bias:F3} for input x={x:F3}");
            // }
            //Debug.LogWarning($"CalculateActivation: Using weights[0]={weights[0]:F3}, bias={bias:F3} for input x={x:F3}");
            
            weightedInput = x * weights[0] + bias; // Simplified for single input
        }
        
        // Apply activation function
        return (float)neuralNetwork.CalculateNeuronActivation(currentLayerIndex, currentNeuronIndex, weightedInput);
    }

    // Start activation animation
    public void StartActivationAnimation()
    {
        if (collectedDataPoints.Count == 0 || isAnimatingActivation || animationCompleted)
        {
            Debug.LogWarning("No points to animate, animation already in progress, or animation already completed!");
            return;
        }

        Debug.Log($"Starting activation animation with {collectedDataPoints.Count} points");
        
        // Reset animation state
        isAnimatingActivation = true;
        activationAnimationProgress = 0f;

        if (isOutputLayer)
        {
            StartOutputLayerAnimation();
        }
        else
        {
            StartRegularAnimation();
        }
    }

    // Start regular activation animation for hidden layers
    private void StartRegularAnimation()
    {
        // Show and update network data line
        networkDataLine.enabled = true;
        UpdateNetworkDataLine();

        // Calculate target positions based on current neuron's parameters
        targetActivationPositions.Clear();
        for (int i = 0; i < collectedDataPoints.Count; i++)
        {
            float x = collectedDataPoints[i].x;
            float activation = CalculateActivation(x);
            Vector3 targetPos = GetWorldPositionOnPlot(x, activation);
            targetActivationPositions.Add(targetPos);
            
            // Debug.Log($"Point {i}: Input={x}, Activation={activation}, TargetPos={targetPos}"); // DISABLED - too verbose
        }

        // Update result plot with initial positions using weighted input
        if (resultPlotVisualizer != null)
        {
            List<Vector2> resultPoints = new List<Vector2>();
            for (int i = 0; i < collectedDataPoints.Count; i++)
            {
                float rawInput = collectedDataPoints[i].x;
                float weightedInput = CalculateWeightedInput(rawInput); // NEW: Use weighted input
                resultPoints.Add(new Vector2(weightedInput, 0)); // Start at y=0, x=weighted input
            }
            resultPlotVisualizer.UpdatePlot(resultPoints);
        }
    }

    // Start output layer animation with two curves combining
    private void StartOutputLayerAnimation()
    {
        Debug.Log("Starting output layer animation with curve combination");
        
        // Show the three curves
        curve1Line.enabled = true;
        curve2Line.enabled = true;
        combinedLine.enabled = true;
        
        // Plot the three curves
        PlotOutputLayerCurves();
        
        // Create points for both curves and calculate their combination targets
        CreateOutputLayerPoints();
        
        // Update result plot with initial positions using weighted input
        if (resultPlotVisualizer != null)
        {
            List<Vector2> resultPoints = new List<Vector2>();
            for (int i = 0; i < collectedDataPoints.Count; i++)
            {
                float rawInput = collectedDataPoints[i].x;
                float weightedInput = CalculateWeightedInput(rawInput); // NEW: Use weighted input
                resultPoints.Add(new Vector2(weightedInput, 0)); // Start at y=0, x=weighted input
            }
            resultPlotVisualizer.UpdatePlot(resultPoints);
        }
    }

    // Update visualization based on collected points
    public void UpdateVisualization()
    {
        // Plot the current activation function
        for (int i = 0; i < resolution; i++)
        {
            float x = Mathf.Lerp(-xRange, xRange, (float)i / (resolution - 1));
            float y = CalculateActivation(x);
            Vector3 position = new Vector3(x, y, 0);
            currentFunctionLine.SetPosition(i, position);
        }
    }

    private float InterpolateActivationFromCollected(float x)
    {
        if (collectedDataPoints.Count == 0) return Mathf.Max(0, x); // Default ReLU

        // Find the two closest points
        int leftIndex = 0;
        int rightIndex = collectedDataPoints.Count - 1;

        for (int i = 0; i < collectedDataPoints.Count - 1; i++)
        {
            if (collectedDataPoints[i].x <= x && collectedDataPoints[i + 1].x >= x)
            {
                leftIndex = i;
                rightIndex = i + 1;
                break;
            }
        }

        // Linear interpolation
        float t = (x - collectedDataPoints[leftIndex].x) / 
                 (collectedDataPoints[rightIndex].x - collectedDataPoints[leftIndex].x);
        return Mathf.Lerp(collectedDataPoints[leftIndex].y, collectedDataPoints[rightIndex].y, t);
    }

    private void ResetVisualization()
    {
        // Reset the current function line to match the neural network's activation function
        for (int i = 0; i < resolution; i++)
        {
            float x = Mathf.Lerp(-xRange, xRange, (float)i / (resolution - 1));
            float y = CalculateActivation(x);
            Vector3 position = new Vector3(x, y, 0);
            currentFunctionLine.SetPosition(i, position);
        }
        
        // Reset animation state flags
        animationCompleted = false;
        
        // FIXED: Hide the red curve initially - it should only appear after animation completes
        currentFunctionLine.enabled = false;
        
        // Debug log to verify reset
        Debug.Log("Visualization reset to default - red curve hidden until animation completes");
    }
    
    // Method to position visualization at the end of the tunnel
    public void PositionAtTunnelEnd(Vector3 playerPosition, Vector3 playerForward, float tunnelLength, float tunnelRadius)
    {
        // Place the visualization at the end of the tunnel
        Vector3 endPosition = playerPosition + playerForward * tunnelLength;
        
        // Position slightly up from the center line
        endPosition += Vector3.up * (tunnelRadius * 0.5f);
        
        // Update visualization position
        visualizationPosition = endPosition;
        
        // Create a rotation that faces the player without flipping the x-axis
        Vector3 lookDirection = playerPosition - endPosition;
        
        // Create rotation facing player but preserving correct axis orientation
        Quaternion lookRotation = Quaternion.LookRotation(lookDirection);
        
        // We need to make sure the X-axis is properly oriented
        visualizationRotation = lookRotation * Quaternion.Euler(0, 180, 0);
        
        // Set scale and check if we need to flip the X axis
        visualizationScale = tunnelRadius * 0.4f;
        flipXAxis = false; // Set to true if still flipped after the rotation adjustment
        
        // Update the graph container's transform
        UpdateGraphTransform();
    }
    
    // Method to position visualization as part of the skybox
    public void PositionAsSkyboxElement(Vector3 playerPosition, Vector3 playerForward, float skyboxDistance)
    {
        // Place the visualization far in front as part of the skybox
        Vector3 skyboxPosition = playerPosition + playerForward * skyboxDistance;
        
        // Position it slightly higher to be more visible
        skyboxPosition += Vector3.up * (skyboxDistance * 0.1f);
        
        // Update visualization position
        visualizationPosition = skyboxPosition;
        
        // Create a rotation that faces the player without flipping the x-axis
        // The graph should look like it's painted on the skybox, facing the player
        Vector3 lookDirection = playerPosition - skyboxPosition;
        
        // Create rotation facing player but preserving correct axis orientation
        Quaternion lookRotation = Quaternion.LookRotation(lookDirection);
        
        // We need to make sure the X-axis is properly oriented
        visualizationRotation = lookRotation * Quaternion.Euler(0, 180, 0);
        
        // Set scale and check if we need to flip the X axis
        visualizationScale = skyboxDistance * 0.1f;
        flipXAxis = false; // Set to true if still flipped after the rotation adjustment
        
        UpdateGraphTransform();
    }

    public void ApplyBiasShift(float biasValue)
    {
        // Clamp the bias shift to prevent extreme values
        currentBiasShift = Mathf.Clamp(biasValue * biasShiftSpeed, -maxBiasShift, maxBiasShift);
        UpdateVisualization();
    }

    public Vector3 GetWorldPositionOnPlot(float x, float y)
    {
        // Convert plot coordinates to local space
        float normalizedX = (x + xRange) / (2 * xRange);
        float normalizedY = y / yRange;
        
        // Calculate local position
        Vector3 localPosition = new Vector3(
            Mathf.Lerp(-xRange, xRange, normalizedX),
            y,
            0 // Ensure z is always 0 to keep points in the same plane
        );
        
        // Convert to world position using the graph container's transform
        Vector3 worldPos = graphContainer.transform.TransformPoint(localPosition);
        // Debug.Log($"Converting plot coordinates ({x}, {y}) to world position: {worldPos}"); // DISABLED - too verbose
        return worldPos;
    }

    // Method to be called when network is initialized
    public void OnNetworkInitialized()
    {
        Debug.Log("Network initialized, clearing visualization");
        ClearDataPointMarkers();
        ResetVisualization(); // This will also reset the animationCompleted flag
    }

    // Update network data line
    public void UpdateNetworkDataLine()
    {
        // Plot the activation function using the current neuron's function
        for (int i = 0; i < resolution; i++)
        {
            float x = Mathf.Lerp(-xRange, xRange, (float)i / (resolution - 1));
            float y = CalculateActivation(x);
            Vector3 position = new Vector3(x, y, 0);
            networkDataLine.SetPosition(i, position);
        }
    }

    // Method to visualize data points
    public void VisualizeDataPoints()
    {
        // This method is no longer needed as points are created in AddCollectedDataPoint
        // and animated in Update
    }

    public void AddDataPoint(float inputValue, float label)
    {
        // Add point to result data
        resultDataPoints.Add(new Vector2(inputValue, label));
        
        // Update result plot with current points
        if (resultPlotVisualizer != null)
        {
            resultPlotVisualizer.UpdatePlot(resultDataPoints);
        }
    }

    public void UpdateActivationPlot()
    {
        // Update result plot with activation outputs
        if (resultPlotVisualizer != null)
        {
            List<Vector2> activationResults = new List<Vector2>();
            for (int i = 0; i < collectedDataPoints.Count; i++)
            {
                float activationOutput = CalculateActivation(collectedDataPoints[i].x);
                activationResults.Add(new Vector2(collectedDataPoints[i].x, activationOutput));
            }
            resultPlotVisualizer.UpdatePlot(activationResults);
            resultPlotVisualizer.AnimatePointsToFinalPositions();
        }
    }

    public void SetCurrentNeuron(int layerIndex, int neuronIndex)
    {
        Debug.Log($"=== AFVisualizer: Setting current neuron to Layer {layerIndex}, Neuron {neuronIndex} ===");
        
        // Store previous values for comparison
        int prevLayerIndex = currentLayerIndex;
        int prevNeuronIndex = currentNeuronIndex;
        
        currentLayerIndex = layerIndex;
        currentNeuronIndex = neuronIndex;
        
        // Check if this is the output layer (assuming it's the last layer)
        isOutputLayer = (layerIndex == neuralNetwork.layerSizes.Length - 1);
        Debug.Log($"Is output layer: {isOutputLayer}");
        
        // Debug network structure
        if (neuralNetwork != null)
        {
            Debug.Log($"Network structure: {string.Join("-", neuralNetwork.layerSizes)}");
            Debug.Log($"Current layer {layerIndex}, neuron {neuronIndex}");
            if (isOutputLayer && layerIndex > 0)
            {
                Debug.Log($"Previous layer ({layerIndex - 1}) has {neuralNetwork.layerSizes[layerIndex - 1]} neurons");
            }
            
            // Log current parameters for this neuron
            if (layerIndex > 0)
            {
                try
                {
                    double[] weights = neuralNetwork.GetNeuronWeights(layerIndex - 1, neuronIndex);
                    double bias = neuralNetwork.GetNeuronBias(layerIndex - 1, neuronIndex);
                    Debug.Log($"Neuron parameters: Weights=[{string.Join(", ", System.Array.ConvertAll(weights, w => w.ToString("F3")))}], Bias={bias:F3}");
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"Error getting neuron parameters: {e.Message}");
                }
            }
        }
        
        // Clear existing visualization
        ClearDataPointMarkers();
        ClearOutputLayerVisualization();
        
        // Hide network data line when selecting new neuron
        networkDataLine.enabled = false;
        
        // FIXED: For output layer, never show the red activation curve since it's meaningless (linear y=x)
        // For hidden layers, hide red curve initially - it will show after animation
        if (isOutputLayer)
        {
            currentFunctionLine.enabled = false; // Never show for output layer
            Debug.Log("Output layer selected - red activation curve disabled (meaningless for linear output)");
        }
        else
        {
            currentFunctionLine.enabled = false; // Hide initially for hidden layers
            Debug.Log("Hidden layer selected - red activation curve will show after animation");
        }
        
        // Reset visualization for new neuron
        ResetVisualization();
        
        Debug.Log($"Neuron selection complete - previous: ({prevLayerIndex},{prevNeuronIndex}), new: ({layerIndex},{neuronIndex})");
        
        // If this is not the input layer, get the input from previous layer
        if (layerIndex > 0)
        {
            // Get the activation from the previous layer's neuron
            double input = neuralNetwork.GetNeuronInput(layerIndex, neuronIndex);
            Debug.Log($"Previous layer activation: {input}");
            
            // Add this as a data point
            AddCollectedDataPoint((float)input, 0);
        }
    }

    // Public method to calculate activation for external use
    public float CalculateActivationForInput(float input)
    {
        return CalculateActivation(input);
    }

    // Method to get the final result plot data that would be shown in ResultPlotVisualizer
    public List<Vector2> GetResultPlotData()
    {
        List<Vector2> activationResults = new List<Vector2>();
        for (int i = 0; i < collectedDataPoints.Count; i++)
        {
            float rawInput = collectedDataPoints[i].x;
            float weightedInput = CalculateWeightedInput(rawInput); // NEW: Use weighted input for X-axis
            
            float activationOutput;
            if (isOutputLayer)
            {
                activationOutput = CalculateOutputLayerActivation(rawInput);
            }
            else
            {
                activationOutput = CalculateActivation(rawInput);
            }
            
            // X = weighted input, Y = activation output
            activationResults.Add(new Vector2(weightedInput, activationOutput));
        }
        return activationResults;
    }

    // Method to get output layer curve data for saving
    public OutputLayerCurveData GetOutputLayerCurveData()
    {
        if (!isOutputLayer || collectedDataPoints.Count == 0)
        {
            return null;
        }

        OutputLayerCurveData curveData = new OutputLayerCurveData();
        curveData.inputPoints = new List<Vector2>(collectedDataPoints);
        curveData.curve1Points = new List<Vector2>();
        curveData.curve2Points = new List<Vector2>();
        curveData.combinedPoints = new List<Vector2>();

        // Calculate curve data for each input point
        for (int i = 0; i < collectedDataPoints.Count; i++)
        {
            float x = collectedDataPoints[i].x;
            float activation1 = CalculatePreviousLayerActivation(0, x);
            float activation2 = CalculatePreviousLayerActivation(1, x);
            float combinedActivation = CalculateOutputLayerActivation(x);

            curveData.curve1Points.Add(new Vector2(x, activation1));
            curveData.curve2Points.Add(new Vector2(x, activation2));
            curveData.combinedPoints.Add(new Vector2(x, combinedActivation));
        }

        Debug.Log($"Saved output layer curve data with {curveData.inputPoints.Count} points");
        return curveData;
    }

    // Method to restore the completed visualization state from saved plot data
    public void RestoreCompletedState(List<Vector2> savedPlotData)
    {
        if (savedPlotData == null || savedPlotData.Count == 0)
        {
            Debug.LogWarning("No saved plot data to restore");
            return;
        }

        Debug.Log($"Restoring completed state with {savedPlotData.Count} data points");

        // Clear current state
        ClearDataPointMarkers();

        if (isOutputLayer)
        {
            RestoreOutputLayerState(savedPlotData);
        }
        else
        {
            RestoreRegularLayerState(savedPlotData);
        }
        
        // FIXED: Show the red curve when restoring completed state - but only for hidden layers
        if (!isOutputLayer)
        {
            currentFunctionLine.enabled = true;
            Debug.Log("Restored completed state - red activation curve visible for hidden layer");
        }
        else
        {
            currentFunctionLine.enabled = false;
            Debug.Log("Restored completed state - red activation curve stays hidden for output layer");
        }
    }

    // Restore state for regular (hidden) layers
    private void RestoreRegularLayerState(List<Vector2> savedPlotData)
    {
        // Recreate collected data points from saved data
        collectedDataPoints.Clear();
        weightedInputPoints.Clear();
        targetActivationPositions.Clear();

        for (int i = 0; i < savedPlotData.Count; i++)
        {
            Vector2 dataPoint = savedPlotData[i];
            
            // Add to collected points
            collectedDataPoints.Add(new Vector2(dataPoint.x, 0)); // y=0 as it's on x-axis initially
            
            // Create the visual point at the final activation position
            Vector3 finalPosition = GetWorldPositionOnPlot(dataPoint.x, dataPoint.y);
            GameObject point = Instantiate(dataPointPrefab, finalPosition, Quaternion.identity, graphContainer.transform);
            point.transform.localScale = Vector3.one * pointSize;
            
            // Set blue color to indicate it's completed
            Renderer renderer = point.GetComponent<Renderer>();
            if (renderer != null)
            {
                if (pointMaterial != null)
                    renderer.material = pointMaterial;
                renderer.material.color = Color.blue; // Blue indicates completed activation
            }
            
            // Disable DataPoint component to prevent interaction
            DataPoint dataPointComponent = point.GetComponent<DataPoint>();
            if (dataPointComponent != null)
            {
                dataPointComponent.enabled = false;
            }
            
            weightedInputPoints.Add(point);
            targetActivationPositions.Add(finalPosition);
        }

        // Set the completed state flags
        animationCompleted = true;
        isAnimatingActivation = false;
        activationAnimationProgress = 0f;

        // Show the network data line
        networkDataLine.enabled = true;
        UpdateNetworkDataLine();

        // Update result plot
        if (resultPlotVisualizer != null)
        {
            resultPlotVisualizer.UpdatePlot(savedPlotData);
        }

        Debug.Log("Regular layer completed state restored successfully");
    }

    // Restore state for output layer with curves
    private void RestoreOutputLayerState(List<Vector2> savedPlotData)
    {
        // Recreate collected data points from combined output data
        collectedDataPoints.Clear();
        curve1Points.Clear();
        curve2Points.Clear();
        combinedTargetPositions.Clear();

        // Extract input values from the saved combined output data
        for (int i = 0; i < savedPlotData.Count; i++)
        {
            Vector2 combinedPoint = savedPlotData[i];
            collectedDataPoints.Add(new Vector2(combinedPoint.x, 0)); // y=0 as starting point
        }

        // Show and plot the three curves
        curve1Line.enabled = true;
        curve2Line.enabled = true;
        combinedLine.enabled = true;
        PlotOutputLayerCurves();

        // FIXED: Create only single set of final combined points - no intermediate points needed
        for (int i = 0; i < collectedDataPoints.Count; i++)
        {
            float x = collectedDataPoints[i].x;
            float combinedActivation = CalculateOutputLayerActivation(x);
            
            // Create final combined position
            Vector3 finalCombinedPos = GetWorldPositionOnPlot(x, combinedActivation);
            combinedTargetPositions.Add(finalCombinedPos);

            // Create only one point representing the final combined result
            GameObject finalPoint = Instantiate(dataPointPrefab, finalCombinedPos, Quaternion.identity, graphContainer.transform);
            finalPoint.transform.localScale = Vector3.one * pointSize;
            
            Renderer renderer = finalPoint.GetComponent<Renderer>();
            if (renderer != null)
            {
                if (pointMaterial != null) renderer.material = pointMaterial;
                renderer.material.color = combinedCurveColor; // Final combined color
            }
            
            // Disable DataPoint component
            DataPoint dataPointComponent = finalPoint.GetComponent<DataPoint>();
            if (dataPointComponent != null)
            {
                dataPointComponent.enabled = false;
            }
            
            // Add to curve1Points list to maintain compatibility
            curve1Points.Add(finalPoint);
        }
        
        Debug.Log($"Restored {curve1Points.Count} final combined points for output layer");

        // Set the completed state flags
        animationCompleted = true;
        isAnimatingActivation = false;
        activationAnimationProgress = 0f;

        // Update result plot with combined output
        if (resultPlotVisualizer != null)
        {
            resultPlotVisualizer.UpdatePlot(savedPlotData);
        }

        Debug.Log("Output layer completed state restored successfully");
    }

    // Method to restore output layer state from saved curve data
    public void RestoreOutputLayerFromCurveData(OutputLayerCurveData curveData)
    {
        if (curveData == null || !isOutputLayer)
        {
            Debug.LogWarning("Invalid curve data or not output layer");
            return;
        }

        Debug.Log($"Restoring output layer from curve data with {curveData.inputPoints.Count} points");

        // Clear current state
        ClearDataPointMarkers();

        // Restore collected data points
        collectedDataPoints.Clear();
        collectedDataPoints.AddRange(curveData.inputPoints);

        // Show and plot the three curves
        curve1Line.enabled = true;
        curve2Line.enabled = true;
        combinedLine.enabled = true;
        PlotOutputLayerCurves();

        // FIXED: Create only single set of final combined points - no intermediate points needed
        curve1Points.Clear();
        curve2Points.Clear();
        combinedTargetPositions.Clear();

        for (int i = 0; i < curveData.combinedPoints.Count; i++)
        {
            Vector2 combinedPoint = curveData.combinedPoints[i];
            Vector3 finalCombinedPos = GetWorldPositionOnPlot(combinedPoint.x, combinedPoint.y);
            combinedTargetPositions.Add(finalCombinedPos);

            // Create only one point representing the final combined result
            GameObject finalPoint = Instantiate(dataPointPrefab, finalCombinedPos, Quaternion.identity, graphContainer.transform);
            finalPoint.transform.localScale = Vector3.one * pointSize;
            
            Renderer renderer = finalPoint.GetComponent<Renderer>();
            if (renderer != null)
            {
                if (pointMaterial != null) renderer.material = pointMaterial;
                renderer.material.color = combinedCurveColor; // Final combined color
            }
            
            DataPoint dataPointComponent = finalPoint.GetComponent<DataPoint>();
            if (dataPointComponent != null)
            {
                dataPointComponent.enabled = false;
            }
            
            // Add to curve1Points list to maintain compatibility
            curve1Points.Add(finalPoint);
        }
        
        Debug.Log($"Restored {curve1Points.Count} final combined points (no intermediate points)");

        // Set completed state
        animationCompleted = true;
        isAnimatingActivation = false;
        activationAnimationProgress = 0f;

        // Update result plot
        if (resultPlotVisualizer != null)
        {
            resultPlotVisualizer.UpdatePlot(curveData.combinedPoints);
        }

        Debug.Log("Output layer state restored from curve data successfully");
    }

    // Create line renderers for output layer visualization
    private void CreateOutputLayerLines()
    {
        // Create curve 1 line (first neuron from previous layer)
        GameObject curve1LineObj = new GameObject("Curve1Line");
        curve1LineObj.transform.SetParent(graphContainer.transform);
        curve1Line = curve1LineObj.AddComponent<LineRenderer>();
        curve1Line.material = lineMaterial;
        curve1Line.startColor = curve1Color;
        curve1Line.endColor = curve1Color;
        curve1Line.startWidth = lineWidth;
        curve1Line.endWidth = lineWidth;
        curve1Line.positionCount = resolution;
        curve1Line.useWorldSpace = false;
        curve1Line.enabled = false; // Initially hidden

        // Create curve 2 line (second neuron from previous layer)
        GameObject curve2LineObj = new GameObject("Curve2Line");
        curve2LineObj.transform.SetParent(graphContainer.transform);
        curve2Line = curve2LineObj.AddComponent<LineRenderer>();
        curve2Line.material = lineMaterial;
        curve2Line.startColor = curve2Color;
        curve2Line.endColor = curve2Color;
        curve2Line.startWidth = lineWidth;
        curve2Line.endWidth = lineWidth;
        curve2Line.positionCount = resolution;
        curve2Line.useWorldSpace = false;
        curve2Line.enabled = false; // Initially hidden

        // Create combined line (output layer result)
        GameObject combinedLineObj = new GameObject("CombinedLine");
        combinedLineObj.transform.SetParent(graphContainer.transform);
        combinedLine = combinedLineObj.AddComponent<LineRenderer>();
        combinedLine.material = lineMaterial;
        combinedLine.startColor = combinedCurveColor;
        combinedLine.endColor = combinedCurveColor;
        combinedLine.startWidth = lineWidth * 1.5f; // Slightly thicker
        combinedLine.endWidth = lineWidth * 1.5f;
        combinedLine.positionCount = resolution;
        combinedLine.useWorldSpace = false;
        combinedLine.enabled = false; // Initially hidden
    }

    // Clear output layer visualization elements
    private void ClearOutputLayerVisualization()
    {
        // Hide output layer lines
        if (curve1Line != null) curve1Line.enabled = false;
        if (curve2Line != null) curve2Line.enabled = false;
        if (combinedLine != null) combinedLine.enabled = false;
        
        // Clear output layer points
        foreach (var point in curve1Points)
        {
            if (point != null) Destroy(point);
        }
        foreach (var point in curve2Points)
        {
            if (point != null) Destroy(point);
        }
        
        curve1Points.Clear();
        curve2Points.Clear();
        combinedTargetPositions.Clear();
    }

    // Calculate the activation output for a specific neuron in the previous layer
    private float CalculatePreviousLayerActivation(int neuronIndex, float input)
    {
        if (neuralNetwork == null || currentLayerIndex <= 0) return 0f;
        
        // Validate neuron index bounds for the previous layer
        int previousLayerIndex = currentLayerIndex - 1;
        if (previousLayerIndex < 0 || previousLayerIndex >= neuralNetwork.layerSizes.Length) return 0f;
        if (neuronIndex < 0 || neuronIndex >= neuralNetwork.layerSizes[previousLayerIndex]) return 0f;
        
        try
        {
            // Get weights and bias for the specified neuron in the previous layer
            // We need weights from the layer BEFORE the previous layer (if it exists)
            if (previousLayerIndex == 0)
            {
                // If previous layer is input layer, just return the input value
                return input;
            }
            
            // Get weights from the layer connecting to the previous layer
            double[] weights = neuralNetwork.GetNeuronWeights(previousLayerIndex - 1, neuronIndex);
            double bias = neuralNetwork.GetNeuronBias(previousLayerIndex - 1, neuronIndex);
            
            // Validate weights array
            if (weights == null || weights.Length == 0) return 0f;
            
            // Calculate weighted input
            double weightedInput = input * weights[0] + bias; // Simplified for single input
            
            // Apply activation function for the previous layer
            return (float)neuralNetwork.CalculateNeuronActivation(previousLayerIndex, neuronIndex, weightedInput);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error calculating previous layer activation for neuron {neuronIndex}: {e.Message}");
            return 0f;
        }
    }

    // Calculate the combined output for the output layer
    private float CalculateOutputLayerActivation(float input)
    {
        if (neuralNetwork == null || !isOutputLayer) return 0f;
        
        // Validate that we have at least 2 neurons in the previous layer
        int previousLayerIndex = currentLayerIndex - 1;
        if (previousLayerIndex < 0 || previousLayerIndex >= neuralNetwork.layerSizes.Length) return 0f;
        if (neuralNetwork.layerSizes[previousLayerIndex] < 2) return 0f;
        
        try
        {
            // Get activations from both neurons in the previous layer
            float activation1 = CalculatePreviousLayerActivation(0, input);
            float activation2 = CalculatePreviousLayerActivation(1, input);
            
            // Get weights and bias for the output neuron
            double[] weights = neuralNetwork.GetNeuronWeights(currentLayerIndex - 1, currentNeuronIndex);
            double bias = neuralNetwork.GetNeuronBias(currentLayerIndex - 1, currentNeuronIndex);
            
            // Validate weights array has at least 2 elements
            if (weights == null || weights.Length < 2) return 0f;
            
            // Calculate combined output: (activation1 * weight1 + activation2 * weight2) + bias
            double combinedOutput = (activation1 * weights[0] + activation2 * weights[1]) + bias;
            
            // Apply activation function for the output layer
            return (float)neuralNetwork.CalculateNeuronActivation(currentLayerIndex, currentNeuronIndex, combinedOutput);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error calculating output layer activation: {e.Message}");
            return 0f;
        }
    }

    // Plot the three curves for output layer visualization
    private void PlotOutputLayerCurves()
    {
        // DEBUG: Log current output layer parameters
        if (neuralNetwork != null && currentLayerIndex > 0)
        {
            try
            {
                double[] weights = neuralNetwork.GetNeuronWeights(currentLayerIndex - 1, currentNeuronIndex);
                double bias = neuralNetwork.GetNeuronBias(currentLayerIndex - 1, currentNeuronIndex);
                
                Debug.Log($"=== OUTPUT LAYER CURVE PARAMETERS ===");
                Debug.Log($"W3 (weight[0]): {weights[0]:F3}");
                Debug.Log($"W4 (weight[1]): {weights[1]:F3}"); 
                Debug.Log($"B5 (bias): {bias:F3}");
                Debug.Log($"Expected: Red = Cyan × {weights[0]:F3} + Purple × {weights[1]:F3} + {bias:F3}");
                
                if (Mathf.Abs((float)bias) > 0.001f)
                {
                    Debug.Log($"⚠️ BIAS IS NOT ZERO! This explains the vertical shift.");
                    Debug.Log($"⚠️ The network has been trained or parameters have been modified.");
                }
                else
                {
                    Debug.Log($"✅ Bias is approximately zero - no vertical shift expected.");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error getting output layer parameters: {e.Message}");
            }
        }
        
        for (int i = 0; i < resolution; i++)
        {
            float x = Mathf.Lerp(-xRange, xRange, (float)i / (resolution - 1));
            
            // Calculate activations for both neurons in previous layer
            float activation1 = CalculatePreviousLayerActivation(0, x);
            float activation2 = CalculatePreviousLayerActivation(1, x);
            float combinedActivation = CalculateOutputLayerActivation(x);
            
            // Set positions for all three curves
            curve1Line.SetPosition(i, new Vector3(x, activation1, 0));
            curve2Line.SetPosition(i, new Vector3(x, activation2, 0));
            combinedLine.SetPosition(i, new Vector3(x, combinedActivation, 0));
        }
    }

    // Create points for both curves in output layer visualization
    private void CreateOutputLayerPoints()
    {
        targetActivationPositions.Clear();
        combinedTargetPositions.Clear();
        
        for (int i = 0; i < collectedDataPoints.Count; i++)
        {
            float x = collectedDataPoints[i].x;
            
            // Calculate activations for both previous layer neurons
            float activation1 = CalculatePreviousLayerActivation(0, x);
            float activation2 = CalculatePreviousLayerActivation(1, x);
            float combinedActivation = CalculateOutputLayerActivation(x);
            
            // Create point for curve 1
            Vector3 curve1StartPos = GetWorldPositionOnPlot(x, 0);
            Vector3 curve1TargetPos = GetWorldPositionOnPlot(x, activation1);
            GameObject curve1Point = Instantiate(dataPointPrefab, curve1StartPos, Quaternion.identity, graphContainer.transform);
            curve1Point.transform.localScale = Vector3.one * pointSize;
            
            Renderer renderer1 = curve1Point.GetComponent<Renderer>();
            if (renderer1 != null)
            {
                if (pointMaterial != null) renderer1.material = pointMaterial;
                renderer1.material.color = curve1Color;
            }
            
            curve1Points.Add(curve1Point);
            
            // Create point for curve 2
            Vector3 curve2StartPos = GetWorldPositionOnPlot(x, 0);
            Vector3 curve2TargetPos = GetWorldPositionOnPlot(x, activation2);
            GameObject curve2Point = Instantiate(dataPointPrefab, curve2StartPos, Quaternion.identity, graphContainer.transform);
            curve2Point.transform.localScale = Vector3.one * pointSize;
            
            Renderer renderer2 = curve2Point.GetComponent<Renderer>();
            if (renderer2 != null)
            {
                if (pointMaterial != null) renderer2.material = pointMaterial;
                renderer2.material.color = curve2Color;
            }
            
            curve2Points.Add(curve2Point);
            
            // Calculate the final combined position where both points will meet
            Vector3 combinedTargetPos = GetWorldPositionOnPlot(x, combinedActivation);
            combinedTargetPositions.Add(combinedTargetPos);
            
            // Debug.Log($"Output Layer Point {i}: x={x}, activation1={activation1}, activation2={activation2}, combined={combinedActivation}"); // DISABLED - too verbose
        }
    }

    // Animate regular points for hidden layers
    private void AnimateRegularPoints(float progress)
    {
        // Animate each point to its activation position
        for (int i = 0; i < weightedInputPoints.Count; i++)
        {
            if (weightedInputPoints[i] != null && i < targetActivationPositions.Count)
            {
                // Get the start position (on x-axis)
                float x = collectedDataPoints[i].x;
                Vector3 startPos = GetWorldPositionOnPlot(x, 0);
                
                // Get the target position from our stored positions
                Vector3 endPos = targetActivationPositions[i];
                
                // Interpolate position
                weightedInputPoints[i].transform.position = Vector3.Lerp(startPos, endPos, progress);
            }
        }
    }

    // Animate output layer points with two-stage movement
    private void AnimateOutputLayerPoints(float progress)
    {
        // First stage: Move points from x-axis to their respective curve positions (0-0.5)
        // Second stage: Move both points to the combined position (0.5-1.0)
        
        float firstStageEnd = 0.6f; // First 60% of animation
        
        for (int i = 0; i < collectedDataPoints.Count; i++)
        {
            float x = collectedDataPoints[i].x;
            Vector3 startPos = GetWorldPositionOnPlot(x, 0);
            
            if (progress <= firstStageEnd)
            {
                // First stage: Move to individual curve positions
                float stageProgress = progress / firstStageEnd;
                
                // Animate curve 1 points
                if (i < curve1Points.Count && curve1Points[i] != null)
                {
                    float activation1 = CalculatePreviousLayerActivation(0, x);
                    Vector3 curve1Target = GetWorldPositionOnPlot(x, activation1);
                    curve1Points[i].transform.position = Vector3.Lerp(startPos, curve1Target, stageProgress);
                }
                
                // Animate curve 2 points
                if (i < curve2Points.Count && curve2Points[i] != null)
                {
                    float activation2 = CalculatePreviousLayerActivation(1, x);
                    Vector3 curve2Target = GetWorldPositionOnPlot(x, activation2);
                    curve2Points[i].transform.position = Vector3.Lerp(startPos, curve2Target, stageProgress);
                }
            }
            else
            {
                // Second stage: Move both points to combined position
                float stageProgress = (progress - firstStageEnd) / (1f - firstStageEnd);
                
                // Get the current positions on curves
                float activation1 = CalculatePreviousLayerActivation(0, x);
                float activation2 = CalculatePreviousLayerActivation(1, x);
                Vector3 curve1Pos = GetWorldPositionOnPlot(x, activation1);
                Vector3 curve2Pos = GetWorldPositionOnPlot(x, activation2);
                
                // Get the combined target position
                Vector3 combinedTarget = combinedTargetPositions[i];
                
                // Move both points toward the combined position
                if (i < curve1Points.Count && curve1Points[i] != null)
                {
                    curve1Points[i].transform.position = Vector3.Lerp(curve1Pos, combinedTarget, stageProgress);
                }
                
                if (i < curve2Points.Count && curve2Points[i] != null)
                {
                    curve2Points[i].transform.position = Vector3.Lerp(curve2Pos, combinedTarget, stageProgress);
                }
            }
        }
    }

    /// <summary>
    /// Force refresh the visualization - useful after parameter changes from other scenes
    /// </summary>
    public void ForceRefreshVisualization()
    {
        Debug.Log("=== AFVisualizer: Force refreshing visualization ===");
        
        // Log current parameters before refresh
        LogCurrentParameters();
        
        // ENHANCED: Compare with pre-epoch parameters to show training effect
        LogParameterComparison();
        
        // Update all visualization components
        PlotDefaultFunction();           // Update default function line
        UpdateVisualization();          // Update current function line  
        UpdateNetworkDataLine();        // Update network data line
        
        // If animation is completed, refresh the final positions
        if (animationCompleted)
        {
            // Recalculate target positions with new parameters
            for (int i = 0; i < collectedDataPoints.Count; i++)
            {
                if (i < targetActivationPositions.Count)
                {
                    float x = collectedDataPoints[i].x;
                    float newActivation = CalculateActivation(x);
                    Vector3 newTargetPos = GetWorldPositionOnPlot(x, newActivation);
                    targetActivationPositions[i] = newTargetPos;
                    
                    // Update point position if it exists
                    if (i < weightedInputPoints.Count && weightedInputPoints[i] != null)
                    {
                        weightedInputPoints[i].transform.position = newTargetPos;
                    }
                }
            }
            
            // Update result plot with new activation values using weighted input
            if (resultPlotVisualizer != null)
            {
                List<Vector2> newResultPoints = new List<Vector2>();
                for (int i = 0; i < collectedDataPoints.Count; i++)
                {
                    float rawInput = collectedDataPoints[i].x;
                    float weightedInput = CalculateWeightedInput(rawInput); // NEW: Use weighted input
                    float newActivation = isOutputLayer ? 
                        CalculateOutputLayerActivation(rawInput) : 
                        CalculateActivation(rawInput);
                    newResultPoints.Add(new Vector2(weightedInput, newActivation)); // X = weighted input
                }
                resultPlotVisualizer.UpdatePlot(newResultPoints);
            }
        }
        
        Debug.Log("Force refresh completed");
    }
    
    /// <summary>
    /// Compare current parameters with pre-epoch parameters to show training effects
    /// </summary>
    private void LogParameterComparison()
    {
        if (neuralNetwork == null || currentLayerIndex <= 0) return;
        
        try
        {
            // Get current (trained) parameters
            double[] currentWeights = neuralNetwork.GetNeuronWeights(currentLayerIndex - 1, currentNeuronIndex);
            double currentBias = neuralNetwork.GetNeuronBias(currentLayerIndex - 1, currentNeuronIndex);
            
            // Get pre-epoch parameters (from before training) using helper methods
            double[] preEpochWeights = neuralNetwork.GetPreEpochNeuronWeights(currentLayerIndex - 1, currentNeuronIndex);
            double preEpochBias = neuralNetwork.GetPreEpochNeuronBias(currentLayerIndex - 1, currentNeuronIndex);
            
            Debug.Log($"=== PARAMETER COMPARISON: Training Effects ===");
            
            if (preEpochWeights != null && preEpochWeights.Length > 0)
            {
                for (int i = 0; i < Mathf.Min(currentWeights.Length, preEpochWeights.Length); i++)
                {
                    double weightChange = currentWeights[i] - preEpochWeights[i];
                    Debug.Log($"  Weight[{i}]: {preEpochWeights[i]:F3} → {currentWeights[i]:F3} (Δ{weightChange:+0.000;-0.000})");
                }
                
                double biasChange = currentBias - preEpochBias;
                Debug.Log($"  Bias: {preEpochBias:F3} → {currentBias:F3} (Δ{biasChange:+0.000;-0.000})");
                
                if (Mathf.Abs((float)biasChange) > 0.001f)
                {
                    Debug.Log($"  ✅ BIAS CHANGED! Activation curve should be vertically shifted by {biasChange:F3}");
                    Debug.Log($"  ✅ For Tanh activation: curve moves UP if bias increased, DOWN if decreased");
                }
                else
                {
                    Debug.Log($"  ⚠️ No significant bias change - curve may appear similar");
                }
                
                // Calculate total parameter change magnitude
                double totalWeightChange = 0;
                for (int i = 0; i < Mathf.Min(currentWeights.Length, preEpochWeights.Length); i++)
                {
                    totalWeightChange += Mathf.Abs((float)(currentWeights[i] - preEpochWeights[i]));
                }
                totalWeightChange += Mathf.Abs((float)biasChange);
                
                if (totalWeightChange > 0.01)
                {
                    Debug.Log($"  ✅ SIGNIFICANT PARAMETER CHANGES! Total change magnitude: {totalWeightChange:F3}");
                    Debug.Log($"  ✅ Activation function visualization SHOULD show noticeable differences");
                }
                else
                {
                    Debug.Log($"  ⚠️ Small parameter changes (magnitude: {totalWeightChange:F3}) - differences may be subtle");
                }
            }
            else
            {
                Debug.Log("  ⚠️ Pre-epoch parameters not available for comparison");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error comparing parameters: {e.Message}");
        }
    }
}