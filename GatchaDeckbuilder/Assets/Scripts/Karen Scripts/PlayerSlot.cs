using UnityEngine;

public class PlayerSlot : MonoBehaviour
{
    [Tooltip("1 for Player 1 (Host/Bottom), 2 for Player 2 (Client/Top)")]
    public int slot = 1;
}