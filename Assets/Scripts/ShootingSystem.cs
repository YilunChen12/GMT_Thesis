using UnityEngine;
using Valve.VR;

public class ShootingSystem : MonoBehaviour
{
    [Header("Controller Settings")]
    public SteamVR_Input_Sources handType = SteamVR_Input_Sources.LeftHand;
    public SteamVR_Behaviour_Pose controllerPose;
    public SteamVR_Action_Boolean shootAction = SteamVR_Actions.default_InteractUI;

    [Header("Laser Settings")]
    public GameObject laserPrefab;
    public float laserMaxDistance = 100f;
    public Color laserColor = Color.cyan;
    private GameObject laser;
    private LineRenderer laserLine;

    [Header("Shooting Settings")]
    public GameObject projectilePrefab;
    public float projectileSpeed = 20f;
    public float shootCooldown = 0.2f;
    private float lastShootTime;

    [Header("Target Prefabs")]
    public GameObject validDataPointPrefab;  // 球体预制体
    public GameObject noiseDataPointPrefab;  // 矩形预制体

    private void Start()
    {
        // 初始化激光
        laser = Instantiate(laserPrefab);
        laserLine = laser.GetComponent<LineRenderer>();
        laserLine.startColor = laserColor;
        laserLine.endColor = laserColor;
    }

    private void Update()
    {
        UpdateLaser();
        HandleShooting();
    }

    private void UpdateLaser()
    {
        if (controllerPose != null)
        {
            // 更新激光位置和方向
            laser.transform.position = controllerPose.transform.position;
            laser.transform.rotation = controllerPose.transform.rotation;

            // 射线检测
            RaycastHit hit;
            if (Physics.Raycast(controllerPose.transform.position, controllerPose.transform.forward, out hit, laserMaxDistance))
            {
                laserLine.SetPosition(0, controllerPose.transform.position);
                laserLine.SetPosition(1, hit.point);
            }
            else
            {
                laserLine.SetPosition(0, controllerPose.transform.position);
                laserLine.SetPosition(1, controllerPose.transform.position + controllerPose.transform.forward * laserMaxDistance);
            }
        }
    }

    private void HandleShooting()
    {
        if (shootAction.GetStateDown(handType) && Time.time > lastShootTime + shootCooldown)
        {
            Shoot();
            lastShootTime = Time.time;
        }
    }

    private void Shoot()
    {
        if (controllerPose != null && projectilePrefab != null)
        {
            // 创建子弹
            GameObject projectile = Instantiate(projectilePrefab, 
                controllerPose.transform.position, 
                controllerPose.transform.rotation);

            // 设置子弹速度
            Rigidbody rb = projectile.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.velocity = controllerPose.transform.forward * projectileSpeed;
            }

            // 设置子弹生命周期
            Destroy(projectile, 5f);
        }
    }

    // 生成测试数据点
    public void SpawnTestDataPoint(bool isValid)
    {
        Vector3 spawnPosition = new Vector3(
            Random.Range(-5f, 5f),
            Random.Range(-5f, 5f),
            Random.Range(10f, 20f)
        );

        GameObject dataPoint = Instantiate(
            isValid ? validDataPointPrefab : noiseDataPointPrefab,
            spawnPosition,
            Quaternion.identity
        );

        // 设置数据点移动方向（朝向玩家）
        Rigidbody rb = dataPoint.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.velocity = -spawnPosition.normalized * 5f; // 朝向玩家移动
        }

        // 设置数据点属性
        DataPoint dataPointComponent = dataPoint.GetComponent<DataPoint>();
        if (dataPointComponent != null)
        {
            dataPointComponent.isValid = isValid;
        }

        // 设置生命周期
        Destroy(dataPoint, 10f);
    }
} 