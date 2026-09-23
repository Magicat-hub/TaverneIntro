using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

public class Choices : MonoBehaviour
{
    [Tooltip("Prefab du choix, identique pour chaque emplacement.")]
    public GameObject choicePrefab;

    [Tooltip("Nombre d'emplacements identiques à instancier.")]
    public int choiceCount = 3;

    [Tooltip("Nombre maximum de joueurs pouvant sélectionner le même emplacement en même temps.")]
    public int maxPlayersPerChoice = 2;

    [Tooltip("Couleur attribuée à chaque joueur selon son index (index 0 = premier joueur, etc.), utilisée pour teinter visuellement le choix qu'il sélectionne. Réutilisées en boucle s'il y a plus de joueurs que de couleurs.")]
    public Color[] playerColors = new Color[]
    {
        new Color(0.90f, 0.20f, 0.20f),
        new Color(0.20f, 0.45f, 0.90f),
        new Color(0.95f, 0.85f, 0.15f),
        new Color(0.25f, 0.80f, 0.35f),
    };

    [Header("Placement")]
    [Tooltip("Parent commun sous lequel chaque choix est instancié. Laisser vide pour utiliser ce transform.")]
    public Transform choicesParent;

    [Tooltip("Emplacements où placer chaque choix, dans l'ordre. Prioritaire sur la répartition automatique. Si vide ou insuffisant pour un choix donné, celui-ci est réparti automatiquement dans choicesParent.")]
    public Transform[] slots;

    [Tooltip("Marge laissée sur les bords gauche/droit de choicesParent lors de la répartition automatique (en unités locales du conteneur).")]
    public float containerPadding = 0f;

    [Tooltip("Écart utilisé pour espacer les choix si choicesParent n'a pas de RectTransform (donc pas de largeur connue pour les répartir).")]
    public Vector2 fallbackSpacing = new Vector2(2f, 0f);

    [Header("Événement")]
    [Tooltip("Appelé quand la sélection d'un joueur change (le choix précédent de ce joueur, s'il y en a un, est automatiquement libéré). Le GameObject transmis est null quand un joueur quitte le lobby et libère simplement son choix.")]
    public ChoicePickedEvent onChoicePicked;

    private readonly List<ChoiceSlot> spawnedChoices = new List<ChoiceSlot>();
    private readonly Dictionary<int, ChoiceSlot> currentSelectionByPlayer = new Dictionary<int, ChoiceSlot>();

    private void Awake()
    {
        if (choicesParent == null) choicesParent = transform;
    }

    private void Start()
    {
        GenerateChoices();
    }

    public void GenerateChoices()
    {
        ClearChoices();

        if (choicePrefab == null) return;

        for (int i = 0; i < choiceCount; i++)
        {
            GameObject instance = Instantiate(choicePrefab, choicesParent);
            PlaceChoice(instance.transform, i, choiceCount);

            ChoiceSlot slot = instance.GetComponent<ChoiceSlot>();
            if (slot == null) slot = instance.AddComponent<ChoiceSlot>();
            slot.Init(this, maxPlayersPerChoice);

            spawnedChoices.Add(slot);
        }
    }

    private void PlaceChoice(Transform choice, int index, int total)
    {
        if (slots != null && index < slots.Length && slots[index] != null)
        {
            Transform point = slots[index];
            choice.SetPositionAndRotation(point.position, point.rotation);
            return;
        }

        RectTransform containerRect = choicesParent as RectTransform;
        if (containerRect == null) containerRect = choicesParent.GetComponent<RectTransform>();

        if (containerRect != null)
        {
            Rect rect = containerRect.rect;
            float t = total > 1 ? (index + 0.5f) / total : 0.5f;
            float localX = Mathf.Lerp(rect.xMin + containerPadding, rect.xMax - containerPadding, t);

            choice.SetPositionAndRotation(containerRect.TransformPoint(new Vector3(localX, 0f, 0f)), containerRect.rotation);
            return;
        }

        choice.position = choicesParent.position + (Vector3)(fallbackSpacing * index);
    }

    public Color GetPlayerColor(int playerIndex)
    {
        if (playerColors == null || playerColors.Length == 0) return Color.white;

        int i = ((playerIndex % playerColors.Length) + playerColors.Length) % playerColors.Length;
        return playerColors[i];
    }

    public ChoiceSlot GetSlotAtWorldPoint(Vector3 worldPoint)
    {
        foreach (var slot in spawnedChoices)
            if (slot != null && slot.ContainsWorldPoint(worldPoint))
                return slot;

        return null;
    }

    public void RequestSelect(ChoiceSlot slot, int playerIndex)
    {
        if (currentSelectionByPlayer.TryGetValue(playerIndex, out ChoiceSlot previous) && previous == slot)
            return;

        if (!slot.CanAccept(playerIndex)) return;

        if (previous != null) previous.Deselect(playerIndex);

        slot.Select(playerIndex);
        currentSelectionByPlayer[playerIndex] = slot;

        onChoicePicked?.Invoke(slot.gameObject, playerIndex);
    }

    /// <summary>Libère le choix d'un joueur qui quitte le lobby, pour que la validation ne reste pas bloquée à l'attendre.</summary>
    public void RemovePlayer(int playerIndex)
    {
        if (currentSelectionByPlayer.TryGetValue(playerIndex, out ChoiceSlot slot) && slot != null)
            slot.Deselect(playerIndex);

        if (!currentSelectionByPlayer.Remove(playerIndex)) return;

        // Slot null : ce n'est pas un choix, c'est un choix libéré. Les abonnés
        // (LobbyValidateButton) n'ont besoin que du signal pour se réévaluer.
        onChoicePicked?.Invoke(null, playerIndex);
    }

    public bool AllPlayersHaveChosen()
    {
        // On parcourt les joueurs réellement connectés plutôt que 0..playerCount :
        // les index ne sont pas garantis contigus (un joueur qui se déconnecte
        // laisse un trou), et boucler sur le compte bloquait la validation en
        // attendant le choix d'un joueur qui n'existe plus.
        var players = PlayerInput.all;
        if (players.Count == 0) return false;

        foreach (var player in players)
            if (player != null && !currentSelectionByPlayer.ContainsKey(player.playerIndex))
                return false;

        return true;
    }

    public Dictionary<int, int> GetSelectionSnapshot()
    {
        var snapshot = new Dictionary<int, int>();

        foreach (var pair in currentSelectionByPlayer)
            snapshot[pair.Key] = spawnedChoices.IndexOf(pair.Value);

        return snapshot;
    }

    public void ClearChoices()
    {
        foreach (var slot in spawnedChoices)
            if (slot != null) Destroy(slot.gameObject);

        spawnedChoices.Clear();
        currentSelectionByPlayer.Clear();
    }
}

[System.Serializable]
public class ChoicePickedEvent : UnityEvent<GameObject, int> { }
