using System.Collections.Generic;
using UnityEngine;

// Keeps track of the domes: shows "Domes 1/3" on screen, floats a label over every dome
// ("Dome 2" until you visit it, "Visited" after), and gives each visited dome a soft red glow.
// The last dome you reach also hums.
//
// You don't need to add this to the scene: it creates itself when the game starts.
public class GoalTracker : MonoBehaviour
{
    [SerializeField] private PlayerController player;
    [SerializeField] private GridManager grid;

    [Tooltip("Shows the Domes counter in the top-right corner.")]
    [SerializeField] private bool showHud = true;

    [Tooltip("Shows a floating label over every dome.")]
    [SerializeField] private bool showWorldLabels = true;

    [Tooltip("How high above the ground the glow and the labels float.")]
    [SerializeField] private float glowHeight = 1.5f;

    private readonly Dictionary<Vector2Int, GoalGlow> glows = new Dictionary<Vector2Int, GoalGlow>();
    private Vector2Int lastVisited;

    private static readonly Color Gold = new Color(1f, 0.85f, 0.25f);
    private static readonly Color Done = new Color(0.45f, 1f, 0.55f);

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
        player.GoalVisited += OnGoalVisited;
        player.ReachedGoal += OnAllReached;
        player.WasReset += ClearGlows;
    }

    void OnDisable()
    {
        if (player == null) return;
        player.GoalVisited -= OnGoalVisited;
        player.ReachedGoal -= OnAllReached;
        player.WasReset -= ClearGlows;
    }

    void OnGoalVisited(Vector2Int cell)
    {
        lastVisited = cell;
        if (glows.ContainsKey(cell)) return;
        glows[cell] = GoalGlow.Create(grid.CellToWorld(cell) + Vector3.up * glowHeight, grid.cellSize * 4f, false);
    }

    // The last dome is the one that hums.
    void OnAllReached()
    {
        if (glows.TryGetValue(lastVisited, out GoalGlow glow) && glow != null) glow.humEnabled = true;
    }

    void ClearGlows()
    {
        foreach (GoalGlow glow in glows.Values)
            if (glow != null) Destroy(glow.gameObject);
        glows.Clear();
    }

    // ---------- Drawing (quick on-screen GUI, no Canvas needed) ----------

    void OnGUI()
    {
        if (player == null || grid == null) return;

        if (showWorldLabels) DrawWorldLabels();
        if (showHud) DrawHud();
        GUI.color = Color.white;
    }

    void DrawHud()
    {
        int total = grid.Goals.Count;
        float width = 150f;
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
    }

    void DrawWorldLabels()
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        var style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 14, fontStyle = FontStyle.Bold };

        for (int i = 0; i < grid.Goals.Count; i++)
        {
            Vector2Int cell = grid.Goals[i];
            Vector3 screen = cam.WorldToScreenPoint(grid.CellToWorld(cell) + Vector3.up * (glowHeight + 1.2f));
            if (screen.z <= 0f) continue; // behind the camera

            bool visited = player.HasVisited(cell);
            string text = visited ? "Visited" : "Dome " + (i + 1);
            Rect rect = new Rect(screen.x - 60f, Screen.height - screen.y - 12f, 120f, 24f);

            // Shadow first so the text reads on any background.
            style.normal.textColor = new Color(0f, 0f, 0f, 0.7f);
            GUI.Label(new Rect(rect.x + 1.5f, rect.y + 1.5f, rect.width, rect.height), text, style);
            style.normal.textColor = visited ? Done : Gold;
            GUI.Label(rect, text, style);
        }
    }

    static void Fill(Rect rect, Color color)
    {
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = Color.white;
    }
}