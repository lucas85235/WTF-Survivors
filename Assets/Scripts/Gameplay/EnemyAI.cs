using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class EnemyAI : MonoBehaviour
{
    [Header("References")]
    public Transform player;
    public Animator animator;
    private NavMeshAgent navMeshAgent;
    private Rigidbody mainRigidbody;
    private Collider mainCollider;

    [Header("Movement")]
    public float speed = 5f;
    public float stoppingDistance = 2f;
    public float updateInterval = 0.3f;
    public float rotationSpeed = 10f;

    [Header("Car Detection")]
    public float detectionRadius = 2.5f;
    public float pushForce = 25f;
    public float minRagdollSpeed = 5f;

    [Header("Ragdoll")]
    public string carTag = "Car";
    public float impactForce = 50f;
    public float speedMultiplier = 2f;
    public float ragdollCleanupTime = 10f;

    [Header("Animator Parameters")]
    public string moveSpeedParam = "MoveSpeed";

    private List<Rigidbody> ragdollBones = new List<Rigidbody>();
    private bool isRagdolled = false;
    private bool isPushed = false;
    private float nextUpdateTime = 0f;

    void Awake()
    {
        player = GameObject.FindGameObjectWithTag("Player")?.transform;
        navMeshAgent = GetComponent<NavMeshAgent>();
        mainRigidbody = GetComponent<Rigidbody>();
        mainCollider = GetComponent<Collider>();

        // Collect bone rigidbodies for ragdoll
        foreach (var rb in GetComponentsInChildren<Rigidbody>())
        {
            if (rb != mainRigidbody)
            {
                ragdollBones.Add(rb);
                rb.isKinematic = true;
            }
        }
    }

    void Start()
    {
        if (navMeshAgent == null || player == null || mainRigidbody == null)
        {
            Debug.LogError("EnemyAI: Missing components on " + gameObject.name);
            enabled = false;
            return;
        }

        navMeshAgent.stoppingDistance = stoppingDistance;
        navMeshAgent.speed = speed;
        navMeshAgent.updateRotation = false;
        navMeshAgent.updateUpAxis = false;

        mainRigidbody.useGravity = true;
        mainRigidbody.constraints = RigidbodyConstraints.FreezeRotation;
    }

    void Update()
    {
        if (isRagdolled || player == null || navMeshAgent == null)
            return;

        // Chase the player
        if (Time.time >= nextUpdateTime && !isPushed)
        {
            if (!navMeshAgent.pathPending)
            {
                navMeshAgent.SetDestination(player.position);
            }
            nextUpdateTime = Time.time + updateInterval;
        }

        // Rotate towards player
        if (!isPushed)
            RotateTowardsTarget(player.position);

        // Update animator with movement speed
        UpdateAnimator();

        // Detect car push
        DetectCarPush();
    }

    void RotateTowardsTarget(Vector3 targetPos)
    {
        Vector3 direction = (targetPos - transform.position).normalized;
        if (direction.sqrMagnitude > 0.01f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(direction);
            transform.rotation = Quaternion.Lerp(transform.rotation, targetRotation, Time.deltaTime * rotationSpeed);
        }
    }

    void UpdateAnimator()
    {
        if (animator == null) return;

        float currentSpeed = 0f;
        if (navMeshAgent.isOnNavMesh && !isPushed)
            currentSpeed = navMeshAgent.desiredVelocity.magnitude;

        animator.SetFloat(moveSpeedParam, currentSpeed);
    }

    void DetectCarPush()
    {
        if (isPushed || isRagdolled)
            return;

        Vector3 checkPosition = transform.position + Vector3.up * 0.3f;
        Collider[] colliders = Physics.OverlapSphere(checkPosition, detectionRadius);

        foreach (Collider col in colliders)
        {
            if (col.CompareTag(carTag))
            {
                Rigidbody carRb = col.attachedRigidbody;
                if (carRb == null) continue;

                float carSpeed = carRb.linearVelocity.magnitude;

                // If high speed -> ragdoll
                if (carSpeed >= minRagdollSpeed)
                {
                    Vector3 impactDirection = carRb.linearVelocity.normalized + Vector3.up * 0.5f;
                    float magnitude = impactForce + (carSpeed * speedMultiplier);
                    Vector3 finalForce = impactDirection.normalized * magnitude;
                    Vector3 impactPoint = col.ClosestPoint(transform.position);

                    ActivateRagdoll(finalForce, impactPoint);
                    return;
                }

                // Low speed -> simple push
                PushEnemy(col, carSpeed);
                return;
            }
        }
    }

    void PushEnemy(Collider col, float carSpeed)
    {
        navMeshAgent.isStopped = true;
        navMeshAgent.updatePosition = false;

        Vector3 pushDir = (transform.position - col.transform.position).normalized;
        pushDir.y = 0.1f;

        mainRigidbody.isKinematic = false;
        mainRigidbody.linearVelocity = Vector3.zero;
        mainRigidbody.AddForce(pushDir * pushForce, ForceMode.Impulse);

        isPushed = true;
        StartCoroutine(RecoverFromPush());
    }

    IEnumerator RecoverFromPush()
    {
        yield return new WaitForSeconds(0.1f);

        float timer = 0f;
        while (timer < 1f && mainRigidbody.linearVelocity.magnitude > 0.1f)
        {
            timer += 0.05f;
            yield return new WaitForSeconds(0.05f);
        }

        mainRigidbody.linearVelocity = Vector3.zero;
        mainRigidbody.isKinematic = true;

        if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 2f, NavMesh.AllAreas))
        {
            navMeshAgent.Warp(hit.position);
        }

        navMeshAgent.updatePosition = true;
        navMeshAgent.isStopped = false;
        isPushed = false;
    }

    public void ActivateRagdoll(Vector3 force, Vector3 impactPoint)
    {
        if (isRagdolled) return;
        isRagdolled = true;

        navMeshAgent.enabled = false;
        if (animator) animator.enabled = false;
        mainCollider.enabled = false;
        mainRigidbody.isKinematic = true;

        // Enable bones and apply force at the closest bone
        Rigidbody closestBone = null;
        float smallestDist = float.MaxValue;

        foreach (var bone in ragdollBones)
        {
            bone.isKinematic = false;
            float dist = Vector3.Distance(bone.position, impactPoint);
            if (dist < smallestDist)
            {
                smallestDist = dist;
                closestBone = bone;
            }
        }

        if (closestBone != null)
            closestBone.AddForceAtPosition(force, impactPoint, ForceMode.Impulse);

        StartCoroutine(CleanupRagdoll());
    }

    IEnumerator CleanupRagdoll()
    {
        yield return new WaitForSeconds(ragdollCleanupTime);
        Destroy(gameObject);
    }

    void OnTriggerEnter(Collider other)
    {
        if (isRagdolled) return;

        if (other.CompareTag(carTag))
        {
            Rigidbody carRb = other.attachedRigidbody;
            if (carRb == null) return;

            float carSpeed = carRb.linearVelocity.magnitude;

            if (carSpeed >= minRagdollSpeed)
            {
                Vector3 impactDirection = carRb.linearVelocity.normalized + Vector3.up * 0.5f;
                float magnitude = impactForce + (carSpeed * speedMultiplier);
                Vector3 finalForce = impactDirection.normalized * magnitude;
                Vector3 impactPoint = other.ClosestPoint(transform.position);

                ActivateRagdoll(finalForce, impactPoint);
            }
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.3f, detectionRadius);
    }
}
