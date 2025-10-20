using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(AudioSource))]
public class EnemyAI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Animator animator;
    [SerializeField] private NavMeshAgent navMeshAgent;
    [SerializeField] private Rigidbody mainRigidbody;
    [SerializeField] private Collider mainCollider;
    [SerializeField] private GameObject[] models;

    [Header("Movement")]
    [SerializeField] private float baseSpeed = 5f;
    [SerializeField] private float stoppingDistance = 2f;
    [SerializeField] private float updateInterval = 0.3f;
    [SerializeField] private float rotationSpeed = 10f;

    [Header("Car Detection")]
    [SerializeField] private float detectionRadius = 2.5f;
    [SerializeField] private float pushForce = 25f;
    [SerializeField] private float minRagdollSpeed = 5f;

    [Header("Ragdoll")]
    [SerializeField] private string playerTag = "Player";
    [SerializeField] private float impactForce = 50f;
    [SerializeField] private float speedMultiplier = 2f;

    [Header("Animator Parameters")]
    [SerializeField] private string moveSpeedParam = "MoveSpeed";
    [SerializeField] private string AnimationSpeed = "AnimationSpeed";

    [Header("Audio")]
    [Tooltip("SFX source for non-spatial or localized sounds")]
    [SerializeField] private AudioSource sfxSource;
    [Tooltip("Clips used for hard impacts (cars) - played at impact point")]
    [SerializeField] private AudioClip[] impactClips;
    [Tooltip("Clips for light hits / pushes")]
    [SerializeField] private AudioClip[] pushClips;
    [Tooltip("Zombie hurt / grunt variations")]
    [SerializeField] private AudioClip[] hurtClips;
    [Tooltip("Death / large ragdoll sounds")]
    [SerializeField] private AudioClip[] deathClips;
    [Range(0f, 0.5f)]
    [SerializeField] private float pitchVariance = 0.08f;
    [Range(0f, 1f)]
    [SerializeField] private float sfxVolume = 1f;

    private List<Rigidbody> ragdollBones = new List<Rigidbody>();
    private Transform player;
    private PlayerHealth playerHealth;
    private bool isRagdolled = false;
    private bool isPushed = false;
    private float nextUpdateTime = 0f;
    private float currentDifficulty = 1f;
    private float currentHealth = 30f;
    private bool isInitialized = false;

    public float Health => currentHealth;

    void OnEnable()
    {
        if (!isInitialized)
            return;

        if (navMeshAgent != null)
            navMeshAgent.enabled = true;

        ResetEnemy();
    }

    void Awake()
    {
        navMeshAgent = GetComponent<NavMeshAgent>();
        mainRigidbody = GetComponent<Rigidbody>();
        mainCollider = GetComponent<Collider>();

        if (animator == null)
            animator = GetComponent<Animator>();

        // get or add audio source (used for local SFX)
        if (sfxSource == null)
        {
            sfxSource = GetComponent<AudioSource>();
            if (sfxSource == null)
                sfxSource = gameObject.AddComponent<AudioSource>();
        }
        // prefer 3D sound for sfxSource if used for spatial cues
        sfxSource.spatialBlend = 0.0f; // 0 = 2D by default; keep 0 for non-positional one-shots
        // if you want the source to be spatial: sfxSource.spatialBlend = 1f;

        CollectRagdollBones();

        if (navMeshAgent == null || mainRigidbody == null)
        {
            Debug.LogError("EnemyAIPooled: NavMeshAgent ou Rigidbody faltando em " + gameObject.name);
            enabled = false;
            return;
        }

        isInitialized = true;

        int randomModelIndex = Random.Range(0, models.Length);
        for (int i = models.Length - 1; i >= 0; i--)
        {
            if (i == randomModelIndex)
            {
                models[i].SetActive(true);
            }
            else Destroy(models[i]);
        }
        models = null;
    }

    void CollectRagdollBones()
    {
        ragdollBones.Clear();
        foreach (var rb in GetComponentsInChildren<Rigidbody>())
        {
            if (rb != mainRigidbody)
            {
                ragdollBones.Add(rb);
                rb.isKinematic = true;
            }
        }
    }

    public void Initialize(Vector3 position, float difficulty)
    {
        player = GameObject.FindGameObjectWithTag("Player")?.transform;

        if (player == null)
        {
            Debug.LogWarning("Player not found!");
            return;
        }

        playerHealth = player.GetComponent<PlayerHealth>();

        currentDifficulty = difficulty;
        currentHealth = 30f * difficulty;

        transform.position = position;
        transform.rotation = Quaternion.identity;

        if (navMeshAgent != null)
        {
            navMeshAgent.stoppingDistance = stoppingDistance;
            navMeshAgent.speed = baseSpeed * (0.8f + difficulty * 0.2f);
            navMeshAgent.updateRotation = false;
            navMeshAgent.updateUpAxis = false;
        }

        if (mainRigidbody != null)
        {
            mainRigidbody.useGravity = true;
            mainRigidbody.isKinematic = false;
            mainRigidbody.constraints = RigidbodyConstraints.FreezeRotation;
            mainRigidbody.linearVelocity = Vector3.zero;
            mainRigidbody.angularVelocity = Vector3.zero;
        }

        if (mainCollider != null)
            mainCollider.enabled = true;

        isRagdolled = false;
        isPushed = false;
        nextUpdateTime = 0f;

        foreach (var bone in ragdollBones)
        {
            bone.isKinematic = true;
            bone.linearVelocity = Vector3.zero;
            bone.angularVelocity = Vector3.zero;
        }

        if (animator != null)
        {
            animator.enabled = true;
            animator.SetFloat(moveSpeedParam, 0f);
        }

        StopAllCoroutines();
    }

    private void ResetEnemy()
    {
        if (isRagdolled)
        {
            isRagdolled = false;
            isPushed = false;

            if (navMeshAgent != null)
                navMeshAgent.enabled = true;

            if (animator != null)
                animator.enabled = true;

            if (mainCollider != null)
                mainCollider.enabled = true;

            if (mainRigidbody != null)
                mainRigidbody.isKinematic = false;

            foreach (var bone in ragdollBones)
            {
                bone.isKinematic = true;
                bone.linearVelocity = Vector3.zero;
                bone.angularVelocity = Vector3.zero;
            }
        }
    }

    void Update()
    {
        if (isRagdolled || player == null || navMeshAgent == null)
            return;

        if (!navMeshAgent.isOnNavMesh)
            return;

        if (Time.time >= nextUpdateTime && !isPushed)
        {
            if (!navMeshAgent.pathPending)
            {
                navMeshAgent.SetDestination(player.position);
            }
            nextUpdateTime = Time.time + updateInterval;
        }

        if (!isPushed)
            RotateTowardsTarget(player.position);

        UpdateAnimator();

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
        animator.SetFloat(AnimationSpeed, baseSpeed * currentDifficulty);

        if (isPushed)
        {
            animator.SetTrigger("Attack");
        }
    }

    void DetectCarPush()
    {
        if (isPushed || isRagdolled)
            return;

        Vector3 checkPosition = transform.position + Vector3.up * 0.3f;
        Collider[] colliders = Physics.OverlapSphere(checkPosition, detectionRadius);

        foreach (Collider col in colliders)
        {
            if (col.CompareTag(playerTag))
            {
                Rigidbody carRb = col.attachedRigidbody;
                if (carRb == null) continue;

                float carSpeed = carRb.linearVelocity.magnitude;

                if (carSpeed >= minRagdollSpeed)
                {
                    Vector3 impactDirection = carRb.linearVelocity.normalized + Vector3.up * 0.5f;
                    float magnitude = impactForce + (carSpeed * speedMultiplier);
                    Vector3 finalForce = impactDirection.normalized * magnitude;
                    Vector3 impactPoint = col.ClosestPoint(transform.position);

                    // play heavy impact at point (spatial)
                    PlayImpactAtPoint(impactClips, impactPoint, Mathf.Clamp01(carSpeed / 20f + 0.5f));
                    ActivateRagdoll(finalForce, impactPoint);
                    return;
                }
                else
                {
                    playerHealth?.TakeDamage(1);
                }

                // play a light push sound
                PlayLocalOneShot(pushClips);
                PushEnemy(col, carSpeed);
                return;
            }
        }
    }

    void PushEnemy(Collider col, float carSpeed)
    {
        if (navMeshAgent == null || mainRigidbody == null) return;

        navMeshAgent.isStopped = true;
        navMeshAgent.updatePosition = false;

        Vector3 pushDir = (transform.position - col.transform.position).normalized;
        pushDir.y = 0.1f;

        mainRigidbody.isKinematic = false;
        mainRigidbody.linearVelocity = Vector3.zero;
        mainRigidbody.AddForce(pushDir * pushForce, ForceMode.Impulse);

        // small push grunt
        PlayLocalOneShot(pushClips);

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

        if (mainRigidbody != null)
        {
            mainRigidbody.linearVelocity = Vector3.zero;
            mainRigidbody.isKinematic = true;
        }

        if (navMeshAgent != null && NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 2f, NavMesh.AllAreas))
        {
            navMeshAgent.Warp(hit.position);
        }

        if (navMeshAgent != null)
        {
            navMeshAgent.updatePosition = true;
            navMeshAgent.isStopped = false;
        }

        isPushed = false;
    }

    public void ActivateRagdoll(Vector3 force, Vector3 impactPoint)
    {
        if (isRagdolled) return;
        isRagdolled = true;

        if (navMeshAgent != null)
            navMeshAgent.enabled = false;

        if (animator != null)
            animator.enabled = false;

        if (mainCollider != null)
            mainCollider.enabled = false;

        if (mainRigidbody != null)
            mainRigidbody.isKinematic = true;

        Rigidbody closestBone = null;
        float smallestDist = float.MaxValue;

        foreach (var bone in ragdollBones)
        {
            bone.isKinematic = false;
            bone.linearVelocity = Vector3.zero;
            bone.angularVelocity = Vector3.zero;

            float dist = Vector3.Distance(bone.position, impactPoint);
            if (dist < smallestDist)
            {
                smallestDist = dist;
                closestBone = bone;
            }
        }

        // heavy ragdoll impact sound at impactPoint
        PlayImpactAtPoint(impactClips, impactPoint, 1f);

        if (closestBone != null)
            closestBone.AddForceAtPosition(force, impactPoint, ForceMode.Impulse);

        // optional death sound/voice
        PlayImpactAtPoint(deathClips, transform.position, 0.8f);

        ComboScoreSystem.AddScore(10);

        StartCoroutine(ReturnToPool());
    }

    IEnumerator ReturnToPool()
    {
        yield return new WaitForSeconds(3f);
        ZombiePool.Instance.ReturnZombie(this);
    }

    void OnTriggerEnter(Collider other)
    {
        if (isRagdolled) return;

        if (other.CompareTag(playerTag))
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

                PlayImpactAtPoint(impactClips, impactPoint, 1f);
                ActivateRagdoll(finalForce, impactPoint);
            }
        }

        if (other.CompareTag("Bullet"))
        {
            // bullet hit sound (small) at contact point
            Vector3 hitPoint = other.ClosestPoint(transform.position);
            PlayImpactAtPoint(hurtClips, hitPoint, 0.6f);

            ActivateRagdoll(other.transform.forward * 10f, hitPoint);
            Destroy(other.gameObject);
        }
    }

    public void TakeDamage(float damage)
    {
        currentHealth -= damage;

        // play hurt grunt with variation
        PlayLocalOneShot(hurtClips);

        if (currentHealth <= 0)
        {
            // death sound right before returning to pool
            PlayLocalOneShot(deathClips);
            ZombiePool.Instance.ReturnZombie(this);
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.3f, detectionRadius);
    }

    #region Audio Helpers
    // plays a random clip from array on the local sfxSource (non-positional by default)
    private void PlayLocalOneShot(AudioClip[] clips, float volume = -1f)
    {
        if (clips == null || clips.Length == 0 || sfxSource == null) return;
        AudioClip clip = clips[Random.Range(0, clips.Length)];
        float vol = (volume < 0f) ? sfxVolume : volume;
        float prevPitch = sfxSource.pitch;
        sfxSource.pitch = Random.Range(1f - pitchVariance, 1f + pitchVariance);
        sfxSource.PlayOneShot(clip, vol);
        sfxSource.pitch = prevPitch;
    }

    // plays a random clip at world position (spatial). uses built-in PlayClipAtPoint (no pitch variance).
    private void PlayImpactAtPoint(AudioClip[] clips, Vector3 point, float volume = 1f)
    {
        if (clips == null || clips.Length == 0) return;
        AudioClip clip = clips[Random.Range(0, clips.Length)];
        AudioSource.PlayClipAtPoint(clip, point, Mathf.Clamp01(volume * sfxVolume));
    }
    #endregion
}
