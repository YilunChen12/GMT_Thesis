using UnityEngine;
using System;
using TMPro;

[System.Serializable]
public class DataPoint : MonoBehaviour
{
    [Header("Original Data")]
    public double[] inputs;  // Original input data from dataset
    public bool isValid = true;
    public double targetValue;

    [Header("Movement Settings")]
    public float speed = 5f;
    public float rotationSpeed = 50f;
    public float spawnTime;
        [Header("Auto-Destruction Settings")]
        [Tooltip("Maximum lifetime in seconds for an un-hit datapoint before auto-destroy")] 
        public float maxLifetime = 15f;
        [Tooltip("If a datapoint stays within this radius of the player's head for too long, auto-destroy it")]
        public float nearPlayerRadius = 0.6f;
        [Tooltip("Time in seconds a datapoint is allowed to stay near the player's head before auto-destroy")] 
        public float maxNearPlayerTime = 1.5f;
        private float nearPlayerTimer = 0f;

    [Header("Neural Network Calculations")]
    public float weightedInput = 0f;  // (input * weight) + bias
    public float activation = 0f;     // Result after activation function
    public float currentWeight = 1f;  // Current weight applied
    public float currentBias = 0f;    // Current bias applied

    [Header("Visualization References")]
    [SerializeField] private AFVisualizer activationVis;

    [Header("Blade Interaction")]
    public bool hasBeenHitByWeight = false;
    public bool hasBeenHitByBias = false;
    public float originalSize = 1f;
    private bool isAnimatingToVisualizer = false;
    private Vector3 targetVisualizerPosition;
    private float animationSpeed = 10f;
    private float animationProgress = 0f;
    private bool isAnimationCompleted = false;

    [Header("Neural Network References")]
    [SerializeField] private NeuralNetwork neuralNetwork;

    [Header("Angle Indicators")]
    public GameObject weightIndicatorPrefab;
    public GameObject biasIndicatorPrefab;
    private GameObject weightIndicator;
    private GameObject biasIndicator;
    public float weightAngle;
    public float biasAngle;
    public float angleAccuracy = 0f; // 0-1 value representing how accurate the hits were
    [Range(1f, 5f)]
    public float indicatorDistanceMultiplier = 2.25f; // Configurable multiplier for indicator distance
    [Header("UI")]
    public TextMeshPro textLabel;  // assign in prefab or create dynamically


    
    public DataPoint(double[] inputs, bool isValid, Vector3 initialPosition, float speed, double targetValue = 0)
    {
        this.inputs = inputs;
        this.isValid = isValid;
        this.speed = speed;
        this.targetValue = targetValue;
        this.spawnTime = Time.time;
        
        // Set initial position
        if (this.gameObject != null)
        {
            this.transform.position = initialPosition;
        }
    }

    private void Start()
    {
        // Record spawn time for lifetime tracking
        spawnTime = Time.time;

        // Set color based on validity
        Renderer renderer = GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.material.color = isValid ? Color.green : Color.red;
        }

        // Create angle indicators
        CreateAngleIndicators();
    }

    private void CreateAngleIndicators()
    {
        if (weightIndicatorPrefab != null)
        {
            weightIndicator = Instantiate(weightIndicatorPrefab, transform);
            
            // Calculate position on the sphere's surface using the weight angle
            float diameter = transform.localScale.x * indicatorDistanceMultiplier;
            float x = Mathf.Sin(weightAngle * Mathf.Deg2Rad) * diameter;
            float y = Mathf.Cos(weightAngle * Mathf.Deg2Rad) * diameter;
            weightIndicator.transform.localPosition = new Vector3(x, y, 0);
            
            // Make indicator face outward from the sphere
            weightIndicator.transform.LookAt(transform.position + weightIndicator.transform.localPosition * 2);
            
            // Set color to indicate weight
            Renderer weightRenderer = weightIndicator.GetComponent<Renderer>();
            if (weightRenderer != null)
            {
                weightRenderer.material.color = Color.blue;
            }
        }

        if (biasIndicatorPrefab != null)
        {
            biasIndicator = Instantiate(biasIndicatorPrefab, transform);
            
            // Calculate position on the sphere's surface using the bias angle
            float diameter = transform.localScale.x * indicatorDistanceMultiplier;
            float x = Mathf.Sin(biasAngle * Mathf.Deg2Rad) * diameter;
            float y = Mathf.Cos(biasAngle * Mathf.Deg2Rad) * diameter;
            biasIndicator.transform.localPosition = new Vector3(x, y, 0);
            
            // Make indicator face outward from the sphere
            biasIndicator.transform.LookAt(transform.position + biasIndicator.transform.localPosition * 2);
            
            // Set color to indicate bias
            Renderer biasRenderer = biasIndicator.GetComponent<Renderer>();
            if (biasRenderer != null)
            {
                biasRenderer.material.color = Color.red;
            }
        }

        // Rotate the entire indicator plane to face the player
        UpdateIndicatorPlaneRotation();
    }

    private void UpdateIndicatorPlaneRotation()
    {
        if (Camera.main != null)
        {
            // Get direction to camera
            Vector3 directionToCamera = Camera.main.transform.position - transform.position;
            
            // Create rotation to face camera
            Quaternion targetRotation = Quaternion.LookRotation(directionToCamera);
            
            // Apply rotation to both indicators
            if (weightIndicator != null)
            {
                weightIndicator.transform.rotation = targetRotation;
            }
            if (biasIndicator != null)
            {
                biasIndicator.transform.rotation = targetRotation;
            }
        }
    }

    public void SetTargetAngles(float weightAngle, float biasAngle)
    {
        this.weightAngle = weightAngle;
        this.biasAngle = biasAngle;
        
        // Update indicator positions if they exist
        if (weightIndicator != null)
        {
            float diameter = transform.localScale.x * indicatorDistanceMultiplier;
            float x = Mathf.Sin(weightAngle * Mathf.Deg2Rad) * diameter;
            float y = Mathf.Cos(weightAngle * Mathf.Deg2Rad) * diameter;
            weightIndicator.transform.localPosition = new Vector3(x, y, 0);
            weightIndicator.transform.LookAt(transform.position + weightIndicator.transform.localPosition * 2);
        }
        
        if (biasIndicator != null)
        {
            float diameter = transform.localScale.x * indicatorDistanceMultiplier;
            float x = Mathf.Sin(biasAngle * Mathf.Deg2Rad) * diameter;
            float y = Mathf.Cos(biasAngle * Mathf.Deg2Rad) * diameter;
            biasIndicator.transform.localPosition = new Vector3(x, y, 0);
            biasIndicator.transform.LookAt(transform.position + biasIndicator.transform.localPosition * 2);
        }

        // Update the plane rotation to face player
        UpdateIndicatorPlaneRotation();
    }

    public float CheckBladeAngle(Vector3 bladeDirection, bool isWeightBlade)
    {
        float targetAngle = isWeightBlade ? weightAngle : biasAngle;
        
        // Convert blade direction to angle in the XY plane
        float bladeAngle = Mathf.Atan2(bladeDirection.x, bladeDirection.y) * Mathf.Rad2Deg;
        
        // Calculate angle difference
        float angleDiff = Mathf.Abs(Mathf.DeltaAngle(bladeAngle, targetAngle));
        
        // Return accuracy (1 = perfect hit, 0 = completely off)
        return Mathf.Clamp01(1f - (angleDiff / 180f));
    }

    private void Update()
    {
        // If animation is completed, don't move the point
        if (isAnimationCompleted)
        {
            return;
        }
        
        if (isAnimatingToVisualizer)
        {
            // Animate towards the visualizer with a smooth curve
            animationProgress += Time.deltaTime * animationSpeed;
            float smoothProgress = Mathf.SmoothStep(0, 1, animationProgress);
            
            // Use a curved path for more natural movement
            Vector3 currentPos = transform.position;
            Vector3 targetPos = targetVisualizerPosition;
            Vector3 controlPoint = Vector3.Lerp(currentPos, targetPos, 0.5f) + Vector3.up * 2f; // Add some height to the curve
            
            // Quadratic Bezier curve interpolation
            float t = smoothProgress;
            Vector3 position = Vector3.Lerp(
                Vector3.Lerp(currentPos, controlPoint, t),
                Vector3.Lerp(controlPoint, targetPos, t),
                t
            );
            
            transform.position = position;
            
            // When animation is complete
            if (animationProgress >= 1f)
            {
                isAnimatingToVisualizer = false;
                isAnimationCompleted = true; // Mark animation as completed
                HandleVisualizerArrival();
            }
        }
        else
        {
            // Auto-destroy safeguards for un-hit datapoints
            TryAutoDestroyUnhit();

            // Normal movement and rotation
            transform.Translate(Vector3.forward * speed * Time.deltaTime);
            transform.Rotate(Vector3.up * rotationSpeed * Time.deltaTime);

            // Update indicator plane rotation to always face player
            UpdateIndicatorPlaneRotation();
        }
    }

    /// <summary>
    /// Automatically destroy datapoints that linger too long or stay too close to the player's head.
    /// This also removes the child weight/bias indicators since they are parented to the datapoint.
    /// </summary>
    private void TryAutoDestroyUnhit()
    {
        // Do not auto-destroy while animating towards the visualizer (that is a successful hit flow)
        if (isAnimatingToVisualizer)
        {
            return;
        }

        // Lifetime timeout
        if (maxLifetime > 0f && Time.time - spawnTime >= maxLifetime)
        {
            Destroy(gameObject);
            return;
        }

        // Proximity timeout relative to player's head (camera)
        if (Camera.main != null && nearPlayerRadius > 0f && maxNearPlayerTime > 0f)
        {
            float distanceToHead = Vector3.Distance(Camera.main.transform.position, transform.position);
            if (distanceToHead <= nearPlayerRadius)
            {
                nearPlayerTimer += Time.deltaTime;
                if (nearPlayerTimer >= maxNearPlayerTime)
                {
                    Destroy(gameObject);
                }
            }
            else
            {
                // Reset timer when moving away from head
                nearPlayerTimer = 0f;
            }
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        // Check if hit by a projectile
        if (other.CompareTag("Projectile"))
        {
            HandleHit();
            Destroy(other.gameObject);  // Destroy the projectile
        }
    }

    public void HandleHit()
    {
        if (isValid)
        {
            // Valid datapoint hit
            Debug.Log("Valid data point hit!");

            // Add the datapoint to the visualization
            if (activationVis != null)
            {
                // Send this datapoint's values to the visualizer
                activationVis.AddCollectedDataPoint(weightedInput, activation);
            }
        }
        else
        {
            // Noise datapoint hit
            Debug.Log("Noise data point hit!");
        }

        // Play hit effect
        // TODO: Add particle effect or animation

        // Destroy datapoint
        Destroy(gameObject);
    }

    public void UpdatePosition()
    {
        transform.Translate(Vector3.forward * speed * Time.deltaTime);
    }

    public bool IsOutOfBounds(float zThreshold)
    {
        return transform.position.z < zThreshold;
    }

    public void SetVisualizer(AFVisualizer visualizer)
    {
        this.activationVis = visualizer;
    }

    // Set neural network data for this datapoint
    public void SetNeuralNetworkData(float weightedInput, float activation)
    {
        this.weightedInput = weightedInput;
        this.activation = activation;
        if (textLabel != null)
        {
            textLabel.text = $"x={inputs[0]:F2}";
        }
    }

    public void HandleWeightBladeHit(float weightValue, Vector3 bladeDirection)
    {
        if (!hasBeenHitByWeight)
        {
            hasBeenHitByWeight = true;
            currentWeight = weightValue;
            
            // Calculate accuracy of the hit
            float weightAccuracy = CheckBladeAngle(bladeDirection, true);
            angleAccuracy = weightAccuracy;
            
            // Calculate weighted input using neural network if available
            if (neuralNetwork != null)
            {
                // Create a temporary input array with the current weight applied
                double[] weightedInputs = new double[inputs.Length];
                for (int i = 0; i < inputs.Length; i++)
                {
                    weightedInputs[i] = inputs[i] * weightValue;
                }
                
                // Get the weighted input from neural network
                double[] networkOutput = neuralNetwork.Forward(weightedInputs);
                weightedInput = (float)networkOutput[0];
            }
            else
            {
                // Fallback to local calculation
                if (inputs != null && inputs.Length > 0)
                {
                    weightedInput = (float)(inputs[0] * weightValue) + currentBias;
                }
            }
            
            // Scale the datapoint based on the weight
            float scaleFactor = Mathf.Abs(weightValue);
            transform.localScale = Vector3.one * (originalSize * scaleFactor);
            
            // Start animation to visualizer
            if (activationVis != null)
            {
                Vector3 plotPosition = activationVis.GetWorldPositionOnPlot(weightedInput, 0);
                targetVisualizerPosition = plotPosition;
                isAnimatingToVisualizer = true;
                animationProgress = 0f;
                
                Collider collider = GetComponent<Collider>();
                if (collider != null)
                {
                    collider.enabled = false;
                }
                
                transform.LookAt(plotPosition);
                
                Debug.Log($"Data point hit - Original Input: {inputs[0]}, Weight: {weightValue}, Weighted Input: {weightedInput}, Accuracy: {weightAccuracy}");
            }
        }
    }

    public void HandleBiasBladeHit(float biasValue, Vector3 bladeDirection)
    {
        if (!hasBeenHitByBias)
        {
            hasBeenHitByBias = true;
            currentBias = biasValue;
            
            // Calculate accuracy of the hit
            float biasAccuracy = CheckBladeAngle(bladeDirection, false);
            angleAccuracy = (angleAccuracy + biasAccuracy) * 0.5f; // Average accuracy
            
            // Recalculate weighted input using neural network if available
            if (neuralNetwork != null)
            {
                // Create a temporary input array with current weight and bias
                double[] weightedInputs = new double[inputs.Length];
                for (int i = 0; i < inputs.Length; i++)
                {
                    weightedInputs[i] = inputs[i] * currentWeight + biasValue;
                }
                
                // Get the weighted input from neural network
                double[] networkOutput = neuralNetwork.Forward(weightedInputs);
                weightedInput = (float)networkOutput[0];
            }
            else
            {
                // Fallback to local calculation
                if (inputs != null && inputs.Length > 0)
                {
                    weightedInput = (float)(inputs[0] * currentWeight) + biasValue;
                }
            }
            
            // Update position on plot if already hit by weight
            if (hasBeenHitByWeight && activationVis != null)
            {
                Vector3 plotPosition = activationVis.GetWorldPositionOnPlot(weightedInput, 0);
                targetVisualizerPosition = plotPosition;
            }
            
            Debug.Log($"Bias applied - New Weighted Input: {weightedInput}, Accuracy: {biasAccuracy}");
        }
    }

    private void HandleVisualizerArrival()
    {
        // Add the datapoint to the visualization with accuracy-based color
        if (activationVis != null)
        {
            activationVis.AddCollectedDataPoint(weightedInput, 0, angleAccuracy); // Pass accuracy for color
        }
        
        // Destroy the datapoint after reaching the visualizer
        Destroy(gameObject);
    }

    public void SetNeuralNetwork(NeuralNetwork network)
    {
        this.neuralNetwork = network;
    }
}