using UnityEngine;

public class WaveSpawner : MonoBehaviour
{
    [SerializeField] private Transform playerTransform;
    [SerializeField] private Transform[] customSpawnPoints = new Transform[0];
    [SerializeField] private float spawnRadius = 5f;
    [SerializeField] private float cameraViewDistance = 35f;
    
    [SerializeField] private float initialSpawnRate = 0.5f;
    [SerializeField] private float maxSpawnRate = 0.1f;
    [SerializeField] private float difficultyIncrement = 0.15f;
    
    [SerializeField] private int initialZombiesPerWave = 3;
    [SerializeField] private int maxZombiesPerWave = 15;
    
    [SerializeField] private float waveDuration = 300f; // 5 minutos
    
    private float spawnTimer = 0f;
    private float currentSpawnRate;
    private float elapsedTime = 0f;
    private float currentDifficulty = 1f;
    private int currentWaveSize = 3;
    private bool waveActive = true;

    private Camera mainCamera;

    private void Start()
    {
        mainCamera = Camera.main;
        currentSpawnRate = initialSpawnRate;
        
        if (customSpawnPoints.Length == 0)
        {
            Debug.LogWarning("Nenhum ponto de spawn foi definido! Adicione pontos ao array 'customSpawnPoints'");
        }
    }

    private void Update()
    {
        if (!waveActive) return;

        elapsedTime += Time.deltaTime;

        // Verifica se a wave terminou
        if (elapsedTime >= waveDuration)
        {
            EndWave();
            return;
        }

        // Aumenta dificuldade progressivamente
        UpdateDifficulty();

        // Spawn de zumbis
        spawnTimer += Time.deltaTime;
        if (spawnTimer >= currentSpawnRate)
        {
            SpawnZombie();
            spawnTimer = 0f;
        }
    }

    private void UpdateDifficulty()
    {
        float progress = elapsedTime / waveDuration;
        currentDifficulty = 1f + (progress * 2f); // De 1 para 3
        
        // Aumenta velocidade de spawn
        currentSpawnRate = Mathf.Lerp(initialSpawnRate, maxSpawnRate, progress);
        
        // Aumenta quantidade de zumbis por onda
        currentWaveSize = Mathf.RoundToInt(Mathf.Lerp(initialZombiesPerWave, maxZombiesPerWave, progress));
    }

    private void SpawnZombie()
    {
        Vector3 spawnPos = GetValidSpawnPosition();
        if (spawnPos != Vector3.zero)
        {
            ZombiePool.Instance.SpawnZombie(spawnPos, currentDifficulty);
        }
    }

    private Vector3 GetValidSpawnPosition()
    {
        if (customSpawnPoints.Length == 0)
            return Vector3.zero;

        // Tenta encontrar um ponto de spawn válido
        for (int attempts = 0; attempts < customSpawnPoints.Length; attempts++)
        {
            int randomIndex = Random.Range(0, customSpawnPoints.Length);
            Transform spawnPoint = customSpawnPoints[randomIndex];
            
            if (spawnPoint == null) continue;

            Vector3 spawnPos = spawnPoint.position;
            spawnPos.y = GetGroundHeight(spawnPos);

            if (!IsInViewFrustum(spawnPos))
            {
                return spawnPos;
            }
        }

        return GetFarthestSpawnPoint();
    }

    private Vector3 GetFarthestSpawnPoint()
    {
        if (customSpawnPoints.Length == 0)
            return Vector3.zero;

        Transform farthestPoint = customSpawnPoints[0];
        float maxDistance = 0f;

        foreach (Transform point in customSpawnPoints)
        {
            if (point == null) continue;
            
            float distance = Vector3.Distance(playerTransform.position, point.position);
            if (distance > maxDistance)
            {
                maxDistance = distance;
                farthestPoint = point;
            }
        }

        Vector3 spawnPos = farthestPoint.position + Random.insideUnitSphere * spawnRadius;
        spawnPos.y = GetGroundHeight(spawnPos);
        
        return spawnPos;
    }

    private bool IsInViewFrustum(Vector3 position)
    {
        Vector3 screenPos = mainCamera.WorldToViewportPoint(position);
        
        // Dentro da câmera
        if (screenPos.x > 0 && screenPos.x < 1 && screenPos.y > 0 && screenPos.y < 1 && screenPos.z > 0)
        {
            return true;
        }

        // Muito perto do player
        if (Vector3.Distance(position, playerTransform.position) < cameraViewDistance)
        {
            return true;
        }

        return false;
    }

    private float GetGroundHeight(Vector3 position)
    {
        if (Physics.Raycast(position + Vector3.up * 100f, Vector3.down, out RaycastHit hit, 200f))
        {
            return hit.point.y;
        }
        return 0f;
    }

    private void EndWave()
    {
        waveActive = false;
        Debug.Log($"Wave finalizada! Zumbis sobreviventes: {ZombiePool.Instance.GetActiveZombieCount()}");
    }

    public float GetTimeRemaining() => waveDuration - elapsedTime;
    public float GetDifficulty() => currentDifficulty;
    public bool IsWaveActive() => waveActive;
}