using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// An arcade-style car controller using Rigidbody velocity for stable physics.
/// Features distinct acceleration, deceleration, braking, and a functional drift mechanic.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class CarController : MonoBehaviour
{
    [Header("Movement Settings")]
    [SerializeField] private float _maxForwardSpeed = 20f;
    [SerializeField] private float _maxReverseSpeed = 8f;
    [SerializeField] private float _acceleration = 30f;
    [Tooltip("How quickly the car slows down when coasting (no input).")]
    [SerializeField] private float _deceleration = 15f;
    [Tooltip("The power of the brakes. Higher values stop the car faster.")]
    [SerializeField] private float _brakePower = 50f;

    [Header("Steering Settings")]
    [Tooltip("How fast the car turns. This is modified by the steering curve.")]
    [SerializeField] private float _steeringSpeed = 120f;
    [Tooltip("Defines steering sensitivity based on speed (X-axis: 0=stopped, 1=max speed).")]
    [SerializeField] private AnimationCurve _steeringCurve = AnimationCurve.EaseInOut(0, 1, 1, 0.5f);

    [Header("Drifting & Grip")]
    [SerializeField] private bool _enableDrifting = true;
    [Tooltip("How much sideways grip the car has (0-1). Higher values mean less sliding.")]
    [Range(0, 1)]
    [SerializeField] private float _sidewaysGrip = 0.9f;
    [Tooltip("Sideways grip during a drift. Lower than normal grip to allow sliding.")]
    [Range(0, 1)]
    [SerializeField] private float _driftGrip = 0.4f;
    [Tooltip("A multiplier for steering speed during a drift.")]
    [SerializeField] private float _driftSteeringBoost = 1.5f;

    // Internal State
    private float _steeringInput = 0f;
    private float _accelerationInput = 0f;
    private bool _isBraking = false;
    private bool _isDrifting = false;

    private InputSystem_Actions _inputActions;
    private Rigidbody _rb;

    #region Unity Lifecycle & Input

    private void Awake()
    {
        _inputActions = new InputSystem_Actions();
        _rb = GetComponent<Rigidbody>();
    }

    private void OnEnable()
    {
        _inputActions.Car.Enable();
        _inputActions.Car.Accelerate.performed += OnAccelerate;
        _inputActions.Car.Accelerate.canceled += OnAccelerate;
        _inputActions.Car.Brake.performed += OnBrake;
        _inputActions.Car.Brake.canceled += OnBrake;
        _inputActions.Car.Steer.performed += OnSteer;
        _inputActions.Car.Steer.canceled += OnSteer;
        _inputActions.Car.Drift.performed += OnDrift;
        _inputActions.Car.Drift.canceled += OnDrift;
    }

    private void OnDisable()
    {
        _inputActions.Car.Disable();
        _inputActions.Car.Accelerate.performed -= OnAccelerate;
        _inputActions.Car.Accelerate.canceled -= OnAccelerate;
        _inputActions.Car.Brake.performed -= OnBrake;
        _inputActions.Car.Brake.canceled -= OnBrake;
        _inputActions.Car.Steer.performed -= OnSteer;
        _inputActions.Car.Steer.canceled -= OnSteer;
        _inputActions.Car.Drift.performed -= OnDrift;
        _inputActions.Car.Drift.canceled -= OnDrift;
    }

    private void FixedUpdate()
    {
        ProcessMovement();
        ProcessSteering();
        ApplySidewaysGrip();
    }

    #endregion

    #region Input Callbacks

    private void OnAccelerate(InputAction.CallbackContext context) => _accelerationInput = context.ReadValue<float>();
    private void OnBrake(InputAction.CallbackContext context) => _isBraking = context.performed;
    private void OnSteer(InputAction.CallbackContext context) => _steeringInput = context.ReadValue<float>();
    private void OnDrift(InputAction.CallbackContext context) => _isDrifting = context.performed && _enableDrifting;

    #endregion

    #region Core Logic

    private void ProcessMovement()
    {
        // Get the current speed along the car's forward direction.
        // Dot product gives us this: positive is forward, negative is reverse.
        float currentForwardSpeed = Vector3.Dot(transform.forward, _rb.linearVelocity);

        // Determine the target speed based on player input.
        float targetSpeed = 0f;
        if (_accelerationInput > 0)
            targetSpeed = _maxForwardSpeed;
        else if (_accelerationInput < 0)
            targetSpeed = -_maxReverseSpeed;

        // Determine the rate of change (acceleration, deceleration, or braking).
        float accelerationRate;

        // Condition for braking: Player presses the brake key, OR is trying to reverse while moving forward.
        if (_isBraking || (Mathf.Sign(_accelerationInput) == -1 && Mathf.Sign(currentForwardSpeed) == 1))
        {
            accelerationRate = _brakePower;
        }
        else if (Mathf.Abs(_accelerationInput) > 0)
        {
            accelerationRate = _acceleration;
        }
        else // No input, apply coasting deceleration.
        {
            accelerationRate = _deceleration;
        }

        // Use MoveTowards to smoothly change the forward speed.
        float newForwardSpeed = Mathf.MoveTowards(currentForwardSpeed, targetSpeed, accelerationRate * Time.fixedDeltaTime);

        // Apply the new forward speed.
        _rb.linearVelocity = transform.forward * newForwardSpeed + transform.right * Vector3.Dot(_rb.linearVelocity, transform.right);
    }

    private void ProcessSteering()
    {
        // Calculate steering sensitivity based on the car's current speed.
        float speedFactor = Mathf.Clamp01(_rb.linearVelocity.magnitude / _maxForwardSpeed);
        float steeringMultiplier = _steeringCurve.Evaluate(speedFactor);
        float currentSteeringSpeed = _steeringSpeed * steeringMultiplier;

        if (_isDrifting)
        {
            currentSteeringSpeed *= _driftSteeringBoost;
        }

        // Calculate and apply rotation.
        float steeringAmount = _steeringInput * currentSteeringSpeed * Time.fixedDeltaTime;
        Quaternion deltaRotation = Quaternion.Euler(Vector3.up * steeringAmount);
        _rb.MoveRotation(_rb.rotation * deltaRotation);
    }

    private void ApplySidewaysGrip()
    {
        // Get velocity sideways to the car's direction.
        Vector3 sidewaysVelocity = transform.right * Vector3.Dot(_rb.linearVelocity, transform.right);

        // Determine the current grip based on whether we are drifting.
        float currentGrip = _isDrifting ? _driftGrip : _sidewaysGrip;

        // Apply a counter-force to reduce sideways velocity, simulating grip.
        Vector3 counterForce = -sidewaysVelocity * (1 - currentGrip) * 10f; // Multiplier to make grip feel responsive
        _rb.AddForce(counterForce, ForceMode.VelocityChange);
    }

    #endregion
}
