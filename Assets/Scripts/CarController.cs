using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// A top-down car controller with realistic mechanics for acceleration, braking, reversing, and steering.
/// Designed to work with a Rigidbody for stable physics.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class CarController : MonoBehaviour
{
    [Header("Movement Settings")]
    [SerializeField] private float _maxForwardSpeed = 20f;
    [SerializeField] private float _maxReverseSpeed = 8f;
    [SerializeField] private float _acceleration = 25f;
    [Tooltip("The rate at which the car slows down when not accelerating. This is the natural 'friction' or 'drag'.")]
    [SerializeField] private float _deceleration = 15f;
    [Tooltip("How powerful the brakes are. Should be higher than acceleration.")]
    [SerializeField] private float _brakePower = 40f;

    [Header("Steering Settings")]
    [SerializeField] private float _steeringSpeed = 120f;
    [Tooltip("How much steering is reduced at max speed. 0 = no reduction, 1 = full reduction.")]
    [Range(0, 1)]
    [SerializeField] private float _minSpeedToSteer = 0.5f;
    [SerializeField] private AnimationCurve _steeringSpeedCurve;

    [Header("Drifting")]
    [SerializeField] private bool _enableDrifting = true;
    [Tooltip("How much grip the car loses during a drift (0-1). Lower values mean more slip.")]
    [Range(0, 1)]
    [SerializeField] private float _driftGrip = 0.85f;
    [Tooltip("A multiplier for steering speed during a drift.")]
    [SerializeField] private float _driftSteeringBoost = 1.3f;

    // Internal State
    private float _currentSpeed = 0f;
    private float _steeringInput = 0f;
    private float _accelerationInput = 0f; // Combined forward/reverse input
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
        // Subscribe to all input actions
        _inputActions.Car.Accelerate.performed += OnAccelerate;
        _inputActions.Car.Accelerate.canceled += OnAccelerate;
        _inputActions.Car.Brake.performed += OnBrake;
        _inputActions.Car.Brake.canceled += OnBrake;
        _inputActions.Car.Steer.performed += OnSteer;
        _inputActions.Car.Steer.canceled += OnSteer;
        _inputActions.Car.Drift.performed += OnDrift;
        _inputActions.Car.Drift.canceled += OnDrift;
        _inputActions.Car.Enable();
    }

    private void OnDisable()
    {
        // Unsubscribe to prevent memory leaks
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

    /// <summary>
    /// Physics calculations should always be in FixedUpdate.
    /// </summary>
    private void FixedUpdate()
    {
        ProcessMovement();
        ProcessSteering();
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
        // Determine the target speed based on input
        float targetSpeed = 0f;
        if (_accelerationInput > 0)
            targetSpeed = _maxForwardSpeed;
        else if (_accelerationInput < 0)
            targetSpeed = -_maxReverseSpeed;

        // Determine the rate of acceleration/deceleration
        float accelerationRate;

        // Check for braking conditions
        bool isActivelyBraking = (_isBraking || (Mathf.Sign(_accelerationInput) != Mathf.Sign(_currentSpeed) && _currentSpeed != 0));

        if (isActivelyBraking)
        {
            accelerationRate = _brakePower;
        }
        else if (_accelerationInput != 0)
        {
            accelerationRate = _acceleration;
        }
        else // No input, apply natural deceleration (our new friction)
        {
            accelerationRate = _deceleration;
        }

        // Use MoveTowards to smoothly change the current speed
        _currentSpeed = Mathf.MoveTowards(_currentSpeed, targetSpeed, accelerationRate * Time.fixedDeltaTime);

        // Apply movement to the Rigidbody
        Vector3 movement = transform.forward * _currentSpeed * Time.fixedDeltaTime;
        _rb.MovePosition(_rb.position + movement);
    }

    private void ProcessSteering()
    {
        if (Mathf.Abs(_currentSpeed) < _minSpeedToSteer) return;

        float inputMagnitude = Mathf.Abs(_currentSpeed) / _maxForwardSpeed;
        float steeringMultiplier = _steeringSpeedCurve.Evaluate(inputMagnitude);

        // Reduce steering effectiveness at higher speeds for more realistic handling
        float speedFactor = 1 - (Mathf.Abs(_currentSpeed) / _maxForwardSpeed * steeringMultiplier);
        float currentSteeringSpeed = _steeringSpeed * speedFactor;

        // Apply drift boost if drifting
        if (_isDrifting)
        {
            currentSteeringSpeed *= _driftSteeringBoost;
        }

        // Calculate rotation and apply it to the Rigidbody
        float steeringAmount = _steeringInput * currentSteeringSpeed * Time.fixedDeltaTime;
        Quaternion deltaRotation = Quaternion.Euler(0, steeringAmount, 0);
        _rb.MoveRotation(_rb.rotation * deltaRotation);
    }

    #endregion
}
