using UnityEngine;
using UnityEngine.UI;

public class TokenButton : MonoBehaviour
{
    [Header("Token Config")]
    public int tokenIndex; // 1-based index (e.g., 1 to 9)

    private Image buttonImage;
    private TokenSelectionManager manager;

    private void Awake()
    {
        buttonImage = GetComponent<Image>();
    }

    public void Setup(int index, TokenSelectionManager parentManager)
    {
        tokenIndex = index;
        manager = parentManager;
    }
}