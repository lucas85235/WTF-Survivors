using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Enemy AI that follows the player using a NavMeshAgent and handles
/// obstacle avoidance, push-by-car behavior, and NavMesh recovery.
/// Logic preserved from the original Portuguese version.
/// </summary>
public class EnemyAI : MonoBehaviour
{
    [Header("References")]
    public Transform player;
    private NavMeshAgent navMeshAgent;
    private Rigidbody rb;

    [Header("Movement")]
    public float baseSpeed = 5f;
    public float maxSpeed = 7f;
    public float stoppingDistance = 2f;

    [Header("Behavior")]
    public float detectionDistance = 50f;
    public float pathUpdateInterval = 0.3f;
    private float nextUpdateTime = 0f;

    [Header("Obstacle Collision")]
    public float obstacleDetectRadius = 1.5f;
    public float escapeForce = 10f;
    public float maxEscapeTime = 0.8f;
    private float currentEscapeTime = 0f;
    private bool isEscaping = false;

    [Header("Car Push")]
    public float pushForce = 25f;
    public float pushDetectRadius = 2.5f;
    public float minPushTime = 0.5f;
    private bool isPushed = false;
    private float currentPushTime = 0f;

    [Header("NavMesh Recovery")]
    public float minNavMeshSpeed = 0.1f;
    public float navMeshRecoveryTime = 0.6f;

    private Vector3 currentEscapeDirection = Vector3.zero;
    private NavMeshPath currentPath;

    void Start()
    {
        navMeshAgent = GetComponent<NavMeshAgent>();
        rb = GetComponent<Rigidbody>();

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

        if (rb == null)
        {
            Debug.LogError("EnemyAI: Rigidbody not found on " + gameObject.name);
            enabled = false;
            return;
        }

        // NavMeshAgent configuration for stability
        navMeshAgent.updateRotation = false;
        navMeshAgent.updateUpAxis = false;
        navMeshAgent.stoppingDistance = stoppingDistance;

        // Rigidbody configuration
        rb.useGravity = true;
        rb.constraints = RigidbodyConstraints.FreezeRotation;
        rb.isKinematic = false;

        currentPath = new NavMeshPath();
    }

    void Update()
    {
        if (player == null || navMeshAgent == null || rb == null)
            return;

        // Validate that the enemy is on the NavMesh
        if (!navMeshAgent.isOnNavMesh)
        {
            Debug.LogWarning("EnemyAI: " + gameObject.name + " is not on the NavMesh!");
            return;
        }

        float distanceToPlayer = Vector3.Distance(transform.position, player.position);

        // Deactivate if the player is too far
        if (distanceToPlayer > detectionDistance)
        {
            if (navMeshAgent.hasPath)
                navMeshAgent.velocity = Vector3.zero;
            return;
        }

        // Detect pushes from car/player
        DetectCarPush();

        // Manage push timer
        if (isPushed)
        {
            currentPushTime -= Time.deltaTime;
            if (currentPushTime <= 0)
                isPushed = false;
        }

        // Update path periodically
        if (Time.time >= nextUpdateTime && !isPushed)
        {
            if (distanceToPlayer > stoppingDistance && navMeshAgent.isOnNavMesh)
            {
                if (NavMesh.CalculatePath(transform.position, player.position, NavMesh.AllAreas, currentPath))
                {
                    if (currentPath.status == NavMeshPathStatus.PathComplete)
                    {
                        navMeshAgent.SetPath(currentPath);
                    }
                }
            }
            else if (distanceToPlayer <= stoppingDistance)
            {
                navMeshAgent.velocity = Vector3.zero;
            }

            nextUpdateTime = Time.time + pathUpdateInterval;
        }

        // Manage movement and obstacle escape
        if (!isPushed)
            UpdateMovement();
    }

    void UpdateMovement()
    {
        if (!navMeshAgent.isOnNavMesh)
            return;

        // Decrease escape timer
        if (isEscaping)
        {
            currentEscapeTime -= Time.deltaTime;
            if (currentEscapeTime <= 0)
                isEscaping = false;
        }

        // Detect nearby obstacles
        DetectObstacles();

        // Apply movement
        if (navMeshAgent.hasPath && navMeshAgent.remainingDistance > stoppingDistance)
        {
            if (isEscaping)
            {
                // Escape movement with validation
                Vector3 escapeVelocity = currentEscapeDirection * maxSpeed;
                if (float.IsNaN(escapeVelocity.x) || float.IsNaN(escapeVelocity.y) || float.IsNaN(escapeVelocity.z))
                    escapeVelocity = Vector3.zero;

                navMeshAgent.velocity = escapeVelocity;
            }
            else
            {
                // Normal movement
                Vector3 dir = navMeshAgent.desiredVelocity.normalized;

                if (float.IsNaN(dir.x) || float.IsNaN(dir.y) || float.IsNaN(dir.z))
                    dir = Vector3.zero;

                navMeshAgent.velocity = dir * baseSpeed;
            }
        }
        else
        {
            navMeshAgent.velocity = Vector3.zero;
        }
    }

    void DetectObstacles()
    {
        if (!navMeshAgent.isOnNavMesh)
            return;

        Collider[] obstacles = Physics.OverlapSphere(transform.position, obstacleDetectRadius);

        if (obstacles.Length > 0 && !isEscaping)
        {
            Vector3 fleeDirection = Vector3.zero;
            int obstacleCount = 0;

            foreach (Collider col in obstacles)
            {
                // Ignore self
                if (col.gameObject == gameObject)
                    continue;

                // Ignore the player
                if (col.transform == player)
                    continue;

                // Calculate direction opposite to obstacle
                Vector3 obstacleDir = (transform.position - col.transform.position).normalized;
                fleeDirection += obstacleDir;
                obstacleCount++;
            }

            if (obstacleCount > 1)
            {
                fleeDirection = fleeDirection.normalized;

                // Validate flee direction
                if (!float.IsNaN(fleeDirection.x) && !float.IsNaN(fleeDirection.y) && !float.IsNaN(fleeDirection.z))
                {
                    currentEscapeDirection = fleeDirection;
                    isEscaping = true;
                    currentEscapeTime = maxEscapeTime;
                }
            }
        }
    }

    void DetectCarPush()
    {
        if (isPushed || rb == null)
            return;

        Collider[] cols = Physics.OverlapSphere(transform.position, pushDetectRadius);

        foreach (Collider col in cols)
        {
            if (col.CompareTag("Player"))
            {
                Vector3 pushDir = (transform.position - col.transform.position).normalized;
                pushDir.y = 0.3f;

                // Validate direction
                if (float.IsNaN(pushDir.x) || float.IsNaN(pushDir.y) || float.IsNaN(pushDir.z))
                    pushDir = Vector3.forward;

                // Disable NavMeshAgent
                if (navMeshAgent.isOnNavMesh)
                    navMeshAgent.enabled = false;

                // Apply force
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.isKinematic = false;
                rb.AddForce(pushDir * pushForce, ForceMode.Impulse);

                // Activate push flag
                isPushed = true;
                currentPushTime = minPushTime;

                // Schedule NavMesh re-enable
                CancelInvoke(nameof(ReenableNavMesh));
                Invoke(nameof(ReenableNavMesh), navMeshRecoveryTime);

                break;
            }
        }
    }

    void ReenableNavMesh()
    {
        if (navMeshAgent == null || rb == null)
            return;

        // Validate conditions before re-enabling
        float currentSpeed = rb.linearVelocity.magnitude;
        bool isNearGround = Physics.Raycast(transform.position, Vector3.down, 1.5f);

        // Only re-enable if nearly stopped AND near ground
        if (currentSpeed < minNavMeshSpeed && isNearGround)
        {
            // Check for a valid position on the NavMesh
            if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 2f, NavMesh.AllAreas))
            {
                navMeshAgent.enabled = true;
                navMeshAgent.Warp(hit.position);
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                isEscaping = false;
            }
        }
        else
        {
            // Try again shortly
            Invoke(nameof(ReenableNavMesh), 0.2f);
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, detectionDistance);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, obstacleDetectRadius);

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, pushDetectRadius);
    }
}
