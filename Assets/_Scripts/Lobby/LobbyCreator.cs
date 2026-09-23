using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(PlayerInputManager))]
public class LobbyCreator : MonoBehaviour
{
    [Tooltip("Parent commun sous lequel chaque curseur est déplacé une fois instancié automatiquement par le PlayerInputManager (dont le Player Prefab doit être le prefab Pointer). Laisser vide pour utiliser ce transform.")]
    public Transform pointersParent;

    [Tooltip("Choices de ce lobby, assigné à chaque curseur au moment du spawn pour qu'il puisse détecter directement les emplacements sous lui, sans dépendre du raycasting UI.")]
    public Choices choices;

    [Tooltip("Bouton de validation, assigné à chaque curseur au moment du spawn pour qu'il puisse le détecter directement, sans dépendre du raycasting UI.")]
    public LobbyValidateButton validateButton;

    [Header("Positions de départ")]
    [Tooltip("Emplacement de départ pour chaque joueur, dans l'ordre (index 0 = premier joueur à rejoindre, etc.). Placez autant de repères vides dans la scène qu'il y a de joueurs possibles.")]
    public Transform[] spawnPoints;

    [Tooltip("Si spawnPoints n'a pas assez d'entrées pour un joueur, espace ses curseurs de ce décalage (multiplié par playerIndex) à partir de pointersParent.")]
    public Vector2 fallbackSpawnOffset = new Vector2(1.5f, 0f);

    private void Awake()
    {
        if (pointersParent == null) pointersParent = transform;
    }

    private void OnPlayerJoined(PlayerInput playerInput)
    {
        StartCoroutine(SpawnPointer(playerInput));
    }

    private void OnPlayerLeft(PlayerInput playerInput)
    {
        StartCoroutine(ReleasePlayer(playerInput.playerIndex));
    }

    private IEnumerator ReleasePlayer(int playerIndex)
    {
        // Une frame d'attente : au moment où OnPlayerLeft est envoyé, le joueur qui
        // part peut encore figurer dans PlayerInput.all, et AllPlayersHaveChosen()
        // croirait qu'il manque encore son choix.
        yield return null;

        if (choices != null) choices.RemovePlayer(playerIndex);
    }

    public IEnumerator SpawnPointer(PlayerInput playerInput)
    {
        yield return null;

        LobbyPointer pointer = playerInput.GetComponent<LobbyPointer>();
        if (pointer == null) yield break;

        int playerIndex = playerInput.playerIndex;

        Gamepad joinedGamepad = null;
        foreach (var device in playerInput.devices)
        {
            if (device is Gamepad gp)
            {
                joinedGamepad = gp;
                break;
            }
        }

        if (joinedGamepad == null)
        {
            string devices = "";
            foreach (var device in playerInput.devices) devices += $"{device.displayName} ({device.GetType().Name}) ";
            Debug.LogWarning($"[LobbyCreator] Aucune Gamepad trouvée pour le joueur {playerIndex} parmi ses appareils : {devices}. Le curseur utilisera le clavier si disponible.");
        }

        LobbySelectionData.RegisterGamepad(playerIndex, joinedGamepad);

        playerInput.transform.SetParent(pointersParent, false);
        pointer.gamepad = joinedGamepad;
        pointer.playerIndex = playerIndex;
        pointer.choices = choices;
        pointer.validateButton = validateButton;
        pointer.name = $"LobbyPointer_P{playerIndex}";
        PlacePointer(pointer, playerIndex);
    }

    private void PlacePointer(LobbyPointer pointer, int playerIndex)
    {
        if (spawnPoints != null && playerIndex < spawnPoints.Length && spawnPoints[playerIndex] != null)
        {
            Transform point = spawnPoints[playerIndex];
            pointer.transform.SetPositionAndRotation(point.position, point.rotation);
        }
        else
        {
            pointer.transform.position = pointersParent.position + (Vector3)(fallbackSpawnOffset * playerIndex);
        }
    }
}
