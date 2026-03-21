using _2DOF;
using Bhaptics.SDK2;
using System.Collections;
using UnityEngine;

public class CarTelemetryHandler1 : MonoBehaviour
{
    private const float WAIT_TIME = SendingData.WAIT_TIME / 1000f;

    private ObjectTelemetryData telemetryDataData;
    private SendingData _sendingData;

    [Header("Vehicle References")]
    [SerializeField] private Transform vehicleTransform;
    [SerializeField] private Rigidbody rb;

    [Header("Platform Settings")]
    private const float maxPlatformAngle = 15f;
    private const float maxPlatformVelocity = 200f;
    private float currentPitch = 0f;
    private float currentRoll = 0f;
    private float currentLinearAcceleration = 0f;
    private float lastLinearVelocity = 0f;
    private float currentAngularVelocity = 0f;

    [Header("bHaptics Feedback")]
    [SerializeField] private bool enableHaptics = true;
    [SerializeField] private bool debugHaptics = false;
    [SerializeField] private float accelThreshold = 2.5f;
    [SerializeField] private float lateralThreshold = 2.5f;
    [SerializeField] private float collisionIntensityScale = 3.6f;

    private Vector3 _previousVelocity;

    private void Awake()
    {
        _sendingData = new SendingData();
        telemetryDataData = _sendingData.ObjectTelemetryData;
        _previousVelocity = rb.linearVelocity;
    }

    private void OnEnable()
    {
        StartCoroutine(TelemetryHandler());
        _sendingData.SendingStart();
    }

    private void OnDisable()
    {
        StopCoroutine(TelemetryHandler());
        _sendingData.SendingStop();
    }

    private IEnumerator TelemetryHandler()
    {
        while (true)
        {
            if (telemetryDataData == null)
            {
                yield return new WaitForSeconds(WAIT_TIME * 10f);
                continue;
            }

            UpdatePlatformAngles();

            if (enableHaptics)
                HandleHaptics();

            yield return new WaitForSeconds(WAIT_TIME);
        }
    }

    private void HandleHaptics()
    {
        Vector3 velocity = rb.linearVelocity;
        Vector3 accel = (velocity - _previousVelocity) / Mathf.Max(Time.deltaTime, 0.001f);

        float forwardAccel = Vector3.Dot(accel, vehicleTransform.forward);
        float lateralAccel = Vector3.Dot(accel, vehicleTransform.right);
        float forwardSpeed = Vector3.Dot(velocity, vehicleTransform.forward);


        // --- Давление в спину (ускорение вперёд) ---
        if (forwardAccel > accelThreshold && forwardSpeed > 1f)
        {
            float intensity = Mathf.Clamp01(forwardAccel / 10f);
            BhapticsLibrary.Play("davlenie_ot_uscarenia", 0, intensity, 1, 0, 0);
            if (debugHaptics)
                Debug.Log($"[HAPTICS] Давление кресла: {intensity:F2}");
        }


        // --- Поворот влево ---
        if (lateralAccel < -lateralThreshold)
        {
            float intensity = Mathf.Clamp01(Mathf.Abs(lateralAccel) / 10f);
            BhapticsLibrary.Play("left_povorot", 0, intensity, 1, 0, 0);
            if (debugHaptics)
                Debug.Log($"[HAPTICS] Поворот влево: {intensity:F2}");
        }

        // --- Поворот вправо ---
        if (lateralAccel > lateralThreshold)
        {
            float intensity = Mathf.Clamp01(Mathf.Abs(lateralAccel) / 10f);
            BhapticsLibrary.Play("right_povorot", 0, intensity, 1, 0, 0);
            if (debugHaptics)
                Debug.Log($"[HAPTICS] Поворот вправо: {intensity:F2}");
        }

        _previousVelocity = velocity;
    }

    private float NormalizeAngle(float angle)
    {
        angle = angle > 180 ? angle - 360 : angle;
        return angle;
    }

    private void UpdatePlatformAngles()
    {
        float targetPitch = Mathf.Clamp(NormalizeAngle(vehicleTransform.eulerAngles.x), -maxPlatformAngle, maxPlatformAngle);
        currentPitch = Mathf.Lerp(currentPitch, targetPitch, 0.05f);

        float targetRoll = Mathf.Clamp(NormalizeAngle(vehicleTransform.eulerAngles.z), -maxPlatformAngle, maxPlatformAngle);
        currentRoll = Mathf.Lerp(currentRoll, targetRoll, 0.12f);

        telemetryDataData.Angles = vehicleTransform.transform.eulerAngles;
        telemetryDataData.Velocity = new Vector3(currentPitch *11f, currentRoll *11f, 0);
        //Debug.Log(telemetryDataData.Angles + "       " + telemetryDataData.Velocity);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!enableHaptics) return;

        float intensity = Mathf.Clamp01(Mathf.Abs(lastLinearVelocity * collisionIntensityScale) / 100f);
        BhapticsLibrary.Play("stolcnovenia", 0, intensity, 1, 0, 0);

        if (debugHaptics)
            Debug.Log($"[HAPTICS] Столкновение (ремень): {intensity:F2}");
    }
}
