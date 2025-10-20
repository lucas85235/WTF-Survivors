using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using System.Collections;

public class PlayerHealth : MonoBehaviour
{
    [Header("Health")]
    [SerializeField] private int _maxHealth = 100;
    [SerializeField] private Slider _healthSlider;

    [Header("Damage Effect")]
    [SerializeField] private Image _fillImage;
    [SerializeField] private Color _damageColor = Color.red;
    [SerializeField] private float _animationDuration = 0.5f;
    [SerializeField] private float _scaleMultiplier = 1.1f;

    [Header("Dependencies")]
    [SerializeField] private CarDestruction _carDestruction;

    [Header("Events")]
    public UnityEvent OnPlayerDied;

    private int _currentHealth;
    private Color _originalColor;
    private Coroutine _damageEffectCoroutine;

    private void Start()
    {
        _currentHealth = _maxHealth;
        _healthSlider.maxValue = _maxHealth;
        _healthSlider.value = _currentHealth;

        if (_fillImage != null)
        {
            _originalColor = _fillImage.color;
        }
        else
        {
            Debug.Log("The image of the fill (Fill Image) of the slider was not assigned in the Inspector!");
        }
    }

    public void TakeDamage(int damage)
    {
        if (_currentHealth <= 0) return;

        _currentHealth -= damage;
        _healthSlider.value = _currentHealth;

        if (_damageEffectCoroutine != null)
        {
            StopCoroutine(_damageEffectCoroutine);
        }

        _damageEffectCoroutine = StartCoroutine(DamageEffect());

        if (_currentHealth <= 0)
        {
            Die();
        }
    }

    private IEnumerator DamageEffect()
    {
        _fillImage.color = _damageColor;
        _healthSlider.transform.localScale = Vector3.one * _scaleMultiplier;

        float elapsedTime = 0f;

        while (elapsedTime < _animationDuration)
        {
            elapsedTime += Time.deltaTime;
            float t = elapsedTime / _animationDuration;

            _fillImage.color = Color.Lerp(_damageColor, _originalColor, t);
            _healthSlider.transform.localScale = Vector3.Lerp(Vector3.one * _scaleMultiplier, Vector3.one, t);

            yield return null;
        }

        _fillImage.color = _originalColor;
        _healthSlider.transform.localScale = Vector3.one;
        _damageEffectCoroutine = null;
    }

    private void Die()
    {
        Debug.Log("Player has died!");
        _carDestruction.DestroyCar();

        Invoke("DieEvent", 1.5f);
    }

    private void DieEvent()
    {
        OnPlayerDied?.Invoke();
    }
}
