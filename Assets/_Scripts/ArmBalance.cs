using UnityEngine;

/// <summary>
/// Fait pivoter les segments des bras du personnage par simple code (aucune
/// physique, aucun HingeJoint2D) pour donner un retour visuel pendant que le
/// joueur essaie de garder une assiette en équilibre.
///
/// Chaque segment (Front/Back) a sa propre valeur de -1 à +1, pilotée
/// indépendamment par SetFrontBalance(...) / SetBackBalance(...) : le stick
/// gauche pilote le segment "avant-bras" des deux bras (mirroré), le stick
/// droit pilote le segment "après-bras" des deux bras.
///
/// Mise en place dans Unity :
/// 1. Placez ce script sur le GameObject racine du personnage (PErso).
/// 2. Dans la liste "Arms", ajoutez une entrée par segment (ex. "L-avant-bras"
///    et "R-avant-bras" en Front, "L-apres-bras" et "R-apres-bras" en Back)
///    en glissant son Transform, en choisissant son "Side" (Front/Back), et
///    laissez "Rest Angle" à l'angle déjà présent dans le prefab (visible
///    dans l'Inspecteur du Transform de ce segment).
/// </summary>
public class ArmBalance : MonoBehaviour
{
    public enum ArmSide { Front, Back }

    [System.Serializable]
    public class Arm
    {
        public string name;
        public Transform target;

        [Tooltip("Quel segment : détermine quelle valeur de balance (avant/après) fait bouger ce Transform.")]
        public ArmSide side;

        [Tooltip("Angle de repos du segment (degrés), tel qu'il est posé dans le prefab.")]
        public float restAngle;

        [Tooltip("Angle ajouté (ou retiré) au maximum quand la balance de son côté = ±1.")]
        public float maxSwing = 40f;
    }

    public Arm[] arms;

    [Range(-1f, 1f)]
    [Tooltip("État du segment avant (avant-bras) : -1 à +1. Peut être réglé ici pour tester, ou piloté par SetFrontBalance().")]
    public float frontBalance = 0f;

    [Range(-1f, 1f)]
    [Tooltip("État du segment arrière (après-bras) : -1 à +1. Peut être réglé ici pour tester, ou piloté par SetBackBalance().")]
    public float backBalance = 0f;

    [Tooltip("Vitesse à laquelle les segments suivent un changement de balance (plus haut = plus réactif).")]
    public float followSpeed = 8f;

    private float currentFrontBalance;
    private float currentBackBalance;

    private void Update()
    {
        currentFrontBalance = Mathf.MoveTowards(currentFrontBalance, frontBalance, followSpeed * Time.deltaTime);
        currentBackBalance = Mathf.MoveTowards(currentBackBalance, backBalance, followSpeed * Time.deltaTime);

        foreach (var arm in arms)
        {
            if (arm.target == null) continue;
            float sideBalance = arm.side == ArmSide.Front ? currentFrontBalance : currentBackBalance;
            float angle = arm.restAngle - sideBalance * arm.maxSwing;
            arm.target.localRotation = Quaternion.Euler(0, 0, angle);
        }
    }

    /// <summary>À appeler depuis le script qui gère l'input du segment avant (stick gauche), chaque frame.</summary>
    public void SetFrontBalance(float value)
    {
        frontBalance = Mathf.Clamp(value, -1f, 1f);
    }

    /// <summary>À appeler depuis le script qui gère l'input du segment arrière (stick droit), chaque frame.</summary>
    public void SetBackBalance(float value)
    {
        backBalance = Mathf.Clamp(value, -1f, 1f);
    }
}
