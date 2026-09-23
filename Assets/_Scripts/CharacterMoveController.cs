using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Déplace le personnage horizontalement (gauche/droite, sans saut), piloté par
/// le joueur "bas du corps" de la paire. La manette est assignée directement
/// (typiquement par le spawner de la scène, à partir de LobbySelectionData.PlayerGamepads).
///
/// Les gâchettes (LT/RT) donnent en plus un coup sec sur l'équilibre du joueur
/// "haut du corps" (PlayerBalanceController.Jolt), pour perturber son
/// adversaire/partenaire et rendre l'équilibrage plus vivant.
/// </summary>
public class CharacterMoveController : MonoBehaviour
{
    [Tooltip("Manette qui déplace le personnage (stick gauche) et déclenche les secousses (LT/RT). Assignée par le spawner de la scène.")]
    public Gamepad gamepad;

    [Tooltip("Permet de se déplacer au clavier (A/D) et de secouer (Q/E) pour tester sans manette.")]
    public bool allowKeyboardFallback = true;

    [Tooltip("Vitesse de déplacement horizontal, en unités/seconde à pleine amplitude du stick.")]
    public float moveSpeed = 3f;

    [Tooltip("Zone morte du stick, pour ignorer les petites dérives.")]
    [Range(0f, 0.5f)]
    public float deadZone = 0.15f;

    [Tooltip("Amplitude de la secousse (en degrés) appliquée à PlayerBalanceController quand LT/RT est pressée.")]
    public float shoveAngle = 25f;

    private PlayerBalanceController balance;

    private void Awake()
    {
        balance = GetComponent<PlayerBalanceController>();
    }

    private float ReadStick()
    {
        float x = gamepad != null ? gamepad.leftStick.ReadValue().x : 0f;

        if (allowKeyboardFallback && Keyboard.current != null && Mathf.Abs(x) < deadZone)
        {
            var kb = Keyboard.current;
            if (kb.aKey.isPressed) x = -1f;
            else if (kb.dKey.isPressed) x = 1f;
        }

        if (Mathf.Abs(x) < deadZone) x = 0f;
        return Mathf.Clamp(x, -1f, 1f);
    }

    private void Update()
    {
        float x = ReadStick();
        if (x != 0f) transform.position += Vector3.right * (x * moveSpeed * Time.deltaTime);

        HandleShove();
    }

    private void HandleShove()
    {
        if (balance == null) return;

        bool shoveLeft = (gamepad != null && gamepad.leftTrigger.wasPressedThisFrame)
            || (allowKeyboardFallback && Keyboard.current != null && Keyboard.current.qKey.wasPressedThisFrame);

        bool shoveRight = (gamepad != null && gamepad.rightTrigger.wasPressedThisFrame)
            || (allowKeyboardFallback && Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame);

        if (shoveLeft) balance.Jolt(-shoveAngle);
        if (shoveRight) balance.Jolt(shoveAngle);
    }
}
