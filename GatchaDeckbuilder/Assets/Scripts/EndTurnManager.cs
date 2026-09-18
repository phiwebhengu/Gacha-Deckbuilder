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

    [Header("Combat Count UI References")]
    [Tooltip("TextMeshPro displaying the Player's accumulated Attack value.")]
    [SerializeField] private TextMeshProUGUI playerAttackText;

    [Tooltip("TextMeshPro displaying the Player's accumulated Defense value.")]
    [SerializeField] private TextMeshProUGUI playerDefenseText;

    [Tooltip("TextMeshPro displaying the AI Rival's accumulated Attack value.")]
    [SerializeField] private TextMeshProUGUI aiAttackText;

    [Tooltip("TextMeshPro displaying the AI Rival's accumulated Defense value.")]
    [SerializeField] private TextMeshProUGUI aiDefenseText;

    [Header("Center Difference Display")]
    [SerializeField] private TextMeshProUGUI centerDifferenceText;
    [SerializeField] private float countStepInterval = 0.08f;
    [SerializeField] private float popScaleMultiplier = 1.35f;
    [SerializeField] private float popDuration = 0.06f;
    [SerializeField] private float cardMovementWaitDelay = 0.5f;
    [SerializeField] private float phaseTransitionPause = 0.4f;

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

    private Vector3 originalPlayerAttackScale = Vector3.one;
    private Vector3 originalPlayerDefenseScale = Vector3.one;
    private Vector3 originalAIAttackScale = Vector3.one;
    private Vector3 originalAIDefenseScale = Vector3.one;

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

        CacheAndResetUI();

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

    private void CacheAndResetUI()
    {
        if (centerDifferenceText != null)
        {
            originalCenterScale = centerDifferenceText.transform.localScale;
            centerDifferenceText.text = "";
        }

        if (playerHPText != null) originalPlayerHPScale = playerHPText.transform.localScale;
        if (aiHPText != null) originalAIHPScale = aiHPText.transform.localScale;

        if (playerAttackText != null)
        {
            originalPlayerAttackScale = playerAttackText.transform.localScale;
            playerAttackText.text = "0";
        }
        if (playerDefenseText != null)
        {
            originalPlayerDefenseScale = playerDefenseText.transform.localScale;
            playerDefenseText.text = "0";
        }
        if (aiAttackText != null)
        {
            originalAIAttackScale = aiAttackText.transform.localScale;
            aiAttackText.text = "0";
        }
        if (aiDefenseText != null)
        {
            originalAIDefenseScale = aiDefenseText.transform.localScale;
            aiDefenseText.text = "0";
        }

        UpdateHPUI();
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

        // Move cards to central played hand areas
        handManager.SubmitSelectedCardsToHand(selectedHandTransform);

        if (aiRival != null)
        {
            aiRival.SubmitRivalHand();
        }

        if (endTurnButton != null)
        {
            endTurnButton.gameObject.SetActive(false);
        }

        StartCoroutine(Routine_ResolveCombatSequence());
    }

    private List<BalatroCardController> GetControllersFromTransform(RectTransform container)
    {
        List<BalatroCardController> list = new List<BalatroCardController>();
        if (container == null) return list;

        CardUI[] cards = container.GetComponentsInChildren<CardUI>();
        foreach (CardUI card in cards)
        {
            BalatroCardController ctrl = card.GetComponent<BalatroCardController>();
            if (ctrl != null) list.Add(ctrl);
        }
        return list;
    }

    private int CalculateTotalValueForCategory(List<BalatroCardController> cards, CardCategory targetCategory)
    {
        int total = 0;
        foreach (BalatroCardController card in cards)
        {
            if (card != null && card.Category == targetCategory)
            {
                total += card.CardValue;
            }
        }
        return total;
    }

    private IEnumerator Routine_ResolveCombatSequence()
    {
        // 1. Wait for played cards to finish animating into center slots
        yield return new WaitForSeconds(cardMovementWaitDelay);

        List<BalatroCardController> playerCards = GetControllersFromTransform(selectedHandTransform);
        List<BalatroCardController> aiCards = (aiRival != null) ? GetControllersFromTransform(aiRival.RivalSelectedHandTransform) : new List<BalatroCardController>();

        int playerAttack = CalculateTotalValueForCategory(playerCards, CardCategory.Attack);
        int playerDefense = CalculateTotalValueForCategory(playerCards, CardCategory.Defense);
        int aiAttack = CalculateTotalValueForCategory(aiCards, CardCategory.Attack);
        int aiDefense = CalculateTotalValueForCategory(aiCards, CardCategory.Defense);

        // -------------------------------------------------------------
        // STEP 1: CALCULATE & DISPLAY ATTACK
        // -------------------------------------------------------------
        HighlightCardsByCategory(playerCards, aiCards, CardCategory.Attack);

        yield return StartCoroutine(Routine_CountUpPair(
            playerAttack, playerAttackText, originalPlayerAttackScale,
            aiAttack, aiAttackText, originalAIAttackScale
        ));

        yield return new WaitForSeconds(phaseTransitionPause);
        ResetCardHighlights(playerCards, aiCards);

        // -------------------------------------------------------------
        // STEP 2: CALCULATE & DISPLAY DEFENSE
        // -------------------------------------------------------------
        HighlightCardsByCategory(playerCards, aiCards, CardCategory.Defense);

        yield return StartCoroutine(Routine_CountUpPair(
            playerDefense, playerDefenseText, originalPlayerDefenseScale,
            aiDefense, aiDefenseText, originalAIDefenseScale
        ));

        yield return new WaitForSeconds(phaseTransitionPause);
        ResetCardHighlights(playerCards, aiCards);

        // -------------------------------------------------------------
        // STEP 3: CALCULATE NET DAMAGE & DISPLAY IN CENTER
        // -------------------------------------------------------------
        int netDamageToAI = Mathf.Max(0, playerAttack - aiDefense);
        int netDamageToPlayer = Mathf.Max(0, aiAttack - playerDefense);

        int maxNetDamage = Mathf.Max(netDamageToAI, netDamageToPlayer);

        if (centerDifferenceText != null && maxNetDamage > 0)
        {
            centerDifferenceText.text = "0";
            for (int i = 1; i <= maxNetDamage; i++)
            {
                centerDifferenceText.text = i.ToString();
                StartCoroutine(Routine_PopText(centerDifferenceText.transform, originalCenterScale));
                yield return new WaitForSeconds(countStepInterval);
            }
        }

        yield return new WaitForSeconds(phaseTransitionPause);

        // -------------------------------------------------------------
        // STEP 4: APPLY DAMAGE TO LOSING HAND
        // -------------------------------------------------------------
        if (netDamageToPlayer > 0)
        {
            for (int i = 1; i <= netDamageToPlayer; i++)
            {
                currentPlayerHP = Mathf.Max(0, currentPlayerHP - 1);
                UpdateHPUI();

                if (playerHPText != null)
                {
                    StartCoroutine(Routine_PopText(playerHPText.transform, originalPlayerHPScale));
                }

                StartCoroutine(Routine_CameraShake());
                yield return new WaitForSeconds(countStepInterval);
            }
        }

        if (netDamageToAI > 0)
        {
            for (int i = 1; i <= netDamageToAI; i++)
            {
                currentAIHP = Mathf.Max(0, currentAIHP - 1);
                UpdateHPUI();

                if (aiHPText != null)
                {
                    StartCoroutine(Routine_PopText(aiHPText.transform, originalAIHPScale));
                }

                StartCoroutine(Routine_CameraShake());
                yield return new WaitForSeconds(countStepInterval);
            }
        }

        yield return new WaitForSeconds(0.5f);
        CheckNextRoundOrEndGame();
    }

    private void HighlightCardsByCategory(List<BalatroCardController> playerList, List<BalatroCardController> aiList, CardCategory category)
    {
        foreach (var card in playerList) card.HighlightCardForCategory(category);
        foreach (var card in aiList) card.HighlightCardForCategory(category);
    }

    private void ResetCardHighlights(List<BalatroCardController> playerList, List<BalatroCardController> aiList)
    {
        foreach (var card in playerList) card.ResetCombatHighlight();
        foreach (var card in aiList) card.ResetCombatHighlight();
    }

    private IEnumerator Routine_CountUpPair(
        int playerTargetVal, TextMeshProUGUI playerText, Vector3 playerScale,
        int aiTargetVal, TextMeshProUGUI aiText, Vector3 aiScale)
    {
        int maxSteps = Mathf.Max(playerTargetVal, aiTargetVal);

        for (int step = 1; step <= maxSteps; step++)
        {
            if (step <= playerTargetVal && playerText != null)
            {
                playerText.text = step.ToString();
                StartCoroutine(Routine_PopText(playerText.transform, playerScale));
            }

            if (step <= aiTargetVal && aiText != null)
            {
                aiText.text = step.ToString();
                StartCoroutine(Routine_PopText(aiText.transform, aiScale));
            }

            yield return new WaitForSeconds(countStepInterval);
        }
    }

    private void CheckNextRoundOrEndGame()
    {
        if (currentAIHP <= 0 && currentPlayerHP <= 0)
        {
            Debug.Log("[Match Over] BOTH PLAYERS KNOCKED OUT! DRAW GAME!");
            ClearAllSubmittedCards();
            return;
        }
        if (currentAIHP <= 0)
        {
            Debug.Log("[Match Over] PLAYER WINS BY KNOCKOUT!");
            ClearAllSubmittedCards();
            return;
        }
        if (currentPlayerHP <= 0)
        {
            Debug.Log("[Match Over] AI RIVAL WINS BY KNOCKOUT!");
            ClearAllSubmittedCards();
            return;
        }

        ClearAllSubmittedCards();
        ResetTurnUI();

        if (timerManager != null)
        {
            ResetTurnBlocker();
            timerManager.TriggerNextRound();
        }
    }

    private void ResetTurnUI()
    {
        if (playerAttackText != null) playerAttackText.text = "0";
        if (playerDefenseText != null) playerDefenseText.text = "0";
        if (aiAttackText != null) aiAttackText.text = "0";
        if (aiDefenseText != null) aiDefenseText.text = "0";
        if (centerDifferenceText != null) centerDifferenceText.text = "";
    }

    private void ClearAllSubmittedCards()
    {
        if (handManager != null && selectedHandTransform != null)
        {
            handManager.ReturnSubmittedCardsToHand(selectedHandTransform);
        }

        if (aiRival != null)
        {
            PlayerHandManager aiHandManager = aiRival.GetComponentInChildren<PlayerHandManager>();
            RectTransform aiSelectedTransform = aiRival.RivalSelectedHandTransform;

            if (aiHandManager != null && aiSelectedTransform != null)
            {
                aiHandManager.ReturnSubmittedCardsToHand(aiSelectedTransform);
            }
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