using UnityEngine;
using System.Collections.Generic;

public class DataVis : MonoBehaviour
{
    [Header("References")]
    public NeuralNetwork network;
    public GameObject dataPointPrefab;
    public Transform inputOutputGraph;

    [Header("Axis Configuration")]
    public Vector2 xRange = new Vector2(0, 1);
    public Vector2 yRange = new Vector2(0, 1);
    public Vector2 graphSize = new Vector2(10, 10);

    private List<GameObject> activePoints = new List<GameObject>();

    void Update()
    {
        VisualizeDataFlow();
    }

    public void VisualizeDataFlow()
    {
        // Clear old points
        foreach (var point in activePoints)
        {
            Destroy(point);
        }
        activePoints.Clear();

        // Create new points
        foreach (var pair in network.inputOutputPairs)
        {
            Vector3 graphPosition = new Vector3(
                Mathf.InverseLerp(xRange.x, xRange.y, pair.x) * graphSize.x,
                Mathf.InverseLerp(yRange.x, yRange.y, pair.y) * graphSize.y,
                0
            );

            GameObject point = Instantiate(
                dataPointPrefab,
                inputOutputGraph.position + graphPosition,
                Quaternion.identity,
                inputOutputGraph
            );

            activePoints.Add(point);
        }
    }
}