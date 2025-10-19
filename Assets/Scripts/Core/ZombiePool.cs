using UnityEngine;
using System.Collections.Generic;

public class ZombiePool : MonoBehaviour
{
    [SerializeField] private GameObject zombiePrefab;
    [SerializeField] private int initialPoolSize = 50;
    [SerializeField] private int maxPoolSize = 200;
    
    private Stack<EnemyAI> availableZombies = new Stack<EnemyAI>();
    private HashSet<EnemyAI> activeZombies = new HashSet<EnemyAI>();

    public static ZombiePool Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        InitializePool();
    }

    private void InitializePool()
    {
        for (int i = 0; i < initialPoolSize; i++)
        {
            CreateZombieInstance();
        }
    }

    private EnemyAI CreateZombieInstance()
    {
        GameObject zombieObj = Instantiate(zombiePrefab, transform);
        EnemyAI agent = zombieObj.GetComponent<EnemyAI>();
        agent.gameObject.SetActive(false);
        availableZombies.Push(agent);
        return agent;
    }

    public EnemyAI SpawnZombie(Vector3 position, float difficulty)
    {
        EnemyAI zombie;

        if (availableZombies.Count > 0)
        {
            zombie = availableZombies.Pop();
        }
        else if (activeZombies.Count < maxPoolSize)
        {
            zombie = CreateZombieInstance();
        }
        else
        {
            // Mata o zumbi mais fraco se pool está cheio
            var weakest = GetWeakestZombie();
            if (weakest != null)
            {
                ReturnZombie(weakest);
                zombie = availableZombies.Pop();
            }
            else
            {
                return null;
            }
        }

        zombie.Initialize(position, difficulty);
        zombie.gameObject.SetActive(true);
        activeZombies.Add(zombie);
        return zombie;
    }

    public void ReturnZombie(EnemyAI zombie)
    {
        if (zombie == null) return;
        
        activeZombies.Remove(zombie);
        zombie.gameObject.SetActive(false);
        availableZombies.Push(zombie);
    }

    private EnemyAI GetWeakestZombie()
    {
        EnemyAI weakest = null;
        float minHealth = float.MaxValue;

        foreach (var zombie in activeZombies)
        {
            if (zombie.Health < minHealth)
            {
                minHealth = zombie.Health;
                weakest = zombie;
            }
        }

        return weakest;
    }

    public int GetActiveZombieCount() => activeZombies.Count;
    public int GetAvailablePoolCount() => availableZombies.Count;
}
