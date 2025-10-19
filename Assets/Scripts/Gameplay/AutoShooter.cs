using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class AutoShooter : MonoBehaviour
{
    [Header("Atributos de Ataque")]
    [SerializeField] private float attackRange = 10f;
    [SerializeField] private float fireRate = 1f;
    [Tooltip("A largura do cone de tiro. 90 significa 45 graus para cada lado.")]
    [SerializeField] private float fireAngle = 90f;

    [Header("Referências do Unity")]
    [SerializeField] private string enemyTag = "Enemy";
    [SerializeField] private GameObject bulletPrefab;
    [SerializeField] private Transform firePoint;

    [Header("Audio")]
    [SerializeField] private AudioSource sfxSource;
    [SerializeField] private AudioClip[] shootClips;
    [Range(0f, 0.5f)]
    [SerializeField] private float pitchVariance = 0.08f;
    [Range(0f, 1f)]
    [SerializeField] private float sfxVolume = 1f;

    private Collider targetCollider;
    private float fireCountdown = 0f;

    private void Start()
    {
        InvokeRepeating("UpdateTarget", 0f, 0.5f);
    }

    private void Update()
    {
        if (targetCollider == null)
            return;

        if (fireCountdown <= 0f)
        {
            Shoot();
            fireCountdown = 1f / fireRate;
        }

        fireCountdown -= Time.deltaTime;
    }

    private void UpdateTarget()
    {
        GameObject[] enemies = GameObject.FindGameObjectsWithTag(enemyTag);
        float shortestDistance = Mathf.Infinity;
        GameObject nearestEnemy = null;

        foreach (GameObject enemy in enemies)
        {
            float distanceToEnemy = Vector3.Distance(transform.position, enemy.transform.position);

            if (distanceToEnemy < shortestDistance)
            {
                shortestDistance = distanceToEnemy;
                nearestEnemy = enemy;
            }
        }

        if (nearestEnemy != null && shortestDistance <= attackRange)
        {
            Vector3 forwardDirection = transform.forward;
            Vector3 directionToEnemy = (nearestEnemy.transform.position - transform.position).normalized;
            float angleToEnemy = Vector3.Angle(forwardDirection, directionToEnemy);

            if (angleToEnemy <= fireAngle / 2)
            {
                Collider enemyCollider = nearestEnemy.GetComponent<Collider>();

                if (enemyCollider != null)
                {
                    targetCollider = enemyCollider;
                }
                else targetCollider = null;
            }
            else targetCollider = null;
        }
        else targetCollider = null;
    }

    private void Shoot()
    {
        if (bulletPrefab != null && firePoint != null && targetCollider != null)
        {
            Vector3 directionToTarget = (targetCollider.bounds.center - firePoint.position).normalized;
            Quaternion bulletRotation = Quaternion.LookRotation(directionToTarget);
            Instantiate(bulletPrefab, firePoint.position, bulletRotation);
            PlayLocalOneShot(shootClips, sfxVolume);
        }
    }

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

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);

        Gizmos.color = Color.yellow;
        Vector3 leftBoundary = Quaternion.Euler(0, -fireAngle / 2, 0) * transform.forward;
        Vector3 rightBoundary = Quaternion.Euler(0, fireAngle / 2, 0) * transform.forward;

        Gizmos.DrawLine(transform.position, transform.position + leftBoundary * attackRange);
        Gizmos.DrawLine(transform.position, transform.position + rightBoundary * attackRange);
    }
}
