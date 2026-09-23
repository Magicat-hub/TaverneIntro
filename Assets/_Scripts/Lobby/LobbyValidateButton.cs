using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Bouton qui devient cliquable une fois que tous les joueurs connectés ont fait un choix,
/// et qui envoie leur sélection à la scène suivante via LobbySelectionData.
/// </summary>
[RequireComponent(typeof(Button))]
public class LobbyValidateButton : MonoBehaviour, ILobbyClickable
{
    [Tooltip("Choices dont on vérifie que tous les joueurs ont fait un choix.")]
    public Choices choices;

    [Tooltip("Nom de la scène à charger une fois la sélection validée (doit être ajoutée dans Build Settings).")]
    public string nextSceneName;

    private Button button;

    private void Awake()
    {
        button = GetComponent<Button>();
    }

    private void OnEnable()
    {
        button.onClick.AddListener(Validate);
        if (choices != null) choices.onChoicePicked.AddListener(OnChoicePicked);
        RefreshInteractable();
    }

    private void OnDisable()
    {
        button.onClick.RemoveListener(Validate);
        if (choices != null) choices.onChoicePicked.RemoveListener(OnChoicePicked);
    }

    private void OnChoicePicked(GameObject slot, int playerIndex)
    {
        RefreshInteractable();
    }

    private void RefreshInteractable()
    {
        button.interactable = choices != null && choices.AllPlayersHaveChosen();
    }

    private void Validate()
    {
        if (choices == null || !choices.AllPlayersHaveChosen()) return;

        LobbySelectionData.Set(choices.GetSelectionSnapshot());
        SceneManager.LoadScene(nextSceneName);
    }

    public bool ContainsWorldPoint(Vector3 worldPoint)
    {
        RectTransform rect = transform as RectTransform;
        if (rect == null) return false;

        Vector3 local = rect.InverseTransformPoint(worldPoint);
        return rect.rect.Contains(local);
    }

    public void OnLobbyClick(int playerIndex)
    {
        Validate();
    }
}
