using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using TMPro;

#if UNITY_EDITOR
using UnityEditor;
#endif

public struct TurnResultData
{
    public int playerAttack;
    public int playerDefense;
    public int opponentAttack;
    public int opponentDefense;
    public int netDamageToPlayer;
    public int netDamageToOpponent;
    public int finalPlayerHP;
    public int finalOpponentHP;
}

public class EndTurnManager : MonoBehaviour
{
    [Header("Health & Points Settings")]
    [SerializeField] private int playerMaxHP = 20;
    [SerializeField] private int opponentMaxHP = 20;

    [Header("HP UI References")]
    [SerializeField] private TextMeshProUGUI playerHPText;
    [SerializeField] private TextMeshProUGUI opponentHPText;

    [Header("Combat Count UI References")]
    [SerializeField] private TextMeshProUGUI playerAttackText;
    [SerializeField] private TextMeshProUGUI playerDefenseText;
    [SerializeField] private TextMeshProUGUI opponentAttackText;
    [SerializeField] private TextMeshProUGUI opponentDefenseText;

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

    [Header("Audio Settings & Juicing SFX")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip attackHighlightSFX;
    [SerializeField] private AudioClip defenseHighlightSFX;
    [SerializeField] private AudioClip attackDefenseCountSFX;
    [SerializeField] private AudioClip netDamageCountSFX;
    [SerializeField] private AudioClip hpDamageTickSFX;
    [SerializeField] private AudioClip buttonHoverSFX;
    [SerializeField] private AudioClip victorySFX;
    [SerializeField] private AudioClip defeatSFX;

    [Header("Pitch Scaling Pitch Tuning")]
    [SerializeField] private float countMinPitch = 0.85f;
    [SerializeField] private float countMaxPitch = 1.4f;

    [Header("UI References")]
    [SerializeField] private Button endTurnButton;
    [SerializeField] private RectTransform selectedHandTransform;
    [SerializeField] private Image raycastBlockerImage;

    [Header("Game Over UI References")]
    [SerializeField] private GameObject gameOverPanel;
    [SerializeField] private TextMeshProUGUI victoryText;
    [SerializeField] private TextMeshProUGUI lossText;
    [SerializeField] private Button restartButton;
    [SerializeField] private Button quitButton;

    [Header("System References")]
    [SerializeField] private PlayerHandManager handManager;
    [SerializeField] private AIRivalController aiRival;
    [SerializeField] private DrawTimerManager timerManager;

    [Header("Multiplayer Identity")]
    [SerializeField] private bool isMultiplayerMode = false;

    private int currentPlayerHP;
    private int currentOpponentHP;
    private Vector3 originalCenterScale = Vector3.one;
    private Vector3 originalPlayerHPScale = Vector3.one;
    private Vector3 originalOpponentHPScale = Vector3.one;

    private Vector3 originalPlayerAttackScale = Vector3.one;
    private Vector3 originalPlayerDefenseScale = Vector3.one;
    private Vector3 originalOpponentAttackScale = Vector3.one;
    private Vector3 originalOpponentDefenseScale = Vector3.one;

    private Vector3 originalCamPos;
    private Coroutine activeCombatCoroutine;

    public int CurrentPlayerHP => currentPlayerHP;
    public int CurrentOpponentHP => currentOpponentHP;

    private void Start()
    {
        currentPlayerHP = playerMaxHP;
        currentOpponentHP = opponentMaxHP;

        if (mainCamera == null) mainCamera = Camera.main;
        if (mainCamera != null) originalCamPos = mainCamera.transform.localPosition;

        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
        }

        CacheAndResetUI();

        if (endTurnButton != null) endTurnButton.onClick.AddListener(OnEndTurnClicked);
        if (raycastBlockerImage != null) raycastBlockerImage.gameObject.SetActive(false);

        if (gameOverPanel != null) gameOverPanel.SetActive(false);
        if (victoryText != null) victoryText.gameObject.SetActive(false);
        if (lossText != null) lossText.gameObject.SetActive(false);

        SetupGameOverButtons();
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
        if (opponentHPText != null) originalOpponentHPScale = opponentHPText.transform.localScale;

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
        if (opponentAttackText != null)
        {
            originalOpponentAttackScale = opponentAttackText.transform.localScale;
            opponentAttackText.text = "0";
        }
        if (opponentDefenseText != null)
        {
            originalOpponentDefenseScale = opponentDefenseText.transform.localScale;
            opponentDefenseText.text = "0";
        }

        UpdateHPUI();
    }

    private void SetupGameOverButtons()
    {
        if (restartButton != null)
        {
            restartButton.onClick.AddListener(OnRestartButtonClicked);
            AddHoverAndExitListenersToButton(restartButton);
        }

        if (quitButton != null)
        {
            quitButton.onClick.AddListener(OnQuitButtonClicked);
            AddHoverAndExitListenersToButton(quitButton);
        }
    }

    private void AddHoverAndExitListenersToButton(Button targetButton)
    {
        if (targetButton == null) return;

        EventTrigger trigger = targetButton.gameObject.GetComponent<EventTrigger>();
        if (trigger == null) trigger = targetButton.gameObject.AddComponent<EventTrigger>();

        Vector3 baseScale = targetButton.transform.localScale;
        Vector3 hoverScale = baseScale * 1.08f;

        EventTrigger.Entry entryHover = new EventTrigger.Entry
        {
            eventID = EventTriggerType.PointerEnter
        };
        entryHover.callback.AddListener((data) =>
        {
            targetButton.transform.localScale = hoverScale;
            PlaySFX(buttonHoverSFX);
        });
        trigger.triggers.Add(entryHover);

        EventTrigger.Entry entryExit = new EventTrigger.Entry
        {
            eventID = EventTriggerType.PointerExit
        };
        entryExit.callback.AddListener((data) =>
        {
            targetButton.transform.localScale = baseScale;
        });
        trigger.triggers.Add(entryExit);
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

        if (raycastBlockerImage != null) raycastBlockerImage.gameObject.SetActive(true);

        handManager.SubmitSelectedCardsToHand(selectedHandTransform);

        if (endTurnButton != null) endTurnButton.gameObject.SetActive(false);

        if (isMultiplayerMode)
        {
            // TODO: Send client choices to Server/Host via Network RPC
        }
        else
        {
            if (aiRival != null) aiRival.SubmitRivalHand();

            TurnResultData result = CalculateTurnResult();
            activeCombatCoroutine = StartCoroutine(Routine_ResolveCombatSequence(result));
        }
    }

    public TurnResultData CalculateTurnResult()
    {
        List<BalatroCardController> playerCards = GetControllersFromTransform(selectedHandTransform);
        List<BalatroCardController> opponentCards = (aiRival != null) ? GetControllersFromTransform(aiRival.RivalSelectedHandTransform) : new List<BalatroCardController>();

        int pAttack = CalculateTotalValueForCategory(playerCards, CardCategory.Attack);
        int pDefense = CalculateTotalValueForCategory(playerCards, CardCategory.Defense);
        int oAttack = CalculateTotalValueForCategory(opponentCards, CardCategory.Attack);
        int oDefense = CalculateTotalValueForCategory(opponentCards, CardCategory.Defense);

        int damageToOpponent = Mathf.Max(0, pAttack - oDefense);
        int damageToPlayer = Mathf.Max(0, oAttack - pDefense);

        TurnResultData data = new TurnResultData
        {
            playerAttack = pAttack,
            playerDefense = pDefense,
            opponentAttack = oAttack,
            opponentDefense = oDefense,
            netDamageToPlayer = damageToPlayer,
            netDamageToOpponent = damageToOpponent,
            finalPlayerHP = Mathf.Max(0, currentPlayerHP - damageToPlayer),
            finalOpponentHP = Mathf.Max(0, currentOpponentHP - damageToOpponent)
        };

        return data;
    }

    public void OnReceiveNetworkTurnResult(TurnResultData result)
    {
        activeCombatCoroutine = StartCoroutine(Routine_ResolveCombatSequence(result));
    }

    private IEnumerator Routine_ResolveCombatSequence(TurnResultData result)
    {
        yield return new WaitForSeconds(cardMovementWaitDelay);

        List<BalatroCardController> playerCards = GetControllersFromTransform(selectedHandTransform);
        List<BalatroCardController> opponentCards = (aiRival != null) ? GetControllersFromTransform(aiRival.RivalSelectedHandTransform) : new List<BalatroCardController>();

        foreach (var card in playerCards) card.RevealCardVisuals();
        foreach (var card in opponentCards) card.RevealCardVisuals();

        ProcessPlayedSupportCards(playerCards, "Player");
        ProcessPlayedSupportCards(opponentCards, "Opponent");

        // --- ATTACK PHASE ---
        HighlightCardsByCategory(playerCards, opponentCards, CardCategory.Attack);
        PlaySFX(attackHighlightSFX);
        yield return StartCoroutine(Routine_CountUpPair(
            result.playerAttack, playerAttackText, originalPlayerAttackScale,
            result.opponentAttack, opponentAttackText, originalOpponentAttackScale,
            attackDefenseCountSFX
        ));
        yield return new WaitForSeconds(phaseTransitionPause);
        ResetCardHighlights(playerCards, opponentCards);

        // --- DEFENSE PHASE ---
        HighlightCardsByCategory(playerCards, opponentCards, CardCategory.Defense);
        PlaySFX(defenseHighlightSFX);
        yield return StartCoroutine(Routine_CountUpPair(
            result.playerDefense, playerDefenseText, originalPlayerDefenseScale,
            result.opponentDefense, opponentDefenseText, originalOpponentDefenseScale,
            attackDefenseCountSFX
        ));
        yield return new WaitForSeconds(phaseTransitionPause);
        ResetCardHighlights(playerCards, opponentCards);

        // --- CENTER NET DAMAGE CALCULATION ---
        int maxNetDamage = Mathf.Max(result.netDamageToOpponent, result.netDamageToPlayer);
        if (centerDifferenceText != null && maxNetDamage > 0)
        {
            centerDifferenceText.text = "0";
            for (int i = 1; i <= maxNetDamage; i++)
            {
                centerDifferenceText.text = i.ToString();
                PlayPitchEscalatedSound(netDamageCountSFX, i, maxNetDamage);
                StartCoroutine(Routine_PopText(centerDifferenceText.transform, originalCenterScale));
                yield return new WaitForSeconds(countStepInterval);
            }
        }
        yield return new WaitForSeconds(phaseTransitionPause);

        // --- APPLY DAMAGE TO PLAYER ---
        if (result.netDamageToPlayer > 0)
        {
            int totalDamage = result.netDamageToPlayer;
            for (int i = 1; i <= totalDamage; i++)
            {
                currentPlayerHP = Mathf.Max(0, currentPlayerHP - 1);
                UpdateHPUI();

                PlaySFX(hpDamageTickSFX);
                if (playerHPText != null) StartCoroutine(Routine_PopText(playerHPText.transform, originalPlayerHPScale));
                StartCoroutine(Routine_CameraShake());
                yield return new WaitForSeconds(countStepInterval);
            }
        }

        // --- APPLY DAMAGE TO OPPONENT ---
        if (result.netDamageToOpponent > 0)
        {
            int totalDamage = result.netDamageToOpponent;
            for (int i = 1; i <= totalDamage; i++)
            {
                currentOpponentHP = Mathf.Max(0, currentOpponentHP - 1);
                UpdateHPUI();

                PlaySFX(hpDamageTickSFX);
                if (opponentHPText != null) StartCoroutine(Routine_PopText(opponentHPText.transform, originalOpponentHPScale));
                StartCoroutine(Routine_CameraShake());
                yield return new WaitForSeconds(countStepInterval);
            }
        }

        currentPlayerHP = result.finalPlayerHP;
        currentOpponentHP = result.finalOpponentHP;
        UpdateHPUI();

        yield return new WaitForSeconds(0.5f);
        yield return StartCoroutine(Routine_CheckNextRoundOrEndGame());
    }

    private void ProcessPlayedSupportCards(List<BalatroCardController> cards, string ownerName)
    {
        if (cards == null) return;

        foreach (var card in cards)
        {
            if (card == null || card.Category != CardCategory.Support) continue;

            string name = card.SupportName;
            string desc = card.SupportEffect;
            string durationLabel = card.IsForever ? "FOREVER (Rest of Game)" : "IMMEDIATE (Current Round Only)";

            Debug.Log($"[Support Trigger] {ownerName} played Support Card: '{name}' | Duration: {durationLabel} | Description: '{desc}'");
        }
    }

    private void HighlightCardsByCategory(List<BalatroCardController> playerList, List<BalatroCardController> opponentList, CardCategory category)
    {
        foreach (var card in playerList) card.HighlightCardForCategory(category);
        foreach (var card in opponentList) card.HighlightCardForCategory(category);
    }

    private void ResetCardHighlights(List<BalatroCardController> playerList, List<BalatroCardController> opponentList)
    {
        foreach (var card in playerList) card.ResetCombatHighlight();
        foreach (var card in opponentList) card.ResetCombatHighlight();
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

    private IEnumerator Routine_CountUpPair(
        int playerTargetVal, TextMeshProUGUI playerText, Vector3 playerScale,
        int opponentTargetVal, TextMeshProUGUI opponentText, Vector3 opponentScale,
        AudioClip countClip)
    {
        int maxSteps = Mathf.Max(playerTargetVal, opponentTargetVal);

        for (int step = 1; step <= maxSteps; step++)
        {
            if (step <= playerTargetVal && playerText != null)
            {
                playerText.text = step.ToString();
                StartCoroutine(Routine_PopText(playerText.transform, playerScale));
            }

            if (step <= opponentTargetVal && opponentText != null)
            {
                opponentText.text = step.ToString();
                StartCoroutine(Routine_PopText(opponentText.transform, opponentScale));
            }

            PlayPitchEscalatedSound(countClip, step, maxSteps);
            yield return new WaitForSeconds(countStepInterval);
        }
    }

    private void PlayPitchEscalatedSound(AudioClip clip, int currentStep, int totalSteps)
    {
        if (clip == null || audioSource == null || totalSteps <= 0) return;

        float t = (totalSteps > 1) ? (float)(currentStep - 1) / (totalSteps - 1) : 1f;
        float pitch = Mathf.Lerp(countMinPitch, countMaxPitch, t);

        audioSource.pitch = pitch;
        audioSource.PlayOneShot(clip);
    }

    private void PlaySFX(AudioClip clip)
    {
        if (clip == null || audioSource == null) return;
        audioSource.pitch = 1.0f;
        audioSource.PlayOneShot(clip);
    }

    private IEnumerator Routine_CheckNextRoundOrEndGame()
    {
        if (currentOpponentHP <= 0 && currentPlayerHP <= 0)
        {
            Debug.Log("[Match Over] BOTH PLAYERS KNOCKED OUT! DRAW GAME!");
            TriggerGameOver(isPlayerWinner: false);
            yield break;
        }
        if (currentOpponentHP <= 0)
        {
            Debug.Log("[Match Over] PLAYER WINS BY KNOCKOUT!");
            TriggerGameOver(isPlayerWinner: true);
            yield break;
        }
        if (currentPlayerHP <= 0)
        {
            Debug.Log("[Match Over] OPPONENT WINS BY KNOCKOUT!");
            TriggerGameOver(isPlayerWinner: false);
            yield break;
        }

        yield return StartCoroutine(Routine_ClearAllSubmittedCards());
        ResetTurnUI();

        if (timerManager != null)
        {
            ResetTurnBlocker();
            timerManager.TriggerNextRound();
        }
    }

    public void TriggerGameOver(bool isPlayerWinner)
    {
        if (activeCombatCoroutine != null) StopCoroutine(activeCombatCoroutine);
        StopAllCoroutines();

        if (timerManager != null) timerManager.StopAllCoroutines();

        if (raycastBlockerImage != null) raycastBlockerImage.gameObject.SetActive(true);

        if (gameOverPanel != null) gameOverPanel.SetActive(true);

        if (isPlayerWinner)
        {
            if (victoryText != null) victoryText.gameObject.SetActive(true);
            if (lossText != null) lossText.gameObject.SetActive(false);
            PlaySFX(victorySFX);
        }
        else
        {
            if (victoryText != null) victoryText.gameObject.SetActive(false);
            if (lossText != null) lossText.gameObject.SetActive(true);
            PlaySFX(defeatSFX);
        }
    }

    public void OnRestartButtonClicked()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    public void OnQuitButtonClicked()
    {
#if UNITY_EDITOR
        EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void ResetTurnUI()
    {
        if (playerAttackText != null) playerAttackText.text = "0";
        if (playerDefenseText != null) playerDefenseText.text = "0";
        if (opponentAttackText != null) opponentAttackText.text = "0";
        if (opponentDefenseText != null) opponentDefenseText.text = "0";
        if (centerDifferenceText != null) centerDifferenceText.text = "";
    }

    private IEnumerator Routine_ClearAllSubmittedCards()
    {
        List<Coroutine> cleanupRoutines = new List<Coroutine>();

        if (handManager != null && selectedHandTransform != null)
        {
            cleanupRoutines.Add(StartCoroutine(Routine_ProcessContainerCleanup(handManager, selectedHandTransform)));
        }

        if (aiRival != null)
        {
            PlayerHandManager aiHandManager = aiRival.GetComponentInChildren<PlayerHandManager>();
            RectTransform aiSelectedTransform = aiRival.RivalSelectedHandTransform;

            if (aiHandManager != null && aiSelectedTransform != null)
            {
                cleanupRoutines.Add(StartCoroutine(Routine_ProcessContainerCleanup(aiHandManager, aiSelectedTransform)));
            }
        }

        foreach (var routine in cleanupRoutines)
        {
            yield return routine;
        }
    }

    private IEnumerator Routine_ProcessContainerCleanup(PlayerHandManager ownerHandManager, RectTransform selectedTransform)
    {
        CardUI[] cardsInContainer = selectedTransform.GetComponentsInChildren<CardUI>();
        List<CardUI> immediateCardsToDestroy = new List<CardUI>();

        foreach (CardUI card in cardsInContainer)
        {
            if (card == null) continue;

            BalatroCardController controller = card.GetComponent<BalatroCardController>();
            if (controller != null && controller.Category == CardCategory.Support)
            {
                if (!controller.IsForever)
                {
                    immediateCardsToDestroy.Add(card);
                }
            }
        }

        if (immediateCardsToDestroy.Count > 0)
        {
            yield return StartCoroutine(Routine_AnimateImmediateCardsDisappearance(immediateCardsToDestroy));
        }

        ownerHandManager.ReturnSubmittedCardsToHand(selectedTransform);
    }

    private IEnumerator Routine_AnimateImmediateCardsDisappearance(List<CardUI> cardsToClear)
    {
        float popUpDuration = 0.12f;
        float shrinkDuration = 0.18f;
        Vector3 popScale = new Vector3(1.3f, 1.3f, 1f);

        float elapsed = 0f;
        while (elapsed < popUpDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / popUpDuration;

            foreach (CardUI card in cardsToClear)
            {
                if (card != null) card.transform.localScale = Vector3.Lerp(Vector3.one, popScale, t);
            }
            yield return null;
        }

        elapsed = 0f;
        while (elapsed < shrinkDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / shrinkDuration;

            foreach (CardUI card in cardsToClear)
            {
                if (card != null) card.transform.localScale = Vector3.Lerp(popScale, Vector3.zero, t);
            }
            yield return null;
        }

        foreach (CardUI card in cardsToClear)
        {
            if (card != null) Destroy(card.gameObject);
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
        if (playerHPText != null) playerHPText.text = $"{currentPlayerHP}";
        if (opponentHPText != null) opponentHPText.text = $"{currentOpponentHP}";
    }

    public void ResetTurnBlocker()
    {
        if (raycastBlockerImage != null) raycastBlockerImage.gameObject.SetActive(false);
    }
}