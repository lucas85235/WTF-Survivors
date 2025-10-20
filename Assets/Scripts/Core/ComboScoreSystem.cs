using UnityEngine;
using TMPro;
using DG.Tweening;

public class ComboScoreSystem : MonoBehaviour
{
    [System.Serializable]
    public class ComboConfig
    {
        public int minHits = 2;
        public float timeWindow = 2f;
        public float multiplierPerCombo = 0.1f; // +10% por combo
        public float maxMultiplier = 3f;
    }

    [SerializeField] private ComboConfig comboConfig = new();
    [SerializeField] private TextMeshProUGUI scoreText;
    [SerializeField] private TextMeshProUGUI comboText;
    [SerializeField] private CanvasGroup scoreCanvasGroup;
    [SerializeField] private CanvasGroup comboCanvasGroup;
    [SerializeField] private float scorePopupScale = 1.5f;
    [SerializeField] private float scorePopupDuration = 0.5f;
    [SerializeField] private Color comboActiveColor = Color.yellow;
    [SerializeField] private Color comboTextDefaultColor = Color.white;

    private int totalScore = 0;
    private int currentCombo = 0;
    private float lastHitTime = -999f;
    private float currentMultiplier = 1f;

    private static ComboScoreSystem instance;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
        InitializeUI();
    }

    private void InitializeUI()
    {
        if (scoreCanvasGroup == null)
            scoreCanvasGroup = scoreText?.GetComponent<CanvasGroup>();
        if (comboCanvasGroup == null)
            comboCanvasGroup = comboText?.GetComponent<CanvasGroup>();

        UpdateScoreDisplay();
        UpdateComboDisplay();
    }

    public static void AddScore(int points)
    {
        if (instance == null)
        {
            Debug.LogWarning("ComboScoreSystem não foi inicializado!");
            return;
        }
        instance.ProcessScore(points);
    }

    private void ProcessScore(int points)
    {
        // Verifica se o combo ainda está ativo
        if (Time.time - lastHitTime > comboConfig.timeWindow)
        {
            ResetCombo();
        }

        // Incrementa combo e atualiza multiplicador
        currentCombo++;
        lastHitTime = Time.time;
        currentMultiplier = Mathf.Min(1f + (comboConfig.multiplierPerCombo * (currentCombo - 1)),
                                      comboConfig.maxMultiplier);

        // Calcula pontos com multiplicador
        int finalPoints = Mathf.CeilToInt(points * currentMultiplier);
        totalScore += finalPoints;

        // Feedbacks visuais
        AnimateScorePopup(finalPoints);
        UpdateScoreDisplay();
        UpdateComboDisplay();

        // Cancela o reset anterior
        DOTween.Kill("comboReset");

        // Agenda novo reset se não houver mais hits
        DOVirtual.DelayedCall(comboConfig.timeWindow, ResetCombo)
            .SetId("comboReset");
    }

    private void AnimateScorePopup(int points)
    {
        if (scoreText == null) return;

        scoreText.rectTransform.localScale = Vector3.one;
        scoreText.alpha = 1f;

        var sequence = DOTween.Sequence();
        sequence.Append(scoreText.rectTransform.DOScale(scorePopupScale, scorePopupDuration * 0.5f)
            .SetEase(Ease.OutQuad));
        sequence.Join(scoreText.DOFade(0.8f, scorePopupDuration * 0.5f));
        sequence.Append(scoreText.DOFade(1f, scorePopupDuration * 0.5f));
        sequence.AppendCallback(() => scoreText.text = totalScore.ToString());
    }

    private void UpdateScoreDisplay()
    {
        if (scoreText != null)
            scoreText.text = totalScore.ToString();
    }

    private void UpdateComboDisplay()
    {
        if (comboText == null) return;

        if (currentCombo < comboConfig.minHits)
        {
            comboCanvasGroup?.DOFade(0f, 0.2f);
            return;
        }

        comboCanvasGroup?.DOFade(1f, 0.1f);
        comboText.text = $"COMBO x{currentCombo}\n{currentMultiplier:F1}x";
        comboText.color = comboActiveColor;

        // Animação de "pulse" no combo
        comboText.rectTransform.localScale = Vector3.one;
        comboText.rectTransform.DOScale(1.1f, 0.15f)
            .SetEase(Ease.OutQuad)
            .OnComplete(() =>
                comboText.rectTransform.DOScale(1f, 0.1f).SetEase(Ease.InQuad)
            );
    }

    private void ResetCombo()
    {
        currentCombo = 0;
        currentMultiplier = 1f;

        if (comboText != null)
        {
            comboText.color = comboTextDefaultColor;
            comboCanvasGroup?.DOFade(0f, 0.3f).SetEase(Ease.InQuad);
        }
    }

    public int GetTotalScore() => totalScore;
    public int GetCurrentCombo() => currentCombo;
    public float GetCurrentMultiplier() => currentMultiplier;

    public void ResetScore()
    {
        totalScore = 0;
        ResetCombo();
        UpdateScoreDisplay();
        UpdateComboDisplay();
    }
}
