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
    private Vector3 lastNavMeshPosition;

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

        if (sfxSource == null)
        {
            sfxSource = GetComponent<AudioSource>();
            if (sfxSource == null)
                sfxSource = gameObject.AddComponent<AudioSource>();
        }
        sfxSource.spatialBlend = 0.0f;

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
            navMeshAgent.updateRotation = true;
            navMeshAgent.updatePosition = true;
            navMeshAgent.updateUpAxis = false;
        }

        if (mainRigidbody != null)
        {
            mainRigidbody.useGravity = true;
            mainRigidbody.isKinematic = true; // Inimigo não-ragdollado usa cinemático
            mainRigidbody.constraints = RigidbodyConstraints.FreezeRotation;
            mainRigidbody.linearVelocity = Vector3.zero;
            mainRigidbody.angularVelocity = Vector3.zero;
        }

        if (mainCollider != null)
            mainCollider.enabled = true;

        isRagdolled = false;
        isPushed = false;
        nextUpdateTime = 0f;
        lastNavMeshPosition = position;

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
            {
                navMeshAgent.enabled = true;
                navMeshAgent.updateRotation = true;
                navMeshAgent.updatePosition = true;
                navMeshAgent.isStopped = false;
            }

            if (animator != null)
                animator.enabled = true;

            if (mainCollider != null)
                mainCollider.enabled = true;

            if (mainRigidbody != null)
                mainRigidbody.isKinematic = true;

            foreach (var bone in ragdollBones)
            {
                bone.isKinematic = true;
                bone.linearVelocity = Vector3.zero;
                bone.angularVelocity = Vector3.zero;
            }
        }
    }

    void FixedUpdate()
    {
        if (isRagdolled || player == null || navMeshAgent == null)
            return;

        if (!navMeshAgent.isOnNavMesh)
            return;

        // Atualizar destino periodicamente
        if (Time.time >= nextUpdateTime && !isPushed)
        {
            if (!navMeshAgent.pathPending)
            {
                navMeshAgent.SetDestination(player.position);
            }
            nextUpdateTime = Time.time + updateInterval;
        }

        // Sincronizar posição com NavMeshAgent
        lastNavMeshPosition = navMeshAgent.nextPosition;

        UpdateAnimator();
        DetectCarPush();
    }

    void UpdateAnimator()
    {
        if (animator == null) return;

        float currentSpeed = 0f;
        if (navMeshAgent.isOnNavMesh && !isPushed)
            currentSpeed = navMeshAgent.desiredVelocity.magnitude;

        animator.SetFloat(moveSpeedParam, currentSpeed);
        animator.SetFloat(AnimationSpeed, baseSpeed * currentDifficulty);
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

                    PlayImpactAtPoint(impactClips, impactPoint, Mathf.Clamp01(carSpeed / 20f + 0.5f));
                    ActivateRagdoll(finalForce, impactPoint);
                    return;
                }
                else
                {
                    playerHealth?.TakeDamage(1);
                }

                PlayLocalOneShot(pushClips);
                PushEnemy(col, carSpeed);
                return;
            }
        }
    }

    void PushEnemy(Collider col, float carSpeed)
    {
        if (navMeshAgent == null || mainRigidbody == null) return;

        // Desativar NavMesh temporariamente
        navMeshAgent.isStopped = true;

        Vector3 pushDir = (transform.position - col.transform.position).normalized;
        pushDir.y = 0.1f;

        // Ativar física para o push
        mainRigidbody.isKinematic = false;
        mainRigidbody.linearVelocity = Vector3.zero;
        mainRigidbody.AddForce(pushDir * pushForce, ForceMode.Impulse);

        PlayLocalOneShot(pushClips);

        isPushed = true;
        StartCoroutine(RecoverFromPush());
    }

    IEnumerator RecoverFromPush()
    {
        yield return new WaitForSeconds(0.15f);

        // Aguardar até que a velocidade seja mínima ou timeout
        float timer = 0f;
        float maxWaitTime = 1.5f;
        while (timer < maxWaitTime && mainRigidbody.linearVelocity.magnitude > 0.2f)
        {
            timer += Time.deltaTime;
            yield return null;
        }

        // Resetar para cinemático
        if (mainRigidbody != null)
        {
            mainRigidbody.linearVelocity = Vector3.zero;
            mainRigidbody.angularVelocity = Vector3.zero;
            mainRigidbody.isKinematic = true;
        }

        // Reacomodar no NavMesh
        if (navMeshAgent != null)
        {
            if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 5f, NavMesh.AllAreas))
            {
                navMeshAgent.Warp(hit.position);
            }

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

        PlayImpactAtPoint(impactClips, impactPoint, 1f);

        if (closestBone != null)
            closestBone.AddForceAtPosition(force, impactPoint, ForceMode.Impulse);

        PlayImpactAtPoint(deathClips, transform.position, 0.8f);

        ComboScoreSystem.AddScore(100);

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
            Vector3 hitPoint = other.ClosestPoint(transform.position);
            PlayImpactAtPoint(hurtClips, hitPoint, 0.6f);

            ActivateRagdoll(other.transform.forward * 10f, hitPoint);
            Destroy(other.gameObject);
        }
    }

    public void TakeDamage(float damage)
    {
        currentHealth -= damage;

        PlayLocalOneShot(hurtClips);

        if (currentHealth <= 0)
        {
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

    private void PlayImpactAtPoint(AudioClip[] clips, Vector3 point, float volume = 1f)
    {
        if (clips == null || clips.Length == 0) return;
        AudioClip clip = clips[Random.Range(0, clips.Length)];
        AudioSource.PlayClipAtPoint(clip, point, Mathf.Clamp01(volume * sfxVolume));
    }
    #endregion
}
