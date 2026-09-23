using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Au lancement de la scène, regroupe les joueurs par emplacement choisi dans le
/// lobby (LobbySelectionData.PlayerChoices) et fait apparaître un personnage par
/// paire : le joueur d'index le plus bas déplace le personnage de gauche à droite,
/// l'autre pivote son core pour équilibrer le plateau (via PlayerBalanceController).
///
/// Les emplacements choisis par un seul joueur (pas de binôme) sont ignorés.
/// </summary>
public class CharacterPairSpawner : MonoBehaviour
{
    [Tooltip("Prefab du personnage à faire apparaître par paire de joueurs (doit avoir PlayerBalanceController).")]
    public GameObject characterPrefab;

    [Tooltip("Emplacements où faire apparaître chaque personnage, dans l'ordre des paires trouvées. Si vide ou insuffisant pour une paire donnée, elle est décalée automatiquement à partir de spawnParent.")]
    public Transform[] spawnPoints;

    [Tooltip("Parent commun sous lequel chaque personnage est instancié. Laisser vide pour utiliser ce transform.")]
    public Transform spawnParent;

    [Tooltip("Si spawnPoints n'a pas assez d'entrées pour une paire, espace les personnages de ce décalage (multiplié par l'index de la paire) à partir de spawnParent.")]
    public Vector2 fallbackSpawnOffset = new Vector2(3f, 0f);

    private void Awake()
    {
        if (spawnParent == null) spawnParent = transform;
    }

    private void Start()
    {
        SpawnPairs();
    }

    public void SpawnPairs()
    {
        if (characterPrefab == null) return;

        var playersBySlot = new Dictionary<int, List<int>>();
        foreach (var pair in LobbySelectionData.PlayerChoices)
        {
            if (!playersBySlot.TryGetValue(pair.Value, out List<int> players))
            {
                players = new List<int>();
                playersBySlot[pair.Value] = players;
            }

            players.Add(pair.Key);
        }

        int pairIndex = 0;
        foreach (var players in playersBySlot.Values)
        {
            if (players.Count < 2) continue;

            players.Sort();
            int moverIndex = players[0];
            int pivotIndex = players[1];

            SpawnCharacter(pairIndex, moverIndex, pivotIndex);
            pairIndex++;
        }
    }

    private void SpawnCharacter(int pairIndex, int moverPlayerIndex, int pivotPlayerIndex)
    {
        GameObject instance = Instantiate(characterPrefab, spawnParent);
        PlaceCharacter(instance.transform, pairIndex);

        // GetComponentInChildren des deux côtés : avec GetComponent, un
        // CharacterMoveController descendu dans la hiérarchie du prefab passait
        // inaperçu et un second exemplaire était ajouté sur la racine.
        var mover = instance.GetComponentInChildren<CharacterMoveController>();
        if (mover == null) mover = instance.AddComponent<CharacterMoveController>();
        mover.gamepad = LobbySelectionData.PlayerGamepads.TryGetValue(moverPlayerIndex, out Gamepad moverPad) ? moverPad : null;

        var pivot = instance.GetComponentInChildren<PlayerBalanceController>();
        if (pivot != null)
            pivot.gamepad = LobbySelectionData.PlayerGamepads.TryGetValue(pivotPlayerIndex, out Gamepad pivotPad) ? pivotPad : null;
    }

    private void PlaceCharacter(Transform character, int pairIndex)
    {
        if (spawnPoints != null && pairIndex < spawnPoints.Length && spawnPoints[pairIndex] != null)
        {
            Transform point = spawnPoints[pairIndex];
            character.SetPositionAndRotation(point.position, point.rotation);
            return;
        }

        character.position = spawnParent.position + (Vector3)(fallbackSpawnOffset * pairIndex);
    }
}
