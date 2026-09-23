using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Lit les commandes d'UN joueur (manette et/ou une moitié du clavier) et les traduit
/// en actions de Tetris pour la frame en cours.
///
/// Manette Xbox (une idée = un bouton, et HAUT tourne comme au clavier) :
///   stick gauche ou croix : bouger ; HAUT = tourner ; BAS = descendre vite
///   A      : tourner
///   B      : tourner dans l'autre sens
///   X      : COMPTOIR (LB, RB et LT marchent aussi)
///   Y / RT : lâcher la pièce d'un coup
/// Clavier J1 : A / D bouger, W tourner, Q autre sens, E comptoir, Espace lâcher, S descendre
///             (sur un clavier AZERTY : Q / D, Z, A, E, Espace, S — mêmes touches physiques).
/// Clavier J2 : flèches, Haut tourner, Maj droite autre sens, Ctrl droit comptoir, Entrée lâcher.
///
/// Confort :
///  - Un déplacement maintenu se répète après un court délai (repeatDelay), puis vite
///    (repeatInterval), comme dans tous les Tetris.
///  - Le stick a une hystérésis (il s'enclenche franchement et se relâche plus bas) et
///    ne compte que son axe dominant : pas de rotation en voulant aller à gauche.
///  - Tourner ou poser au comptoir juste AVANT l'arrivée de la pièce suivante compte
///    quand même (mémoire tampon de bufferTime secondes).
/// </summary>
public class CanTetrisInput
{
    public enum KeySet { None, Left, Right }

    /// <summary>Une ligne de l'aide affichée à l'écran : les touches, puis ce qu'elles font.</summary>
    public struct HelpLine
    {
        public string[] keys;
        public string action;
        public HelpLine(string action, params string[] keys) { this.action = action; this.keys = keys; }
    }

    public Gamepad gamepad;
    public KeySet keys;
    public string label;

    public float repeatDelay = 0.15f;
    public float repeatInterval = 0.04f;
    public float bufferTime = 0.15f;

    /// <summary>Déplacement horizontal à appliquer cette frame : -1, 0 ou +1.</summary>
    public int Move { get; private set; }
    public bool SoftDrop { get; private set; }
    public bool HardDrop { get; private set; }
    public bool Confirm { get; private set; }
    public bool Back { get; private set; }

    public bool IsGamepad => gamepad != null;

    private int heldDirection;
    private float repeatTimer;
    private bool stickHorizontal;
    private bool stickDown;
    private bool stickUp;
    private bool upWasHeld;

    // Actions mémorisées un court instant si la pièce n'est pas encore là pour les recevoir.
    private float rotateClockwiseAt = -1f;
    private float rotateCounterAt = -1f;
    private float holdAt = -1f;

    public CanTetrisInput(Gamepad pad, KeySet keySet, string displayLabel)
    {
        gamepad = pad;
        keys = keySet;
        label = displayLabel;
    }

    public void Poll(float deltaTime)
    {
        int direction = 0;
        bool clockwise = false, counter = false, hard = false, soft = false, hold = false, confirm = false, back = false;

        if (gamepad != null)
        {
            Vector2 stick = gamepad.leftStick.ReadValue();

            // Hystérésis + priorité à l'axe dominant.
            bool mostlyHorizontal = Mathf.Abs(stick.x) >= Mathf.Abs(stick.y) * 0.8f;
            float horizontal = Mathf.Abs(stick.x);
            stickHorizontal = mostlyHorizontal && (stickHorizontal ? horizontal > 0.3f : horizontal > 0.5f);
            stickDown = stickDown ? stick.y < -0.4f : stick.y < -0.6f && !mostlyHorizontal;
            stickUp = stickUp ? stick.y > 0.35f : stick.y > 0.65f && !mostlyHorizontal;

            if (gamepad.dpad.left.isPressed || (stickHorizontal && stick.x < 0f)) direction -= 1;
            if (gamepad.dpad.right.isPressed || (stickHorizontal && stick.x > 0f)) direction += 1;
            soft |= gamepad.dpad.down.isPressed || stickDown;

            // HAUT (croix ou stick) tourne, comme la flèche du haut au clavier.
            bool upHeld = gamepad.dpad.up.isPressed || stickUp;
            clockwise |= upHeld && !upWasHeld;
            upWasHeld = upHeld;

            clockwise |= gamepad.buttonSouth.wasPressedThisFrame;
            counter |= gamepad.buttonEast.wasPressedThisFrame;
            hold |= gamepad.buttonWest.wasPressedThisFrame || gamepad.leftShoulder.wasPressedThisFrame
                 || gamepad.rightShoulder.wasPressedThisFrame || gamepad.leftTrigger.wasPressedThisFrame;
            hard |= gamepad.buttonNorth.wasPressedThisFrame || gamepad.rightTrigger.wasPressedThisFrame;
            confirm |= gamepad.buttonSouth.wasPressedThisFrame || gamepad.startButton.wasPressedThisFrame;
            back |= gamepad.buttonEast.wasPressedThisFrame || gamepad.selectButton.wasPressedThisFrame;
        }

        var kb = Keyboard.current;
        if (kb != null && keys == KeySet.Left)
        {
            if (kb.aKey.isPressed) direction -= 1;
            if (kb.dKey.isPressed) direction += 1;
            soft |= kb.sKey.isPressed;
            hard |= kb.spaceKey.wasPressedThisFrame;
            clockwise |= kb.wKey.wasPressedThisFrame;
            counter |= kb.qKey.wasPressedThisFrame;
            hold |= kb.eKey.wasPressedThisFrame;
            confirm |= kb.spaceKey.wasPressedThisFrame;
            back |= kb.escapeKey.wasPressedThisFrame;
        }
        else if (kb != null && keys == KeySet.Right)
        {
            if (kb.leftArrowKey.isPressed) direction -= 1;
            if (kb.rightArrowKey.isPressed) direction += 1;
            soft |= kb.downArrowKey.isPressed;
            hard |= kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame;
            clockwise |= kb.upArrowKey.wasPressedThisFrame;
            counter |= kb.rightShiftKey.wasPressedThisFrame;
            hold |= kb.rightCtrlKey.wasPressedThisFrame || kb.numpad0Key.wasPressedThisFrame;
            confirm |= kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame;
            back |= kb.escapeKey.wasPressedThisFrame;
        }

        direction = Mathf.Clamp(direction, -1, 1);

        // Appui = un pas tout de suite ; maintien = répétition après repeatDelay.
        Move = 0;
        if (direction != heldDirection)
        {
            heldDirection = direction;
            if (direction != 0)
            {
                Move = direction;
                repeatTimer = repeatDelay;
            }
        }
        else if (direction != 0)
        {
            repeatTimer -= deltaTime;
            if (repeatTimer <= 0f)
            {
                Move = direction;
                repeatTimer += repeatInterval;
            }
        }

        float now = Time.unscaledTime;
        if (clockwise) rotateClockwiseAt = now;
        if (counter) rotateCounterAt = now;
        if (hold) holdAt = now;

        SoftDrop = soft;
        HardDrop = hard;
        Confirm = confirm;
        Back = back;
    }

    /// <summary>Vrai si une rotation horaire a été demandée récemment ; la consomme.</summary>
    public bool ConsumeRotateClockwise() => Consume(ref rotateClockwiseAt);

    /// <summary>Vrai si une rotation anti-horaire a été demandée récemment ; la consomme.</summary>
    public bool ConsumeRotateCounterClockwise() => Consume(ref rotateCounterAt);

    /// <summary>Vrai si le comptoir a été demandé récemment ; la demande est consommée.</summary>
    public bool ConsumeHold() => Consume(ref holdAt);

    /// <summary>Oublie les actions en attente (nouvelle partie...).</summary>
    public void ClearBuffer()
    {
        rotateClockwiseAt = rotateCounterAt = holdAt = -1f;
    }

    private bool Consume(ref float pressedAt)
    {
        if (pressedAt < 0f || Time.unscaledTime - pressedAt > bufferTime) return false;
        pressedAt = -1f;
        return true;
    }

    /// <summary>
    /// Les commandes de CE joueur, pour l'aide affichée sous sa pièce suivante. Pour le
    /// clavier, on affiche la lettre réellement imprimée sur la touche (Z et non W sur un
    /// clavier AZERTY).
    /// </summary>
    public List<HelpLine> Help()
    {
        var lines = new List<HelpLine>();
        var kb = Keyboard.current;

        if (gamepad != null)
        {
            lines.Add(new HelpLine("BOUGER", "STICK"));
            lines.Add(new HelpLine("TOURNER", "A", "HAUT"));
            lines.Add(new HelpLine("AUTRE SENS", "B"));
            lines.Add(new HelpLine("COMPTOIR", "X"));
            lines.Add(new HelpLine("LÂCHER", "Y", "RT"));
            lines.Add(new HelpLine("DESCENDRE", "BAS"));
        }
        else if (keys == KeySet.Left)
        {
            lines.Add(new HelpLine("BOUGER", KeyName(kb, Key.A, "A"), KeyName(kb, Key.D, "D")));
            lines.Add(new HelpLine("TOURNER", KeyName(kb, Key.W, "W")));
            lines.Add(new HelpLine("AUTRE SENS", KeyName(kb, Key.Q, "Q")));
            lines.Add(new HelpLine("COMPTOIR", KeyName(kb, Key.E, "E")));
            lines.Add(new HelpLine("LÂCHER", "ESPACE"));
            lines.Add(new HelpLine("DESCENDRE", KeyName(kb, Key.S, "S")));
        }
        else if (keys == KeySet.Right)
        {
            lines.Add(new HelpLine("BOUGER", "FLÈCHES"));
            lines.Add(new HelpLine("TOURNER", "HAUT"));
            lines.Add(new HelpLine("AUTRE SENS", "MAJ D."));
            lines.Add(new HelpLine("COMPTOIR", "CTRL D."));
            lines.Add(new HelpLine("LÂCHER", "ENTRÉE"));
            lines.Add(new HelpLine("DESCENDRE", "BAS"));
        }
        return lines;
    }

    private static string KeyName(Keyboard kb, Key key, string fallback)
    {
        if (kb == null) return fallback;
        string name = kb[key].displayName;
        return string.IsNullOrEmpty(name) ? fallback : name.ToUpperInvariant();
    }
}
