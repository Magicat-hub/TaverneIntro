using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Un casier de CANETTRIS : le plateau d'UNE équipe (1 ou 2 joueurs). Créé et piloté par
/// CanTetrisLane, qui en place un par équipe côte à côte.
///
/// Dans le casier, c'est de la coopération : les deux pièces tombent en même temps, une
/// ligne ne part que si elle est pleine sur toute la largeur, et LE COMPTOIR est une
/// réserve partagée. LA MOUSSE : popThreshold canettes de la même marque qui se touchent
/// éclatent (et peuvent déclencher des chaînes). LA TOURNÉE : une ligne d'une seule marque
/// rapporte un bonus.
///
/// Entre les casiers, c'est la compétition : chaque ligne (à partir de 2 d'un coup), chaque
/// mousse, tournée ou chaîne envoie des CANETTES CABOSSÉES à l'équipe en tête. Elles
/// arrivent par le bas, avec un trou, à la prochaine pièce posée sans rien effacer. Effacer
/// des lignes avant qu'elles n'arrivent les annule (« PARÉ ! »).
///
/// Tout est construit en coordonnées LOCALES sous <see cref="root"/> : CanTetrisLane peut
/// ainsi réduire et déplacer le casier entier pour faire tenir toutes les équipes à l'écran.
/// </summary>
public class CanTetrisCrate
{
    public enum State { Idle, Playing, Resolving, Out }

    public const int PanelOrder = 1;
    public const int BoardOrder = 5;
    public const int GhostOrder = 6;
    public const int GlowOrder = 7;
    public const int PieceOrder = 8;
    public const int FlashOrder = 9;
    public const int TextOrder = 20;
    public const int PopupOrder = 30;
    public const int OverlayOrder = 34;

    private const int JunkCan = -1;
    private static readonly Color JunkTint = new Color(0.85f, 0.85f, 0.85f);
    private static readonly Color Danger = new Color(1f, 0.28f, 0.22f);
    private static readonly Color OverlayDark = new Color(0.03f, 0.02f, 0.04f, 0.7f);

    private class Piece
    {
        public int player;
        public int type;
        public int rotation;
        public int brand;
        public Vector2Int position;   // coin bas-gauche de la boîte de la pièce
        public float fallTimer;
        public float lockTimer;
        public int lockResets;
        public int lowestRow;
        public bool active;
        public bool holdUsed;
        public int nextType;
        public int nextBrand;
        public CanTetrisShapes.Bag bag = new CanTetrisShapes.Bag();

        public Vector2Int[] Cells => CanTetrisShapes.Cells(type, rotation);
    }

    private class Effect
    {
        public Transform target;
        public TextMeshPro text;
        public SpriteRenderer sprite;
        public Vector3 velocity;
        public float age;
        public float life;
        public float rise;
        public Color color;
    }

    public readonly int team;
    public readonly Transform root;
    public readonly CanTetrisInput[] inputs = new CanTetrisInput[2];
    public readonly int[] playerNumbers = { -1, -1 };

    /// <summary>Rectangle occupé par le casier et ses panneaux, en coordonnées locales.</summary>
    public Vector2 BoundsMin { get; private set; }
    public Vector2 BoundsMax { get; private set; }

    public State CurrentState => state;
    public bool Alive => state != State.Out;
    public int Score => score;
    public int Lines => lines;
    public string Name => $"ÉQUIPE {team + 1}";

    /// <summary>Où arrivent les canettes cabossées (pour l'animation du tir).</summary>
    public Vector3 JunkMeterWorld => root.TransformPoint(new Vector3(junkX, -wellSize.y * 0.25f, 0f));
    public Vector3 TopWorld => root.TransformPoint(new Vector3(0f, wellSize.y * 0.5f, 0f));

    private readonly CanTetrisLane game;
    private readonly bool versus;
    private readonly int columns;
    private readonly int rows;
    private readonly float cellWidth;
    private readonly float cellHeight;
    private readonly Vector3 canScale;
    private readonly Vector2 wellSize;
    private readonly Vector3 origin;           // centre local de la case (0, 0)
    private readonly float junkX;

    private State state = State.Idle;
    private readonly int[,] board;             // 0 = vide, -1 = cabossée, sinon marque + 1
    private readonly bool[,] fizzing;
    private readonly bool[,] clearing;
    private readonly Piece[] pieces = { new Piece { player = 0 }, new Piece { player = 1 } };

    private bool counterFull;
    private int counterType;
    private int counterBrand;
    private int counterOwner;
    private float counterPunch;

    private readonly List<int> clearingRows = new List<int>();
    private readonly List<List<Vector2Int>> clearingGroups = new List<List<Vector2Int>>();
    private float resolveTimer;
    private int chain;
    private int resolvingPlayer;
    private int attack;

    private int score;
    private int lines;
    private int level;
    private float shownScore;
    private float scorePunch;
    private int pendingJunk;
    private float shownJunk;
    private float junkPunch;

    // Affichage
    private SpriteRenderer[,] boardCans;
    private readonly SpriteRenderer[][] pieceCans = new SpriteRenderer[2][];
    private readonly SpriteRenderer[][] pieceGlows = new SpriteRenderer[2][];
    private readonly SpriteRenderer[][] ghostCans = new SpriteRenderer[2][];
    private readonly SpriteRenderer[][] nextCans = new SpriteRenderer[2][];
    private readonly Vector3[] nextAnchors = new Vector3[2];
    private readonly TextMeshPro[] playerLabels = new TextMeshPro[2];
    private SpriteRenderer[] counterCans;
    private SpriteRenderer counterPanel;
    private Vector3 counterAnchor;
    private TextMeshPro scoreText;
    private TextMeshPro infoText;
    private SpriteRenderer junkBar;
    private SpriteRenderer overlay;
    private TextMeshPro overlayText;
    private bool won;
    private readonly List<Effect> effects = new List<Effect>();

    private int PlayerCount => (inputs[0] != null ? 1 : 0) + (inputs[1] != null ? 1 : 0);
    private float FallInterval => Mathf.Max(game.minFallInterval, game.startFallInterval * Mathf.Pow(game.speedUpPerLevel, level));

    public CanTetrisCrate(CanTetrisLane game, int team, IList<CanTetrisInput> players, IList<int> numbers, bool versus, Transform parent)
    {
        this.game = game;
        this.team = team;
        this.versus = versus;

        for (int p = 0; p < players.Count && p < 2; p++)
        {
            inputs[p] = players[p];
            playerNumbers[p] = numbers[p];
        }

        // Un joueur seul a un casier plus étroit (10 colonnes pour 14 à deux par défaut).
        columns = PlayerCount >= 2 ? Mathf.Max(6, game.columns) : Mathf.Max(6, Mathf.RoundToInt(game.columns * 5f / 7f));
        rows = Mathf.Max(8, game.rows);
        cellWidth = game.CellWidth;
        cellHeight = game.cellHeight;
        canScale = game.CanScale;
        wellSize = new Vector2(columns * cellWidth, rows * cellHeight);
        origin = -(Vector3)(wellSize * 0.5f) + new Vector3(cellWidth * 0.5f, cellHeight * 0.5f, 0f);
        junkX = -(wellSize.x * 0.5f + 0.26f);

        board = new int[columns, rows];
        fizzing = new bool[columns, rows];
        clearing = new bool[columns, rows];

        root = new GameObject($"Casier {Name}").transform;
        root.SetParent(parent, false);

        BuildVisuals();
    }

    // ------------------------------------------------------------------ partie

    public void ResetForMatch()
    {
        System.Array.Clear(board, 0, board.Length);
        System.Array.Clear(fizzing, 0, fizzing.Length);
        System.Array.Clear(clearing, 0, clearing.Length);
        score = 0;
        shownScore = 0f;
        lines = 0;
        level = 0;
        counterFull = false;
        pendingJunk = 0;
        shownJunk = 0f;
        attack = 0;

        foreach (var piece in pieces)
        {
            piece.active = false;
            piece.holdUsed = false;
            piece.bag = new CanTetrisShapes.Bag();
            piece.nextType = piece.bag.Next();
            piece.nextBrand = Random.Range(0, game.BrandSprites.Length);
        }

        foreach (var input in inputs) input?.ClearBuffer();

        overlay.enabled = false;
        overlay.color = OverlayDark;
        overlayText.text = "";
        won = false;
        state = State.Playing;
    }

    /// <summary>Fin du match : les pièces disparaissent, le casier reste affiché.</summary>
    public void Stop(bool won)
    {
        foreach (var piece in pieces) piece.active = false;
        if (state != State.Out) state = State.Idle;

        if (won)
        {
            // Le casier gagnant s'illumine dans la couleur de l'équipe, comme la barre LED.
            won = true;
            overlay.enabled = true;
            overlayText.text = "GAGNÉ !";
            overlayText.color = game.TeamColor(team);
        }
    }

    public void Step(float dt)
    {
        switch (state)
        {
            case State.Playing:
                for (int p = 0; p < 2 && state == State.Playing; p++)
                    if (inputs[p] != null) StepPlayer(p, dt);
                break;

            case State.Resolving:
                resolveTimer -= dt;
                if (resolveTimer <= 0f)
                {
                    ApplyClear();
                    NextResolveStep();
                }
                break;
        }
    }

    /// <summary>Affichage, textes et effets : appelé à chaque frame, même hors partie.</summary>
    public void Tick(float dt)
    {
        Render(dt);
        UpdateTexts(dt);
        UpdateEffects(dt);
    }

    public bool Has(CanTetrisInput input) => input != null && (inputs[0] == input || inputs[1] == input);

    public void AnnounceJoin(CanTetrisInput input, string text)
    {
        for (int p = 0; p < 2; p++)
            if (inputs[p] == input)
                Popup(text, nextAnchors[p] + Vector3.up * 1.3f, PlayerColor(p), 3.5f);
    }

    /// <summary>Des canettes cabossées arrivent : elles tomberont à la prochaine pièce posée sans rien effacer.</summary>
    public void ReceiveJunk(int amount)
    {
        if (amount <= 0 || state == State.Out) return;
        pendingJunk += amount;
        junkPunch = 1f;
        for (int p = 0; p < 2; p++)
            if (inputs[p] != null) game.Rumble(inputs[p].gamepad, 0.15f, 0.35f, 0.12f);
    }

    private void StepPlayer(int p, float dt)
    {
        Piece piece = pieces[p];
        CanTetrisInput input = inputs[p];

        if (!piece.active)
        {
            if (!TrySpawn(piece, piece.nextType, piece.nextBrand, true)) return;
        }

        if (input.ConsumeHold()) TryHold(piece);

        if (input.Move != 0) TryMove(piece, new Vector2Int(input.Move, 0));
        if (input.ConsumeRotateClockwise()) TryRotate(piece, true);
        if (input.ConsumeRotateCounterClockwise()) TryRotate(piece, false);

        if (input.HardDrop)
        {
            int dropped = 0;
            while (TryMove(piece, Vector2Int.down, true)) dropped++;
            score += dropped * 2;
            piece.fallTimer = 0f;

            // Posée sur le fond ou la pile : on fige tout de suite. Posée sur la pièce du
            // partenaire : on attend, elle se figerait sinon en plein vol.
            if (RestsOnBoard(piece))
            {
                game.Rumble(input.gamepad, 0.5f, 0.2f, 0.08f);
                Lock(piece);
            }
            return;
        }

        float interval = FallInterval / (input.SoftDrop ? game.softDropFactor : 1f);
        piece.fallTimer += dt;
        while (piece.fallTimer >= interval)
        {
            piece.fallTimer -= interval;
            if (!TryMove(piece, Vector2Int.down, true))
            {
                piece.fallTimer = 0f;
                break;
            }
            if (input.SoftDrop) score += 1;
        }

        if (RestsOnBoard(piece))
        {
            piece.lockTimer += dt;
            if (piece.lockTimer >= game.lockDelay) Lock(piece);
        }
        else
        {
            piece.lockTimer = 0f;
        }
    }

    // ------------------------------------------------------------------ règles

    private Piece Partner(Piece piece) => pieces[1 - piece.player];

    private Vector2Int SpawnPosition(int player)
    {
        int center = PlayerCount < 2 ? columns / 2 : player == 0 ? columns / 4 : columns - 1 - columns / 4;
        return new Vector2Int(center - 1, rows - 3);
    }

    private bool InsideAndFree(Vector2Int cell)
    {
        return cell.x >= 0 && cell.x < columns && cell.y >= 0 && cell.y < rows && board[cell.x, cell.y] == 0;
    }

    private bool FitsBoard(Vector2Int[] cells, Vector2Int position)
    {
        foreach (var c in cells)
            if (!InsideAndFree(position + c)) return false;
        return true;
    }

    /// <summary>La pièce tient ici : dans le casier, sur des cases vides, et sans chevaucher la pièce du partenaire.</summary>
    private bool Fits(Piece piece, Vector2Int[] cells, Vector2Int position)
    {
        if (!FitsBoard(cells, position)) return false;

        Piece partner = Partner(piece);
        if (!partner.active) return true;

        Vector2Int[] theirs = partner.Cells;
        foreach (var mine in cells)
            foreach (var other in theirs)
                if (position + mine == partner.position + other) return false;

        return true;
    }

    private bool RestsOnBoard(Piece piece)
    {
        return !FitsBoard(piece.Cells, piece.position + Vector2Int.down);
    }

    private bool TryMove(Piece piece, Vector2Int delta, bool isGravity = false)
    {
        Vector2Int target = piece.position + delta;
        if (!Fits(piece, piece.Cells, target)) return false;

        piece.position = target;
        if (target.y < piece.lowestRow)
        {
            // Nouvelle profondeur atteinte : on rend tout le délai de pose.
            piece.lowestRow = target.y;
            piece.lockResets = 0;
            piece.lockTimer = 0f;
        }
        else if (!isGravity)
        {
            ResetLock(piece);
        }
        return true;
    }

    private void TryRotate(Piece piece, bool clockwise)
    {
        if (piece.type == CanTetrisShapes.O) return;

        int target = (piece.rotation + (clockwise ? 1 : 3)) % 4;
        Vector2Int[] rotated = CanTetrisShapes.Cells(piece.type, target);

        foreach (var kick in CanTetrisShapes.Kicks(piece.type, piece.rotation, clockwise))
        {
            if (!Fits(piece, rotated, piece.position + kick)) continue;

            piece.rotation = target;
            piece.position += kick;
            ResetLock(piece);
            return;
        }
    }

    /// <summary>Bouger une pièce posée lui redonne un peu de temps, mais pas à l'infini.</summary>
    private void ResetLock(Piece piece)
    {
        if (piece.lockTimer <= 0f || piece.lockResets >= game.maxLockResets) return;
        piece.lockTimer = 0f;
        piece.lockResets++;
    }

    /// <summary>
    /// Fait apparaître une pièce en haut du côté du joueur. Si c'est la pile qui bloque :
    /// l'équipe est K.O. Si c'est seulement la pièce du partenaire : on réessaiera plus tard.
    /// </summary>
    private bool TrySpawn(Piece piece, int type, int brand, bool fromQueue)
    {
        Vector2Int[] cells = CanTetrisShapes.Cells(type, 0);
        Vector2Int position = SpawnPosition(piece.player);

        if (!FitsBoard(cells, position))
        {
            KnockOut();
            return false;
        }
        if (!Fits(piece, cells, position)) return false;

        piece.type = type;
        piece.rotation = 0;
        piece.brand = brand;
        piece.position = position;
        piece.fallTimer = 0f;
        piece.lockTimer = 0f;
        piece.lockResets = 0;
        piece.lowestRow = position.y;
        piece.active = true;

        if (fromQueue)
        {
            piece.nextType = piece.bag.Next();
            piece.nextBrand = Random.Range(0, game.BrandSprites.Length);
        }
        return true;
    }

    /// <summary>
    /// LE COMPTOIR : pose la pièce en cours sur la réserve partagée et prend ce qui s'y
    /// trouvait (ou la pièce suivante si le comptoir était vide). Une fois par pièce.
    /// </summary>
    private void TryHold(Piece piece)
    {
        if (piece.holdUsed) return;

        bool fromCounter = counterFull;
        int takeType = fromCounter ? counterType : piece.nextType;
        int takeBrand = fromCounter ? counterBrand : piece.nextBrand;
        int leftType = piece.type;
        int leftBrand = piece.brand;

        // Si la pièce reprise ne peut pas apparaître (partenaire dans le chemin), on n'échange pas.
        Vector2Int[] cells = CanTetrisShapes.Cells(takeType, 0);
        if (!Fits(piece, cells, SpawnPosition(piece.player))) return;

        piece.active = false;
        TrySpawn(piece, takeType, takeBrand, !fromCounter);
        piece.holdUsed = true;

        counterFull = true;
        counterType = leftType;
        counterBrand = leftBrand;
        counterOwner = piece.player;
        counterPunch = 1f;

        Popup(fromCounter ? "ÉCHANGE !" : "AU COMPTOIR", counterAnchor + Vector3.down * 0.9f, PlayerColor(piece.player), 2.6f);
        game.Rumble(inputs[piece.player].gamepad, 0.2f, 0.3f, 0.06f);
    }

    private void Lock(Piece piece)
    {
        foreach (var c in piece.Cells)
        {
            Vector2Int cell = piece.position + c;
            if (cell.x >= 0 && cell.x < columns && cell.y >= 0 && cell.y < rows)
                board[cell.x, cell.y] = piece.brand + 1;
        }

        piece.active = false;
        piece.holdUsed = false;

        chain = 0;
        attack = 0;
        resolvingPlayer = piece.player;
        NextResolveStep();
    }

    // ------------------------------------------------------------------ lignes, mousse, chaînes, attaques

    /// <summary>
    /// Cherche la prochaine chose à faire disparaître : d'abord les lignes pleines, puis
    /// les groupes qui moussent. S'il y a quelque chose, le casier se fige le temps de
    /// l'animation ; sinon on règle les attaques et on reprend.
    /// </summary>
    private void NextResolveStep()
    {
        System.Array.Clear(clearing, 0, clearing.Length);
        clearingRows.Clear();
        clearingGroups.Clear();

        for (int y = 0; y < rows; y++)
        {
            bool full = true;
            for (int x = 0; x < columns && full; x++) full = board[x, y] != 0;
            if (!full) continue;

            clearingRows.Add(y);
            for (int x = 0; x < columns; x++) clearing[x, y] = true;
        }

        if (clearingRows.Count == 0)
        {
            foreach (var group in FindGroups(game.popThreshold))
            {
                clearingGroups.Add(group);
                foreach (var cell in group) clearing[cell.x, cell.y] = true;
            }
        }

        if (clearingRows.Count > 0 || clearingGroups.Count > 0)
        {
            chain++;
            state = State.Resolving;
            resolveTimer = game.clearDelay;

            foreach (int y in clearingRows) Flash(y);
            if (clearingGroups.Count > 0) RumbleTeam(0.4f, 0.8f, 0.25f);
            return;
        }

        // Plus rien à effacer.
        if (chain == 0)
        {
            // Pièce posée sans rien effacer : les canettes cabossées en attente tombent.
            if (pendingJunk > 0) ApplyJunk();
        }
        else if (attack > 0)
        {
            // Ce qu'on vient de faire annule d'abord ce qui nous arrive, le reste part chez l'adversaire.
            int parried = Mathf.Min(attack, pendingJunk);
            pendingJunk -= parried;
            attack -= parried;
            if (parried > 0) Popup("PARÉ !", new Vector3(junkX + 0.9f, -wellSize.y * 0.25f, 0f), Color.white, 3.5f);
            if (attack > 0) game.SendAttack(this, attack, PlayerColor(resolvingPlayer));
        }

        attack = 0;
        if (state != State.Out) state = State.Playing;
        RefreshFizz();
    }

    private void ApplyClear()
    {
        int multiplier = Mathf.Max(1, chain) * (level + 1);
        Color color = PlayerColor(resolvingPlayer);

        if (clearingRows.Count > 0)
        {
            int tournees = 0;
            foreach (int y in clearingRows)
            {
                int first = board[0, y];
                bool sameBrand = first > 0;
                for (int x = 1; x < columns && sameBrand; x++) sameBrand = board[x, y] == first;
                if (sameBrand) tournees++;
            }

            Vector3 at = CellCenter(new Vector2(columns * 0.5f - 0.5f, clearingRows[0]));

            // Du haut vers le bas, pour que les indices des lignes restantes restent valables.
            for (int i = clearingRows.Count - 1; i >= 0; i--)
            {
                int row = clearingRows[i];
                for (int y = row; y < rows - 1; y++)
                    for (int x = 0; x < columns; x++)
                        board[x, y] = board[x, y + 1];
                for (int x = 0; x < columns; x++) board[x, rows - 1] = 0;
            }

            int count = clearingRows.Count;
            int[] table = game.lineScores;
            int gained = (table[Mathf.Min(count, table.Length - 1)] + tournees * game.tourneeBonus) * multiplier;
            AddScore(gained);
            attack += game.AttackForLines(count) + tournees;

            int previousLevel = level;
            lines += count;
            level = lines / Mathf.Max(1, game.linesPerLevel);

            Popup($"+{gained}", at, color, 6f);
            if (count >= 4) Popup("CANETTRIS !", at + Vector3.up * 0.8f, Color.white, 6f);
            if (tournees > 0) Popup(tournees > 1 ? $"TOURNÉE x{tournees} !" : "TOURNÉE !", at + Vector3.up * 1.5f, new Color(1f, 0.25f, 0.85f), 5.5f);
            if (level > previousLevel) Popup($"NIVEAU {level + 1}", Vector3.up, Color.white, 5f);
            RumbleTeam(0.3f + 0.15f * count, 0.4f + 0.15f * count, 0.15f + 0.05f * count);
        }
        else
        {
            int popped = 0;
            var columnsHit = new Dictionary<int, int>();   // colonne -> plus basse case vidée
            Vector3 center = Vector3.zero;

            foreach (var group in clearingGroups)
            {
                foreach (var cell in group)
                {
                    Vector3 at = CellCenter(cell);
                    Foam(at);
                    center += at;
                    board[cell.x, cell.y] = 0;
                    popped++;
                    columnsHit[cell.x] = columnsHit.TryGetValue(cell.x, out int lowest) ? Mathf.Min(lowest, cell.y) : cell.y;
                }
            }

            // Les canettes au-dessus d'une mousse tombent dans le trou (colonne par colonne).
            foreach (var hit in columnsHit)
            {
                int write = hit.Value;
                for (int y = hit.Value; y < rows; y++)
                {
                    if (board[hit.Key, y] == 0) continue;
                    int value = board[hit.Key, y];
                    board[hit.Key, y] = 0;
                    board[hit.Key, write++] = value;
                }
            }

            int gained = game.popPointsPerCan * popped * multiplier;
            AddScore(gained);
            attack += 1;
            center /= Mathf.Max(1, popped);
            Popup($"MOUSSE ! +{gained}", center, Color.white, 5.5f);
        }

        if (chain >= 2)
        {
            attack += 1;
            Popup($"CHAÎNE x{chain} !", Vector3.up * 1.6f, new Color(1f, 0.85f, 0.2f), 6.5f);
            RumbleTeam(0.6f, 1f, 0.3f);
        }

        System.Array.Clear(clearing, 0, clearing.Length);
        PushActivePiecesOut();
    }

    /// <summary>
    /// Les canettes cabossées montent par le bas (avec un trou dans la même colonne pour
    /// tout l'envoi). Si ça pousse la pile hors du casier : K.O.
    /// </summary>
    private void ApplyJunk()
    {
        int amount = Mathf.Min(pendingJunk, Mathf.Max(1, game.maxJunkPerLock), rows);
        pendingJunk -= amount;

        bool overflow = false;
        for (int y = rows - amount; y < rows; y++)
            for (int x = 0; x < columns; x++)
                if (board[x, y] != 0) overflow = true;

        for (int y = rows - 1; y >= amount; y--)
            for (int x = 0; x < columns; x++)
                board[x, y] = board[x, y - amount];

        int hole = Random.Range(0, columns);
        for (int y = 0; y < amount; y++)
            for (int x = 0; x < columns; x++)
                board[x, y] = x == hole ? 0 : JunkCan;

        // La pièce du partenaire, encore en l'air, remonte avec la pile.
        foreach (var piece in pieces)
        {
            if (!piece.active) continue;
            for (int guard = 0; guard <= rows && Overlaps(piece); guard++) piece.position += Vector2Int.up;
            piece.lowestRow = piece.position.y;
            foreach (var c in piece.Cells)
                if (piece.position.y + c.y >= rows) overflow = true;
        }

        Popup(amount > 1 ? $"+{amount} CABOSSÉES" : "+1 CABOSSÉE", CellCenter(new Vector2(columns * 0.5f - 0.5f, amount)), Danger, 4.5f);
        RumbleTeam(0.6f, 0.3f, 0.2f);

        if (overflow) KnockOut();
    }

    private bool Overlaps(Piece piece)
    {
        foreach (var c in piece.Cells)
        {
            Vector2Int cell = piece.position + c;
            if (cell.x >= 0 && cell.x < columns && cell.y >= 0 && cell.y < rows && board[cell.x, cell.y] != 0) return true;
        }
        return false;
    }

    /// <summary>Après une chute de canettes, une pièce en cours peut se retrouver dans la pile : on la remonte juste assez.</summary>
    private void PushActivePiecesOut()
    {
        foreach (var piece in pieces)
        {
            if (!piece.active) continue;
            for (int guard = 0; guard < rows && !FitsBoard(piece.Cells, piece.position); guard++)
                piece.position += Vector2Int.up;
        }
    }

    private void KnockOut()
    {
        if (state == State.Out) return;

        state = State.Out;
        foreach (var piece in pieces) piece.active = false;
        pendingJunk = 0;
        RumbleTeam(0.8f, 0.8f, 0.6f);

        if (versus)
        {
            overlay.enabled = true;
            overlayText.text = "K.O.";
            overlayText.color = Danger;
        }
        game.OnKnockedOut(this);
    }

    /// <summary>Groupes de canettes de même marque qui se touchent (haut, bas, gauche, droite), d'au moins <paramref name="minSize"/> canettes.</summary>
    private List<List<Vector2Int>> FindGroups(int minSize)
    {
        var groups = new List<List<Vector2Int>>();
        var seen = new bool[columns, rows];
        var stack = new Stack<Vector2Int>();

        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < columns; x++)
            {
                int brand = board[x, y];
                if (brand <= 0 || seen[x, y]) continue;   // vide ou cabossée : ne mousse jamais

                var group = new List<Vector2Int>();
                stack.Push(new Vector2Int(x, y));
                seen[x, y] = true;

                while (stack.Count > 0)
                {
                    Vector2Int cell = stack.Pop();
                    group.Add(cell);

                    foreach (var step in Neighbours)
                    {
                        Vector2Int n = cell + step;
                        if (n.x < 0 || n.x >= columns || n.y < 0 || n.y >= rows) continue;
                        if (seen[n.x, n.y] || board[n.x, n.y] != brand) continue;
                        seen[n.x, n.y] = true;
                        stack.Push(n);
                    }
                }

                if (group.Count >= minSize) groups.Add(group);
            }
        }

        return groups;
    }

    private static readonly Vector2Int[] Neighbours = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

    /// <summary>Marque les canettes à une pièce (4 canettes) de mousser : elles frétillent.</summary>
    private void RefreshFizz()
    {
        System.Array.Clear(fizzing, 0, fizzing.Length);
        foreach (var group in FindGroups(Mathf.Max(1, game.popThreshold - 4)))
            foreach (var cell in group)
                fizzing[cell.x, cell.y] = true;
    }

    private void AddScore(int gained)
    {
        score += gained;
        scorePunch = 1f;
    }

    private void RumbleTeam(float low, float high, float duration)
    {
        foreach (var input in inputs)
            if (input != null) game.Rumble(input.gamepad, low, high, duration);
    }

    private Color PlayerColor(int p) => game.PlayerColor(playerNumbers[p]);

    // ------------------------------------------------------------------ construction de l'affichage

    private Vector3 CellCenter(Vector2 cell)
    {
        return origin + new Vector3(cell.x * cellWidth, cell.y * cellHeight, 0f);
    }

    private SpriteRenderer NewRenderer(string name, Sprite sprite, int order, Transform parent = null)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent != null ? parent : root, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingOrder = order;
        return sr;
    }

    private SpriteRenderer NewPanel(string name, Vector2 size, Color color, int order, Vector3 position, Transform parent = null)
    {
        var sr = NewRenderer(name, game.Rounded, order, parent);
        sr.drawMode = SpriteDrawMode.Sliced;
        sr.size = size;
        sr.color = color;
        sr.transform.localPosition = position;
        return sr;
    }

    private TextMeshPro NewText(string name, Vector3 position, float size, Color color, TextAlignmentOptions alignment, Transform parent = null, float width = 8f)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent != null ? parent : root, false);
        go.transform.localPosition = position;

        var tmp = go.AddComponent<TextMeshPro>();
        if (game.font != null) tmp.font = game.font;
        tmp.fontSize = size;
        tmp.color = color;
        tmp.alignment = alignment;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.rectTransform.sizeDelta = new Vector2(width, 1.5f);
        tmp.sortingOrder = TextOrder;
        return tmp;
    }

    private SpriteRenderer[] NewMiniPiece(string name, float scale)
    {
        var cans = new SpriteRenderer[4];
        for (int i = 0; i < 4; i++)
        {
            cans[i] = NewRenderer(name, null, PieceOrder);
            cans[i].transform.localScale = canScale * scale;
        }
        return cans;
    }

    private void BuildVisuals()
    {
        float top = wellSize.y * 0.5f;
        float bottom = -top;
        float half = wellSize.x * 0.5f;
        float panelX = half + 1.55f;
        var dim = new Color(1f, 1f, 1f, 0.7f);

        // Le casier
        NewPanel("Casier", wellSize + new Vector2(0.3f, 0.3f), new Color(0.06f, 0.05f, 0.07f, 0.82f), PanelOrder, Vector3.zero);
        if (PlayerCount >= 2)
            NewPanel("Milieu", new Vector2(0.03f, wellSize.y), new Color(1f, 1f, 1f, 0.08f), PanelOrder, Vector3.zero);

        var boardRoot = new GameObject("Canettes posées").transform;
        boardRoot.SetParent(root, false);
        boardCans = new SpriteRenderer[columns, rows];
        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < columns; x++)
            {
                var sr = NewRenderer($"{x},{y}", null, BoardOrder, boardRoot);
                sr.transform.localPosition = CellCenter(new Vector2(x, y));
                sr.transform.localScale = canScale;
                sr.enabled = false;
                boardCans[x, y] = sr;
            }
        }

        // Jauge des canettes cabossées qui arrivent, contre le bord gauche du casier.
        NewPanel("Jauge fond", new Vector2(0.12f, wellSize.y), new Color(1f, 1f, 1f, versus ? 0.06f : 0f), PanelOrder, new Vector3(junkX, 0f, 0f));
        junkBar = NewPanel("Jauge", new Vector2(0.12f, 0.12f), Danger, PanelOrder + 1, new Vector3(junkX, bottom, 0f));
        junkBar.enabled = false;

        // Le comptoir, au-dessus du casier, au milieu : il appartient à toute l'équipe.
        counterAnchor = new Vector3(0f, top + 0.95f, 0f);
        counterPanel = NewPanel("Comptoir", new Vector2(2.4f, 1.25f), new Color(0.33f, 0.22f, 0.15f, 0.95f), PanelOrder, counterAnchor);
        NewText("Comptoir titre", counterAnchor + new Vector3(0f, 0.45f, 0f), 2.4f, new Color(1f, 0.95f, 0.85f), TextAlignmentOptions.Center).text = "COMPTOIR";
        counterCans = NewMiniPiece("Comptoir canette", 0.7f);

        float maxY = top + 1.6f;
        if (versus)
        {
            NewText("Équipe", new Vector3(0f, top + 1.95f, 0f), 4f, game.TeamColor(team), TextAlignmentOptions.Center).text = Name;
            maxY = top + 2.3f;
        }

        for (int p = 0; p < 2; p++)
        {
            pieceCans[p] = new SpriteRenderer[4];
            pieceGlows[p] = new SpriteRenderer[4];
            ghostCans[p] = new SpriteRenderer[4];

            float x = p == 0 ? -panelX : panelX;
            nextAnchors[p] = new Vector3(x, top - 1.45f, 0f);

            if (inputs[p] == null) continue;

            Color color = PlayerColor(p);
            for (int i = 0; i < 4; i++)
            {
                pieceCans[p][i] = NewRenderer($"J{playerNumbers[p] + 1} canette", null, PieceOrder);
                pieceCans[p][i].transform.localScale = canScale;

                pieceGlows[p][i] = NewPanel($"J{playerNumbers[p] + 1} halo", new Vector2(cellWidth * 0.96f, cellHeight * 0.98f), WithAlpha(color, 0.5f), GlowOrder, Vector3.zero);

                ghostCans[p][i] = NewRenderer($"J{playerNumbers[p] + 1} fantôme", null, GhostOrder);
                ghostCans[p][i].transform.localScale = canScale;
                ghostCans[p][i].color = WithAlpha(color, 0.3f);
            }

            playerLabels[p] = NewText($"J{playerNumbers[p] + 1} Nom", new Vector3(x, top - 0.15f, 0f), 3.2f, color, TextAlignmentOptions.Center);

            NewPanel($"J{playerNumbers[p] + 1} Suivante fond", new Vector2(1.9f, 1.5f), new Color(0.06f, 0.05f, 0.07f, 0.7f), PanelOrder, new Vector3(x, top - 1.35f, 0f));
            NewText($"J{playerNumbers[p] + 1} Suivante titre", new Vector3(x, top - 0.75f, 0f), 2f, dim, TextAlignmentOptions.Center).text = "SUIVANTE";
            nextCans[p] = NewMiniPiece($"J{playerNumbers[p] + 1} suivante", 0.75f);

            BuildHelp(inputs[p], new Vector3(x, bottom + 2.6f, 0f), color);   // en bas : le bandeau central ne la cache pas
        }

        // Score à gauche, lignes et niveau à droite, sous les pièces suivantes.
        float statsY = top - 2.8f;
        NewText("Score titre", new Vector3(-panelX, statsY, 0f), 2f, dim, TextAlignmentOptions.Center).text = "SCORE";
        scoreText = NewText("Score", new Vector3(-panelX, statsY - 0.55f, 0f), 5f, Color.white, TextAlignmentOptions.Center);
        NewText("Infos titre", new Vector3(panelX, statsY, 0f), 2f, dim, TextAlignmentOptions.Center).text = "LIGNES · NIVEAU";
        infoText = NewText("Infos", new Vector3(panelX, statsY - 0.55f, 0f), 5f, Color.white, TextAlignmentOptions.Center);

        // Voile « K.O. » / « GAGNÉ ! » par-dessus le casier.
        overlay = NewPanel("Voile", wellSize + new Vector2(0.3f, 0.3f), OverlayDark, OverlayOrder, Vector3.zero);
        overlay.enabled = false;
        overlayText = NewText("Voile texte", new Vector3(0f, -wellSize.y * 0.25f, 0f), 9f, Danger, TextAlignmentOptions.Center);   // sous le bandeau central
        overlayText.sortingOrder = OverlayOrder + 1;
        overlayText.fontStyle = FontStyles.Bold;

        float extent = panelX + 1.25f;
        BoundsMin = new Vector2(-extent, bottom - 0.3f);
        BoundsMax = new Vector2(extent, maxY);
    }

    /// <summary>
    /// L'aide des commandes du joueur, sous son score : une ligne par action, avec les
    /// boutons Xbox dessinés dans leurs couleurs (A vert, B rouge, X bleu, Y jaune).
    /// </summary>
    private void BuildHelp(CanTetrisInput input, Vector3 topLeftCenter, Color color)
    {
        var help = new GameObject("Commandes").transform;
        help.SetParent(root, false);
        help.localPosition = topLeftCenter;

        NewText("Commandes titre", new Vector3(0f, 0.5f, 0f), 2f, WithAlpha(color, 0.9f), TextAlignmentOptions.Center, help).text =
            input.IsGamepad ? "MANETTE" : "CLAVIER";

        float y = 0f;
        foreach (var line in input.Help())
        {
            float x = -1.12f;
            foreach (string key in line.keys)
                x += DrawKey(help, key, input.IsGamepad, new Vector2(x, y)) + 0.06f;

            NewText(line.action, new Vector3(x + 0.06f + 1.5f, y, 0f), 1.8f, new Color(1f, 1f, 1f, 0.85f), TextAlignmentOptions.Left, help, 3f).text = line.action;
            y -= 0.44f;
        }
    }

    /// <summary>Dessine une touche à partir de <paramref name="left"/> (bord gauche) et renvoie sa largeur.</summary>
    private float DrawKey(Transform parent, string key, bool gamepad, Vector2 left)
    {
        bool round = FaceButtonColor(key, out Color face) && gamepad;

        if (round)
        {
            const float size = 0.36f;
            var disc = NewRenderer($"Bouton {key}", game.Circle, TextOrder - 1, parent);
            disc.transform.localPosition = new Vector3(left.x + size * 0.5f, left.y, 0f);
            disc.transform.localScale = Vector3.one * size;
            disc.color = face;
            var letter = NewText($"Lettre {key}", new Vector3(left.x + size * 0.5f, left.y - 0.01f, 0f), 2.1f, Color.white, TextAlignmentOptions.Center, parent, 1f);
            letter.text = key;
            letter.fontStyle = FontStyles.Bold;
            return size;
        }

        float width = 0.2f + key.Length * 0.095f;
        NewPanel($"Touche {key}", new Vector2(width, 0.32f), new Color(0.88f, 0.86f, 0.82f, 0.95f), TextOrder - 1, new Vector3(left.x + width * 0.5f, left.y, 0f), parent);
        var label = NewText($"Touche {key} texte", new Vector3(left.x + width * 0.5f, left.y - 0.01f, 0f), 1.6f, new Color(0.1f, 0.08f, 0.1f), TextAlignmentOptions.Center, parent, 2f);
        label.text = key;
        return width;
    }

    private static bool FaceButtonColor(string key, out Color color)
    {
        switch (key)
        {
            case "A": color = new Color(0.35f, 0.7f, 0.2f); return true;
            case "B": color = new Color(0.85f, 0.2f, 0.18f); return true;
            case "X": color = new Color(0.2f, 0.45f, 0.9f); return true;
            case "Y": color = new Color(0.95f, 0.72f, 0.1f); return true;
            default: color = Color.white; return false;
        }
    }

    // ------------------------------------------------------------------ rendu

    private void Render(float dt)
    {
        float t = Time.time;
        float clearProgress = state == State.Resolving ? 1f - Mathf.Clamp01(resolveTimer / Mathf.Max(0.01f, game.clearDelay)) : 0f;
        bool dimmed = state == State.Out;
        Sprite junkSprite = game.JunkSprite;

        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < columns; x++)
            {
                int value = board[x, y];
                var sr = boardCans[x, y];
                sr.enabled = value != 0;
                if (value == 0) continue;

                Color tint = Color.white;
                Quaternion rotation = Quaternion.identity;
                Vector3 scale = canScale;

                if (value == JunkCan)
                {
                    // Canette cabossée : grise et de travers.
                    sr.sprite = junkSprite;
                    tint = JunkTint;
                    rotation = Quaternion.Euler(0f, 0f, ((x * 7 + y * 13) % 5 - 2) * 6f);
                    scale = canScale * 0.94f;
                }
                else
                {
                    sr.sprite = game.BrandSprites[(value - 1) % game.BrandSprites.Length];
                }

                if (clearing[x, y])
                {
                    // Disparition : la canette gonfle et s'efface.
                    scale = canScale * (1f + 0.3f * clearProgress);
                    rotation = Quaternion.identity;
                    tint = Color.Lerp(tint, new Color(1f, 1f, 1f, 0.2f), clearProgress);
                }
                else if (fizzing[x, y] && !dimmed)
                {
                    // À une pièce de mousser : la canette frétille.
                    float wobble = Mathf.Sin(t * 18f + x * 1.7f + y * 2.3f);
                    rotation = Quaternion.Euler(0f, 0f, wobble * 5f);
                    scale = canScale * (1f + 0.04f * Mathf.Abs(wobble));
                }

                if (dimmed) tint *= new Color(0.45f, 0.45f, 0.45f, 1f);

                sr.color = tint;
                sr.transform.localRotation = rotation;
                sr.transform.localScale = scale;
            }
        }

        bool playing = state == State.Playing || state == State.Resolving;
        for (int p = 0; p < 2; p++)
        {
            if (inputs[p] == null) continue;

            Piece piece = pieces[p];
            bool visible = piece.active && playing;
            Vector2Int ghost = visible ? GhostPosition(piece) : Vector2Int.zero;
            Sprite sprite = game.BrandSprites[piece.brand % game.BrandSprites.Length];
            Vector2Int[] cells = piece.Cells;

            for (int i = 0; i < 4; i++)
            {
                pieceCans[p][i].enabled = visible;
                pieceGlows[p][i].enabled = visible;
                ghostCans[p][i].enabled = visible && ghost != piece.position;
                if (!visible) continue;

                Vector3 at = CellCenter(piece.position + cells[i]);
                pieceCans[p][i].sprite = sprite;
                pieceCans[p][i].transform.localPosition = at;
                pieceGlows[p][i].transform.localPosition = at;

                ghostCans[p][i].sprite = sprite;
                ghostCans[p][i].transform.localPosition = CellCenter(ghost + cells[i]);
            }

            DrawMini(nextCans[p], piece.nextType, piece.nextBrand, nextAnchors[p], 0.75f, playing);
        }

        bool showCounter = counterFull && playing;
        DrawMini(counterCans, counterType, counterBrand, counterAnchor + Vector3.down * 0.12f, 0.7f, showCounter);

        counterPunch = Mathf.MoveTowards(counterPunch, 0f, dt * 3f);
        Color panel = new Color(0.33f, 0.22f, 0.15f, 0.95f);
        counterPanel.color = showCounter && inputs[counterOwner] != null ? Color.Lerp(panel, PlayerColor(counterOwner), 0.25f + 0.5f * counterPunch) : panel;
        counterPanel.transform.localScale = Vector3.one * (1f + 0.12f * counterPunch);

        // Jauge des canettes cabossées en attente.
        shownJunk = Mathf.MoveTowards(shownJunk, Mathf.Min(pendingJunk, rows), dt * 12f);
        junkPunch = Mathf.MoveTowards(junkPunch, 0f, dt * 2.5f);
        junkBar.enabled = shownJunk > 0.05f;
        if (junkBar.enabled)
        {
            float height = Mathf.Max(0.12f, shownJunk * cellHeight);
            float width = 0.12f * (1f + 0.8f * junkPunch);
            junkBar.size = new Vector2(width, height);
            junkBar.transform.localPosition = new Vector3(junkX, -wellSize.y * 0.5f + height * 0.5f, 0f);
            float pulse = 0.75f + 0.25f * Mathf.Sin(t * 10f);
            junkBar.color = Color.Lerp(Danger * pulse, Color.white, junkPunch * 0.6f);
        }

        if (won)
        {
            // Lueur qui pulse dans la couleur de l'équipe.
            Color glow = game.TeamColor(team);
            glow.a = 0.22f + 0.14f * Mathf.Sin(t * 5f);
            overlay.color = glow;
        }

        if (!string.IsNullOrEmpty(overlayText.text))
            overlayText.transform.localScale = Vector3.one * (1f + 0.05f * Mathf.Sin(t * 4f));
    }

    /// <summary>Dessine une pièce en miniature, centrée sur <paramref name="center"/>.</summary>
    private void DrawMini(SpriteRenderer[] cans, int type, int brand, Vector3 center, float scale, bool visible)
    {
        Vector2Int[] cells = CanTetrisShapes.Cells(type, 0);
        Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
        Vector2 max = new Vector2(float.MinValue, float.MinValue);
        foreach (var c in cells)
        {
            min = Vector2.Min(min, c);
            max = Vector2.Max(max, c);
        }
        Vector2 middle = (min + max) * 0.5f;

        for (int i = 0; i < 4; i++)
        {
            cans[i].enabled = visible;
            if (!visible) continue;
            cans[i].sprite = game.BrandSprites[brand % game.BrandSprites.Length];
            Vector2 offset = cells[i] - middle;
            cans[i].transform.localPosition = center + new Vector3(offset.x * cellWidth * scale, offset.y * cellHeight * scale, 0f);
        }
    }

    /// <summary>Où la pièce tomberait si on la lâchait maintenant (aperçu transparent).</summary>
    private Vector2Int GhostPosition(Piece piece)
    {
        Vector2Int position = piece.position;
        while (Fits(piece, piece.Cells, position + Vector2Int.down)) position += Vector2Int.down;
        return position;
    }

    private void UpdateTexts(float dt)
    {
        // Compteur en float : avec un int, à haute fréquence d'images le pas arrondi tombait
        // à 0 et le score affiché restait bloqué quelques points sous le vrai score.
        shownScore = Mathf.MoveTowards(shownScore, score, Mathf.Max(30f, Mathf.Abs(score - shownScore) * 8f) * dt);
        scoreText.text = Mathf.RoundToInt(shownScore).ToString();
        scorePunch = Mathf.MoveTowards(scorePunch, 0f, dt * 3f);
        scoreText.transform.localScale = Vector3.one * (1f + 0.3f * scorePunch);

        infoText.text = $"{lines} · {level + 1}";

        for (int p = 0; p < 2; p++)
            if (inputs[p] != null)
                playerLabels[p].text = $"J{playerNumbers[p] + 1} · {inputs[p].label}";
    }

    // ------------------------------------------------------------------ effets

    private void Flash(int row)
    {
        var sr = NewPanel("Éclair", new Vector2(wellSize.x, cellHeight), Color.white, FlashOrder, new Vector3(0f, CellCenter(new Vector2(0, row)).y, 0f));
        effects.Add(new Effect { target = sr.transform, sprite = sr, life = game.clearDelay + 0.1f, color = new Color(1f, 1f, 1f, 0.9f) });
    }

    /// <summary>Mousse : quelques bulles qui jaillissent de la canette qui éclate.</summary>
    private void Foam(Vector3 at)
    {
        for (int i = 0; i < 3; i++)
        {
            var bubble = NewRenderer("Bulle", game.Circle, PopupOrder - 1);
            bubble.transform.localPosition = at + (Vector3)(Random.insideUnitCircle * cellWidth * 0.5f);
            bubble.transform.localScale = Vector3.one * Random.Range(0.08f, 0.18f);
            effects.Add(new Effect
            {
                target = bubble.transform,
                sprite = bubble,
                life = Random.Range(0.5f, 0.9f),
                velocity = new Vector3(Random.Range(-0.6f, 0.6f), Random.Range(1f, 2.2f), 0f),
                color = new Color(1f, 1f, 1f, 0.85f),
            });
        }
    }

    private void Popup(string text, Vector3 localPosition, Color color, float size)
    {
        var tmp = NewText("Popup", localPosition, size, color, TextAlignmentOptions.Center);
        tmp.text = text;
        tmp.sortingOrder = PopupOrder;
        tmp.fontStyle = FontStyles.Bold;
        effects.Add(new Effect { target = tmp.transform, text = tmp, life = 1.1f, rise = 1f, color = color });
    }

    private void UpdateEffects(float dt)
    {
        for (int i = effects.Count - 1; i >= 0; i--)
        {
            Effect fx = effects[i];
            fx.age += dt;
            float t = Mathf.Clamp01(fx.age / fx.life);

            if (fx.target != null)
            {
                fx.target.localPosition += (Vector3.up * (fx.rise * (1f - t) * 1.5f) + fx.velocity) * dt;
                if (fx.text != null)
                {
                    fx.text.alpha = t < 0.6f ? 1f : 1f - (t - 0.6f) / 0.4f;
                    fx.target.localScale = Vector3.one * (1f + 0.4f * Mathf.Max(0f, 1f - fx.age / 0.12f));
                }
                if (fx.sprite != null) fx.sprite.color = WithAlpha(fx.color, fx.color.a * (1f - t));
            }

            if (fx.age >= fx.life)
            {
                if (fx.target != null) Object.Destroy(fx.target.gameObject);
                effects.RemoveAt(i);
            }
        }
    }

    private static Color WithAlpha(Color c, float a)
    {
        c.a = a;
        return c;
    }
}
