using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using System.Linq; // Add this for Max() function

public class NetworkVis : MonoBehaviour
{
    private NeuralNetwork network; // Changed from public to private
    public GameObject nodePrefab;
    public LineRenderer connectionPrefab;
    public float nodeSpacing = 4f;
    public float visualizationScale = 1.0f;

    [Header("Position Offset")]
    public Vector3 networkOffset = new Vector3(-0.5f, 0.5f, 0f); // Offset to center the network
    public float minUpdateInterval = 0.1f;
    private float lastUpdateTime;

    [SerializeField] private List<GameObject> nodes = new List<GameObject>();
    [SerializeField] private List<LineRenderer> connections = new List<LineRenderer>();

    // Add event for neuron selection
    public UnityEvent<int, int> OnNeuronSelected = new UnityEvent<int, int>(); // Layer index, Neuron index

    // Expose connections list for external access
    public List<LineRenderer> Connections => connections;

    // Add reference to Stage1Manager
    public Stage1Manager stageManager;

    [Header("Neuron State")]
    public Color visitedNeuronColor = Color.green;
    private Dictionary<string, bool> visitedNeurons = new Dictionary<string, bool>();

    [Header("Plot Data Storage")]
    public ResultPlotVisualizer resultPlotVisualizer; // Reference to the result plot visualizer

    [Header("UI Display")]
    public TMPro.TextMeshProUGUI selectedNeuronText; // Text component to display selected neuron info

    void Start()
    {
        // Get singleton reference
        network = NeuralNetwork.Instance;
        if (network == null)
        {
            Debug.LogError("NetworkVis: NeuralNetwork singleton not found!");
            return;
        }
        
        Debug.Log($"NetworkVis: Using NeuralNetwork singleton: {network.name}");
        
        // Subscribe to events (in case OnEnable was called before network was initialized)
        network.OnWeightsUpdated -= UpdateConnectionVisuals; // Remove first to prevent duplicates
        network.OnWeightsUpdated += UpdateConnectionVisuals;
        
        network.OnNetworkUpdated += RebuildVisualization;
        RebuildVisualization();
    }

    public void RebuildVisualization()
    {
        // Throttle updates
        if (Time.time - lastUpdateTime < minUpdateInterval) return;

        // Original visualization code
        ClearVisualization();
        CreateNodes();
        CreateConnections();

        lastUpdateTime = Time.time;
    }

    void ClearVisualization()
    {
        foreach (GameObject node in nodes) Destroy(node);
        foreach (LineRenderer conn in connections) Destroy(conn.gameObject);
        nodes.Clear();
        connections.Clear();
    }

    void CreateNodes()
    {
        // Calculate total network size
        float totalWidth = (network.layerSizes.Length - 1) * nodeSpacing * visualizationScale;
        float maxHeight = network.layerSizes.Max() * nodeSpacing * visualizationScale;

        // Cache scale compensation so layout spacing is consistent even if the canvas uses a very small world-space scale
        Vector3 scaleComp = new Vector3(
            transform.lossyScale.x != 0 ? 1f / transform.lossyScale.x : 1f,
            transform.lossyScale.y != 0 ? 1f / transform.lossyScale.y : 1f,
            transform.lossyScale.z != 0 ? 1f / transform.lossyScale.z : 1f
        );

        for (int l = 0; l < network.layerSizes.Length; l++)
        {
            for (int n = 0; n < network.layerSizes[l]; n++)
            {
                // Calculate position with centering offset
                Vector3 pos = new Vector3(
                    l * nodeSpacing * visualizationScale - totalWidth/2, // Center horizontally
                    n * nodeSpacing * visualizationScale - (network.layerSizes[l] - 1) * nodeSpacing * visualizationScale / 2, // Center vertically
                    l * 0.0005f // small z offset to mitigate z-fighting on canvas
                ) + networkOffset; // Apply the offset

                // Instantiate as a child and assign localPosition so layout is in the canvas' local space
                GameObject node = Instantiate(nodePrefab, Vector3.zero, Quaternion.identity, transform);
                // Compensate for parent scale so spacing is visually correct in world space
                node.transform.localPosition = Vector3.Scale(pos, scaleComp);
                node.transform.localScale *= visualizationScale * 2f;
                
                // Add collider if not present
                if (node.GetComponent<Collider>() == null)
                {
                    SphereCollider collider = node.AddComponent<SphereCollider>();
                    collider.radius = 1.0f;
                }
                
                // Set layer for raycast detection
                node.layer = LayerMask.NameToLayer("neuron");
                
                // Add neuron interaction component
                NeuronInteraction neuronInteraction = node.AddComponent<NeuronInteraction>();
                neuronInteraction.Initialize(l, n, this);
                
                // Check if neuron was previously visited
                string key = $"{l}_{n}";
                if (visitedNeurons.ContainsKey(key) && visitedNeurons[key])
                {
                    Renderer renderer = node.GetComponent<Renderer>();
                    if (renderer != null)
                    {
                        renderer.material.color = visitedNeuronColor;
                    }
                }
                
                nodes.Add(node);
                
                Debug.Log($"Created neuron at layer {l}, index {n}, position {pos}");
            }
        }
    }

    void CreateConnections()
    {
        int nodeIndex = 0;
        for (int l = 0; l < network.layerSizes.Length - 1; l++)
        {
            int currentLayerSize = network.layerSizes[l];
            int nextLayerSize = network.layerSizes[l + 1];

            for (int i = 0; i < currentLayerSize; i++)
            {
                for (int j = 0; j < nextLayerSize; j++)
                {
                    LineRenderer conn = Instantiate(connectionPrefab, transform);
                    conn.transform.localPosition = Vector3.zero;
                    // Ensure the line uses the parent's local space so points match node localPositions
                    conn.useWorldSpace = false;
                    conn.SetPositions(new Vector3[] {
                        nodes[nodeIndex + i].transform.localPosition,
                        nodes[nodeIndex + currentLayerSize + j].transform.localPosition
                    });

                    float weight = (float)network.weights[l][i * nextLayerSize + j];

                    conn.startColor = weight > 0 ? Color.green : Color.red;
                    conn.endColor = weight > 0 ? Color.green : Color.red;
                    conn.startWidth = Mathf.Abs(weight) * 0.2f * visualizationScale;

                    connections.Add(conn);
                }
            }
            nodeIndex += currentLayerSize;
        }
    }

    void OnEnable()
    {
        Debug.Log("NetworkVis OnEnable called");
        // Add null check since OnEnable is called before Start()
        if (network != null)
        {
        network.OnWeightsUpdated += UpdateConnectionVisuals;
            Debug.Log("Subscribed to network events");
        }
        else
        {
            Debug.Log("Network is null in OnEnable - will subscribe in Start()");
        }
    }

    void OnDisable()
    {
        Debug.Log("NetworkVis OnDisable called");
        // Add null check to prevent errors when disabling
        if (network != null)
    {
        network.OnWeightsUpdated -= UpdateConnectionVisuals;
            Debug.Log("Unsubscribed from network events");
        }
    }

    void UpdateConnectionVisuals()
    {
        Debug.Log("Updating connection visuals...");
        int connectionIndex = 0;
        for (int l = 0; l < network.weights.Length; l++)
        {
            int currentLayerSize = network.layerSizes[l];
            int nextLayerSize = network.layerSizes[l + 1];

            for (int i = 0; i < currentLayerSize; i++)
            {
                for (int j = 0; j < nextLayerSize; j++)
                {
                    if (connectionIndex >= connections.Count) break;

                    float weight = (float)network.weights[l][j * currentLayerSize + i];
                    LineRenderer conn = connections[connectionIndex];

                    conn.startWidth = Mathf.Lerp(
                        conn.startWidth,
                        Mathf.Abs(weight) * 0.2f * visualizationScale,
                        Time.deltaTime * 5f
                    );

                    conn.startColor = weight > 0 ? Color.green : Color.red;
                    conn.endColor = conn.startColor;

                    connectionIndex++;
                }
            }
        }
    }

    void Update()
    {
        UpdateConnectionPositions();
    }

    void UpdateConnectionPositions()
    {
        int nodeIndex = 0;
        int connectionIndex = 0;
        
        for (int l = 0; l < network.layerSizes.Length - 1; l++)
        {
            int currentLayerSize = network.layerSizes[l];
            int nextLayerSize = network.layerSizes[l + 1];

            for (int i = 0; i < currentLayerSize; i++)
            {
                for (int j = 0; j < nextLayerSize; j++)
                {
                    if (connectionIndex >= connections.Count) break;
                    
                    LineRenderer conn = connections[connectionIndex];
                    conn.SetPositions(new Vector3[] {
                        nodes[nodeIndex + i].transform.localPosition,
                        nodes[nodeIndex + currentLayerSize + j].transform.localPosition
                    });
                    
                    connectionIndex++;
                }
            }
            nodeIndex += currentLayerSize;
        }
    }

    // New method to toggle visibility
    public void ToggleVisibility(bool isVisible)
    {
        // Toggle all nodes
        foreach (Transform child in transform)
        {
            child.gameObject.SetActive(isVisible);
        }

        // Toggle all connections
        foreach (LineRenderer connection in connections)
        {
            if (connection != null)
            {
                connection.enabled = isVisible;
            }
        }
    }

    // Add method to mark neuron as visited
    public void MarkNeuronAsVisited(int layerIndex, int neuronIndex)
    {
        string key = $"{layerIndex}_{neuronIndex}";
        visitedNeurons[key] = true;
        
        // Find and update the neuron's color and interaction status
        int nodeIndex = 0;
        for (int l = 0; l < network.layerSizes.Length; l++)
        {
            if (l == layerIndex)
            {
                GameObject neuron = nodes[nodeIndex + neuronIndex];
                Renderer renderer = neuron.GetComponent<Renderer>();
                if (renderer != null)
                {
                    renderer.material.color = visitedNeuronColor;
                }
                
                // Update the NeuronInteraction component's visited status
                NeuronInteraction interaction = neuron.GetComponent<NeuronInteraction>();
                if (interaction != null)
                {
                    interaction.UpdateVisitedStatus();
                }
                break;
            }
            nodeIndex += network.layerSizes[l];
        }
    }

    // Add method to check if neuron is visited
    public bool IsNeuronVisited(int layerIndex, int neuronIndex)
    {
        string key = $"{layerIndex}_{neuronIndex}";
        return visitedNeurons.ContainsKey(key) && visitedNeurons[key];
    }

    // Update the selected neuron text display
    public void UpdateSelectedNeuronText(int layerIndex, int neuronIndex)
    {
        if (selectedNeuronText != null)
        {
            string visitedStatus = IsNeuronVisited(layerIndex, neuronIndex) ? " (Completed)" : " (Playing)";
            selectedNeuronText.text = $"Selected: Layer {layerIndex}, Neuron {neuronIndex}{visitedStatus}";
            Debug.Log($"[NetworkVis] Updated selected neuron text: {selectedNeuronText.text}");
        }
        else
        {
            Debug.LogWarning("[NetworkVis] selectedNeuronText is null!");
        }
    }

    // Clear the selected neuron text display
    public void ClearSelectedNeuronText()
    {
        if (selectedNeuronText != null)
        {
            selectedNeuronText.text = "";
            Debug.Log("[NetworkVis] Cleared selected neuron text");
        }
    }
}

// Add new class for neuron interaction
public class NeuronInteraction : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{
    private int layerIndex;
    private int neuronIndex;
    private NetworkVis networkVis;
    private Renderer nodeRenderer;
    private Color originalColor;
    private bool isVisited = false;

    // Public properties to access layer and neuron indices
    public int LayerIndex => layerIndex;
    public int NeuronIndex => neuronIndex;

    public void Initialize(int layer, int neuron, NetworkVis vis)
    {
        layerIndex = layer;
        neuronIndex = neuron;
        networkVis = vis;
        nodeRenderer = GetComponent<Renderer>();
        if (nodeRenderer != null)
        {
            originalColor = nodeRenderer.material.color;
            isVisited = networkVis.IsNeuronVisited(layerIndex, neuronIndex);
        }
    }
    
    // Method to update the visited status
    public void UpdateVisitedStatus()
    {
        isVisited = networkVis.IsNeuronVisited(layerIndex, neuronIndex);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        // Check if this is a visited neuron
        if (isVisited || networkVis.IsNeuronVisited(layerIndex, neuronIndex))
        {
            // If neuron is visited, show its saved result plot
            if (networkVis.resultPlotVisualizer != null)
            {
                networkVis.resultPlotVisualizer.ShowNeuronPlot(layerIndex, neuronIndex);
                Debug.Log($"Showing saved plot for visited neuron ({layerIndex}, {neuronIndex})");
            }

            // Also restore the AFVisualizer state for this neuron
            if (networkVis.stageManager != null && networkVis.stageManager.afVisualizer != null && networkVis.resultPlotVisualizer != null)
            {
                // Set the current neuron in the AFVisualizer to show the activation function
                networkVis.stageManager.afVisualizer.SetCurrentNeuron(layerIndex, neuronIndex);
                
                // Check if this is an output layer neuron with curve data
                if (networkVis.resultPlotVisualizer.HasOutputLayerCurveData(layerIndex, neuronIndex))
                {
                    // Restore output layer curve data
                    OutputLayerCurveData curveData = networkVis.resultPlotVisualizer.GetOutputLayerCurveData(layerIndex, neuronIndex);
                    if (curveData != null)
                    {
                        networkVis.stageManager.afVisualizer.RestoreOutputLayerFromCurveData(curveData);
                        Debug.Log($"Restored output layer curve data for visited neuron ({layerIndex}, {neuronIndex})");
                    }
                }
                else
                {
                    // Regular neuron - restore with plot data
                    List<Vector2> savedPlotData = networkVis.resultPlotVisualizer.GetNeuronPlotData(layerIndex, neuronIndex);
                    if (savedPlotData != null && savedPlotData.Count > 0)
                    {
                        networkVis.stageManager.afVisualizer.RestoreCompletedState(savedPlotData);
                        Debug.Log($"Restored completed AFVisualizer state for visited neuron ({layerIndex}, {neuronIndex})");
                    }
                    else
                    {
                        Debug.LogWarning($"No saved plot data found for neuron ({layerIndex}, {neuronIndex})");
                    }
                }
            }

            // Update selected neuron text to show we're viewing this neuron
            networkVis.UpdateSelectedNeuronText(layerIndex, neuronIndex);
            
            // Visual feedback for selection
            StartCoroutine(SelectionFeedback());
            return;
        }

        // Don't allow new game selection if stage is active
        if (networkVis.stageManager != null && networkVis.stageManager.isStageActive)
        {
            Debug.Log("Cannot start new neuron while stage is active");
            return;
        }

        // Clear previous visualization if it exists
        if (networkVis.stageManager != null && networkVis.stageManager.afVisualizer != null)
        {
            networkVis.stageManager.afVisualizer.ClearDataPointMarkers();
            
            // Set the current neuron in the visualizer
            networkVis.stageManager.afVisualizer.SetCurrentNeuron(layerIndex, neuronIndex);
        }

        // Clear any existing result plot for new neuron selection
        if (networkVis.resultPlotVisualizer != null)
        {
            networkVis.resultPlotVisualizer.UpdatePlot(new List<Vector2>());
        }

        // Update selected neuron text to show we're playing this neuron
        networkVis.UpdateSelectedNeuronText(layerIndex, neuronIndex);

        // Trigger neuron selection event
        networkVis.OnNeuronSelected?.Invoke(layerIndex, neuronIndex);
        
        // Visual feedback for selection
        StartCoroutine(SelectionFeedback());
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        // Only show hover effect if neuron hasn't been visited
        if (!isVisited && !networkVis.IsNeuronVisited(layerIndex, neuronIndex) && nodeRenderer != null)
        {
            originalColor = nodeRenderer.material.color;
            nodeRenderer.material.color = Color.yellow;
        }
    }

        public void OnPointerExit(PointerEventData eventData)
    {
        // Only restore color if neuron hasn't been visited
        if (!isVisited && !networkVis.IsNeuronVisited(layerIndex, neuronIndex) && nodeRenderer != null)
        {
            nodeRenderer.material.color = originalColor;
        }
    }

    private System.Collections.IEnumerator SelectionFeedback()
    {
        // Store original scale
        Vector3 originalScale = transform.localScale;
        
        // Scale up briefly
        transform.localScale = originalScale * 1.2f;
        
        // Wait for a short duration
        yield return new WaitForSeconds(0.2f);
        
        // Return to original scale
        transform.localScale = originalScale;
    }
}