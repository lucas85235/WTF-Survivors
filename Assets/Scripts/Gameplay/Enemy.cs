using System;
using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.AI; // Importante para o NavMeshAgent

/// <summary>
/// Manages enemy behavior using a state machine and NavMeshAgent for movement.
/// Includes a robust ragdoll system for vehicle collisions.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class Enemy : MonoBehaviour
{
    private enum EnemyState { Walking, Attacking, Ragdolled }

    [Header("Stats")]
    [SerializeField] private int _maxHealth = 100;
    [SerializeField] private int _attackDamage = 10;
    [SerializeField] private float _attackRange = 1.5f;
    [SerializeField] private float _attackCooldown = 2f;

    [Header("Setup")]
    [SerializeField] private string _playerTag = "Player";
    [SerializeField] private Animator _animator;

    [Header("Ragdoll Settings")]
    [SerializeField] private string _vehicleTag = "Car";
    [SerializeField] private float _minSpeedToRagdoll = 5f;
    [SerializeField] private float _baseImpactForce = 50f;
    [SerializeField] private float _upwardForceComponent = 0.5f;
    [SerializeField] private float _speedToForceMultiplier = 2f;
    [SerializeField] private float _impactForceMultiplier = 15f;
    [SerializeField] private float _ragdollCleanupTime = 10f;

    public event Action<Enemy> OnDeath;

    // --- State & Component References ---
    private EnemyState _currentState;
    private Transform _playerTransform;
    // private PlayerHealth _playerHealth;
    private float _nextAttackTime = 0f;

    private NavMeshAgent _navAgent;
    private Rigidbody _mainRigidbody; // O Rigidbody na raiz do objeto
    private Collider _mainCollider;
    private List<Rigidbody> _ragdollRigidbodies = new List<Rigidbody>();

    private void Awake()
    {
        // --- Pegar todos os componentes necessários ---
        _navAgent = GetComponent<NavMeshAgent>();
        _mainRigidbody = GetComponent<Rigidbody>();
        _mainCollider = GetComponent<Collider>();

        // Encontrar e configurar os ossos do ragdoll
        foreach (var rb in GetComponentsInChildren<Rigidbody>())
        {
            if (rb != _mainRigidbody)
            {
                _ragdollRigidbodies.Add(rb);
                rb.isKinematic = true; // Garantir que o ragdoll comece desativado
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            }
        }
    }

    private void Start()
    {
        _currentState = EnemyState.Walking;
        GameObject playerObject = GameObject.FindGameObjectWithTag(_playerTag);
        if (playerObject != null)
        {
            _playerTransform = playerObject.transform;
            // _playerHealth = playerObject.GetComponent<PlayerHealth>();
        }
        else
        {
            Debug.LogError("Inimigo não encontrou o jogador! Verifique a tag do jogador.");
            enabled = false;
        }
    }

    private void Update()
    {
        // A máquina de estados controla o que o inimigo faz
        switch (_currentState)
        {
            case EnemyState.Walking:
                HandleWalkingState();
                break;
            case EnemyState.Attacking:
                HandleAttackingState();
                break;
            case EnemyState.Ragdolled:
                // Não faz nada, a física está no controle
                break;
        }
    }

    private void HandleWalkingState()
    {
        // Dizer ao NavMeshAgent para seguir o jogador
        if (_playerTransform != null)
        {
            _navAgent.SetDestination(_playerTransform.position);
        }

        // Animação de caminhada baseada na velocidade do NavMeshAgent
        _animator.SetFloat("Speed", _navAgent.velocity.magnitude);

        // Transição para o estado de ataque se estiver perto o suficiente
        if (Vector3.Distance(transform.position, _playerTransform.position) <= _attackRange)
        {
            _currentState = EnemyState.Attacking;
        }
    }

    private void HandleAttackingState()
    {
        // Parar de se mover para atacar
        _navAgent.SetDestination(transform.position);
        _animator.SetFloat("Speed", 0);

        // Olhar para o jogador
        transform.LookAt(_playerTransform.position);

        if (Time.time >= _nextAttackTime)
        {
            _animator.SetTrigger("Attack"); // Dispara uma animação de ataque
            // _playerHealth?.TakeDamage(_attackDamage);
            _nextAttackTime = Time.time + _attackCooldown;
        }

        // Voltar a andar se o jogador fugir
        if (Vector3.Distance(transform.position, _playerTransform.position) > _attackRange)
        {
            _currentState = EnemyState.Walking;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_currentState == EnemyState.Ragdolled) return;

        if (other.CompareTag(_vehicleTag))
        {
            Debug.Log($"Enemy triggered by {other.gameObject.name}");

            // _navAgent.enabled = false;
            // canMove = false;

            Rigidbody carRigidbody = other.attachedRigidbody;
            if (carRigidbody == null) return;

            float carSpeed = carRigidbody.linearVelocity.magnitude;

            // 1. CHECAR A VELOCIDADE MÍNIMA
            if (carSpeed < _minSpeedToRagdoll)
            {
                // Opcional: tocar um som de "baque" leve e não fazer nada
                return;
            }

            // 2. CALCULAR A DIREÇÃO E FORÇA DO IMPACTO
            Vector3 impactDirection = (carRigidbody.linearVelocity.normalized + (Vector3.up * _upwardForceComponent)).normalized;
            float impactMagnitude = _baseImpactForce + (carSpeed * _speedToForceMultiplier);
            Vector3 finalForce = impactDirection * impactMagnitude;

            // Encontrar o ponto de impacto
            Vector3 impactPoint = other.ClosestPoint(transform.position);

            ActivateRagdoll(finalForce, impactPoint);
        }
    }

    public void ActivateRagdoll(Vector3 force, Vector3 hitPoint)
    {
        if (_currentState == EnemyState.Ragdolled) return;

        _currentState = EnemyState.Ragdolled;

        // --- A MÁGICA ACONTECE AQUI ---
        // 1. Desativar os controladores de IA e Animação
        _navAgent.enabled = false;
        _animator.enabled = false;
        _mainCollider.enabled = false;

        // 2. Isolar o Rigidbody principal para que ele não interfira
        if (_mainRigidbody) _mainRigidbody.isKinematic = true;

        // 3. Ativar a física do Ragdoll
        Rigidbody closestBone = null;
        float closestDistance = float.MaxValue;
        foreach (var boneRb in _ragdollRigidbodies)
        {
            boneRb.isKinematic = false; // <<< ATIVA O RAGDOLL

            float dist = Vector3.Distance(boneRb.position, hitPoint);
            if (dist < closestDistance)
            {
                closestDistance = dist;
                closestBone = boneRb;
            }
        }

        // 4. Aplicar a força do impacto
        if (closestBone != null)
        {
            closestBone.AddForceAtPosition(force, hitPoint, ForceMode.Impulse);
        }

        OnDeath?.Invoke(this);
        StartCoroutine(CleanupRagdoll());
    }

    private IEnumerator CleanupRagdoll()
    {
        yield return new WaitForSeconds(_ragdollCleanupTime);
        Destroy(gameObject);
    }

    // O dano de outras fontes também deve ativar o ragdoll
    public void TakeDamage(int damage)
    {
        // ... sua lógica de dano aqui ...
    }
}
