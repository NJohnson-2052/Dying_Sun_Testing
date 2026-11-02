using System.Collections.Generic;
using UnityEngine;

// Placement + the on-screen "river" of cards.
// Mouse: click a card to select it, scroll wheel to rotate, left click a cell to place,
//        left click a placed tile to walk there, right click to remove a tile.
// Keyboard (optional): 1/2/3 pick a card, R rotates.
public class PlacementSystem : MonoBehaviour
{
    [SerializeField]
    GameObject mouseIndicator; // optional: a marker that snaps to the hovered cell

    [SerializeField]
    private InputManager inputManager;

    [SerializeField]
    private GridManager grid;

    [SerializeField]
    private TileDeck deck;

    [SerializeField]
    private PlayerController player;

    [Header("Card river layout (screen pixels)")]
    [SerializeField] private float cardWidth = 110f;
    [SerializeField] private float cardHeight = 150f;
    [SerializeField] private float cardGap = 12f;
    [SerializeField] private float bottomMargin = 12f;

    private int selectedSlot = 0;
    private int rotationSteps = 0;

    [Header("Ghost preview")]
    [Tooltip("Lifts the preview slightly so it doesn't flicker against the blank tile underneath.")]
    [SerializeField] private float ghostLift = 0.05f;
    [SerializeField] private Color ghostValidColor = new Color(0.6f, 1f, 0.6f, 0.6f);
    [SerializeField] private Color ghostInvalidColor = new Color(1f, 0.4f, 0.4f, 0.6f);

    private GameObject ghost;
    private TileDefinition ghostFor;
    private readonly List<Material> ghostMaterials = new List<Material>();

    [Header("Model calibration")]
    [Tooltip("Shows yellow bars on the ghost where the GAME thinks the road is. Press O to turn the model until its road lines up. Turn this off when you're done.")]
    [SerializeField] private bool calibrationMode = false;

    private GameObject openingMarkers;
    private readonly GameObject[] markerArms = new GameObject[4]; // North, East, South, West

    private GoalGlow goalGlow;

    [Header("Shuffle animation")]
    [Tooltip("How long the shuffle takes, in seconds. Placing is paused while the cards are being dealt.")]
    [SerializeField] private float shuffleDuration = 0.9f;

    private float shuffleStart = -100f;
    private bool IsShuffling => Time.time >= shuffleStart && Time.time - shuffleStart < shuffleDuration;

    [Header("Freeze-away animation (the old cards)")]
    [Tooltip("How long the discarded cards take to shiver, turn icy blue and fall away, in seconds. This plays before the shuffle.")]
    [SerializeField] private float freezeDuration = 0.6f;

    private TileDefinition[] outgoingHand;   // the cards that are about to freeze away
    private int outgoingRotation;
    private float freezeStart = -100f;
    private bool IsFreezing => outgoingHand != null && Time.time - freezeStart < freezeDuration;

    // Placing is paused for the whole sequence: freeze away, then shuffle and deal.
    private bool IsBusy => IsFreezing || IsShuffling;

    // Fades every card shape drawn by FillRect (used while a card is falling away).
    private static float cardAlpha = 1f;

    void OnEnable()
    {
        if (player != null) player.ReachedGoal += OnReachedGoal;
        if (deck != null) deck.Shuffled += StartShuffle;
    }

    void OnDisable()
    {
        if (player != null) player.ReachedGoal -= OnReachedGoal;
        if (deck != null) deck.Shuffled -= StartShuffle;
    }

    void StartShuffle()
    {
        // If we remembered the old hand, it freezes away first and the shuffle starts after that.
        float freezePart = outgoingHand != null ? freezeDuration : 0f;
        freezeStart = Time.time;
        shuffleStart = Time.time + freezePart;
    }

    // Remembers the cards that are about to disappear so they can freeze away.
    // The card that was just played is left out, because it went onto the board.
    void SnapshotHand(int playedSlot)
    {
        outgoingHand = (TileDefinition[])deck.Hand.Clone();
        if (playedSlot >= 0 && playedSlot < outgoingHand.Length) outgoingHand[playedSlot] = null;
        outgoingRotation = rotationSteps;
    }

    // The player stepped onto the goal: start the soft red glow and hum.
    void OnReachedGoal()
    {
        if (goalGlow != null) Destroy(goalGlow.gameObject);
        goalGlow = GoalGlow.Create(grid.CellToWorld(grid.GoalCell) + Vector3.up * 1.5f, grid.cellSize * 4f);
    }

    private void Update()
    {
        if (deck == null || deck.Hand == null) return;

        HandleInput();

        selectedSlot = Mathf.Clamp(selectedSlot, 0, deck.Hand.Length - 1);

        Vector3 mouseWorld = inputManager.GetSelectedMapPosition();
        Vector2Int cell = grid.WorldToCell(mouseWorld);
        bool overCards = IsMouseOverRiver();
        bool gameWon = player != null && player.HasWon;
        bool inBounds = grid.IsInBounds(cell) && !overCards && !gameWon && !IsBusy;

        if (mouseIndicator != null)
        {
            mouseIndicator.SetActive(inBounds);
            if (inBounds) mouseIndicator.transform.position = grid.CellToWorld(cell);
        }

        TileDefinition tile = deck.Hand[selectedSlot];
        UpdateGhost(tile, inBounds, cell);

        if (!inBounds) return;

        if (Input.GetMouseButtonDown(0))
        {
            if (grid.IsOccupied(cell))
            {
                // Clicking an existing tile = walk there.
                if (player != null) player.TryMoveTo(cell);
            }
            else if (tile != null && grid.TryPlace(cell, tile, rotationSteps))
            {
                SnapshotHand(selectedSlot); // so the unused cards can freeze away
                deck.ReplaceSlot(selectedSlot);
            }
        }

        // Right click removes a tile, but never the one the player is standing on.
        if (Input.GetMouseButtonDown(1) && (player == null || cell != player.CurrentCell))
        {
            // Removing a tile changes where things can go, so re-check the hand.
            if (grid.Remove(cell)) deck.EnsurePlayable();
        }
    }

    void HandleInput()
    {
        for (int i = 0; i < deck.Hand.Length && i < 9; i++)
            if (Input.GetKeyDown(KeyCode.Alpha1 + i)) selectedSlot = i;

        if (Input.GetKeyDown(KeyCode.R)) rotationSteps = (rotationSteps + 1) % 4;

        // Mouse wheel rotates: up = clockwise, down = counter-clockwise.
        float scroll = Input.mouseScrollDelta.y;
        if (scroll > 0f) rotationSteps = (rotationSteps + 1) % 4;
        else if (scroll < 0f) rotationSteps = (rotationSteps + 3) % 4;

        if (calibrationMode && Input.GetKeyDown(KeyCode.O)) CycleModelOffset();
    }

    void UpdateGhost(TileDefinition tile, bool inBounds, Vector2Int cell)
    {
        if (tile == null)
        {
            if (ghost != null) ghost.SetActive(false);
            UpdateOpeningMarkers(null, cell, false);
            return;
        }

        if (ghost == null || ghostFor != tile) BuildGhost(tile);

        // Show over any free cell: green if the tile can go here, red if it can't.
        bool show = inBounds && !grid.IsOccupied(cell) && cell != grid.startCell && cell != grid.GoalCell;
        ghost.SetActive(show);
        UpdateOpeningMarkers(tile, cell, show);
        if (!show) return;

        Color tint = grid.CanPlace(cell, tile, rotationSteps) ? ghostValidColor : ghostInvalidColor;
        foreach (Material m in ghostMaterials) m.color = tint;

        ghost.transform.position = grid.PositionFor(tile.prefab, cell, rotationSteps + tile.modelRotationOffset) + Vector3.up * ghostLift;
        ghost.transform.rotation = Quaternion.Euler(0f, 90f * (rotationSteps + tile.modelRotationOffset), 0f);
    }

    void BuildGhost(TileDefinition tile)
    {
        DestroyGhost();

        ghost = Instantiate(tile.prefab);
        ghostFor = tile;
        foreach (var col in ghost.GetComponentsInChildren<Collider>()) col.enabled = false;

        foreach (var rend in ghost.GetComponentsInChildren<Renderer>())
        {
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows = false;

            // Reading .materials gives this ghost its own copies, so the real tile materials stay untouched.
            foreach (Material m in rend.materials)
            {
                MakeTransparent(m);
                ghostMaterials.Add(m);
            }
        }
    }

    void DestroyGhost()
    {
        if (ghost != null) Destroy(ghost);
        foreach (Material m in ghostMaterials) if (m != null) Destroy(m);
        ghostMaterials.Clear();
        ghost = null;
        ghostFor = null;
    }

    // Switches a Standard-shader material to see-through (Fade/Transparent mode).
    static void MakeTransparent(Material m)
    {
        m.SetFloat("_Mode", 3f);
        m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetInt("_ZWrite", 0);
        m.DisableKeyword("_ALPHATEST_ON");
        m.EnableKeyword("_ALPHABLEND_ON");
        m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        m.renderQueue = 3000;
    }

    // ---------- Model calibration (yellow bars = what the game thinks) ----------

    void BuildOpeningMarkers()
    {
        openingMarkers = new GameObject("OpeningMarkers");
        float length = grid.cellSize * 0.5f; // center to edge
        const float thickness = 0.15f;
        Vector3[] directions = { Vector3.forward, Vector3.right, Vector3.back, Vector3.left };

        for (int i = 0; i < 4; i++)
        {
            GameObject arm = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(arm.GetComponent<Collider>());
            arm.transform.SetParent(openingMarkers.transform, false);
            arm.transform.localPosition = directions[i] * (length * 0.5f);
            arm.transform.localScale = (i % 2 == 0)
                ? new Vector3(thickness, thickness, length)
                : new Vector3(length, thickness, thickness);
            arm.GetComponent<Renderer>().material.color = Color.yellow;
            markerArms[i] = arm;
        }
    }

    void UpdateOpeningMarkers(TileDefinition tile, Vector2Int cell, bool show)
    {
        show = show && calibrationMode && tile != null;
        if (!show)
        {
            if (openingMarkers != null) openingMarkers.SetActive(false);
            return;
        }

        if (openingMarkers == null) BuildOpeningMarkers();
        openingMarkers.SetActive(true);

        // The bars follow the game's rules only: they turn with your rotation, NOT with the model offset.
        openingMarkers.transform.position = grid.CellToWorld(cell) + Vector3.up * (ghostLift + 0.4f);
        openingMarkers.transform.rotation = Quaternion.Euler(0f, 90f * rotationSteps, 0f);

        Openings open = PathUtil.BaseOpenings(tile.pathType);
        markerArms[0].SetActive((open & Openings.North) != 0);
        markerArms[1].SetActive((open & Openings.East) != 0);
        markerArms[2].SetActive((open & Openings.South) != 0);
        markerArms[3].SetActive((open & Openings.West) != 0);
    }

    // O turns the selected tile's model by 90 degrees. Shift+O does every tile with the same shape.
    void CycleModelOffset()
    {
        TileDefinition current = deck.Hand[selectedSlot];
        if (current == null || deck.pool == null) return;

        int next = (current.modelRotationOffset + 1) % 4;
        bool wholeShape = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        foreach (TileDefinition def in deck.pool)
        {
            if (def == null) continue;
            if (def != current && !(wholeShape && def.pathType == current.pathType)) continue;

            def.modelRotationOffset = next;
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(def); // so the value is kept after you stop Play mode
#endif
        }

        grid.RefreshPlacedTiles(); // update tiles that are already on the board
    }

    void DrawCalibrationHelp()
    {
        if (!calibrationMode) return;

        TileDefinition t = deck.Hand[Mathf.Clamp(selectedSlot, 0, deck.Hand.Length - 1)];
        if (t == null) return;

        GUI.Label(new Rect(10, 80, 560, 22), "CALIBRATION: yellow bars show where the game thinks the road goes.");
        GUI.Label(new Rect(10, 100, 560, 22), "Press O to turn this model (Shift+O: every " + t.pathType + " tile). " +
                                              t.displayName + " offset = " + t.modelRotationOffset);
    }

    // ---------- Card river (IMGUI, no Canvas setup needed) ----------

    // Rect in GUI space (origin top-left) covering all cards.
    Rect RiverRect()
    {
        int count = deck != null && deck.Hand != null ? deck.Hand.Length : 0;
        float totalWidth = count * cardWidth + Mathf.Max(0, count - 1) * cardGap;
        float x = (Screen.width - totalWidth) * 0.5f;
        float y = Screen.height - cardHeight - bottomMargin;
        return new Rect(x, y, totalWidth, cardHeight);
    }

    // Used to stop clicks on cards from also placing tiles in the world behind them.
    bool IsMouseOverRiver()
    {
        Vector2 m = Input.mousePosition;
        m.y = Screen.height - m.y; // Input origin is bottom-left, GUI origin is top-left
        Rect r = RiverRect();
        r.xMin -= cardGap; r.xMax += cardGap; r.yMin -= cardGap;
        // Also covers the Restart button in the top-right corner.
        return r.Contains(m) || new Rect(Screen.width - 120, 0, 120, 48).Contains(m);
    }

    // ---------- Freeze-away animation ----------
    // The discarded cards shiver, turn icy blue with frost on their edges, then drop away and fade out.

    void DrawFrozenCard(int i, Rect card)
    {
        if (outgoingHand == null || i >= outgoingHand.Length) return;

        TileDefinition def = outgoingHand[i];
        if (def == null) return; // the card that was just played isn't discarded

        float f = Mathf.Clamp01((Time.time - freezeStart) / freezeDuration);

        const float shiverEnd = 0.45f;
        float shiver = Mathf.Clamp01(f / shiverEnd);                     // 0 -> 1 while shivering
        float fall = Mathf.Clamp01((f - shiverEnd) / (1f - shiverEnd));  // 0 -> 1 while falling

        // Shiver: a quick left-right jitter that grows, then stops as the card falls.
        float jitterX = Mathf.Sin(Time.time * 70f + i * 1.7f) * 4f * shiver * (1f - fall);
        float jitterAngle = Mathf.Sin(Time.time * 55f + i * 2.3f) * 2.5f * shiver * (1f - fall);

        // Fall: speed up downward, tilt to one side, fade out.
        float dropY = fall * fall * 260f;
        float tilt = fall * (i % 2 == 0 ? -22f : 22f);
        float alpha = 1f - fall;
        float ice = 0.7f * Mathf.SmoothStep(0f, 1f, shiver);             // how blue the card has turned

        Matrix4x4 savedMatrix = GUI.matrix;
        Vector2 c = card.center;
        GUI.matrix = Matrix4x4.TRS(new Vector3(c.x + jitterX, c.y + dropY, 0f),
                                   Quaternion.Euler(0f, 0f, jitterAngle + tilt),
                                   Vector3.one)
                   * Matrix4x4.TRS(new Vector3(-c.x, -c.y, 0f), Quaternion.identity, Vector3.one);

        cardAlpha = alpha;
        Color savedContent = GUI.contentColor;
        GUI.contentColor = new Color(1f, 1f, 1f, alpha);

        // The card exactly as the player last saw it.
        FillRect(card, new Color(0.15f, 0.15f, 0.18f, 0.95f));
        Openings openings = PathUtil.Rotate(PathUtil.BaseOpenings(def.pathType), outgoingRotation);
        DrawPathIcon(new Rect(card.x + 15, card.y + 10, card.width - 30, card.width - 30), openings, ResourceColor(def.resource));
        GUI.Label(new Rect(card.x + 6, card.y + card.width - 12, card.width - 12, 40), def.displayName);
        GUI.Label(new Rect(card.x + 6, card.y + cardHeight - 22, card.width - 12, 20), "[" + (i + 1) + "]");

        // Icy blue tint over everything, plus frost that creeps in from the top and bottom edges.
        FillRect(card, new Color(0.45f, 0.75f, 1f, ice));
        float frostThickness = 2f + 7f * shiver;
        Color frost = new Color(0.9f, 0.97f, 1f, 0.9f * shiver);
        FillRect(new Rect(card.x, card.y, card.width, frostThickness), frost);
        FillRect(new Rect(card.x, card.yMax - frostThickness, card.width, frostThickness), frost);

        // Little ice sparkles that twinkle, then drift down with the card.
        for (int s = 0; s < 6; s++)
        {
            float sx = card.x + card.width * (0.12f + 0.15f * s);
            float sy = card.y + card.height * (0.1f + 0.15f * ((s * 3) % 6)) + fall * 40f * (1f + s * 0.1f);
            float twinkle = 0.5f + 0.5f * Mathf.Sin(Time.time * 18f + s * 1.9f);
            FillRect(new Rect(sx, sy, 5f, 5f), new Color(0.9f, 0.97f, 1f, shiver * twinkle));
        }

        GUI.contentColor = savedContent;
        cardAlpha = 1f;
        GUI.matrix = savedMatrix;
    }

    // ---------- Shuffle animation ----------
    // 1. the cards slide together into a pile   2. the pile riffles and hops
    // 3. the cards are dealt back out           4. they flip face up, one after another

    // Sets GUI.matrix so card i is drawn where the animation says it is right now.
    // Returns true while the card should still show its back.
    bool ApplyShuffleLook(int i, int count, Rect card, Rect river)
    {
        float t = Mathf.Clamp01((Time.time - shuffleStart) / shuffleDuration);

        Vector2 center = card.center;
        Vector2 pile = river.center;                  // where the cards gather
        float fan = (i - (count - 1) * 0.5f) * 7f;    // degrees: the pile fans out a little

        Vector2 offset = Vector2.zero;
        float angle = 0f;
        float scaleX = 1f;
        bool showBack = true;

        const float gatherEnd = 0.18f;
        const float riffleEnd = 0.5f;
        const float spreadEnd = 0.7f;

        if (t < gatherEnd)
        {
            float p = Smooth(t / gatherEnd);
            offset = (pile - center) * p;
            angle = fan * p;
        }
        else if (t < riffleEnd)
        {
            float p = (t - gatherEnd) / (riffleEnd - gatherEnd);
            float wobble = Mathf.Sin(p * Mathf.PI * 6f + i * 2f);
            offset = (pile - center) + new Vector2(wobble * cardWidth * 0.3f, -Mathf.Abs(wobble) * 14f);
            angle = fan + wobble * 8f;
        }
        else if (t < spreadEnd)
        {
            float p = Smooth((t - riffleEnd) / (spreadEnd - riffleEnd));
            offset = (pile - center) * (1f - p);
            angle = fan * (1f - p);
        }
        else
        {
            // Flip: squash the card to a sliver, switch from back to face, then widen it again.
            float p = (t - spreadEnd) / (1f - spreadEnd);
            float local = Mathf.Clamp01((p - i * 0.15f) / 0.55f);
            scaleX = Mathf.Max(0.02f, Mathf.Abs(Mathf.Cos(local * Mathf.PI)));
            showBack = local < 0.5f;
        }

        // Move the card's center to the origin, rotate and squash it there, then move it to where it should be.
        GUI.matrix = Matrix4x4.TRS(new Vector3(center.x + offset.x, center.y + offset.y, 0f),
                                   Quaternion.Euler(0f, 0f, angle),
                                   new Vector3(scaleX, 1f, 1f))
                   * Matrix4x4.TRS(new Vector3(-center.x, -center.y, 0f), Quaternion.identity, Vector3.one);
        return showBack;
    }

    static float Smooth(float x) => x * x * (3f - 2f * x); // gentle start and stop

    void DrawCardBack(Rect card)
    {
        FillRect(new Rect(card.x + 6, card.y + 6, card.width - 12, card.height - 12), new Color(0.25f, 0.3f, 0.45f, 1f));
        FillRect(new Rect(card.x + 12, card.y + 12, card.width - 24, card.height - 24), new Color(0.15f, 0.18f, 0.3f, 1f));

        var style = new GUIStyle(GUI.skin.label)
        {
            fontSize = 36,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };
        style.normal.textColor = new Color(0.6f, 0.7f, 1f);
        GUI.Label(card, "?", style);
    }

    void OnGUI()
    {
        if (deck == null || deck.Hand == null) return;

        Rect river = RiverRect();
        for (int i = 0; i < deck.Hand.Length; i++)
        {
            var def = deck.Hand[i];
            var card = new Rect(river.x + i * (cardWidth + cardGap), river.y, cardWidth, cardHeight);

            // First the old cards freeze away (the new hand isn't shown yet).
            if (IsFreezing)
            {
                DrawFrozenCard(i, card);
                continue;
            }

            // While the hand is being shuffled, each card is moved, tilted and flipped (see ApplyShuffleLook).
            bool shuffling = IsShuffling;
            Matrix4x4 savedMatrix = GUI.matrix;
            bool showBack = shuffling && ApplyShuffleLook(i, deck.Hand.Length, card, river);

            // Selection border.
            if (i == selectedSlot && !shuffling)
                FillRect(new Rect(card.x - 4, card.y - 4, card.width + 8, card.height + 8), Color.yellow);

            FillRect(card, new Color(0.15f, 0.15f, 0.18f, 0.95f));

            if (showBack)
            {
                DrawCardBack(card);
                GUI.matrix = savedMatrix;
                continue;
            }

            if (!shuffling && GUI.Button(card, GUIContent.none, GUIStyle.none)) selectedSlot = i;
            if (def == null) { GUI.matrix = savedMatrix; continue; }

            // Path icon preview, rotated the same way the tile will be placed.
            Openings openings = PathUtil.Rotate(PathUtil.BaseOpenings(def.pathType), rotationSteps);
            DrawPathIcon(new Rect(card.x + 15, card.y + 10, card.width - 30, card.width - 30), openings, ResourceColor(def.resource));

            GUI.Label(new Rect(card.x + 6, card.y + card.width - 12, card.width - 12, 40), def.displayName);
            GUI.Label(new Rect(card.x + 6, card.y + cardHeight - 22, card.width - 12, 20), "[" + (i + 1) + "]");
            GUI.matrix = savedMatrix;
        }

        GUI.Label(new Rect(river.x, river.y - 24, 400, 22), "Rotation: " + rotationSteps * 90 + "°  (scroll wheel or R)");

        DrawStatus();
        DrawCalibrationHelp();
    }

    // Restart button (always visible) plus win / stuck messages.
    void DrawStatus()
    {
        if (GUI.Button(new Rect(Screen.width - 110, 10, 100, 28), "Restart")) RestartGame();

        string message = null;
        if (player != null && player.HasWon) message = "You reached the goal!";
        else if (deck.NoMovesLeft) message = "No legal placements left. Right-click a tile to remove it, or Restart.";
        if (message == null) return;

        var style = new GUIStyle(GUI.skin.label)
        {
            fontSize = 24,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };
        style.normal.textColor = Color.white;
        GUI.Label(new Rect(0, Screen.height * 0.2f, Screen.width, 50), message, style);
    }

    void RestartGame()
    {
        if (goalGlow != null) Destroy(goalGlow.gameObject);
        if (player != null) player.ResetPlayer();
        grid.ResetBoard();
        SnapshotHand(-1); // the whole old hand freezes away
        deck.ResetDeck();
        selectedSlot = 0;
        rotationSteps = 0;
    }

    // Draws a little cross-shaped icon: a center square plus an arm for each open side.
    void DrawPathIcon(Rect area, Openings openings, Color color)
    {
        FillRect(area, new Color(0.3f, 0.3f, 0.3f, 1f)); // background square
        Vector2 c = area.center;
        float arm = area.width * 0.18f; // half thickness of the path
        float half = area.width * 0.5f;

        FillRect(new Rect(c.x - arm, c.y - arm, arm * 2, arm * 2), color);
        if ((openings & Openings.North) != 0) FillRect(new Rect(c.x - arm, c.y - half, arm * 2, half), color);
        if ((openings & Openings.South) != 0) FillRect(new Rect(c.x - arm, c.y, arm * 2, half), color);
        if ((openings & Openings.East)  != 0) FillRect(new Rect(c.x, c.y - arm, half, arm * 2), color);
        if ((openings & Openings.West)  != 0) FillRect(new Rect(c.x - half, c.y - arm, half, arm * 2), color);
    }

    static Color ResourceColor(ResourceType r)
    {
        switch (r)
        {
            case ResourceType.Bio:     return new Color(0.3f, 0.8f, 0.4f);
            case ResourceType.Scrap:   return new Color(0.85f, 0.6f, 0.25f);
            case ResourceType.Rations: return new Color(0.9f, 0.35f, 0.35f);
            default: return new Color(0.8f, 0.8f, 0.8f);
        }
    }

    static void FillRect(Rect r, Color c)
    {
        Color old = GUI.color;
        GUI.color = new Color(c.r, c.g, c.b, c.a * cardAlpha);
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = old;
    }
}