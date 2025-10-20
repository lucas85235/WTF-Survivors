using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Cinemachine;

/// <summary>
/// An arcade-style car controller using Rigidbody velocity for stable physics.
/// Features distinct acceleration, deceleration, braking, and a functional drift mechanic.
/// Audio: engine loop, accel/blip, brake, skid (loop), collision impacts.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(AudioSource))]
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

    [Header("Visual Feedback")]
    [SerializeField] private float _tiltSmoothness = 8f;

    [Header("Wheel Feedback")]
    [SerializeField] private Transform[] _frontWheels;
    [SerializeField] private Transform[] _allWheels;
    [SerializeField] private float _wheelRadius = 0.35f;
    [SerializeField] private float _maxWheelSteerAngle = 30f;

    [Header("Camera")]
    [SerializeField] private CinemachineThirdPersonFollow _carCamera;

    [Header("Collision Feedback")]
    [Tooltip("The tag used to identify enemies.")]
    [SerializeField] private string _zombieTag = "Enemy";
    [Tooltip("How much speed the car loses when hitting an enemy (e.g., 0.1 = 10% speed loss).")]
    [Range(0, 1)]
    [SerializeField] private float _speedLossPerHit = 0.1f;

    #region Audio (SFX)

    [Header("Audio - General")]
    [Tooltip("Main SFX source. Used for engine loop and one-shot non-positional sounds.")]
    [SerializeField] private AudioSource _sfxSource;

    [Tooltip("Optional audio source used for a positional/looping skid sound")]
    [SerializeField] private AudioSource _skidSource;

    [Tooltip("Engine loop clip (single). Will loop on _sfxSource.")]
    [SerializeField] private AudioClip _engineLoopClip;

    [Tooltip("Small blip / rev sounds when pressing accelerate")]
    [SerializeField] private AudioClip[] _accelClips;

    [Tooltip("Brake one-shot clips")]
    [SerializeField] private AudioClip[] _brakeClips;

    [Tooltip("Skid start one-shot (played when drift starts)")]
    [SerializeField] private AudioClip[] _skidStartClips;

    [Tooltip("Loop clip for skid (if provided) - assigned to _skidSource.clip and looped)")]
    [SerializeField] private AudioClip _skidLoopClip;

    [Tooltip("Collision impact sounds (played at contact point)")]
    [SerializeField] private AudioClip[] _collisionClips;

    [Tooltip("Optional zombie hit sounds (played at contact point)")]
    [SerializeField] private AudioClip[] _zombieHitClips;

    [Header("Audio Settings")]
    [Range(0f, 1f)]
    [SerializeField] private float _sfxVolume = 1f;
    [SerializeField] private float _enginePitchRange = 0.8f; // added on top of base pitch (0..1)
    [Range(0f, 0.2f)]
    [SerializeField] private float _pitchVariance = 0.06f;
    [Tooltip("Minimum forward speed for squeal logic")]
    [SerializeField] private float _squealSpeedThreshold = 8f;
    [Tooltip("Angle (abs steering) above which we consider a squeal/strong turn")]
    [SerializeField] private float _squealSteerThreshold = 0.6f;

    #endregion

    // Internal State
    private float _steeringInput = 0f;
    private float _accelerationInput = 0f;
    private float _currentWheelAngle = 0f;
    private float _currentCameraSide = 0.5f; // 0 = left, 1 = right
    private bool _isBraking = false;
    private bool _isDrifting = false;

    private InputSystem_Actions _inputActions;
    private Rigidbody _rb;

    // audio state trackers to avoid repeating sounds
    private float _prevAccelerationInput = 0f;
    private bool _prevIsBraking = false;
    private bool _prevIsDrifting = false;
    private bool _isSkidLooping = false;
    private bool _prevSquealPlayed = false;

    #region Unity Lifecycle & Input

    private void Awake()
    {
        _inputActions = new InputSystem_Actions();
        _rb = GetComponent<Rigidbody>();

        // ensure main sfx source exists
        if (_sfxSource == null)
        {
            _sfxSource = GetComponent<AudioSource>();
            if (_sfxSource == null)
                _sfxSource = gameObject.AddComponent<AudioSource>();
        }
        // default: engine loop is non-positional 2D (spatialBlend = 0). If you want the engine to be 3D, change to 1.
        _sfxSource.spatialBlend = 0f;
        _sfxSource.loop = false; // we'll manage engine loop manually below

        // optionally create skid source for looped positional skid
        if (_skidSource == null && _skidLoopClip != null)
        {
            GameObject skidObj = new GameObject("SkidSource");
            skidObj.transform.SetParent(transform, false);
            _skidSource = skidObj.AddComponent<AudioSource>();
            _skidSource.loop = true;
            _skidSource.spatialBlend = 1f; // 3D so skid follows the car position
            _skidSource.playOnAwake = false;
            _skidSource.clip = _skidLoopClip;
        }

        // start engine loop if provided (we will modulate pitch)
        if (_engineLoopClip != null)
        {
            _sfxSource.clip = _engineLoopClip;
            _sfxSource.loop = true;
            _sfxSource.volume = _sfxVolume * 0.8f;
            _sfxSource.pitch = 1f;
            _sfxSource.Play();
        }
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

    private void Update()
    {
        UpdateWheels();
        CameraSide();

        // Update engine pitch based on forward speed
        UpdateEnginePitch();

        // Handle squeal logic (turning hard at high speed)
        HandleSqueal();
    }

    private void FixedUpdate()
    {
        ProcessMovement();
        ProcessSteering();
        ApplySidewaysGrip();

        // audio event checks for accel/brake/drift transitions
        HandleAudioStateTransitions();
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

        if (_accelerationInput < 0)
        {
            currentSteeringSpeed = -currentSteeringSpeed;
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

    #region Feedbacks

    private void CameraSide()
    {
        _steeringInput = Mathf.Clamp(_steeringInput, -1f, 1f);
        float cameraTargetSide = (_steeringInput + 1f) / 2f; // Normalize to 0-1 for Cinemachine
        _currentCameraSide = Mathf.Lerp(_currentCameraSide, cameraTargetSide, Time.deltaTime * 2f);
        _carCamera.CameraSide = _currentCameraSide;
    }

    private void UpdateWheels()
    {
        if (_frontWheels != null && _frontWheels.Length > 0)
        {
            float targetWheelAngle = _steeringInput * _maxWheelSteerAngle;
            _currentWheelAngle = Mathf.LerpAngle(_currentWheelAngle, targetWheelAngle, _tiltSmoothness * Time.deltaTime);

            foreach (var wheel in _frontWheels)
            {
                var euler = wheel.localRotation.eulerAngles;
                wheel.localRotation = Quaternion.Euler(euler.x, _currentWheelAngle, euler.z);
            }
        }

        if (_allWheels != null && _wheelRadius > 0)
        {
            float speed = Vector3.Dot(transform.forward, _rb.linearVelocity);
            float wheelRotation = (speed * Time.deltaTime / (2f * Mathf.PI * _wheelRadius)) * 360f;

            foreach (var wheel in _allWheels)
            {
                wheel.Rotate(wheelRotation, 0, 0, Space.Self);
            }
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag(_zombieTag))
        {
            // Reduce car speed by a configured percentage
            _rb.linearVelocity *= (1 - _speedLossPerHit);

            // // Play collision impact at contact point (positional)
            // Vector3 contactPoint = other.ClosestPoint(transform.position);
            // PlayImpactAtPoint(_collisionClips, contactPoint, 1f);

            // // Play zombie hit sound at contact point (optional)
            // PlayImpactAtPoint(_zombieHitClips, contactPoint, 0.9f);
        }
        else if (other.CompareTag("KillBox"))
        {
            GetComponent<PlayerHealth>()?.TakeDamage(9999);
        }
        else
        {
            // Play general collision impact sound at contact point
            Vector3 contactPoint = other.ClosestPoint(transform.position);
            PlayImpactAtPoint(_collisionClips, contactPoint, 1.0f);
        }
    }

    #endregion

    #region Audio Helpers & State Handling

    private void UpdateEnginePitch()
    {
        if (_sfxSource == null || _engineLoopClip == null) return;

        // Use forward speed to modulate pitch
        float forwardSpeed = Mathf.Abs(Vector3.Dot(transform.forward, _rb.linearVelocity));
        float speedNormalized = Mathf.Clamp01(forwardSpeed / _maxForwardSpeed);
        float targetPitch = 1f + (_enginePitchRange * speedNormalized);
        // small randomized variance for realism
        float varied = Random.Range(1f - _pitchVariance, 1f + _pitchVariance);
        _sfxSource.pitch = targetPitch * varied;
    }

    private void HandleSqueal()
    {
        // Play a one-shot squeal when turning hard at speed (prevent flood with prev flag)
        float forwardSpeed = Mathf.Abs(Vector3.Dot(transform.forward, _rb.linearVelocity));
        bool shouldSqueal = forwardSpeed > _squealSpeedThreshold && Mathf.Abs(_steeringInput) > _squealSteerThreshold;

        if (shouldSqueal && !_prevSquealPlayed)
        {
            // reuse accelClips as possible squeal alternatives if left empty; otherwise no-op
            PlayLocalOneShot(_accelClips, 1.0f);
            _prevSquealPlayed = true;
        }
        else if (!shouldSqueal)
        {
            _prevSquealPlayed = false;
        }
    }

    private void HandleAudioStateTransitions()
    {
        // Acceleration press start
        if (_accelerationInput > 0.1f && _prevAccelerationInput <= 0.1f)
        {
            PlayLocalOneShot(_accelClips);
        }

        // Brake pressed start
        if (_isBraking && !_prevIsBraking)
        {
            PlayLocalOneShot(_brakeClips);
        }

        // Drift start
        if (_isDrifting && !_prevIsDrifting)
        {
            PlayLocalOneShot(_skidStartClips);
            StartSkidLoop();
        }

        // Drift end
        if (!_isDrifting && _prevIsDrifting)
        {
            StopSkidLoop();
        }

        // store previous states for next check
        _prevAccelerationInput = _accelerationInput;
        _prevIsBraking = _isBraking;
        _prevIsDrifting = _isDrifting;
    }

    private void StartSkidLoop()
    {
        if (_skidSource == null || _skidLoopClip == null) return;
        if (!_isSkidLooping)
        {
            _skidSource.volume = _sfxVolume;
            _skidSource.pitch = 1f + Random.Range(-_pitchVariance, _pitchVariance);
            _skidSource.Play();
            _isSkidLooping = true;
        }
    }

    private void StopSkidLoop()
    {
        if (_skidSource == null || !_isSkidLooping) return;
        _skidSource.Stop();
        _isSkidLooping = false;
    }

    // plays a random clip from array on the local sfxSource (non-positional)
    private void PlayLocalOneShot(AudioClip[] clips, float volume = -1f)
    {
        if (clips == null || clips.Length == 0 || _sfxSource == null) return;
        AudioClip clip = clips[Random.Range(0, clips.Length)];
        float vol = (volume < 0f) ? _sfxVolume : volume;
        float prevPitch = _sfxSource.pitch;
        _sfxSource.pitch = Random.Range(1f - _pitchVariance, 1f + _pitchVariance);
        _sfxSource.PlayOneShot(clip, vol);
        _sfxSource.pitch = prevPitch;
    }

    // plays a random clip at world position (spatial). uses built-in PlayClipAtPoint (no pitch variance).
    private void PlayImpactAtPoint(AudioClip[] clips, Vector3 point, float volume = 1f)
    {
        if (clips == null || clips.Length == 0) return;
        AudioClip clip = clips[Random.Range(0, clips.Length)];
        AudioSource.PlayClipAtPoint(clip, point, Mathf.Clamp01(volume * _sfxVolume));
    }

    #endregion
}
