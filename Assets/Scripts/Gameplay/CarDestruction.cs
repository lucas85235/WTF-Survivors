using UnityEngine;
using System.Collections.Generic; // Para usar Listas

public class CarDestruction : MonoBehaviour
{
    [Header("Referências")]
    [Tooltip("O GameObject que contém o modelo do carro inteiro. Será desativado na destruição.")]
    [SerializeField] private GameObject _normalModel;

    [Tooltip("O GameObject pai que contém todas as peças separadas do carro.")]
    [SerializeField] private Transform _destroyedPartsParent;
    
    // Opcional: Referência ao script de controle do carro para desativá-lo.
    // Troque 'YourCarController' pelo nome real do seu script de movimento.
    [SerializeField] private MonoBehaviour[] _disableScripts; 

    [Header("Configurações da Explosão")]
    [SerializeField] private float _explosionForce = 500f;
    [SerializeField] private float _explosionRadius = 5f;
    [SerializeField] private float _upwardsModifier = 1.5f;

    // Lista para guardar todos os Rigidbodies das peças.
    private List<Rigidbody> _partRigidbodies;
    private bool _isDestroyed = false;

    /// <summary>
    /// Awake é chamado antes de Start. Perfeito para preparar referências.
    /// </summary>
    private void Awake()
    {
        // Inicializa a lista.
        _partRigidbodies = new List<Rigidbody>();

        // Procura por todos os componentes Rigidbody nos filhos do objeto pai das peças.
        if (_destroyedPartsParent != null)
        {
            // O comando GetComponentsInChildren<T>() é ótimo para isso.
            _partRigidbodies.AddRange(_destroyedPartsParent.GetComponentsInChildren<Rigidbody>());

            // Garante que todas as peças comecem como cinemáticas (kinematic).
            foreach (Rigidbody rb in _partRigidbodies)
            {
                rb.isKinematic = true;
            }
        }
        else
        {
            Debug.LogError("O objeto pai das peças destruídas (_destroyedPartsParent) não foi atribuído!");
        }
    }

    /// <summary>
    /// Esta é a função principal que inicia a sequência de destruição.
    /// Ela pode ser chamada de qualquer outro script.
    /// </summary>
    [ContextMenu("Destroy Car")]
    public void DestroyCar()
    {
        // Previne que a função seja chamada múltiplas vezes.
        if (_isDestroyed) return;
        _isDestroyed = true;

        Debug.Log("Iniciando destruição do carro!");

        // 1. Desativa o script de controle do carro.
        if (_disableScripts.Length > 0)
        {
            foreach (var script in _disableScripts)
            {
                script.enabled = false;
            }
        }

        // 2. Desativa o modelo do carro inteiro.
        if (_normalModel != null)
        {
            _normalModel.SetActive(false);
        }

        // 3. Ativa a física e aplica a força de explosão em cada peça.
        foreach (Rigidbody rb in _partRigidbodies)
        {
            // Ativa a física na peça.
            rb.isKinematic = false;

            // Adiciona uma força de explosão para lançar a peça.
            // A força se origina do centro do carro.
            rb.AddExplosionForce(_explosionForce, transform.position, _explosionRadius, _upwardsModifier);
        }

        // 4. (Opcional) Desvincula as peças do carro principal.
        // Isso permite que o objeto "PlayerCar" seja destruído sem apagar as peças que estão voando.
        if (_destroyedPartsParent != null)
        {
            _destroyedPartsParent.SetParent(null);
        }
    }
}