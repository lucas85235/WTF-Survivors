using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Manages the spawning of enemy waves based on a list of WaveSO assets.
/// It tracks the state of waves and notifies other systems via UnityEvents.
/// </summary>
public class WaveSpawnerOld : MonoBehaviour
{
    // Enum to manage the spawner's state machine.
    private enum SpawnerState { Idle, SpawningWave, WaitingForNextWave }

    [Header("Wave Configuration")]
    [Tooltip("A list of WaveSO assets that defines the sequence of enemy waves.")]
    [SerializeField] private List<WaveSO> _waves;

    [Header("Spawner Settings")]
    [Tooltip("A list of points where enemies can be spawned.")]
    [SerializeField] private Transform[] _spawnPoints;
    [Tooltip("The time in seconds to wait between completing one wave and starting the next.")]
    [SerializeField] private float _timeBetweenWaves = 5.0f;

    [Header("Events")]
    [Tooltip("Fired when a new wave starts. Passes the wave number (e.g., 1, 2, 3).")]
    public UnityEvent<int> OnWaveStarted;
    [Tooltip("Fired when all waves are successfully completed.")]
    public UnityEvent OnAllWavesCompleted;

    private int _currentWaveIndex = -1;
    private int _enemiesRemainingAlive;
    private float _nextWaveTimer;
    private SpawnerState _currentState = SpawnerState.Idle;

    private void Start()
    {
        // Start the process
        StartNextWave();
    }

    private void Update()
    {
        // State machine logic
        if (_currentState == SpawnerState.WaitingForNextWave)
        {
            // If the wave is clear and we are waiting, start a countdown for the next one.
            if (_enemiesRemainingAlive <= 0)
            {
                _nextWaveTimer -= Time.deltaTime;
                if (_nextWaveTimer <= 0)
                {
                    StartNextWave();
                }
            }
        }
    }

    /// <summary>
    /// Begins the next wave in the sequence.
    /// </summary>
    private void StartNextWave()
    {
        _currentWaveIndex++;

        if (_currentWaveIndex >= _waves.Count)
        {
            // All waves are completed.
            _currentState = SpawnerState.Idle;
            Debug.Log("All waves completed!");
            OnAllWavesCompleted?.Invoke();
            return;
        }

        WaveSO currentWave = _waves[_currentWaveIndex];
        _enemiesRemainingAlive = currentWave.EnemyCount;
        _nextWaveTimer = _timeBetweenWaves;
        _currentState = SpawnerState.SpawningWave;

        Debug.Log($"Starting Wave {_currentWaveIndex + 1}...");
        OnWaveStarted?.Invoke(_currentWaveIndex + 1);
        StartCoroutine(SpawnWaveCoroutine(currentWave));
    }

    /// <summary>
    /// Coroutine that handles spawning enemies over time based on the wave's properties.
    /// </summary>
    private IEnumerator SpawnWaveCoroutine(WaveSO wave)
    {
        if (_spawnPoints.Length == 0)
        {
            Debug.LogError("No spawn points assigned to the WaveSpawner!");
            yield break; // Stop the coroutine if there's nowhere to spawn enemies.
        }

        for (int i = 0; i < wave.EnemyCount; i++)
        {
            // Spawn one enemy.
            SpawnEnemy(wave.EnemyPrefab);

            // Wait for the specified time before spawning the next one.
            yield return new WaitForSeconds(1f / wave.SpawnRate);
        }

        // After all enemies are spawned, transition to the waiting state.
        _currentState = SpawnerState.WaitingForNextWave;
    }

    private void SpawnEnemy(GameObject enemyPrefab)
    {
        // Choose a random spawn point from the list.
        Transform randomSpawnPoint = _spawnPoints[Random.Range(0, _spawnPoints.Length)];

        // Instantiate the enemy.
        GameObject enemyInstance = Instantiate(enemyPrefab, randomSpawnPoint.position, randomSpawnPoint.rotation);

        // Subscribe to the enemy's death event.
        // Enemy enemyScript = enemyInstance.GetComponent<Enemy>();
        // if (enemyScript != null)
        // {
        //     enemyScript.OnDeath += OnEnemyDied;
        // }
    }

    /// <summary>
    /// Callback method that is triggered when an enemy dies.
    /// </summary>
    // private void OnEnemyDied(Enemy enemy)
    // {
    //     _enemiesRemainingAlive--;

    //     // Unsubscribe to prevent memory leaks.
    //     enemy.OnDeath -= OnEnemyDied;
    // }
}
