using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// CANETTRIS — Tetris en équipes avec les canettes LA TAVERNE.
///
/// Chaque équipe du lobby (un emplacement = une équipe de 1 ou 2 joueurs) a son propre
/// casier (voir CanTetrisCrate), et TOUTES les équipes jouent en même temps, côte à côte :
///  - dans un casier, on coopère : deux pièces qui tombent ensemble, des lignes à remplir
///    sur toute la largeur, LE COMPTOIR partagé, LA MOUSSE et LA TOURNÉE ;
///  - entre les casiers, on s'affronte : les lignes, mousses, tournées et chaînes envoient
///    des CANETTES CABOSSÉES à l'équipe en tête. Une équipe dont le casier déborde est K.O.,
///    la dernière debout gagne.
/// Avec une seule équipe, c'est le mode coop classique : on joue jusqu'au débordement.
///
/// Joueurs :
///  - depuis le lobby : les emplacements choisis, chacun avec sa manette ;
///  - scène lancée seule (test) : J1 au clavier gauche et J2 au clavier droit dans la même
///    équipe ; chaque manette rejoint en appuyant sur un bouton (elle remplace d'abord les
///    claviers, puis forme de nouvelles équipes deux par deux). TAB : chaque joueur dans sa
///    propre équipe (pour tester le versus seul au clavier).
///
/// Ce composant garde tous les réglages : les casiers les lisent.
/// </summary>
public class CanTetrisLane : MonoBehaviour
{
    [Header("Canettes")]
    [Tooltip("Un sprite par marque. Chaque pièce est entièrement d'une seule marque.")]
    public Sprite[] canSprites;

    [Tooltip("Police des textes. Vide = police TextMeshPro par défaut.")]
    public TMP_FontAsset font;

    [Header("Casier")]
    [Tooltip("Largeur d'un casier à deux joueurs. Un joueur seul a un casier plus étroit (5/7 de cette largeur).")]
    public int columns = 14;
    public int rows = 16;

    [Tooltip("Hauteur d'une case, en unités monde. La largeur suit la forme des canettes.")]
    public float cellHeight = 0.5f;

    [Tooltip("Part de la hauteur de case occupée par la canette.")]
    [Range(0.5f, 1f)] public float canFill = 0.94f;

    [Tooltip("Espace entre deux canettes voisines, en fraction de la largeur d'une canette.")]
    [Range(0f, 1f)] public float canGap = 0.12f;

    [Header("Rythme")]
    [Tooltip("Temps (s) pour descendre d'une case au niveau 1.")]
    public float startFallInterval = 0.8f;
    public float minFallInterval = 0.08f;

    [Tooltip("Chaque niveau multiplie le temps de chute par cette valeur.")]
    [Range(0.5f, 1f)] public float speedUpPerLevel = 0.85f;
    public int linesPerLevel = 10;

    [Tooltip("Descente rapide (bas maintenu) : vitesse multipliée par cette valeur.")]
    public float softDropFactor = 20f;

    [Tooltip("Temps (s) pendant lequel une pièce posée peut encore glisser avant de se figer.")]
    public float lockDelay = 0.5f;

    [Tooltip("Nombre de fois qu'on peut relancer ce délai en bougeant une pièce posée (remis à zéro dès qu'elle descend plus bas).")]
    public int maxLockResets = 15;

    [Header("Commandes")]
    [Tooltip("Délai avant qu'un déplacement maintenu se répète.")]
    public float repeatDelay = 0.15f;

    [Tooltip("Intervalle entre deux pas quand le déplacement se répète.")]
    public float repeatInterval = 0.04f;

    [Tooltip("Vibrations des manettes.")]
    public bool rumble = true;

    [Header("Mousse et points")]
    [Tooltip("Nombre de canettes de la même marque qui se touchent pour mousser et éclater.")]
    public int popThreshold = 10;

    [Tooltip("Points pour 0, 1, 2, 3 et 4 lignes d'un coup (multipliés par le niveau et la chaîne).")]
    public int[] lineScores = { 0, 100, 300, 500, 800 };

    [Tooltip("Points par canette qui mousse (multipliés par le niveau et la chaîne).")]
    public int popPointsPerCan = 20;

    [Tooltip("Bonus par ligne entièrement d'une même marque (multiplié par le niveau et la chaîne).")]
    public int tourneeBonus = 500;

    [Tooltip("Durée de l'animation d'une ligne ou d'une mousse, pendant laquelle le casier se fige.")]
    public float clearDelay = 0.3f;

    [Header("Équipes")]
    [Tooltip("Nombre maximum de casiers à l'écran.")]
    [Range(1, 4)] public int maxTeams = 3;

    [Tooltip("Les équipes s'envoient des canettes cabossées (s'il y a plus d'une équipe).")]
    public bool attacks = true;

    [Tooltip("Canettes cabossées envoyées pour 0, 1, 2, 3 et 4 lignes d'un coup. Chaque tournée, mousse et étape de chaîne en ajoute une.")]
    public int[] attackLines = { 0, 0, 1, 2, 4 };

    [Tooltip("Lignes cabossées maximum qui arrivent d'un coup (le reste attend la pièce suivante).")]
    public int maxJunkPerLock = 6;

    [Header("Joueurs")]
    [Tooltip("Couleur de chaque joueur (J1, J2, J3...). S'il en manque, une palette par défaut prend le relais.")]
    public Color[] playerColors =
    {
        new Color(1f, 0.47f, 0.2f), new Color(0.25f, 0.72f, 1f), new Color(0.45f, 0.9f, 0.35f),
        new Color(1f, 0.4f, 0.75f), new Color(1f, 0.85f, 0.25f), new Color(0.7f, 0.5f, 1f),
    };
    public string menuSceneName = "Menu";

    private static readonly Color[] DefaultPalette =
    {
        new Color(1f, 0.47f, 0.2f), new Color(0.25f, 0.72f, 1f), new Color(0.45f, 0.9f, 0.35f),
        new Color(1f, 0.4f, 0.75f), new Color(1f, 0.85f, 0.25f), new Color(0.7f, 0.5f, 1f),
        new Color(0.3f, 0.95f, 0.85f), new Color(0.95f, 0.95f, 0.95f),
    };

    private enum State { Waiting, Playing, Over }

    private class Shot
    {
        public SpriteRenderer sprite;
        public Vector3 from;
        public Vector3 to;
        public float age;
        public float life;
    }

    private State state = State.Waiting;
    private readonly List<CanTetrisCrate> crates = new List<CanTetrisCrate>();
    private readonly List<CanTetrisCrate> knockedOut = new List<CanTetrisCrate>();

    // Mode test : joueurs dans l'ordre d'arrivée, regroupés deux par deux (ou seuls avec TAB).
    private bool testMode;
    private bool soloTeams;
    private readonly List<CanTetrisInput> testPlayers = new List<CanTetrisInput>();
    private readonly HashSet<Gamepad> claimedPads = new HashSet<Gamepad>();
    private float lastJoinTime = -10f;

    private float overAt;
    private bool overPromptShown;
    private string overTitle;
    private string overSubtitle;

    private int layoutWidth;
    private int layoutHeight;
    private float crateScale = 1f;

    private Transform banner;
    private TextMeshPro bannerTitle;
    private TextMeshPro bannerSub;
    private SpriteRenderer bannerBack;
    private readonly List<Shot> shots = new List<Shot>();
    private readonly Dictionary<Gamepad, float> rumbleEnds = new Dictionary<Gamepad, float>();

    private const float OverInputDelay = 1.2f;
    private const int BannerOrder = 40;

    // Partagé avec les casiers
    public Sprite[] BrandSprites { get; private set; }
    public Sprite Rounded { get; private set; }
    public Sprite Circle { get; private set; }
    public Sprite JunkSprite { get; private set; }
    public float CellWidth { get; private set; }
    public Vector3 CanScale { get; private set; }

    public Color PlayerColor(int number)
    {
        if (number < 0) return Color.white;
        if (playerColors != null && number < playerColors.Length) return playerColors[number];
        return DefaultPalette[number % DefaultPalette.Length];
    }

    public int AttackForLines(int count)
    {
        if (attackLines == null || attackLines.Length == 0) return 0;
        return Mathf.Max(0, attackLines[Mathf.Clamp(count, 0, attackLines.Length - 1)]);
    }

    // ------------------------------------------------------------------ mise en place

    private void Start()
    {
        Rounded = MakeRoundedSprite();
        Circle = MakeCircleSprite();
        SetupSprites();
        SetupGeometry();
        BuildBanner();

        if (!SetupFromLobby())
        {
            testMode = true;
            testPlayers.Add(NewInput(null, CanTetrisInput.KeySet.Left, "CLAVIER"));
            testPlayers.Add(NewInput(null, CanTetrisInput.KeySet.Right, "CLAVIER"));
            BuildTestTeams();
        }

        ShowWaiting();
    }

    private void OnDisable()
    {
        // Ne jamais laisser une manette vibrer après avoir quitté la scène.
        foreach (var pad in rumbleEnds.Keys)
            if (pad != null) pad.SetMotorSpeeds(0f, 0f);
        rumbleEnds.Clear();
    }

    private void SetupSprites()
    {
        var valid = new List<Sprite>();
        if (canSprites != null)
            foreach (var sprite in canSprites)
                if (sprite != null) valid.Add(sprite);

        if (valid.Count == 0)
        {
            // Pas de sprites branchés : des canettes de couleur générées, pour que ça tourne quand même.
            Color[] fallback = { Color.black, Color.white, new Color(1f, 0.2f, 0.85f), new Color(0.2f, 0.2f, 0.2f) };
            foreach (var c in fallback) valid.Add(MakeCanSprite(c));
        }

        BrandSprites = valid.ToArray();
        JunkSprite = MakeJunkSprite(BrandSprites[0].bounds.size);
    }

    private void SetupGeometry()
    {
        Vector2 spriteSize = BrandSprites[0].bounds.size;
        float canHeight = cellHeight * canFill;
        float scale = canHeight / Mathf.Max(0.001f, spriteSize.y);
        float canWidth = spriteSize.x * scale;

        CanScale = new Vector3(scale, scale, 1f);
        CellWidth = canWidth * (1f + canGap);
    }

    /// <summary>Une équipe par emplacement du lobby qui a au moins un joueur.</summary>
    private bool SetupFromLobby()
    {
        var bySlot = new SortedDictionary<int, List<int>>();
        foreach (var pair in LobbySelectionData.PlayerChoices)
        {
            if (!bySlot.TryGetValue(pair.Value, out var list)) bySlot[pair.Value] = list = new List<int>();
            list.Add(pair.Key);
        }

        var teams = new List<List<CanTetrisInput>>();
        int keyboardsUsed = 0;
        foreach (var players in bySlot.Values)
        {
            if (teams.Count >= maxTeams) break;
            players.Sort();

            var team = new List<CanTetrisInput>();
            for (int i = 0; i < players.Count && i < 2; i++)
            {
                LobbySelectionData.PlayerGamepads.TryGetValue(players[i], out Gamepad pad);
                var keys = CanTetrisInput.KeySet.None;
                if (pad == null)
                {
                    // Joueur sans manette : une moitié du clavier, tant qu'il en reste.
                    keys = keyboardsUsed == 0 ? CanTetrisInput.KeySet.Left : keyboardsUsed == 1 ? CanTetrisInput.KeySet.Right : CanTetrisInput.KeySet.None;
                    keyboardsUsed++;
                }
                team.Add(NewInput(pad, keys, pad != null ? "MANETTE" : "CLAVIER"));
            }
            teams.Add(team);
        }

        if (teams.Count == 0) return false;
        BuildCrates(teams);
        return true;
    }

    private void BuildTestTeams()
    {
        var teams = new List<List<CanTetrisInput>>();
        int perTeam = soloTeams ? 1 : 2;
        for (int i = 0; i < testPlayers.Count && teams.Count < maxTeams; i += perTeam)
        {
            var team = new List<CanTetrisInput>();
            for (int j = i; j < i + perTeam && j < testPlayers.Count; j++) team.Add(testPlayers[j]);
            teams.Add(team);
        }
        BuildCrates(teams);
    }

    private void BuildCrates(List<List<CanTetrisInput>> teams)
    {
        foreach (var crate in crates)
            if (crate.root != null) Destroy(crate.root.gameObject);
        crates.Clear();

        bool versus = teams.Count > 1;
        int number = 0;
        for (int t = 0; t < teams.Count; t++)
        {
            var numbers = new List<int>();
            for (int i = 0; i < teams[t].Count; i++) numbers.Add(number++);
            crates.Add(new CanTetrisCrate(this, t, teams[t], numbers, versus, transform));
        }

        layoutWidth = layoutHeight = -1;
        Layout();
    }

    private CanTetrisInput NewInput(Gamepad pad, CanTetrisInput.KeySet keys, string label)
    {
        return new CanTetrisInput(pad, keys, label) { repeatDelay = repeatDelay, repeatInterval = repeatInterval };
    }

    /// <summary>Place les casiers côte à côte et les réduit si besoin pour qu'ils tiennent tous à l'écran.</summary>
    private void Layout()
    {
        if (Screen.width == layoutWidth && Screen.height == layoutHeight) return;
        layoutWidth = Screen.width;
        layoutHeight = Screen.height;

        Camera cam = Camera.main;
        Vector3 center = cam != null ? cam.transform.position : transform.position;
        float viewHeight = cam != null && cam.orthographic ? cam.orthographicSize * 2f : 10f;
        float viewWidth = viewHeight * (cam != null ? cam.aspect : 16f / 9f);

        // Le bandeau reste un peu au-dessus du centre de l'écran (pour ne pas cacher l'aide
        // des commandes, en bas des panneaux), et pas plus large que lui.
        float bannerScale = Mathf.Min(1f, viewWidth * 0.95f / 11.5f);
        banner.position = new Vector3(center.x, center.y + viewHeight * 0.1f, 0f);
        banner.localScale = Vector3.one * bannerScale;

        if (crates.Count == 0) return;

        const float gap = 0.4f;
        float totalWidth = -gap;
        float height = 0f;
        foreach (var crate in crates)
        {
            totalWidth += crate.BoundsMax.x - crate.BoundsMin.x + gap;
            height = Mathf.Max(height, crate.BoundsMax.y - crate.BoundsMin.y);
        }

        crateScale = Mathf.Min(1f, viewWidth * 0.97f / totalWidth, viewHeight * 0.97f / height);

        float x = center.x - totalWidth * crateScale * 0.5f;
        foreach (var crate in crates)
        {
            float width = crate.BoundsMax.x - crate.BoundsMin.x;
            float middleY = (crate.BoundsMin.y + crate.BoundsMax.y) * 0.5f;
            crate.root.position = new Vector3(x - crate.BoundsMin.x * crateScale, center.y - middleY * crateScale, 0f);
            crate.root.localScale = Vector3.one * crateScale;
            x += (width + gap) * crateScale;
        }
    }

    // ------------------------------------------------------------------ boucle

    private void Update()
    {
        float dt = Time.deltaTime;
        foreach (var crate in crates)
            foreach (var input in crate.inputs)
                input?.Poll(dt);

        Layout();

        switch (state)
        {
            case State.Waiting:
                bool changed = false;
                if (testMode)
                {
                    changed = TryJoinPads();
                    var kb = Keyboard.current;
                    if (kb != null && kb.tabKey.wasPressedThisFrame)
                    {
                        soloTeams = !soloTeams;
                        BuildTestTeams();
                        ShowWaiting();
                        changed = true;
                    }
                }
                if (!changed && StartPressed()) BeginGame();
                break;

            case State.Playing:
                foreach (var crate in crates) crate.Step(dt);
                CheckEnd();
                break;

            case State.Over:
                // Les touches pour rejouer sont aussi celles pour jouer : sans ce délai, un
                // joueur qui martèle « lâcher » relancerait la partie sans voir le résultat.
                if (Time.unscaledTime - overAt < OverInputDelay) break;
                if (!overPromptShown)
                {
                    overPromptShown = true;
                    SetBanner(overTitle, overSubtitle + "\nA / ENTRÉE / ESPACE : REJOUER      B / ÉCHAP : MENU");
                }
                if (AnyInput(i => i.Confirm)) BeginGame();
                else if (AnyInput(i => i.Back)) ToMenu();
                break;
        }

        foreach (var crate in crates) crate.Tick(dt);
        UpdateShots(dt);
        UpdateRumble();
    }

    private bool AnyInput(System.Func<CanTetrisInput, bool> test)
    {
        foreach (var crate in crates)
            foreach (var input in crate.inputs)
                if (input != null && test(input)) return true;
        return false;
    }

    private bool StartPressed()
    {
        var kb = Keyboard.current;
        if (kb != null && (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame))
            return true;

        // Depuis le lobby : A ou Start. En test, A sert à rejoindre : seul Start lance la
        // partie, pour qu'un joueur qui appuie deux fois ne la démarre pas avant les autres.
        foreach (var crate in crates)
            foreach (var input in crate.inputs)
                if (input != null && input.gamepad != null
                    && (input.gamepad.startButton.wasPressedThisFrame || (!testMode && input.gamepad.buttonSouth.wasPressedThisFrame)))
                    return true;
        return false;
    }

    /// <summary>
    /// Mode test : une manette rejoint dès qu'on appuie sur un de ses boutons. On n'utilise
    /// pas Gamepad.all tel quel : Windows et Steam y listent souvent des manettes fantômes ou
    /// en double. Une manette qui s'active dans la foulée d'une autre est une copie (pad
    /// virtuel Steam, double pilote...) : on l'écarte.
    /// </summary>
    private bool TryJoinPads()
    {
        bool joined = false;
        foreach (var pad in Gamepad.all)
        {
            if (claimedPads.Contains(pad) || !AnyButtonPressed(pad)) continue;

            claimedPads.Add(pad);
            if (Time.unscaledTime - lastJoinTime < 0.2f) continue;

            // D'abord à la place d'un joueur clavier, sinon comme nouveau joueur.
            CanTetrisInput input = testPlayers.Find(p => p.gamepad == null && p.keys != CanTetrisInput.KeySet.None);
            if (input != null)
            {
                input.gamepad = pad;
                input.keys = CanTetrisInput.KeySet.None;
                input.label = "MANETTE";
            }
            else
            {
                if (testPlayers.Count >= maxTeams * (soloTeams ? 1 : 2)) continue;
                input = NewInput(pad, CanTetrisInput.KeySet.None, "MANETTE");
                testPlayers.Add(input);
            }

            lastJoinTime = Time.unscaledTime;
            joined = true;

            // Reconstruit les casiers (le casier gagne un joueur, ou une équipe apparaît).
            BuildTestTeams();
            ShowWaiting();
            foreach (var crate in crates)
                if (crate.Has(input))
                    crate.AnnounceJoin(input, $"J{testPlayers.IndexOf(input) + 1} : MANETTE");
            Rumble(pad, 0.3f, 0.6f, 0.2f);
        }
        return joined;
    }

    private static bool AnyButtonPressed(Gamepad pad)
    {
        return pad.buttonSouth.wasPressedThisFrame || pad.buttonEast.wasPressedThisFrame
            || pad.buttonWest.wasPressedThisFrame || pad.buttonNorth.wasPressedThisFrame
            || pad.leftShoulder.wasPressedThisFrame || pad.rightShoulder.wasPressedThisFrame
            || pad.leftTrigger.wasPressedThisFrame || pad.rightTrigger.wasPressedThisFrame
            || pad.startButton.wasPressedThisFrame || pad.selectButton.wasPressedThisFrame;
    }

    private void BeginGame()
    {
        knockedOut.Clear();
        foreach (var crate in crates) crate.ResetForMatch();
        state = State.Playing;
        SetBanner(null, null);
    }

    // ------------------------------------------------------------------ versus

    /// <summary>Un casier envoie des canettes cabossées : elles vont à l'équipe en tête (encore en jeu).</summary>
    public void SendAttack(CanTetrisCrate from, int amount, Color color)
    {
        if (!attacks || amount <= 0 || crates.Count < 2) return;

        CanTetrisCrate target = null;
        foreach (var crate in crates)
        {
            if (crate == from || !crate.Alive) continue;
            if (target == null || crate.Score > target.Score) target = crate;
        }
        if (target == null) return;

        target.ReceiveJunk(amount);

        // Le tir : une bulle qui file d'un casier à l'autre.
        var sprite = new GameObject("Tir").AddComponent<SpriteRenderer>();
        sprite.transform.SetParent(transform, false);
        sprite.sprite = Circle;
        sprite.color = color;
        sprite.sortingOrder = CanTetrisCrate.PopupOrder + 2;
        shots.Add(new Shot { sprite = sprite, from = from.TopWorld, to = target.JunkMeterWorld, life = 0.45f });
    }

    public void OnKnockedOut(CanTetrisCrate crate)
    {
        if (!knockedOut.Contains(crate)) knockedOut.Add(crate);
    }

    private void CheckEnd()
    {
        int alive = 0;
        CanTetrisCrate survivor = null;
        foreach (var crate in crates)
        {
            if (!crate.Alive) continue;
            alive++;
            survivor = crate;
        }

        bool over = crates.Count == 1 ? alive == 0 : alive <= 1;
        if (!over) return;

        state = State.Over;
        overAt = Time.unscaledTime;
        overPromptShown = false;

        if (crates.Count == 1)
        {
            var solo = crates[0];
            solo.Stop(false);
            overTitle = "LE CASIER DÉBORDE !";
            overSubtitle = $"{solo.Score} POINTS · {solo.Lines} LIGNES";
        }
        else
        {
            // Tout le monde K.O. en même temps : le meilleur score l'emporte.
            CanTetrisCrate winner = survivor;
            if (winner == null)
                foreach (var crate in crates)
                    if (winner == null || crate.Score > winner.Score) winner = crate;

            foreach (var crate in crates) crate.Stop(crate == winner && survivor != null);

            overTitle = winner != null ? $"{winner.Name} GAGNE !" : "ÉGALITÉ !";
            var scores = new List<string>();
            foreach (var crate in crates) scores.Add($"{crate.Name} : {crate.Score}");
            overSubtitle = string.Join("   ·   ", scores);
        }

        SetBanner(overTitle, overSubtitle);
    }

    private void ToMenu()
    {
        if (!string.IsNullOrEmpty(menuSceneName) && Application.CanStreamedLevelBeLoaded(menuSceneName))
            UnityEngine.SceneManagement.SceneManager.LoadScene(menuSceneName);
    }

    // ------------------------------------------------------------------ vibrations

    public void Rumble(Gamepad pad, float low, float high, float duration)
    {
        if (!rumble || pad == null) return;
        pad.SetMotorSpeeds(low, high);
        rumbleEnds[pad] = Time.unscaledTime + duration;
    }

    private void UpdateRumble()
    {
        if (rumbleEnds.Count == 0) return;

        var finished = new List<Gamepad>();
        foreach (var pair in rumbleEnds)
            if (Time.unscaledTime >= pair.Value) finished.Add(pair.Key);

        foreach (var pad in finished)
        {
            if (pad != null) pad.SetMotorSpeeds(0f, 0f);
            rumbleEnds.Remove(pad);
        }
    }

    // ------------------------------------------------------------------ bandeau et effets

    private void BuildBanner()
    {
        banner = new GameObject("Bandeau").transform;
        banner.SetParent(transform, false);

        var go = new GameObject("Bandeau fond");
        go.transform.SetParent(banner, false);
        bannerBack = go.AddComponent<SpriteRenderer>();
        bannerBack.sprite = Rounded;
        bannerBack.drawMode = SpriteDrawMode.Sliced;
        bannerBack.size = new Vector2(11.5f, 3.9f);
        bannerBack.color = new Color(0.04f, 0.03f, 0.05f, 0.92f);
        bannerBack.sortingOrder = BannerOrder;

        bannerTitle = NewBannerText("Titre", new Vector3(0f, 1f, 0f), 7f, new Vector2(11f, 1.5f));
        bannerSub = NewBannerText("Sous-titre", new Vector3(0f, -0.6f, 0f), 2.1f, new Vector2(11f, 2.6f));
        bannerSub.textWrappingMode = TextWrappingModes.Normal;
        bannerSub.color = new Color(1f, 1f, 1f, 0.88f);
    }

    private TextMeshPro NewBannerText(string name, Vector3 position, float size, Vector2 box)
    {
        var go = new GameObject(name);
        go.transform.SetParent(banner, false);
        go.transform.localPosition = position;
        var tmp = go.AddComponent<TextMeshPro>();
        if (font != null) tmp.font = font;
        tmp.fontSize = size;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.rectTransform.sizeDelta = box;
        tmp.sortingOrder = BannerOrder + 1;
        return tmp;
    }

    private void ShowWaiting()
    {
        bool versus = crates.Count > 1;
        string rules = $"MOUSSE : {popThreshold} canettes de la même marque qui se touchent éclatent · COMPTOIR : une réserve partagée avec votre coéquipier";
        if (versus) rules += "\nVOS LIGNES ENVOIENT DES CANETTES CABOSSÉES À L'ÉQUIPE EN TÊTE · DERNIÈRE ÉQUIPE DEBOUT = VICTOIRE";

        string start = testMode
            ? "\nSTART / ESPACE / ENTRÉE : JOUER\nMANETTE : UN BOUTON POUR REJOINDRE · TAB : " + (soloTeams ? "JOUER EN ÉQUIPES DE 2" : "CHACUN SON CASIER")
            : "\nA / START : JOUER";

        SetBanner(versus ? $"CANETTRIS · {crates.Count} ÉQUIPES" : "CANETTRIS", rules + start);
    }

    private void SetBanner(string title, string subtitle)
    {
        bool visible = !string.IsNullOrEmpty(title);
        bannerBack.enabled = visible;
        bannerTitle.text = visible ? title : "";
        bannerSub.text = visible ? subtitle : "";
    }

    private void UpdateShots(float dt)
    {
        for (int i = shots.Count - 1; i >= 0; i--)
        {
            Shot shot = shots[i];
            shot.age += dt;
            float t = Mathf.Clamp01(shot.age / shot.life);
            float eased = 1f - (1f - t) * (1f - t);

            // Une courbe qui passe au-dessus des casiers.
            Vector3 position = Vector3.Lerp(shot.from, shot.to, eased) + Vector3.up * Mathf.Sin(t * Mathf.PI) * 1.5f * crateScale;
            shot.sprite.transform.position = position;
            shot.sprite.transform.localScale = Vector3.one * (0.45f - 0.2f * t) * crateScale;

            if (shot.age >= shot.life)
            {
                Destroy(shot.sprite.gameObject);
                shots.RemoveAt(i);
            }
        }
    }

    // ------------------------------------------------------------------ sprites générés

    /// <summary>Rectangle arrondi blanc en 9-slice (fond du casier, halos, panneaux, bandeau).</summary>
    private static Sprite MakeRoundedSprite()
    {
        const int size = 64;
        const int radius = 16;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color32[size * size];
        float half = size * 0.5f;
        float inner = half - radius;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float qx = Mathf.Abs(x + 0.5f - half) - inner;
                float qy = Mathf.Abs(y + 0.5f - half) - inner;
                float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude;
                float dist = outside + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(0.5f - dist) * 255f));
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply(false, true);
        float border = radius + 2;
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 400f, 0,
            SpriteMeshType.FullRect, new Vector4(border, border, border, border));
    }

    /// <summary>Disque blanc (bulles, boutons de manette, tirs), 1 unité de diamètre.</summary>
    private static Sprite MakeCircleSprite()
    {
        const int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color32[size * size];
        float c = (size - 1) * 0.5f;
        float r = size * 0.5f - 1f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(r - d) * 255f));
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply(false, true);
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }

    /// <summary>
    /// Canette cabossée : grise, avec deux plis sombres. Même taille à l'écran que les vraies
    /// canettes (<paramref name="size"/>), pour s'aligner dans le casier.
    /// </summary>
    private static Sprite MakeJunkSprite(Vector2 size)
    {
        const int height = 96;
        int width = Mathf.Clamp(Mathf.RoundToInt(height * size.x / Mathf.Max(0.001f, size.y)), 8, 256);
        var tex = new Texture2D(width, height, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color32[width * height];
        float radius = width * 0.28f;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                // Coins arrondis.
                float cx = Mathf.Max(radius - x, x - (width - 1 - radius), 0f);
                float cy = Mathf.Max(radius - y, y - (height - 1 - radius), 0f);
                if (cx * cx + cy * cy > radius * radius) { pixels[y * width + x] = new Color32(0, 0, 0, 0); continue; }

                float u = x / (float)(width - 1);
                float v = y / (float)(height - 1);
                float shade = 0.5f + 0.18f * Mathf.Sin(u * Mathf.PI);                  // volume du cylindre
                if (v < 0.06f || v > 0.94f) shade = 0.72f;                              // bords du haut et du bas
                float dent1 = Mathf.Abs(v - (0.62f + 0.18f * (u - 0.5f)));              // deux plis de travers
                float dent2 = Mathf.Abs(v - (0.34f - 0.22f * (u - 0.5f)));
                if (dent1 < 0.025f || dent2 < 0.02f) shade *= 0.55f;
                else if (dent1 < 0.05f || dent2 < 0.04f) shade *= 1.15f;

                byte g = (byte)(Mathf.Clamp01(shade) * 255f);
                pixels[y * width + x] = new Color32(g, g, (byte)Mathf.Min(255, g + 8), 255);
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply(false, true);
        return Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), height / Mathf.Max(0.001f, size.y));
    }

    /// <summary>Canette de secours (rectangle coloré) si aucun sprite n'est branché.</summary>
    private static Sprite MakeCanSprite(Color color)
    {
        const int width = 40;
        const int height = 104;
        var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        var pixels = new Color32[width * height];
        Color32 body = color;
        Color32 rim = new Color32(200, 200, 200, 255);

        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                pixels[y * width + x] = (y < 4 || y >= height - 4) ? rim : body;

        tex.SetPixels32(pixels);
        tex.Apply(false, true);
        return Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 100f);
    }
}
