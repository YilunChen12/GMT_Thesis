using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using System.Linq;

[System.Serializable]
public class NeuronData
{
    public double weightedInput; // z value
    public double activation;    // output after activation function
    public List<double> weights = new List<double>();
    public double bias;
}

[System.Serializable]
public class LayerData
{
    public List<NeuronData> neurons = new List<NeuronData>();
}

[System.Serializable]
public class NetworkStateSnapshot
{
    public int epoch;
    public List<LayerData> layers = new List<LayerData>();
    public List<Vector2> inputOutputPairs = new List<Vector2>();
    public double loss;
}

[System.Serializable]
public class BackpropChanges
{
    public List<List<double>> weightChanges = new List<List<double>>();
    public List<List<double>> biasChanges = new List<List<double>>();
}

public class NeuralNetworkDataManager : MonoBehaviour
{
    [Header("Network Reference")]
    private NeuralNetwork network; // Changed from public to private

    [Header("Data Loading")]
    public TextAsset trainingDataFile; // CSV file containing training data

    [Header("Save Settings")]
    public bool autoSaveEpochs = true;
    public string saveFolder = "NetworkData";
    
    private List<NetworkStateSnapshot> networkHistory = new List<NetworkStateSnapshot>();
    private List<BackpropChanges> backpropHistory = new List<BackpropChanges>();
    private string SavePath => Path.Combine(Application.dataPath, saveFolder);

    private void Start()
    {
        // Get singleton reference
        network = NeuralNetwork.Instance;
        if (network == null)
        {
            Debug.LogError("NeuralNetworkDataManager: NeuralNetwork singleton not found!");
            return;
        }
        
        Debug.Log($"NeuralNetworkDataManager: Using NeuralNetwork singleton: {network.name}");
        
        // Create save directory if it doesn't exist
        if (!Directory.Exists(SavePath))
        {
            Directory.CreateDirectory(SavePath);
        }

        // Load training data if available
        if (trainingDataFile != null && network != null)
        {
            LoadTrainingData();
        }

        // Subscribe to network events
        if (network != null)
        {
            network.OnWeightsUpdated += HandleNetworkWeightsUpdated;
        }
    }

    private void OnDestroy()
    {
        if (network != null)
        {
            network.OnWeightsUpdated -= HandleNetworkWeightsUpdated;
        }
    }

    // Load training data from CSV
    public void LoadTrainingData()
    {
        if (trainingDataFile == null)
        {
            Debug.LogError("No training data file assigned!");
            return;
        }

        List<NeuralNetwork.TrainingData> data = new List<NeuralNetwork.TrainingData>();
        StringReader reader = new StringReader(trainingDataFile.text);
        int validCount = 0;
        int invalidCount = 0;

        while (reader.Peek() != -1)
        {
            string line = reader.ReadLine().Trim();
            if (string.IsNullOrEmpty(line)) continue;

            string[] values = line.Split(',');
            if (values.Length < 2) continue; // Changed to expect 2 values (input and label)

            try
            {
                double[] inputs = {
                    double.Parse(values[0]) // Single input value
                };
                double[] targets = { double.Parse(values[1]) }; // Binary label (0 or 1)

                // Validate binary label
                if (targets[0] != 0 && targets[0] != 1)
                {
                    Debug.LogWarning($"Invalid label value: {targets[0]}. Expected 0 or 1.");
                    continue;
                }

                // Count valid and invalid points
                if (targets[0] >= 0.5f) validCount++;
                else invalidCount++;

                data.Add(new NeuralNetwork.TrainingData(inputs, targets));
            }
            catch (Exception e)
            {
                Debug.LogError($"Error parsing line: {line}\nError: {e.Message}");
                continue;
            }
        }

        if (network != null)
        {
            network.SetTrainingData(data);
            Debug.Log($"Loaded {data.Count} training samples. Valid: {validCount}, Invalid: {invalidCount}");
            
            // Print first few samples for verification
            for (int i = 0; i < Mathf.Min(5, data.Count); i++)
            {
                Debug.Log($"Sample {i}: Input={data[i].inputs[0]}, Target={data[i].targets[0]}");
            }
        }
    }

    private void HandleNetworkWeightsUpdated()
    {
        CaptureNetworkState();
    }

    // Capture the full network state before backpropagation
    public NetworkStateSnapshot CaptureNetworkState()
    {
        if (network == null) return null;

        NetworkStateSnapshot snapshot = new NetworkStateSnapshot
        {
            epoch = network.currentEpoch,
            inputOutputPairs = new List<Vector2>(network.inputOutputPairs)
        };

        // Calculate loss if we have output data
        snapshot.loss = CalculateLoss();

        // Capture current network state
        for (int layerIdx = 0; layerIdx < network.layerSizes.Length; layerIdx++)
        {
            LayerData layerData = new LayerData();

            // Skip input layer since it doesn't have weights/biases leading to it
            int neuronsInLayer = network.layerSizes[layerIdx];
            for (int neuronIdx = 0; neuronIdx < neuronsInLayer; neuronIdx++)
            {
                NeuronData neuronData = new NeuronData
                {
                    activation = layerIdx < network.activations.Length ?
                                 network.activations[layerIdx][neuronIdx] : 0,
                    weightedInput = layerIdx < network.zValues.Length ?
                                   network.zValues[layerIdx][neuronIdx] : 0
                };

                // Add weights and bias if this isn't the input layer
                if (layerIdx > 0)
                {
                    int prevLayerSize = network.layerSizes[layerIdx - 1];
                    neuronData.bias = network.biases[layerIdx - 1][neuronIdx];

                    // Get weights going into this neuron
                    for (int w = 0; w < prevLayerSize; w++)
                    {
                        int weightIndex = neuronIdx * prevLayerSize + w;
                        if (weightIndex < network.weights[layerIdx - 1].Length)
                        {
                            neuronData.weights.Add(network.weights[layerIdx - 1][weightIndex]);
                        }
                    }
                }

                layerData.neurons.Add(neuronData);
            }

            snapshot.layers.Add(layerData);
        }

        networkHistory.Add(snapshot);

        // Auto-save if enabled
        if (autoSaveEpochs)
        {
            SaveNetworkState(snapshot);
        }

        return snapshot;
    }

    // Capture the difference in weights and biases before and after backpropagation
    public BackpropChanges CaptureBackpropChanges(double[][] oldWeights, double[][] oldBiases)
    {
        BackpropChanges changes = new BackpropChanges();

        for (int i = 0; i < network.weights.Length; i++)
        {
            List<double> layerWeightChanges = new List<double>();
            List<double> layerBiasChanges = new List<double>();

            for (int j = 0; j < network.weights[i].Length; j++)
            {
                layerWeightChanges.Add(network.weights[i][j] - oldWeights[i][j]);
            }

            for (int j = 0; j < network.biases[i].Length; j++)
            {
                layerBiasChanges.Add(network.biases[i][j] - oldBiases[i][j]);
            }

            changes.weightChanges.Add(layerWeightChanges);
            changes.biasChanges.Add(layerBiasChanges);
        }

        backpropHistory.Add(changes);
        return changes;
    }

    // Calculate the current loss of the network
    private double CalculateLoss()
    {
        if (network.trainingData.Count == 0) return 0;

        double totalLoss = 0;
        foreach (var data in network.trainingData)
        {
            double[] output = network.Forward(data.inputs);
            for (int i = 0; i < output.Length; i++)
            {
                double error = output[i] - data.targets[i];
                totalLoss += 0.5 * error * error; // Mean squared error
            }
        }

        return totalLoss / network.trainingData.Count;
    }

    // Save network state to disk
    public void SaveNetworkState(NetworkStateSnapshot snapshot)
    {
        string path = Path.Combine(SavePath, $"epoch_{snapshot.epoch}.json");
        string json = JsonUtility.ToJson(snapshot, true);
        File.WriteAllText(path, json);
        Debug.Log($"Saved network state to {path}");
    }

    // Load network state from disk
    public NetworkStateSnapshot LoadNetworkState(int epoch)
    {
        string path = Path.Combine(SavePath, $"epoch_{epoch}.json");
        if (File.Exists(path))
        {
            string json = File.ReadAllText(path);
            return JsonUtility.FromJson<NetworkStateSnapshot>(json);
        }
        return null;
    }

    // Get the latest network state
    public NetworkStateSnapshot GetLatestState()
    {
        if (networkHistory.Count > 0)
            return networkHistory[networkHistory.Count - 1];
        return null;
    }

    // Get network state by epoch
    public NetworkStateSnapshot GetStateByEpoch(int epoch)
    {
        return networkHistory.Find(s => s.epoch == epoch);
    }

    // Clear history (optional, to free memory)
    public void ClearHistory()
    {
        networkHistory.Clear();
        backpropHistory.Clear();
    }
}