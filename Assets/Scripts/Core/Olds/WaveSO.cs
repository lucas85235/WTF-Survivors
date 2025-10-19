using UnityEngine;

[CreateAssetMenu(fileName = "New Wave", menuName = "Waves/Enemy Wave")]
public class WaveSO : ScriptableObject
{
    [Header("Wave Configuration")]
    [Tooltip("The enemy prefab to spawn in this wave.")]
    public GameObject EnemyPrefab;

    [Tooltip("The total number of enemies to spawn in this wave.")]
    public int EnemyCount = 10;

    [Tooltip("The rate at which enemies spawn, in enemies per second.")]
    public float SpawnRate = 2f;
}
