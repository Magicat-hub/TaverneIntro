using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ChoiceSlot : MonoBehaviour, ILobbyClickable
{
    [Tooltip("Texte affichant les joueurs ayant choisi cet emplacement. Laissé vide pour le chercher automatiquement.")]
    public TMP_Text label;

    [Tooltip("Image dont la couleur change pour montrer quel(s) joueur(s) ont choisi cet emplacement. Laissé vide pour le chercher automatiquement.")]
    public Image background;

    private Choices owner;
    private int maxPlayers = 2;
    private readonly List<int> selectedByPlayers = new List<int>();
    private Color defaultColor = Color.white;

    public void Init(Choices owner, int maxPlayers)
    {
        this.owner = owner;
        this.maxPlayers = maxPlayers;
        if (label == null) label = GetComponentInChildren<TMP_Text>();
        if (background == null) background = GetComponent<Image>();
        if (background != null) defaultColor = background.color;
        UpdateVisual();
    }

    public bool CanAccept(int playerIndex)
    {
        return selectedByPlayers.Contains(playerIndex) || selectedByPlayers.Count < maxPlayers;
    }

    public void Select(int playerIndex)
    {
        if (!selectedByPlayers.Contains(playerIndex)) selectedByPlayers.Add(playerIndex);
        UpdateVisual();
    }

    public void Deselect(int playerIndex)
    {
        selectedByPlayers.Remove(playerIndex);
        UpdateVisual();
    }

    private void UpdateVisual()
    {
        if (label != null)
        {
            var names = new List<string>();
            foreach (int p in selectedByPlayers) names.Add($"J{p + 1}");
            label.text = string.Join(" / ", names);
        }

        if (background == null) return;

        if (selectedByPlayers.Count == 0)
        {
            background.color = defaultColor;
            return;
        }

        Color blended = Color.black;
        foreach (int p in selectedByPlayers) blended += owner.GetPlayerColor(p);
        blended /= selectedByPlayers.Count;
        blended.a = defaultColor.a;
        background.color = blended;
    }

    public void OnLobbyClick(int playerIndex)
    {
        owner.RequestSelect(this, playerIndex);
    }

    public bool ContainsWorldPoint(Vector3 worldPoint)
    {
        RectTransform rect = transform as RectTransform;
        if (rect == null) return false;

        Vector3 local = rect.InverseTransformPoint(worldPoint);
        return rect.rect.Contains(local);
    }
}
