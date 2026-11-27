using System.Collections.Generic;
using UnityEngine;

// Tells the player where they are in the run and marks everything on the board:
//   1. Visit every dome            -> "Domes 1/3" counter, a label over each dome, a soft glow on visited ones
//   2. The Cellar appears          -> a banner, and a purple "Cellar" label
//   3. Visit the Cellar, then return to the ship (the start tile) to finish
// The ship at the start tile hums when the run is complete.
//
// You don't need to add this to the scene: it creates itself when the game starts.
public class GoalTracker : MonoBehaviour
{
    [SerializeField] private PlayerController player;
    [SerializeField] private GridManager grid;

    [Tooltip("Shows the Domes counter and the current objective in the top-right corner.")]
    [SerializeField] private bool showHud = true;

    [Tooltip("Shows a floating label over every dome, the Cellar and the ship.")]
    [SerializeField] private bool showWorldLabels = true;

    [Tooltip("How high above the ground the glow and the labels float.")]
    [SerializeField] private float glowHeight = 1.5f;

    private readonly Dictionary<Vector2Int, GoalGlow> glows = new Dictionary<Vector2Int, GoalGlow>();

    private string bannerTitle;
    private string bannerSub;
    private float bannerUntil;

    private static readonly Color Gold = new Color(1f, 0.85f, 0.25f);
    private static readonly Color Done = new Color(0.45f, 1f, 0.55f);
    private static readonly Color Purple = new Color(0.78f, 0.55f, 1f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (FindObjectOfType<GoalTracker>() != null) return;
        if (FindObjectOfType<PlayerController>() == null || FindObjectOfType<GridManager>() == null) return;
        new GameObject("Goal Tracker").AddComponent<GoalTracker>();
    }

    void Awake()
    {
        if (player == null) player = FindObjectOfType<PlayerController>();
        if (grid == null) grid = FindObjectOfType<GridManager>();
    }

    void OnEnable()
    {
        if (player == null) return;
        player.GoalVisited += OnDomeVisited;
        player.AllDomesVisited += OnAllDomesVisited;
        player.CellarVisited += OnCellarVisited;
        player.ReachedGoal += OnRunComplete;
        player.WasReset += ResetAll;
    }

    void OnDisable()
    {
        if (player == null) return;
        player.GoalVisited -= OnDomeVisited;
        player.AllDomesVisited -= OnAllDomesVisited;
        player.CellarVisited -= OnCellarVisited;
        player.ReachedGoal -= OnRunComplete;
        player.WasReset -= ResetAll;
    }

    // ---------- Events ----------

    void OnDomeVisited(Vector2Int cell)
    {
        EnsureGlow(cell, false);
    }

    // The last dome was visited: the Cellar is on the board now.
    void OnAllDomesVisited()
    {
        if (grid.HasCellar) EnsureGlow(grid.CellarCell, false); // a beacon so it can be spotted

        Banner("THE CELLAR HAS APPEARED!", "Find it, then return to the ship");
        CameraThump.Thump(0.2f, 0.5f);
    }

    void OnCellarVisited(Vector2Int cell)
    {
        EnsureGlow(cell, false);
        Banner("YOU FOUND THE CELLAR!", "Now return to the ship");
    }

    // Back at the ship after the Cellar: the run is complete, and the ship starts to hum.
    void OnRunComplete()
    {
        GoalGlow glow = EnsureGlow(grid.startCell, true);
        if (glow != null) glow.humEnabled = true;
    }

    void ResetAll()
    {
        foreach (GoalGlow glow in glows.Values)
            if (glow != null) Destroy(glow.gameObject);
        glows.Clear();
        bannerUntil = 0f;
    }

    GoalGlow EnsureGlow(Vector2Int cell, bool withHum)
    {
        if (glows.TryGetValue(cell, out GoalGlow existing) && existing != null) return existing;

        GoalGlow glow = GoalGlow.Create(grid.CellToWorld(cell) + Vector3.up * glowHeight, grid.cellSize * 4f, withHum);
        glows[cell] = glow;
        return glow;
    }

    void Banner(string title, string sub)
    {
        bannerTitle = title;
        bannerSub = sub;
        bannerUntil = Time.unscaledTime + 3.5f;
    }

    // ---------- Drawing (quick on-screen GUI, no Canvas needed) ----------

    void OnGUI()
    {
        if (player == null || grid == null) return;

        if (showWorldLabels) DrawWorldLabels();
        if (showHud) DrawHud();
        DrawBanner();
        GUI.color = Color.white;
    }

    // What the player should be doing right now.
    string Objective()
    {
        if (player.HasWon) return "Run complete!";
        if (player.CellarFound) return "Return to the ship";
        if (grid.HasCellar) return "Find the Cellar";
        return "Visit every dome";
    }

    void DrawHud()
    {
        int total = grid.Goals.Count;
        float width = 300f;
        float x = Screen.width - width - 10f;
        float y = 46f; // just under the Restart button

        var style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleRight, fontSize = 14, fontStyle = FontStyle.Bold };
        style.normal.textColor = Color.white;
        GUI.Label(new Rect(x, y, width, 22f), "Domes " + player.GoalsVisited + "/" + total, style);

        // One square per dome: filled once visited.
        float box = 16f;
        float gap = 6f;
        float rowWidth = total * box + (total - 1) * gap;
        float bx = Screen.width - 10f - rowWidth;
        for (int i = 0; i < total; i++)
        {
            bool visited = player.HasVisited(grid.Goals[i]);
            Rect r = new Rect(bx + i * (box + gap), y + 26f, box, box);
            Fill(new Rect(r.x - 2f, r.y - 2f, r.width + 4f, r.height + 4f), new Color(0f, 0f, 0f, 0.8f));
            Fill(r, visited ? Done : new Color(1f, 1f, 1f, 0.12f));
        }

        // The current objective under the squares.
        style.fontSize = 13;
        style.normal.textColor = player.HasWon ? Done : player.CellarFound ? Gold : grid.HasCellar ? Purple : new Color(0.85f, 0.92f, 1f);
        GUI.Label(new Rect(x, y + 48f, width, 22f), "Goal: " + Objective(), style);
    }

    void DrawWorldLabels()
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        var style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 14, fontStyle = FontStyle.Bold };

        // Every dome.
        for (int i = 0; i < grid.Goals.Count; i++)
        {
            Vector2Int cell = grid.Goals[i];
            bool visited = player.HasVisited(cell);
            DrawLabel(cam, style, cell, visited ? "Visited" : "Dome " + (i + 1), visited ? Done : Gold);
        }

        // The Cellar, once it exists.
        if (grid.HasCellar)
        {
            bool visited = player.CellarFound;
            DrawLabel(cam, style, grid.CellarCell, visited ? "Cellar (visited)" : "Cellar", visited ? Done : Purple);
        }

        // The ship, once it's time to go home.
        if (player.CellarFound && !player.HasWon)
        {
            float pulse = 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 4f);
            DrawLabel(cam, style, grid.startCell, "Ship: return here!", Color.Lerp(Gold, Color.white, 1f - pulse));
        }
    }

    void DrawLabel(Camera cam, GUIStyle style, Vector2Int cell, string text, Color color)
    {
        Vector3 screen = cam.WorldToScreenPoint(grid.CellToWorld(cell) + Vector3.up * (glowHeight + 1.2f));
        if (screen.z <= 0f) return; // behind the camera

        Rect rect = new Rect(screen.x - 80f, Screen.height - screen.y - 12f, 160f, 24f);

        // Shadow first so the text reads on any background.
        style.normal.textColor = new Color(0f, 0f, 0f, 0.7f);
        GUI.Label(new Rect(rect.x + 1.5f, rect.y + 1.5f, rect.width, rect.height), text, style);
        style.normal.textColor = color;
        GUI.Label(rect, text, style);
    }

    void DrawBanner()
    {
        if (Time.unscaledTime >= bannerUntil || string.IsNullOrEmpty(bannerTitle)) return;

        float left = bannerUntil - Time.unscaledTime;
        float alpha = Mathf.Clamp01(left / 0.8f) * Mathf.Clamp01((3.5f - left) / 0.3f);

        var title = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 44, fontStyle = FontStyle.Bold };
        var sub = new GUIStyle(title) { fontSize = 22 };

        float y = Screen.height * 0.38f;
        ShadowLabel(new Rect(0f, y, Screen.width, 56f), bannerTitle, title, new Color(Purple.r, Purple.g, Purple.b, alpha));
        ShadowLabel(new Rect(0f, y + 54f, Screen.width, 32f), bannerSub, sub, new Color(1f, 1f, 1f, alpha));
    }

    static void ShadowLabel(Rect rect, string text, GUIStyle style, Color color)
    {
        style.normal.textColor = new Color(0f, 0f, 0f, 0.7f * color.a);
        GUI.Label(new Rect(rect.x + 2.5f, rect.y + 2.5f, rect.width, rect.height), text, style);
        style.normal.textColor = color;
        GUI.Label(rect, text, style);
    }

    static void Fill(Rect rect, Color color)
    {
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = Color.white;
    }
}