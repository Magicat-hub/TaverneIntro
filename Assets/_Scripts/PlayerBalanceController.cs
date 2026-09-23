using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

/// <summary>
/// Pilote les segments des bras du personnage avec une seule manette, en double
/// stick : le stick gauche pilote le segment "avant-bras" (des deux bras, en
/// miroir), le stick droit pilote le segment "après-bras". C'est la différence
/// entre les deux qui incline le plateau, comme un vrai geste d'équilibriste.
///
/// Repli clavier pour tester sans manette : flèches gauche/droite = avant-bras,
/// flèches haut/bas = après-bras (A/D sont réservées à CharacterMoveController,
/// qui vit sur le même GameObject).
///
/// La manette est assignée directement (typiquement par le spawner de la scène,
/// à partir de LobbySelectionData.PlayerGamepads) plutôt que déduite d'un index
/// dans Gamepad.all, pour rester cohérent avec le joueur qui a réellement
/// choisi ce rôle dans le lobby.
/// </summary>
public class PlayerBalanceController : MonoBehaviour
{
    [Header("Manette")]
    [Tooltip("Manette qui pilote les segments des bras (stick gauche = avant-bras, stick droit = après-bras). Assignée par le spawner de la scène.")]
    public Gamepad gamepad;

    [Tooltip("Permet de piloter les segments au clavier (flèches gauche/droite = avant-bras, flèches haut/bas = après-bras) pour tester sans manette. Volontairement sur les flèches : A/D appartiennent à CharacterMoveController, qui est sur le même GameObject.")]
    public bool allowKeyboardFallback = true;

    [Header("Références")]
    [Tooltip("Laisser vide pour le chercher automatiquement sur ce GameObject ou ses enfants.")]
    public ArmBalance armBalance;

    [Tooltip("Le plateau (enfant du personnage) avec son Rigidbody2D en Kinematic.")]
    public Rigidbody2D tray;

    [Header("Réglages")]
    [Tooltip("Inclinaison maximale du plateau, en degrés.")]
    public float maxTrayTilt = 20f;

    [Tooltip("Vitesse de rotation du plateau en degrés/seconde. Trop haut = le plateau saute et éjecte l'assiette.")]
    public float trayTiltSpeed = 120f;

    [Tooltip("Zone morte du stick, pour ignorer les petites dérives.")]
    [Range(0f, 0.5f)]
    public float deadZone = 0.15f;

    [Tooltip("Affiche dans la Console la valeur brute des sticks, pour déboguer.")]
    public bool debugLogGamepad = false;

    private float currentAngle;
    private Vector3 trayLocalOffset;

    private void Awake()
    {
        if (armBalance == null) armBalance = GetComponentInChildren<ArmBalance>();
    }

    private void Start()
    {
        if (tray == null) return;

        // IMPORTANT : un Rigidbody2D placé sur un ENFANT d'un autre Transform ne
        // suit pas correctement MoveRotation. La physique tourne bien le plateau
        // dans le moteur, mais le Transform affiché, lui, ne bouge pas : l'image
        // et la collision se désynchronisent, et l'assiette se met à sauter toute
        // seule sur un plateau qui a l'air parfaitement horizontal.
        //
        // On retient donc la position voulue du plateau par rapport au personnage,
        // puis on le détache au lancement : il devient un objet racine, piloté
        // uniquement par la physique (MovePosition + MoveRotation). Il reste collé
        // aux mains du personnage, même si celui-ci se déplace plus tard.
        trayLocalOffset = transform.InverseTransformPoint(tray.transform.position);
        tray.transform.SetParent(null, true);
        currentAngle = tray.rotation;
    }

    /// <summary>Valeur horizontale du stick gauche (avant-bras) ou droit (après-bras), entre -1 et 1, zone morte appliquée.</summary>
    private float ReadStick(bool rightStick, KeyControl negativeKey, KeyControl positiveKey)
    {
        float x = 0f;
        if (gamepad != null) x = (rightStick ? gamepad.rightStick : gamepad.leftStick).ReadValue().x;

        if (debugLogGamepad)
            Debug.Log($"[PlayerBalanceController] gamepad={(gamepad != null ? gamepad.displayName : "aucune")} {(rightStick ? "right" : "left")}={x:F2}");

        if (allowKeyboardFallback && Mathf.Abs(x) < deadZone)
        {
            if (negativeKey != null && negativeKey.isPressed) x = -1f;
            else if (positiveKey != null && positiveKey.isPressed) x = 1f;
        }

        if (Mathf.Abs(x) < deadZone) x = 0f;
        return Mathf.Clamp(x, -1f, 1f);
    }

    // Le repli clavier utilise le pavé de flèches, et surtout PAS A/D : ce script
    // cohabite avec CharacterMoveController sur le même GameObject, et celui-ci
    // prend déjà A/D pour déplacer le personnage. Sans cette séparation, appuyer
    // sur A déplaçait le personnage ET faisait pivoter les avant-bras.
    private float ReadFrontStick()
    {
        var kb = Keyboard.current;
        return ReadStick(false, kb?.leftArrowKey, kb?.rightArrowKey);
    }

    private float ReadBackStick()
    {
        var kb = Keyboard.current;
        return ReadStick(true, kb?.downArrowKey, kb?.upArrowKey);
    }

    private void Update()
    {
        if (armBalance == null) return;

        armBalance.SetFrontBalance(ReadFrontStick());
        armBalance.SetBackBalance(ReadBackStick());
    }

    private void FixedUpdate()
    {
        if (tray == null) return;

        // Le plateau penche en fonction de l'écart entre avant-bras et après-bras :
        // s'ils sont égaux, il reste à plat, quel que soit leur niveau commun.
        float imbalance = (ReadBackStick() - ReadFrontStick()) * 0.5f;

        // On approche l'angle voulu progressivement au lieu de sauter dessus d'un coup :
        // un MoveRotation() brutal fait "téléporter" le plateau et éjecte l'assiette.
        float target = -imbalance * maxTrayTilt;
        currentAngle = Mathf.MoveTowardsAngle(currentAngle, target, trayTiltSpeed * Time.fixedDeltaTime);

        tray.MoveRotation(currentAngle);
        tray.MovePosition(transform.TransformPoint(trayLocalOffset));
    }

    /// <summary>Ajoute un à-coup instantané à l'inclinaison (ex. secousse de l'autre joueur), qui se résorbe ensuite naturellement au rythme de trayTiltSpeed.</summary>
    public void Jolt(float degrees)
    {
        currentAngle += degrees;
    }
}
