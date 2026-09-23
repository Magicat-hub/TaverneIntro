using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Les 7 pièces (I, O, T, S, Z, J, L) et la rotation officielle des Tetris modernes
/// (SRS, « Super Rotation System »).
///
/// Chaque pièce vit dans une petite boîte (3x3, ou 4x4 pour la barre I) et tourne
/// autour du centre de cette boîte, avec 4 états : 0 (apparition), R (quart de tour
/// horaire), 2 (demi-tour), L (quart de tour anti-horaire). Si la rotation ne passe
/// pas telle quelle, on essaie 4 petits décalages (« wall kicks ») dans un ordre
/// précis : c'est ce qui permet de tourner contre un mur ou de glisser un T dans un
/// trou, exactement comme les joueurs habitués s'y attendent.
///
/// Repère : x vers la droite, y vers le HAUT (comme la grille du casier).
/// </summary>
public static class CanTetrisShapes
{
    public const int Count = 7;
    public const int I = 0, O = 1, T = 2, S = 3, Z = 4, J = 5, L = 6;

    // État 0 de chaque pièce, en coordonnées dans sa boîte.
    private static readonly Vector2Int[][] Spawn =
    {
        new[] { new Vector2Int(0, 2), new Vector2Int(1, 2), new Vector2Int(2, 2), new Vector2Int(3, 2) }, // I (boîte 4x4)
        new[] { new Vector2Int(1, 1), new Vector2Int(2, 1), new Vector2Int(1, 2), new Vector2Int(2, 2) }, // O (ne tourne pas)
        new[] { new Vector2Int(0, 1), new Vector2Int(1, 1), new Vector2Int(2, 1), new Vector2Int(1, 2) }, // T
        new[] { new Vector2Int(0, 1), new Vector2Int(1, 1), new Vector2Int(1, 2), new Vector2Int(2, 2) }, // S
        new[] { new Vector2Int(0, 2), new Vector2Int(1, 2), new Vector2Int(1, 1), new Vector2Int(2, 1) }, // Z
        new[] { new Vector2Int(0, 2), new Vector2Int(0, 1), new Vector2Int(1, 1), new Vector2Int(2, 1) }, // J
        new[] { new Vector2Int(2, 2), new Vector2Int(0, 1), new Vector2Int(1, 1), new Vector2Int(2, 1) }, // L
    };

    // Décalages SRS (x, y vers le haut) pour J, L, S, T, Z. Index : [état de départ, sens (0 = horaire, 1 = anti-horaire)].
    private static readonly Vector2Int[][] KicksJLSTZ =
    {
        K(0, 0, -1, 0, -1, 1, 0, -2, -1, -2), // 0 -> R
        K(0, 0, 1, 0, 1, 1, 0, -2, 1, -2),    // 0 -> L
        K(0, 0, 1, 0, 1, -1, 0, 2, 1, 2),     // R -> 2
        K(0, 0, 1, 0, 1, -1, 0, 2, 1, 2),     // R -> 0
        K(0, 0, 1, 0, 1, 1, 0, -2, 1, -2),    // 2 -> L
        K(0, 0, -1, 0, -1, 1, 0, -2, -1, -2), // 2 -> R
        K(0, 0, -1, 0, -1, -1, 0, 2, -1, 2),  // L -> 0
        K(0, 0, -1, 0, -1, -1, 0, 2, -1, 2),  // L -> 2
    };

    // Décalages SRS de la barre I.
    private static readonly Vector2Int[][] KicksI =
    {
        K(0, 0, -2, 0, 1, 0, -2, -1, 1, 2),   // 0 -> R
        K(0, 0, -1, 0, 2, 0, -1, 2, 2, -1),   // 0 -> L
        K(0, 0, -1, 0, 2, 0, -1, 2, 2, -1),   // R -> 2
        K(0, 0, 2, 0, -1, 0, 2, 1, -1, -2),   // R -> 0
        K(0, 0, 2, 0, -1, 0, 2, 1, -1, -2),   // 2 -> L
        K(0, 0, 1, 0, -2, 0, 1, -2, -2, 1),   // 2 -> R
        K(0, 0, 1, 0, -2, 0, 1, -2, -2, 1),   // L -> 0
        K(0, 0, -2, 0, 1, 0, -2, -1, 1, 2),   // L -> 2
    };

    private static Vector2Int[] K(params int[] xy)
    {
        var kicks = new Vector2Int[xy.Length / 2];
        for (int i = 0; i < kicks.Length; i++) kicks[i] = new Vector2Int(xy[i * 2], xy[i * 2 + 1]);
        return kicks;
    }

    private static readonly Vector2Int[][][] States = BuildStates();

    private static Vector2Int[][][] BuildStates()
    {
        var all = new Vector2Int[Count][][];
        for (int type = 0; type < Count; type++)
        {
            all[type] = new Vector2Int[4][];
            all[type][0] = Spawn[type];

            // Rotation horaire autour du centre de la boîte : (x, y) -> (y, n - x), n = taille - 1.
            int n = type == I || type == O ? 3 : 2;
            for (int state = 1; state < 4; state++)
            {
                var previous = all[type][state - 1];
                var rotated = new Vector2Int[previous.Length];
                for (int i = 0; i < previous.Length; i++)
                    rotated[i] = type == O ? previous[i] : new Vector2Int(previous[i].y, n - previous[i].x);
                all[type][state] = rotated;
            }
        }
        return all;
    }

    /// <summary>Les 4 cases de la pièce dans l'état donné (0 à 3), relatives au coin de sa boîte.</summary>
    public static Vector2Int[] Cells(int type, int state)
    {
        return States[type][((state % 4) + 4) % 4];
    }

    /// <summary>Largeur de la boîte de la pièce (pour la centrer à l'apparition).</summary>
    public static int BoxSize(int type)
    {
        return type == I || type == O ? 4 : 3;
    }

    /// <summary>Décalages à essayer, dans l'ordre, pour passer de <paramref name="fromState"/> à l'état suivant ou précédent.</summary>
    public static Vector2Int[] Kicks(int type, int fromState, bool clockwise)
    {
        if (type == O) return new[] { Vector2Int.zero };
        int index = (((fromState % 4) + 4) % 4) * 2 + (clockwise ? 0 : 1);
        return type == I ? KicksI[index] : KicksJLSTZ[index];
    }

    /// <summary>
    /// Tirage « par sac » : les 7 pièces sortent chacune une fois dans un ordre mélangé,
    /// puis on recommence. Évite les longues séries sans barre (I) de l'aléatoire pur.
    /// </summary>
    public class Bag
    {
        private readonly List<int> pending = new List<int>();

        public int Next()
        {
            if (pending.Count == 0)
            {
                for (int i = 0; i < Count; i++) pending.Add(i);
                for (int i = pending.Count - 1; i > 0; i--)
                {
                    int j = Random.Range(0, i + 1);
                    (pending[i], pending[j]) = (pending[j], pending[i]);
                }
            }

            int type = pending[pending.Count - 1];
            pending.RemoveAt(pending.Count - 1);
            return type;
        }
    }
}
