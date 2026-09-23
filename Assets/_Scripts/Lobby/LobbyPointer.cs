using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

[RequireComponent(typeof(RectTransform))]
public class LobbyPointer : MonoBehaviour
{
    [Tooltip("Manette assignée directement (ex: par LobbyCreator au moment du join). Prioritaire sur playerIndex : évite de dépendre de l'ordre de Gamepad.all, qui peut contenir des entrées fantômes (Steam, plusieurs backends, etc.) et ne pas correspondre à l'ordre de connexion.")]
    public Gamepad gamepad;

    [Tooltip("Utilisé seulement si aucune manette n'est assignée directement : index dans Gamepad.all (0 = première branchée, etc.). Pratique pour tester ce curseur seul dans la scène, sans passer par LobbyCreator.")]
    public int playerIndex = 0;

    [Tooltip("Permet de déplacer ce curseur au clavier (ZQSD/flèches) et de cliquer à l'Entrée, pour tester sans manette.")]
    public bool allowKeyboardFallback = true;

    [Header("Déplacement")]
    [Tooltip("Vitesse du curseur en pixels/seconde à pleine amplitude du stick.")]
    public float moveSpeed = 1000f;

    [Tooltip("Zone morte du stick, pour ignorer les petites dérives.")]
    [Range(0f, 0.5f)]
    public float deadZone = 0.15f;

    [Header("Référence")]
    [Tooltip("Canvas parent du curseur, utilisé pour rester dans les limites de l'écran et convertir les positions. Laisser vide pour le chercher automatiquement.")]
    public Canvas canvas;
    public TMP_Text text;

    [Tooltip("Choix assignés par LobbyCreator au moment du spawn. Permet de détecter directement quel emplacement est sous le curseur (comparaison de position en espace monde), sans dépendre du raycasting UI qui est sensible au Canvas/à la caméra/aux échelles imbriquées.")]
    public Choices choices;

    [Tooltip("Bouton de validation assigné par LobbyCreator au moment du spawn. Détecté directement comme les choix, sans dépendre du raycasting UI.")]
    public LobbyValidateButton validateButton;
    [Tooltip("Affiche dans la Console le nombre de manettes détectées et la valeur brute du stick de chacune, pour déboguer.")]
    public bool debugLogGamepad = false;

    [Header("Apparence par joueur")]
    [Tooltip("Couleur attribuée selon playerIndex (index 0 = première couleur, etc.). Si playerIndex dépasse la liste, les couleurs sont réutilisées en boucle.")]
    public Color[] playerColors = new Color[]
    {
        new Color(0.90f, 0.20f, 0.20f),
        new Color(0.20f, 0.45f, 0.90f),
        new Color(0.95f, 0.85f, 0.15f),
        new Color(0.25f, 0.80f, 0.35f),
    };

    [Header("Animation de clic")]
    [Tooltip("Échelle atteinte au pic de l'animation, multipliée à l'échelle de base du curseur.")]
    public float clickPunchScale = 1.3f;

    [Tooltip("Durée totale de l'animation de clic, en secondes.")]
    public float clickPunchDuration = 0.15f;

    private RectTransform rectTransform;
    private SpriteRenderer spriteRenderer;
    private bool clickHeldLastFrame;
    private int lastAppliedPlayerIndex = int.MinValue;
    private Vector3 baseScale;
    private Coroutine clickAnimation;

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (canvas == null) canvas = GetComponentInParent<Canvas>();
        baseScale = rectTransform.localScale;
    }

    private void Update()
    {
        Move();
        HandleClick();
        UpdatePlayerVisuals();
    }

    private void UpdatePlayerVisuals()
    {
        if (playerIndex == lastAppliedPlayerIndex) return;
        lastAppliedPlayerIndex = playerIndex;

        if (text != null) text.text = $"J{playerIndex + 1}";

        if (spriteRenderer != null && playerColors != null && playerColors.Length > 0)
        {
            int colorIndex = ((playerIndex % playerColors.Length) + playerColors.Length) % playerColors.Length;
            spriteRenderer.color = playerColors[colorIndex];
        }
    }

    private void Move()
    {
        Vector2 stick = ReadStick();
        if (stick.sqrMagnitude < deadZone * deadZone) stick = Vector2.zero;

        rectTransform.anchoredPosition += stick * moveSpeed * Time.deltaTime;
        ClampToCanvas();
    }

    private Gamepad ResolveGamepad()
    {
        if (gamepad != null) return gamepad;

        var pads = Gamepad.all;
        if (playerIndex >= 0 && playerIndex < pads.Count) return pads[playerIndex];
        return null;
    }

    private Vector2 ReadStick()
    {
        Vector2 stick = Vector2.zero;

        Gamepad pad = ResolveGamepad();
        if (pad != null) stick = pad.leftStick.ReadValue();

        if (debugLogGamepad)
            Debug.Log($"[LobbyPointer] gamepad={(pad != null ? pad.displayName : "aucune")} stick={stick:F2}");

        if (allowKeyboardFallback && Keyboard.current != null && stick == Vector2.zero)
        {
            var kb = Keyboard.current;
            float x = (kb.dKey.isPressed || kb.rightArrowKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed || kb.leftArrowKey.isPressed ? 1f : 0f);
            float y = (kb.wKey.isPressed || kb.upArrowKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed || kb.downArrowKey.isPressed ? 1f : 0f);
            stick = new Vector2(x, y);
        }

        return stick;
    }

    private void ClampToCanvas()
    {
        var canvasRect = canvas != null ? canvas.GetComponent<RectTransform>() : null;
        if (canvasRect == null) return;

        Vector2 halfCanvas = canvasRect.rect.size * 0.5f;
        Vector2 pos = rectTransform.anchoredPosition;
        pos.x = Mathf.Clamp(pos.x, -halfCanvas.x, halfCanvas.x);
        pos.y = Mathf.Clamp(pos.y, -halfCanvas.y, halfCanvas.y);
        rectTransform.anchoredPosition = pos;
    }

    private void HandleClick()
    {
        bool clickPressed = IsClickPressed();

        if (clickPressed && !clickHeldLastFrame)
        {
            ClickWhatIsUnderCursor();
            PlayClickAnimation();
        }

        clickHeldLastFrame = clickPressed;
    }

    private void PlayClickAnimation()
    {
        if (clickAnimation != null) StopCoroutine(clickAnimation);
        clickAnimation = StartCoroutine(ClickPunch());
    }

    private IEnumerator ClickPunch()
    {
        float half = clickPunchDuration * 0.5f;

        float t = 0f;
        while (t < half)
        {
            t += Time.deltaTime;
            rectTransform.localScale = Vector3.Lerp(baseScale, baseScale * clickPunchScale, t / half);
            yield return null;
        }

        t = 0f;
        while (t < half)
        {
            t += Time.deltaTime;
            rectTransform.localScale = Vector3.Lerp(baseScale * clickPunchScale, baseScale, t / half);
            yield return null;
        }

        rectTransform.localScale = baseScale;
        clickAnimation = null;
    }

    private bool IsClickPressed()
    {
        Gamepad pad = ResolveGamepad();
        bool padClick = pad != null && pad.buttonSouth.isPressed;
        bool keyboardClick = allowKeyboardFallback && Keyboard.current != null && Keyboard.current.enterKey.isPressed;
        return padClick || keyboardClick;
    }

    private void ClickWhatIsUnderCursor()
    {
        if (choices != null)
        {
            ChoiceSlot slot = choices.GetSlotAtWorldPoint(rectTransform.position);
            if (slot != null)
            {
                slot.OnLobbyClick(playerIndex);
                return;
            }
        }

        if (validateButton != null && validateButton.ContainsWorldPoint(rectTransform.position))
        {
            validateButton.OnLobbyClick(playerIndex);
            return;
        }

        if (EventSystem.current == null) return;

        var pointerData = new PointerEventData(EventSystem.current)
        {
            position = RectTransformUtility.WorldToScreenPoint(canvas != null ? canvas.worldCamera : null, rectTransform.position)
        };

        var results = new List<RaycastResult>();
        EventSystem.current.RaycastAll(pointerData, results);

        foreach (var result in results)
        {
            var lobbyHandler = result.gameObject.GetComponentInParent<ILobbyClickable>();
            if (lobbyHandler != null)
            {
                lobbyHandler.OnLobbyClick(playerIndex);
                break;
            }

            var handler = result.gameObject.GetComponentInParent<IPointerClickHandler>();
            if (handler != null)
            {
                handler.OnPointerClick(pointerData);
                break;
            }
        }
    }
}
