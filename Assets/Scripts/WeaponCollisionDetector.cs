using UnityEngine;
using Valve.VR;

public class WeaponCollisionDetector : MonoBehaviour
{
    [Header("Weapon Type")]
    public bool isWeightBlade = false; // true for weight blade, false for bias blade
    
    [Header("References")]
    public NeuralNetworkWeaponSystem weaponSystem;
    public AFVisualizer afVisualizer; // Reference to the activation function visualizer

    [Header("Hit Effects")]
    public GameObject weightHitEffectPrefab;
    public GameObject biasHitEffectPrefab;

    
    private void OnCollisionEnter(Collision collision)
    {
        HandleDataPointCollision(collision.gameObject);
    }
    
    private void OnTriggerEnter(Collider other)
    {
        HandleDataPointCollision(other.gameObject);
    }
    
    private void HandleDataPointCollision(GameObject dataPointObj)
    {
        // Check if we hit a data point
        if (dataPointObj.CompareTag("DataPoint"))
        {
            // Get the data point component
            DataPoint dataPoint = dataPointObj.GetComponent<DataPoint>();
            if (dataPoint != null)
            {
                // Trigger haptic feedback
                if (weaponSystem != null)
                {
                    SteamVR_Input_Sources handType = isWeightBlade ? 
                        SteamVR_Input_Sources.LeftHand : SteamVR_Input_Sources.RightHand;
                    weaponSystem.TriggerHapticFeedback(handType);
                }

                // Make sure the DataPoint has the reference to the visualizer
                if (afVisualizer != null && dataPoint.GetComponent<DataPoint>() != null)
                {
                    dataPoint.SetVisualizer(afVisualizer);
                }
                
                // Use DataPoint's built-in hit handling
                dataPoint.HandleHit();
                
                // Additional weapon-specific feedback
                HandleDataPointHit(dataPoint);
            }
        }
    }
    
    private void HandleDataPointHit(DataPoint dataPoint)
    {
        if (weaponSystem != null)
        {
            if (isWeightBlade)
            {
                Debug.Log("Weight blade hit data point " + dataPoint.name);
                GameObject effect = Instantiate(weightHitEffectPrefab, dataPoint.transform.position, Quaternion.identity);
                Destroy(effect, 1f);
            }
            else
            {
                Debug.Log("Bias blade hit data point " + dataPoint.name);
                GameObject effect = Instantiate(biasHitEffectPrefab, dataPoint.transform.position, Quaternion.identity);
                Destroy(effect, 1f);
            }
        }
    }
} 