using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Robust Enemy AI that follows the player using a NavMeshAgent and handles
/// push-by-car behavior, NavMesh recovery, ragdoll system and stuck recovery.
/// Improvements: stuck detection + multi-step recovery to avoid agents wandering to edges.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class EnemyAI : MonoBehaviour
{
    [Header("References")]
    public Transform player;
    public Animator animator; // Optional animator to disable when ragdolled
    private NavMeshAgent navMeshAgent;
    private Rigidbody mainRigidbody;
    private Collider mainCollider;

    [Header("Movement")]
    public float baseSpeed = 5f;
    public float maxSpeed = 7f;
    public float stoppingDistance = 2f;

    [Header("Pathing")]
    public float pathUpdateInterval = 0.3f;
    private float nextUpdateTime = 0f;

    [Header("Obstacle / Push Detection (offset Y)")]
    [Tooltip("Vertical offset applied when checking for nearby obstacles.")]
    public float obstacleDetectYOffset = 0.5f;
    [Tooltip("Radius used to check for obstacles (not used to steer; just for info).")]
    public float obstacleDetectRadius = 1.5f;

    [Tooltip("Vertical offset applied when checking for push collisions (vehicles).")]
    public float pushDetectYOffset = 0.3f;
    [Tooltip("Radius used to check for push-capable colliders (vehicles).")]
    public float pushDetectRadius = 2.5f;

    [Header("Car Push")]
    public float pushForce = 25f;
    public float minPushTime = 0.5f;
    private bool isPushed = false;
    private float currentPushTime = 0f;

    [Header("NavMesh Recovery")]
    public float minNavMeshSpeed = 0.1f;
    public float navMeshRecoveryTime = 0.6f;

    [Header("Ragdoll Settings")]
    [Tooltip("Tag used by vehicles that can ragdoll this enemy.")]
    public string vehicleTag = "Car";
    [Tooltip("Minimum vehicle speed required to trigger ragdoll.")]
    public float minSpeedToRagdoll = 5f;
    [Tooltip("Base impact force applied when ragdolling.")]
    public float baseImpactForce = 50f;
    [Tooltip("Upward component to add to the impact direction.")]
    public float upwardForceComponent = 0.5f;
    [Tooltip("Multiplier applied to vehicle speed to contribute to force.")]
    public float speedToForceMultiplier = 2f;
    [Tooltip("How long until the ragdoll object is cleaned up/destroyed.")]
    public float ragdollCleanupTime = 10f;

    [Header("Stuck Detection & Recovery")]
    [Tooltip("Interval (s) between stuck checks.")]
    public float stuckCheckInterval = 1f;
    [Tooltip("Minimum travel distance required during interval to consider agent 'moving'.")]
    public float stuckDistanceThreshold = 0.5f;
    [Tooltip("Maximum consecutive stuck detections before trying recovery.")]
    public int maxStuckRetries = 3;
    [Tooltip("Radius around player to sample alternative NavMesh points during recovery.")]
    public float stuckRecoveryRadius = 5f;
    [Tooltip("Max attempts to sample random NavMesh points during recovery.")]
    public int stuckRecoveryAttempts = 8;

    // Ragdoll bones
    private List<Rigidbody> ragdollRigidbodies = new List<Rigidbody>();
    private bool isRagdolled = false;

    private NavMeshPath currentPath;

    // Stuck detection state
    private Vector3 lastStuckCheckPosition;
    private float stuckCheckTimer = 0f;
    private int stuckRetries = 0;

    void Awake()
    {
        player = GameObject.FindGameObjectWithTag("Player")?.transform;
        navMeshAgent = GetComponent<NavMeshAgent>();
        mainRigidbody = GetComponent<Rigidbody>();
        mainCollider = GetComponent<Collider>();

        // Collect ragdoll rigidbodies (children) and ensure they are initially kinematic
        foreach (var rb in GetComponentsInChildren<Rigidbody>())
        {
            if (rb == mainRigidbody) continue;
            ragdollRigidbodies.Add(rb);
            rb.isKinematic = true;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        }

        currentPath = new NavMeshPath();
        lastStuckCheckPosition = transform.position;
    }

    void Start()
    {
        if (navMeshAgent == null)
        {
            Debug.LogError("EnemyAI: NavMeshAgent not found on " + gameObject.name);
            enabled = false;
            return;
        }

        if (player == null)
        {
            Debug.LogError("EnemyAI: Player not assigned on " + gameObject.name);
            return;
        }

        if (mainRigidbody == null)
        {
            Debug.LogError("EnemyAI: Rigidbody not found on " + gameObject.name);
            enabled = false;
            return;
        }

        // Configure navmesh agent: let agent handle avoidance and movement
        navMeshAgent.updateRotation = false;
        navMeshAgent.updateUpAxis = false;
        navMeshAgent.stoppingDistance = stoppingDistance;
        navMeshAgent.speed = baseSpeed;
        navMeshAgent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
        navMeshAgent.avoidancePriority = 50;

        // Rigidbody configuration
        mainRigidbody.useGravity = true;
        mainRigidbody.constraints = RigidbodyConstraints.FreezeRotation;
        mainRigidbody.isKinematic = false;
    }

    void Update()
    {
        if (isRagdolled) return; // while ragdolled, physics controls the object

        if (player == null || navMeshAgent == null || mainRigidbody == null)
            return;

        if (!navMeshAgent.isOnNavMesh)
        {
            // If not on NavMesh, try sampling and warping to nearest NavMesh spot
            TryRecoverAgentImmediate();
            return;
        }

        // Always chase the player (no distance-based disabling)
        if (Time.time >= nextUpdateTime && !isPushed)
        {
            // Avoid spamming SetDestination when pathPending
            if (!navMeshAgent.pathPending)
            {
                navMeshAgent.speed = baseSpeed;
                navMeshAgent.SetDestination(player.position);
            }
            nextUpdateTime = Time.time + pathUpdateInterval;
        }

        // Manage push timer
        if (isPushed)
        {
            currentPushTime -= Time.deltaTime;
            if (currentPushTime <= 0)
                isPushed = false;
        }

        // Detect push collisions and possibly ragdoll
        DetectCarPush();

        // Stuck detection (runs every stuckCheckInterval)
        stuckCheckTimer += Time.deltaTime;
        if (stuckCheckTimer >= stuckCheckInterval)
        {
            CheckStuck();
            stuckCheckTimer = 0f;
            lastStuckCheckPosition = transform.position;
        }

        // If agent is on an OffMeshLink and seems stuck, try to complete it
        if (navMeshAgent.isOnOffMeshLink)
        {
            // Try to complete link to avoid waiting indefinitely
            navMeshAgent.CompleteOffMeshLink();
        }
    }

    void CheckStuck()
    {
        if (isRagdolled || isPushed) 
        {
            // reset counters while pushed or ragdolling
            stuckRetries = 0;
            return;
        }

        // If agent has no path or is stopped, not considered stuck here
        if (!navMeshAgent.hasPath || navMeshAgent.isStopped)
        {
            stuckRetries = 0;
            return;
        }

        float moved = Vector3.Distance(transform.position, lastStuckCheckPosition);

        // if agent barely moved, increment stuck counter
        if (moved < stuckDistanceThreshold)
        {
            stuckRetries++;
        }
        else
        {
            stuckRetries = 0; // good movement, reset
        }

        // If exceeded allowed retries, attempt recovery
        if (stuckRetries >= maxStuckRetries)
        {
            stuckRetries = 0; // reset for next cycle
            StartCoroutine(RecoverAgentRoutine());
        }
    }

    private IEnumerator RecoverAgentRoutine()
    {
        // Safety early exit
        if (isRagdolled) yield break;

        // 1) Try to recalculate path to player
        if (NavMesh.CalculatePath(transform.position, player.position, NavMesh.AllAreas, currentPath))
        {
            if (currentPath.status == NavMeshPathStatus.PathComplete)
            {
                navMeshAgent.ResetPath();
                navMeshAgent.SetPath(currentPath);
                yield break;
            }
        }

        // 2) Try to sample player's position on NavMesh
        if (NavMesh.SamplePosition(player.position, out NavMeshHit hitPlayer, 2f, NavMesh.AllAreas))
        {
            navMeshAgent.Warp(hitPlayer.position);
            navMeshAgent.ResetPath();
            navMeshAgent.SetDestination(player.position);
            yield break;
        }

        // 3) Try multiple random samples around player
        for (int i = 0; i < stuckRecoveryAttempts; ++i)
        {
            Vector3 randomOffset = Random.insideUnitSphere * stuckRecoveryRadius;
            randomOffset.y = 0f;
            Vector3 samplePos = player.position + randomOffset;

            if (NavMesh.SamplePosition(samplePos, out NavMeshHit sampleHit, 2f, NavMesh.AllAreas))
            {
                navMeshAgent.Warp(sampleHit.position);
                navMeshAgent.ResetPath();
                navMeshAgent.SetDestination(player.position);
                yield break;
            }

            yield return null; // allow one frame between attempts
        }

        // 4) As a last resort, warp to current transform position (forces agent to reattach)
        navMeshAgent.Warp(transform.position);
        navMeshAgent.ResetPath();
        navMeshAgent.SetDestination(player.position);
        yield break;
    }

    // Immediate try to recover when agent is off-navmesh
    void TryRecoverAgentImmediate()
    {
        if (isRagdolled) return;

        if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 3f, NavMesh.AllAreas))
        {
            navMeshAgent.Warp(hit.position);
            navMeshAgent.ResetPath();
            navMeshAgent.SetDestination(player.position);
        }
    }

    /// <summary>
    /// Detect push by the player's car (or "Player"-tagged rigidbody near the enemy).
    /// If the colliding vehicle's speed is high enough, activate ragdoll.
    /// </summary>
    void DetectCarPush()
    {
        if (isPushed || mainRigidbody == null || isRagdolled)
            return;

        Vector3 checkCenter = transform.position + Vector3.up * pushDetectYOffset;
        Collider[] cols = Physics.OverlapSphere(checkCenter, pushDetectRadius);

        foreach (Collider col in cols)
        {
            if (col.CompareTag("Player"))
            {
                Rigidbody carRb = col.attachedRigidbody;
                if (carRb == null) continue;

                float carSpeed = carRb.linearVelocity.magnitude;

                // If speed high -> ragdoll
                if (carSpeed >= minSpeedToRagdoll)
                {
                    Vector3 impactDirection = (carRb.linearVelocity.normalized + (Vector3.up * upwardForceComponent)).normalized;
                    float impactMagnitude = baseImpactForce + (carSpeed * speedToForceMultiplier);
                    Vector3 finalForce = impactDirection * impactMagnitude;
                    Vector3 impactPoint = col.ClosestPoint(transform.position);

                    ActivateRagdoll(finalForce, impactPoint);
                    return;
                }

                // Stop the agent (do not disable component) and let physics move object
                if (navMeshAgent.isOnNavMesh)
                {
                    navMeshAgent.isStopped = true;
                    navMeshAgent.updatePosition = false;
                }

                // Apply force
                Vector3 pushDir = (transform.position - col.transform.position).normalized;
                pushDir.y = 0.1f;
                if (float.IsNaN(pushDir.x) || float.IsNaN(pushDir.y) || float.IsNaN(pushDir.z))
                    pushDir = Vector3.forward;

                mainRigidbody.isKinematic = false;
                mainRigidbody.linearVelocity = Vector3.zero;
                mainRigidbody.angularVelocity = Vector3.zero;
                mainRigidbody.AddForce(pushDir * pushForce, ForceMode.Impulse);

                // Set push state and start recovery coroutine
                isPushed = true;
                currentPushTime = minPushTime;

                // Start recovery coroutine which will re-enable agent safely with timeout
                StopCoroutine("RecoverFromPush");
                StartCoroutine(RecoverFromPush(navMeshRecoveryTime, minNavMeshSpeed));

                break;
            }
        }
    }

    // Recover from push - updated to reset stuck state on success/timeout
    private IEnumerator RecoverFromPush(float timeoutSeconds, float velocityThreshold)
    {
        float timer = 0f;
        float checkInterval = 0.08f;

        // Wait a short initial delay so physics can move the body
        yield return new WaitForSeconds(0.05f);

        while (timer < timeoutSeconds)
        {
            if (isRagdolled) yield break;

            float currentSpeed = mainRigidbody.linearVelocity.magnitude;

            if (currentSpeed < velocityThreshold)
            {
                break;
            }

            timer += checkInterval;
            yield return new WaitForSeconds(checkInterval);
        }

        // Finalize recovery
        mainRigidbody.linearVelocity = Vector3.zero;
        mainRigidbody.angularVelocity = Vector3.zero;
        mainRigidbody.isKinematic = true;

        // Try robust reattachment to NavMesh
        if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 2f, NavMesh.AllAreas))
        {
            navMeshAgent.Warp(hit.position);
        }
        else
        {
            navMeshAgent.Warp(transform.position);
        }

        navMeshAgent.updatePosition = true;
        navMeshAgent.isStopped = false;

        isPushed = false;

        // reset stuck tracking so we don't immediately attempt recovery
        stuckRetries = 0;
        lastStuckCheckPosition = transform.position;

        yield break;
    }

    /// <summary>
    /// Activate ragdoll: disable AI and animator, enable child rigidbodies and apply force.
    /// </summary>
    public void ActivateRagdoll(Vector3 force, Vector3 hitPoint)
    {
        if (isRagdolled) return;
        isRagdolled = true;

        if (navMeshAgent) navMeshAgent.enabled = false;
        if (animator) animator.enabled = false;
        if (mainCollider) mainCollider.enabled = false;

        if (mainRigidbody) mainRigidbody.isKinematic = true;

        // Enable ragdoll bodies and apply impulse to closest bone
        Rigidbody closestBone = null;
        float closestDistance = float.MaxValue;

        foreach (var boneRb in ragdollRigidbodies)
        {
            boneRb.isKinematic = false;
            float dist = Vector3.Distance(boneRb.position, hitPoint);
            if (dist < closestDistance)
            {
                closestDistance = dist;
                closestBone = boneRb;
            }
        }

        if (closestBone != null)
            closestBone.AddForceAtPosition(force, hitPoint, ForceMode.Impulse);

        StartCoroutine(CleanupRagdoll());
    }

    private IEnumerator CleanupRagdoll()
    {
        yield return new WaitForSeconds(ragdollCleanupTime);
        Destroy(gameObject);
    }

    void OnTriggerEnter(Collider other)
    {
        if (isRagdolled) return;

        if (other.CompareTag(vehicleTag))
        {
            Rigidbody carRb = other.attachedRigidbody;
            if (carRb == null) return;

            float carSpeed = carRb.linearVelocity.magnitude;

            if (carSpeed < minSpeedToRagdoll)
            {
                // optional minor hit feedback
                return;
            }

            Vector3 impactDirection = (carRb.linearVelocity.normalized + (Vector3.up * upwardForceComponent)).normalized;
            float impactMagnitude = baseImpactForce + (carSpeed * speedToForceMultiplier);
            Vector3 finalForce = impactDirection * impactMagnitude;
            Vector3 impactPoint = other.ClosestPoint(transform.position);

            ActivateRagdoll(finalForce, impactPoint);
        }
    }

    void OnDrawGizmosSelected()
    {
        // visualize obstacle and push checks with Y offsets
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position + Vector3.up * obstacleDetectYOffset, obstacleDetectRadius);

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position + Vector3.up * pushDetectYOffset, pushDetectRadius);

        // optional: visualize player's sampling radius for stuck recovery
        Gizmos.color = new Color(0.2f, 0.6f, 1f, 0.15f);
        Gizmos.DrawWireSphere(player != null ? player.position : transform.position, stuckRecoveryRadius);
    }
}
