using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.UI;
using TMPro;

public class ResultPlotVisualizer : MonoBehaviour
{
    [Header("Plot Settings")]
    public RectTransform plotArea;
    public GameObject pointPrefab;
    public GameObject linePrefab;
    public float pointSize = 10f;
    public float animationDuration = 1f;
    public Color pointColor = Color.blue;
    public Color lineColor = Color.green;

    [Header("Axis Labels")]
    public TextMeshProUGUI xAxisLabel;
    public TextMeshProUGUI yAxisLabel;

    private List<GameObject> pointObjects = new List<GameObject>();
    private List<GameObject> lineObjects = new List<GameObject>();
    private List<Vector2> dataPoints = new List<Vector2>();
    private bool isAnimating = false;
    private Dictionary<(int layer, int neuron), List<Vector2>> neuronResultPlots = new();
    private Dictionary<(int layer, int neuron), OutputLayerCurveData> outputLayerCurveData = new();

    private void Start()
    {
        if (xAxisLabel) xAxisLabel.text = "Weighted Input (w*x + b)";
        if (yAxisLabel) yAxisLabel.text = "Activation Output";
    }

    public void UpdatePlot(List<Vector2> newDataPoints)
    {
        // Debug.Log($"Updating result plot with {newDataPoints.Count} points"); // DISABLED - too verbose
        dataPoints = newDataPoints.OrderBy(p => p.x).ToList();
        ClearPlot();
        CreatePlot();
    }

    private void ClearPlot()
    {
        foreach (var point in pointObjects)
        {
            if (point != null)
            {
                Destroy(point);
            }
        }
        foreach (var line in lineObjects)
        {
            if (line != null)
            {
                Destroy(line);
            }
        }
        pointObjects.Clear();
        lineObjects.Clear();
    }

    private void CreatePlot()
    {
        if (dataPoints.Count == 0) return;

        float minX = dataPoints.Min(p => p.x);
        float maxX = dataPoints.Max(p => p.x);
        float minY = 0f;
        float maxY = 1f;

        // Prevent division by zero
        if (Mathf.Approximately(maxX, minX))
        {
            maxX = minX + 1f; // Add some range if all x values are the same
        }

        Debug.Log($"Creating plot with X range: {minX} to {maxX}, Y range: {minY} to {maxY}");

        // Create points and lines
        for (int i = 0; i < dataPoints.Count; i++)
        {
            // Normalize x value
            float normalizedX = (dataPoints[i].x - minX) / (maxX - minX);
            float normalizedY = dataPoints[i].y; // Y is already between 0 and 1

            // Debug.Log($"Creating point {i} at normalized position: ({normalizedX}, {normalizedY})"); // DISABLED - too verbose

            // Create point
            GameObject point = Instantiate(pointPrefab, plotArea);
            RectTransform pointRect = point.GetComponent<RectTransform>();
            
            // Convert normalized position to local space
            Vector3 localPosition = new Vector3(
                (normalizedX - 0.5f) * plotArea.rect.width,
                (normalizedY - 0.5f) * plotArea.rect.height,
                0
            );
            
            pointRect.localPosition = localPosition;
            pointRect.sizeDelta = new Vector2(pointSize, pointSize);
            pointObjects.Add(point);

            // Create line to next point
            if (i < dataPoints.Count - 1)
            {
                // Normalize next point
                float nextNormalizedX = (dataPoints[i + 1].x - minX) / (maxX - minX);
                float nextNormalizedY = dataPoints[i + 1].y;

                GameObject line = Instantiate(linePrefab, plotArea);
                RectTransform lineRect = line.GetComponent<RectTransform>();
                
                // Calculate line position and rotation in local space
                Vector3 startPos = new Vector3(
                    (normalizedX - 0.5f) * plotArea.rect.width,
                    (normalizedY - 0.5f) * plotArea.rect.height,
                    0
                );
                
                Vector3 endPos = new Vector3(
                    (nextNormalizedX - 0.5f) * plotArea.rect.width,
                    (nextNormalizedY - 0.5f) * plotArea.rect.height,
                    0
                );
                
                Vector3 midPoint = (startPos + endPos) * 0.5f;
                float angle = Mathf.Atan2(
                    endPos.y - startPos.y,
                    endPos.x - startPos.x
                ) * Mathf.Rad2Deg;
                
                lineRect.localPosition = midPoint;
                lineRect.localRotation = Quaternion.Euler(0, 0, angle);
                
                // Calculate line length
                float length = Vector3.Distance(startPos, endPos);
                lineRect.sizeDelta = new Vector2(length, 2f);
                
                lineObjects.Add(line);
            }
        }
    }

    public void AnimatePointsToFinalPositions()
    {
        if (isAnimating) return;
        isAnimating = true;

        for (int i = 0; i < pointObjects.Count; i++)
        {
            if (pointObjects[i] == null) continue;

            RectTransform pointRect = pointObjects[i].GetComponent<RectTransform>();
            if (pointRect == null) continue;

            Vector3 targetPosition = pointRect.localPosition;
            
            // Start from bottom
            Vector3 startPosition = new Vector3(targetPosition.x, -plotArea.rect.height * 0.5f, 0);
            pointRect.localPosition = startPosition;

            // Animate to final position
            LeanTween.moveLocal(pointRect.gameObject, targetPosition, animationDuration)
                .setEase(LeanTweenType.easeOutQuad);
        }

        // Animate lines after points
        LeanTween.delayedCall(animationDuration, () => {
            for (int i = 0; i < lineObjects.Count; i++)
            {
                if (lineObjects[i] == null) continue;

                RectTransform lineRect = lineObjects[i].GetComponent<RectTransform>();
                if (lineRect == null) continue;

                Vector3 targetScale = lineRect.localScale;
                lineRect.localScale = new Vector3(0, 1, 1);

                LeanTween.scale(lineRect.gameObject, targetScale, animationDuration)
                    .setEase(LeanTweenType.easeOutQuad);
            }
            isAnimating = false;
        });
    }

    // Save plot data for a specific neuron
    public void SaveNeuronPlotData(int layerIndex, int neuronIndex, List<Vector2> plotData)
    {
        var key = (layerIndex, neuronIndex);
        neuronResultPlots[key] = new List<Vector2>(plotData); // Create a copy
        Debug.Log($"Saved plot data for neuron ({layerIndex}, {neuronIndex}) with {plotData.Count} points");
    }

    // Get plot data for a specific neuron
    public List<Vector2> GetNeuronPlotData(int layerIndex, int neuronIndex)
    {
        var key = (layerIndex, neuronIndex);
        if (neuronResultPlots.ContainsKey(key))
        {
            return neuronResultPlots[key];
        }
        return new List<Vector2>(); // Return empty list if no data exists
    }

    // Check if neuron has plot data
    public bool HasNeuronPlotData(int layerIndex, int neuronIndex)
    {
        var key = (layerIndex, neuronIndex);
        return neuronResultPlots.ContainsKey(key) && neuronResultPlots[key].Count > 0;
    }

    // Show plot data for a specific neuron (used for selection)
    public void ShowNeuronPlot(int layerIndex, int neuronIndex)
    {
        if (HasNeuronPlotData(layerIndex, neuronIndex))
        {
            List<Vector2> plotData = GetNeuronPlotData(layerIndex, neuronIndex);
            UpdatePlot(plotData);
            Debug.Log($"Showing saved plot for neuron ({layerIndex}, {neuronIndex}) with {plotData.Count} points");
        }
        else
        {
            // Show empty plot for neurons without data
            UpdatePlot(new List<Vector2>());
            Debug.Log($"No saved plot data for neuron ({layerIndex}, {neuronIndex}) - showing empty plot");
        }
    }

    // Save output layer curve data for a specific neuron
    public void SaveOutputLayerCurveData(int layerIndex, int neuronIndex, OutputLayerCurveData curveData)
    {
        var key = (layerIndex, neuronIndex);
        outputLayerCurveData[key] = curveData;
        
        // Also save the combined points as regular plot data for consistency
        if (curveData != null && curveData.combinedPoints != null)
        {
            SaveNeuronPlotData(layerIndex, neuronIndex, curveData.combinedPoints);
        }
        
        Debug.Log($"Saved output layer curve data for neuron ({layerIndex}, {neuronIndex})");
    }

    // Get output layer curve data for a specific neuron
    public OutputLayerCurveData GetOutputLayerCurveData(int layerIndex, int neuronIndex)
    {
        var key = (layerIndex, neuronIndex);
        if (outputLayerCurveData.ContainsKey(key))
        {
            return outputLayerCurveData[key];
        }
        return null;
    }

    // Check if neuron has output layer curve data
    public bool HasOutputLayerCurveData(int layerIndex, int neuronIndex)
    {
        var key = (layerIndex, neuronIndex);
        return outputLayerCurveData.ContainsKey(key) && outputLayerCurveData[key] != null;
    }
} 