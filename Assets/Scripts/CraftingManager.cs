using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

// The crafting panel: spend the materials you collected (Bio, Scrap, Rations) on useful things.
// Every option is a CraftingRecipe asset, so prices and effects are edited in the Inspector.
//
// Setup: click Path Game > Add Crafting System To Scene. It creates the four starter recipes as assets
// (Assets/Data/Crafting) and wires everything up. Without it, the game still runs with built-in defaults.
public class CraftingManager : MonoBehaviour
{
    public static CraftingManager Instance { get; private set; }

    [SerializeField] private PlayerController player;
    [SerializeField] private GridManager grid;
    [SerializeField] private FlashlightEnergy energy;
    [SerializeField] private TileDeck deck;

    [Tooltip("The options shown in the panel, in this order. Drag more recipe assets in here.")]
    [SerializeField] private List<CraftingRecipe> recipes = new List<CraftingRecipe>();

    [Tooltip("Shows the crafting panel on the left side of the screen.")]
    [SerializeField] private bool showPanel = true;

    [Tooltip("Press this key to fold the panel away or open it again.")]
    [SerializeField] private KeyCode toggleKey = KeyCode.C;

    public bool IsTargeting => targeting != null;

    // Other scripts can listen to this (for example to play a sound).
    public event Action<CraftingRecipe> Crafted;

    private readonly Dictionary<CraftingRecipe, int> uses = new Dictionary<CraftingRecipe, int>();
    private CraftContext context;
    private CraftingRecipe targeting;
    private bool panelOpen = true;

    private string message;
    private Color messageColor = Color.white;
    private float messageUntil;

    private const float PanelX = 10f;
    private const float PanelY = 140f;
    private const float PanelWidth = 260f;
    private const float HeaderHeight = 24f;
    private const float RowHeight = 64f;
    private const float RowGap = 4f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (FindObjectOfType<CraftingManager>() != null) return;
        if (FindObjectOfType<PlayerController>() == null || FindObjectOfType<GridManager>() == null) return;
        new GameObject("Crafting").AddComponent<CraftingManager>();
    }

    void Awake()
    {
        Instance = this;

        if (player == null) player = FindObjectOfType<PlayerController>();
        if (grid == null) grid = FindObjectOfType<GridManager>();
        if (energy == null) energy = FindObjectOfType<FlashlightEnergy>();
        if (deck == null) deck = FindObjectOfType<TileDeck>();

        // Nothing assigned yet? Use the built-in starter recipes so the panel is never empty.
        recipes.RemoveAll(r => r == null);
        if (recipes.Count == 0) recipes.AddRange(BuildDefaultRecipes());

        context = new CraftContext { player = player, grid = grid, energy = energy, deck = deck };
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void OnEnable()
    {
        if (player != null) player.WasReset += ResetRun;
    }

    void OnDisable()
    {
        if (player != null) player.WasReset -= ResetRun;
    }

    // Prices go back to normal when the game restarts.
    void ResetRun()
    {
        uses.Clear();
        targeting = null;
    }

    // ---------- The starter recipes ----------
    // The numbers here are only the starting values. Path Game > Add Crafting System To Scene saves them
    // as assets, and after that you change the numbers in the Inspector instead.

    public static List<CraftingRecipe> BuildDefaultRecipes()
    {
        var list = new List<CraftingRecipe>();

        var charge = ScriptableObject.CreateInstance<RechargeRecipe>();
        charge.displayName = "Quick Charge";
        charge.restoreAmount = 3;
        charge.cost = new[] { new ResourceCost(ResourceType.Bio, 2) };
        list.Add(charge);

        var battery = ScriptableObject.CreateInstance<BatteryUpgradeRecipe>();
        battery.displayName = "Bigger Battery";
        battery.extraCapacity = 1;
        battery.extraCostPerUse = 1;
        battery.cost = new[] { new ResourceCost(ResourceType.Scrap, 3), new ResourceCost(ResourceType.Rations, 2) };
        list.Add(battery);

        var demolish = ScriptableObject.CreateInstance<DemolishRecipe>();
        demolish.displayName = "Demolish Tile";
        demolish.cost = new[] { new ResourceCost(ResourceType.Scrap, 1) };
        list.Add(demolish);

        var teleport = ScriptableObject.CreateInstance<TeleportRecipe>();
        teleport.displayName = "Teleport";
        teleport.cost = new[] { new ResourceCost(ResourceType.Rations, 3), new ResourceCost(ResourceType.Bio, 1) };
        list.Add(teleport);

        return list;
    }

    // ---------- Using a recipe ----------

    int Uses(CraftingRecipe recipe) => uses.TryGetValue(recipe, out int n) ? n : 0;

    bool CanAfford(CraftingRecipe recipe)
    {
        int used = Uses(recipe);
        for (int i = 0; i < recipe.cost.Length; i++)
        {
            if (recipe.cost[i].resource == ResourceType.None) continue;
            if (player.GetResource(recipe.cost[i].resource) < recipe.CostFor(i, used)) return false;
        }
        return true;
    }

    // Why this recipe can't be used for reasons other than price (or null if it's fine).
    string LogicalBlock(CraftingRecipe recipe)
    {
        if (player == null || grid == null) return "Not ready";
        if (player.HasWon) return "You already won!";
        if (player.IsMoving) return "Wait until you stop moving";
        if (recipe.maxUses > 0 && Uses(recipe) >= recipe.maxUses) return "Used up this run";
        return recipe.BlockedReason(context);
    }

    void Pay(CraftingRecipe recipe)
    {
        int used = Uses(recipe);
        for (int i = 0; i < recipe.cost.Length; i++)
        {
            if (recipe.cost[i].resource == ResourceType.None) continue;
            player.RemoveResource(recipe.cost[i].resource, recipe.CostFor(i, used));
        }
    }

    // Called when a recipe button is clicked.
    public void TryCraft(CraftingRecipe recipe)
    {
        if (recipe == null || player == null) return;

        // Clicking the option you're already aiming cancels it.
        if (targeting == recipe)
        {
            CancelTargeting();
            return;
        }

        string blocked = LogicalBlock(recipe);
        if (blocked != null) { Notify(blocked, new Color(1f, 0.6f, 0.3f)); return; }
        if (!CanAfford(recipe)) { Notify("Not enough materials", new Color(1f, 0.4f, 0.35f)); return; }

        if (recipe.NeedsTarget)
        {
            targeting = recipe; // the player now clicks a cell, see TryTarget
            return;
        }

        Finish(recipe, PayAndApply(recipe, default(Vector2Int)));
    }

    // Called by PlacementSystem when the player clicks a cell while choosing a target.
    public void TryTarget(Vector2Int cell)
    {
        if (targeting == null) return;

        if (!targeting.IsValidTarget(context, cell))
        {
            Notify("Pick one of the highlighted cells", new Color(1f, 0.6f, 0.3f));
            return;
        }

        CraftingRecipe recipe = targeting;
        targeting = null;

        // Things may have changed while aiming, so check again.
        string blocked = LogicalBlock(recipe);
        if (blocked != null) { Notify(blocked, new Color(1f, 0.6f, 0.3f)); return; }
        if (!CanAfford(recipe)) { Notify("Not enough materials", new Color(1f, 0.4f, 0.35f)); return; }

        Finish(recipe, PayAndApply(recipe, cell));
    }

    string PayAndApply(CraftingRecipe recipe, Vector2Int cell)
    {
        Pay(recipe);
        string result = recipe.Apply(context, cell);
        uses[recipe] = Uses(recipe) + 1;
        return result;
    }

    void Finish(CraftingRecipe recipe, string result)
    {
        Notify(recipe.displayName + ": " + result, new Color(0.6f, 1f, 0.7f));
        Debug.Log("Crafting: used " + recipe.displayName + ". " + result);
        Crafted?.Invoke(recipe);
    }

    public void CancelTargeting()
    {
        targeting = null;
    }

    void Notify(string text, Color color)
    {
        message = text;
        messageColor = color;
        messageUntil = Time.unscaledTime + 2.5f;
    }

    // ---------- Input ----------

    void Update()
    {
        if (Input.GetKeyDown(toggleKey)) panelOpen = !panelOpen;

        if (targeting != null)
        {
            bool cancel = Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1);
            if (cancel || player == null || player.HasWon || player.IsMoving) CancelTargeting();
        }
    }

    Rect PanelRect()
    {
        float height = HeaderHeight + (panelOpen ? recipes.Count * (RowHeight + RowGap) : 0f);
        return new Rect(PanelX, PanelY, PanelWidth, height);
    }

    // PlacementSystem uses this so a click on the panel doesn't also place a tile underneath it.
    public bool IsMouseOverPanel()
    {
        if (!showPanel) return false;
        Vector2 mouse = Input.mousePosition;
        mouse.y = Screen.height - mouse.y;
        return PanelRect().Contains(mouse);
    }

    // ---------- Drawing (quick on-screen GUI, no Canvas needed) ----------

    void OnGUI()
    {
        if (player == null || grid == null) return;

        if (targeting != null) DrawTargetHints();
        if (showPanel) DrawPanel();
        DrawBanners();
        GUI.color = Color.white;
    }

    void DrawPanel()
    {
        Rect panel = PanelRect();

        // Header: click it (or press the toggle key) to fold the panel.
        Fill(new Rect(panel.x, panel.y, panel.width, HeaderHeight - 2f), new Color(0.1f, 0.1f, 0.14f, 0.92f));
        var header = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
        header.normal.textColor = Color.white;
        GUI.Label(new Rect(panel.x + 8f, panel.y, panel.width - 16f, HeaderHeight - 2f),
                  (panelOpen ? "v " : "> ") + "Crafting  [" + toggleKey + "]", header);
        if (GUI.Button(new Rect(panel.x, panel.y, panel.width, HeaderHeight - 2f), GUIContent.none, GUIStyle.none))
            panelOpen = !panelOpen;

        if (!panelOpen) return;

        var nameStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold };
        var costStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, richText = true };
        var noteStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, wordWrap = true };

        float y = panel.y + HeaderHeight;
        foreach (CraftingRecipe recipe in recipes)
        {
            Rect row = new Rect(panel.x, y, panel.width, RowHeight);
            y += RowHeight + RowGap;

            string blocked = LogicalBlock(recipe);
            bool affordable = CanAfford(recipe);
            bool usable = blocked == null && affordable;
            bool aiming = targeting == recipe;

            Color border = aiming ? Color.yellow : usable ? new Color(0.85f, 0.7f, 0.25f) : new Color(0.3f, 0.3f, 0.34f);
            Fill(row, border);
            Fill(new Rect(row.x + 2f, row.y + 2f, row.width - 4f, row.height - 4f), new Color(0.12f, 0.12f, 0.16f, 0.95f));

            // Name (with how many times it has been used, if it has a limit).
            string title = recipe.displayName;
            if (recipe.maxUses > 0) title += "  (" + Uses(recipe) + "/" + recipe.maxUses + ")";
            nameStyle.normal.textColor = usable || aiming ? Color.white : new Color(0.65f, 0.65f, 0.7f);
            GUI.Label(new Rect(row.x + 8f, row.y + 3f, row.width - 16f, 20f), title, nameStyle);

            // Price, each material in its own color, red if you can't pay that part.
            costStyle.normal.textColor = Color.white;
            GUI.Label(new Rect(row.x + 8f, row.y + 22f, row.width - 16f, 18f), CostText(recipe), costStyle);

            // A reason it can't be used, or what it does.
            bool showReason = blocked != null;
            noteStyle.normal.textColor = showReason ? new Color(1f, 0.6f, 0.3f) : new Color(0.7f, 0.75f, 0.85f);
            GUI.Label(new Rect(row.x + 8f, row.y + 40f, row.width - 16f, 22f), showReason ? blocked : recipe.DescriptionText, noteStyle);

            if (GUI.Button(row, GUIContent.none, GUIStyle.none)) TryCraft(recipe);
        }
    }

    // "2 Bio, 1 Scrap" with each part colored by material, or red when you don't have enough of it.
    string CostText(CraftingRecipe recipe)
    {
        if (recipe.cost == null || recipe.cost.Length == 0) return "Free";

        int used = Uses(recipe);
        var sb = new StringBuilder("Cost: ");
        bool first = true;
        for (int i = 0; i < recipe.cost.Length; i++)
        {
            ResourceType type = recipe.cost[i].resource;
            if (type == ResourceType.None) continue;

            int price = recipe.CostFor(i, used);
            bool enough = player.GetResource(type) >= price;
            Color color = enough ? ResourceColor(type) : new Color(1f, 0.35f, 0.3f);

            if (!first) sb.Append(", ");
            sb.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(color)).Append(">")
              .Append(price).Append(' ').Append(type).Append("</color>");
            first = false;
        }
        return sb.ToString();
    }

    // While choosing a target: pulse every legal cell on the board and show the instruction.
    void DrawTargetHints()
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 5f);
        Color fill = new Color(1f, 0.85f, 0.3f, 0.12f + 0.22f * pulse);
        Color line = new Color(1f, 0.9f, 0.4f, 0.9f);

        for (int x = 0; x < grid.width; x++)
        for (int y = 0; y < grid.height; y++)
        {
            var cell = new Vector2Int(x, y);
            if (!targeting.IsValidTarget(context, cell)) continue;

            Vector3 center = grid.CellToWorld(cell);
            Vector3 a = cam.WorldToScreenPoint(center);
            if (a.z <= 0f) continue; // behind the camera

            Vector3 b = cam.WorldToScreenPoint(center + cam.transform.right * grid.cellSize * 0.5f);
            float r = Mathf.Clamp(Mathf.Abs(b.x - a.x), 8f, 90f);
            Rect rect = new Rect(a.x - r, Screen.height - a.y - r, r * 2f, r * 2f);

            Fill(rect, fill);
            Fill(new Rect(rect.x, rect.y, rect.width, 2f), line);
            Fill(new Rect(rect.x, rect.yMax - 2f, rect.width, 2f), line);
            Fill(new Rect(rect.x, rect.y, 2f, rect.height), line);
            Fill(new Rect(rect.xMax - 2f, rect.y, 2f, rect.height), line);
        }
    }

    void DrawBanners()
    {
        var style = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };

        // The instruction while aiming (sits under the turn counter at the top).
        if (targeting != null)
        {
            ShadowLabel(new Rect(0f, 96f, Screen.width, 30f), targeting.TargetPrompt, style, new Color(1f, 0.9f, 0.4f));
            var small = new GUIStyle(style) { fontSize = 13 };
            ShadowLabel(new Rect(0f, 124f, Screen.width, 20f), "Right-click or Esc to cancel", small, new Color(0.85f, 0.85f, 0.9f));
        }

        // The result of the last thing you crafted, fading out.
        if (Time.unscaledTime < messageUntil && !string.IsNullOrEmpty(message))
        {
            float alpha = Mathf.Clamp01((messageUntil - Time.unscaledTime) / 0.6f);
            Color c = messageColor;
            c.a = alpha;
            ShadowLabel(new Rect(0f, Screen.height * 0.3f, Screen.width, 36f), message, style, c);
        }
    }

    static void ShadowLabel(Rect rect, string text, GUIStyle style, Color color)
    {
        GUI.color = Color.white;
        style.normal.textColor = new Color(0f, 0f, 0f, 0.7f * color.a);
        GUI.Label(new Rect(rect.x + 2f, rect.y + 2f, rect.width, rect.height), text, style);
        style.normal.textColor = color;
        GUI.Label(rect, text, style);
    }

    static void Fill(Rect rect, Color color)
    {
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = Color.white;
    }

    static Color ResourceColor(ResourceType r)
    {
        switch (r)
        {
            case ResourceType.Bio:     return new Color(0.3f, 0.85f, 0.45f);
            case ResourceType.Scrap:   return new Color(0.9f, 0.65f, 0.3f);
            case ResourceType.Rations: return new Color(0.95f, 0.45f, 0.45f);
            default:                   return Color.white;
        }
    }
}