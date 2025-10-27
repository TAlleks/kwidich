using System;
using System.Collections.Generic;
using LogitechG29.Sample.Input;
using UnityEngine;

public class BroomController : MonoBehaviour
{
    [SerializeField] private InputControllerReader inputControllerReader;

    [Header("Broom Physics")]
    public float maxSpeed = 20f;
    public float acceleration = 5f;
    public float steeringSensitivity = 2f;
    public float liftForce = 10f;
    public float hoverHeight = 2f;

    [Header("References")]
    public Transform broomModel;

    private Rigidbody rb;
    private float currentSpeed;
    private bool isGrounded;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
    }

    void Update()
    {
        HandleInput();
        CheckGround();
    }

    void FixedUpdate()
    {
        ApplyHover();
    }


    private void HandleInput()
    {
        // �������� ���� � �����������
        float steering = inputControllerReader.Steering;
        float throttle = inputControllerReader.Throttle;
        float brake = inputControllerReader.Brake;

        // ��������� ����������
        ApplySteering(steering);
        ApplyThrottle(throttle, brake);
        HandleSpecialControls();
    }

    private void ApplySteering(float steeringInput)
    {
        // ������� �����
        float turnAmount = steeringInput * steeringSensitivity * Time.deltaTime;
        transform.Rotate(0, turnAmount, 0);

        // ������ ����� ��� ��������
        if (broomModel != null)
        {
            float targetTilt = steeringInput * 30f;
            Quaternion targetRotation = Quaternion.Euler(0, 0, -targetTilt);
            broomModel.localRotation = Quaternion.Lerp(broomModel.localRotation, targetRotation, Time.deltaTime * 5f);
        }
    }

    private void ApplyThrottle(float throttle, float brake)
    {
        // ������ �������� � ������ ���� � �������
        float targetSpeed = throttle * maxSpeed;
        currentSpeed = Mathf.Lerp(currentSpeed, targetSpeed, acceleration * Time.deltaTime);

        // ����������
        if (brake > 0.1f)
        {
            currentSpeed = Mathf.Lerp(currentSpeed, 0, brake * 2f * Time.deltaTime);
        }

        // �������� ������
        Vector3 moveDirection = transform.forward * currentSpeed;
        rb.linearVelocity = new Vector3(moveDirection.x, rb.linearVelocity.y, moveDirection.z);

        
    }

    private void ApplyHover()
    {
        // ��������� �����
        RaycastHit hit;
        if (Physics.Raycast(transform.position, Vector3.down, out hit, hoverHeight * 2f))
        {
            float hoverForce = liftForce * (1f - (hit.distance / hoverHeight));
            rb.AddForce(Vector3.up * hoverForce, ForceMode.Acceleration);
            isGrounded = hit.distance < hoverHeight + 0.5f;
        }
        else
        {
            // ������� ������� ���� ��� ����������� ��� ������
            rb.AddForce(Vector3.down * 5f, ForceMode.Acceleration);
            isGrounded = false;
        }
    }

    private void HandleSpecialControls()
    {
        // ����������� ����������� �����
        if (Input.GetKey(KeyCode.JoystickButton0) || Input.GetKey(KeyCode.Space)) // ������ A/X ��� ������
        {
            // ���������� �����
            rb.AddForce(Vector3.up * liftForce * 2f, ForceMode.Impulse);
        }

        if (Input.GetKey(KeyCode.JoystickButton1) || Input.GetKey(KeyCode.LeftShift)) // ������ B/Circle ��� Shift
        {
            // �����-���������
            currentSpeed = Mathf.Lerp(currentSpeed, maxSpeed * 1.5f, Time.deltaTime * 3f);
        }
    }

    private void CheckGround()
    {
        // �������������� �������� ����� ��� ������
        RaycastHit hit;
        isGrounded = Physics.Raycast(transform.position, Vector3.down, out hit, hoverHeight + 0.2f);
    }

    // ��������������� ������
    public float GetCurrentSpeed()
    {
        return currentSpeed;
    }

    public bool IsGrounded()
    {
        return isGrounded;
    }

    void OnDrawGizmosSelected()
    {
        // ������������ � ���������
        Gizmos.color = Color.green;
        Gizmos.DrawLine(transform.position, transform.position + Vector3.down * hoverHeight);
        Gizmos.color = Color.red;
        Gizmos.DrawLine(transform.position, transform.position + transform.forward * 2f);
    }
}
