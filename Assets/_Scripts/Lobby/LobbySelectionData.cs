using System.Collections.Generic;
using UnityEngine.InputSystem;

/// <summary>
/// Contient la sélection de chaque joueur (playerIndex -> index de l'emplacement choisi)
/// et sa manette, le temps de passer de la scène de lobby à la scène suivante. Une classe
/// statique suffit : sa valeur survit à un SceneManager.LoadScene tant que l'application
/// reste en cours d'exécution (les InputDevice ne sont pas des UnityEngine.Object détruits
/// au changement de scène, donc les garder ici est sûr).
/// </summary>
public static class LobbySelectionData
{
    public static Dictionary<int, int> PlayerChoices { get; private set; } = new Dictionary<int, int>();
    public static Dictionary<int, Gamepad> PlayerGamepads { get; private set; } = new Dictionary<int, Gamepad>();

    public static void Set(Dictionary<int, int> playerChoices)
    {
        PlayerChoices = playerChoices ?? new Dictionary<int, int>();
    }

    public static void RegisterGamepad(int playerIndex, Gamepad gamepad)
    {
        PlayerGamepads[playerIndex] = gamepad;
    }
}
