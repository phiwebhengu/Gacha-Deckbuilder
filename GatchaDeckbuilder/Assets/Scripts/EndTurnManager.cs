using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using TMPro;

public class EndTurnManager : MonoBehaviour
{
    [Header("Health & Points Settings")]
    [SerializeField] private int playerMaxHP = 20;
    [SerializeField] private int aiMaxHP = 20;

    [Header("HP UI References")]
    [SerializeField] private TextMeshProUGUI playerHPText;
    [SerializeField] private TextMeshProUGUI aiHPText;

    [Header("Round Result Indicator UI")]
    [Tooltip("4 UI Images representing Round 1 to Round 4 status")]
    [SerializeField] private List<Image> roundIndicatorImages = new List<Image>();
    [SerializeField] private Color unevaluatedColor = Color.gray;
    [SerializeField] private Color wonRoundColor = Color.green;
    [SerializeField] private Color lostRoundColor = Color.red;
    [SerializeField] private Color tieRoundColor = Color.yellow;

    [Header("Center Difference Display")]
    [SerializeField] private TextMeshProUGUI centerDifferenceText;
    [SerializeField] private float countStepInterval = 0.08f;
    [SerializeField] private float popScaleMultiplier = 1.35f;
    [SerializeField] private float popDuration = 0.06f;

    [Header("Juice & Camera Shake Settings")]
    [SerializeField] private Camera mainCamera;
    [SerializeField] private float shakeIntensity = 0.25f;
    [SerializeField] private float shakeDuration = 0.08f;

    [Header("UI References")]
    [SerializeField] private Button endTurnButton;
    [SerializeField] private RectTransform selectedHandTransform;

    [Tooltip("Transparent UI Image with Raycast Target enabled to block card interaction after locking in.")]
    [SerializeField] private Image raycastBlockerImage;

    [Header("System References")]
    [SerializeField] private PlayerHandManager handManager;
    [SerializeField] private AIRivalController aiRival;
    [SerializeField] private DrawTimerManager timerManager;

    // Internal State
    private int currentPlayerHP;
    private int currentAIHP;
    private Vector3 originalCenterScale = Vector3.one;
    private Vector3 originalPlayerHPScale = Vector3.one;
    private Vector3 originalAIHPScale = Vector3.one;
    private Vector3 originalCamPos;

    public int CurrentPlayerHP => currentPlayerHP;
    public int CurrentAIHP => currentAIHP;

    private void Start()
    {
        currentPlayerHP = playerMaxHP;
        currentAIHP = aiMaxHP;

        if (mainCamera == null)
        {
            mainCamera = Camera.main;
        }

        if (mainCamera != null)
        {
            originalCamPos = mainCamera.transform.localPosition;
        }

        if (centerDifferenceText != null)
        {
            originalCenterScale = centerDifferenceText.transform.localScale;
            centerDifferenceText.text = "";
        }

        if (playerHPText != null) originalPlayerHPScale = playerHPText.transform.localScale;
        if (aiHPText != null) originalAIHPScale = aiHPText.transform.localScale;

        InitializeRoundIndicators();
        UpdateHPUI();

        if (endTurnButton != null)
        {
            endTurnButton.onClick.AddListener(OnEndTurnClicked);
        }

        if (raycastBlockerImage != null)
        {
            raycastBlockerImage.gameObject.SetActive(false);
        }

        UpdateEndTurnButtonVisibility();
    }

    private void InitializeRoundIndicators()
    {
        foreach (Image img in roundIndicatorImages)
        {
            if (img != null)
            {
                img.color = unevaluatedColor;
            }
        }
    }

    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
        {
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }
    }

    public void UpdateEndTurnButtonVisibility()
    {
        if (handManager == null || endTurnButton == null) return;

        bool hasSelectedCards = handManager.GetSelectedCards().Count > 0;
        endTurnButton.gameObject.SetActive(hasSelectedCards);
    }

    private void OnEndTurnClicked()
    {
        if (handManager == null || selectedHandTransform == null) return;

        List<CardUI> playerSelectedCards = handManager.GetSelectedCards();
        if (playerSelectedCards.Count == 0) return;

        if (raycastBlockerImage != null)
        {
            raycastBlockerImage.gameObject.SetActive(true);
        }

        int playerTotalAttack = CalculateTotalAttack(playerSelectedCards);
        handManager.SubmitSelectedCardsToHand(selectedHandTransform);

        int aiTotalAttack = 0;
        if (aiRival != null)
        {
            PlayerHandManager aiHandManager = aiRival.GetComponentInChildren<PlayerHandManager>();
            if (aiHandManager != null)
            {
                List<CardUI> aiSelectedCards = aiHandManager.GetSelectedCards();
                aiTotalAttack = CalculateTotalAttack(aiSelectedCards);
            }

            aiRival.SubmitRivalHand();
        }

        StartCoroutine(Routine_ResolveCombatSequence(playerTotalAttack, aiTotalAttack));

        if (endTurnButton != null)
        {
            endTurnButton.gameObject.SetActive(false);
        }
    }

    private int CalculateTotalAttack(List<CardUI> cards)
    {
        int total = 0;
        foreach (CardUI card in cards)
        {
            BalatroCardController cardController = card.GetComponent<BalatroCardController>();
            if (cardController != null && cardController.Category == CardCategory.Attack)
            {
                total += cardController.CardValue;
            }
        }
        return total;
    }

    private IEnumerator Routine_ResolveCombatSequence(int playerScore, int aiScore)
    {
        int difference = Mathf.Abs(playerScore - aiScore);
        int currentRoundIndex = (timerManager != null) ? timerManager.CurrentRound - 1 : 0;

        if (difference == 0)
        {
            if (centerDifferenceText != null)
            {
                centerDifferenceText.text = "0";
                yield return StartCoroutine(Routine_PopText(centerDifferenceText.transform, originalCenterScale));
            }

            UpdateRoundIndicator(currentRoundIndex, tieRoundColor);
            yield return new WaitForSeconds(0.8f);
            if (centerDifferenceText != null) centerDifferenceText.text = "";

            CheckNextRoundOrEndGame();
            yield break;
        }

        // Step 1: Count UP in center text
        for (int i = 1; i <= difference; i++)
        {
            if (centerDifferenceText != null)
            {
                centerDifferenceText.text = i.ToString();
                StartCoroutine(Routine_PopText(centerDifferenceText.transform, originalCenterScale));
            }
            yield return new WaitForSeconds(countStepInterval);
        }

        yield return new WaitForSeconds(0.4f);

        // Step 2: Resolve scoring and update round indicator UI
        bool playerLoses = aiScore > playerScore;
        if (playerLoses)
        {
            UpdateRoundIndicator(currentRoundIndex, lostRoundColor);
        }
        else
        {
            UpdateRoundIndicator(currentRoundIndex, wonRoundColor);
        }

        TextMeshProUGUI targetHPText = playerLoses ? playerHPText : aiHPText;
        Transform targetScaleTransform = targetHPText != null ? targetHPText.transform : null;
        Vector3 targetOriginalScale = playerLoses ? originalPlayerHPScale : originalAIHPScale;

        // Step 3: Count DOWN center difference while deducting HP
        for (int i = difference; i > 0; i--)
        {
            if (playerLoses)
            {
                currentPlayerHP = Mathf.Max(0, currentPlayerHP - 1);
            }
            else
            {
                currentAIHP = Mathf.Max(0, currentAIHP - 1);
            }

            UpdateHPUI();

            if (centerDifferenceText != null)
            {
                centerDifferenceText.text = (i - 1) > 0 ? (i - 1).ToString() : "";
                StartCoroutine(Routine_PopText(centerDifferenceText.transform, originalCenterScale));
            }

            if (targetScaleTransform != null)
            {
                StartCoroutine(Routine_PopText(targetScaleTransform, targetOriginalScale));
            }

            StartCoroutine(Routine_CameraShake());

            yield return new WaitForSeconds(countStepInterval);
        }

        yield return new WaitForSeconds(0.5f);
        if (centerDifferenceText != null) centerDifferenceText.text = "";

        // Check for round transition or final match outcome
        CheckNextRoundOrEndGame();
    }

    private void UpdateRoundIndicator(int roundIndex, Color statusColor)
    {
        if (roundIndex >= 0 && roundIndex < roundIndicatorImages.Count)
        {
            if (roundIndicatorImages[roundIndex] != null)
            {
                roundIndicatorImages[roundIndex].color = statusColor;
            }
        }
    }

    private void CheckNextRoundOrEndGame()
    {
        // 1. Check direct knockouts (0 HP reached)
        if (currentAIHP <= 0)
        {
            return;
        }
        if (currentPlayerHP <= 0)
        {
            Debug.Log("[Match Over] AI RIVAL WINS BY KNOCKOUT!");
            return;
        }

    

        // 2. Check round limits
        if (timerManager != null)
        {
            if (timerManager.CurrentRound < timerManager.MaxRounds)
            {
                ResetTurnBlocker();
                timerManager.TriggerNextRound();
            }
            else
            {
                // Final Evaluation after 4 rounds
                EvaluateFinalMatchWinner();
            }
        }
    }



    private void EvaluateFinalMatchWinner()
    {
        Debug.Log($"[Match Over] 4 Rounds Complete! Final Scores - Player: {currentPlayerHP} HP | AI: {currentAIHP} HP");

        if (currentPlayerHP > currentAIHP)
        {
            Debug.Log("[Match Over] PLAYER WINS THE MATCH!");
        }
        else if (currentAIHP > currentPlayerHP)
        {
            Debug.Log("[Match Over] AI RIVAL WINS THE MATCH!");
        }
        else
        {
            Debug.Log("[Match Over] THE MATCH ENDS IN A DRAW!");
        }
    }

    private IEnumerator Routine_PopText(Transform textTransform, Vector3 baseScale)
    {
        Vector3 targetScale = baseScale * popScaleMultiplier;
        float elapsed = 0f;

        while (elapsed < popDuration)
        {
            elapsed += Time.deltaTime;
            textTransform.localScale = Vector3.Lerp(baseScale, targetScale, elapsed / popDuration);
            yield return null;
        }

        elapsed = 0f;

        while (elapsed < popDuration)
        {
            elapsed += Time.deltaTime;
            textTransform.localScale = Vector3.Lerp(targetScale, baseScale, elapsed / popDuration);
            yield return null;
        }

        textTransform.localScale = baseScale;
    }

    private IEnumerator Routine_CameraShake()
    {
        if (mainCamera == null) yield break;

        float elapsed = 0f;

        while (elapsed < shakeDuration)
        {
            elapsed += Time.deltaTime;
            Vector2 randomOffset = Random.insideUnitCircle * shakeIntensity;
            mainCamera.transform.localPosition = originalCamPos + new Vector3(randomOffset.x, randomOffset.y, 0f);
            yield return null;
        }

        mainCamera.transform.localPosition = originalCamPos;
    }

    private void UpdateHPUI()
    {
        if (playerHPText != null)
        {
            playerHPText.text = $"{currentPlayerHP}";
        }

        if (aiHPText != null)
        {
            aiHPText.text = $"{currentAIHP}";
        }
    }

    public void ResetTurnBlocker()
    {
        if (raycastBlockerImage != null)
        {
            raycastBlockerImage.gameObject.SetActive(false);
        }
    }
}