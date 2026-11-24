using System;
using System.Collections.Generic;
using UnityEngine;

// Counts the player's turns and sets off disasters when their turn comes.
// A "turn" is one completed move by the player (one click-to-walk), or every single tile stepped
// if you turn on Count Every Tile Step. Placing tiles does not count as a turn.
//
// Setup: click Path Game > Add Disaster System To Scene. It makes this object, wires it up,
// and creates a Blizzard disaster for you.
public class DisasterManager : MonoBehaviour
{
    [SerializeField] private PlayerController player;
    [SerializeField] private GridManager grid;

    [Tooltip("Every disaster in this list is checked after each turn. Drag more disaster assets in here.")]
    [SerializeField] private List<DisasterDefinition> disasters = new List<DisasterDefinition>();

    [Tooltip("Off: a turn is one completed walk (one click). On: every tile the player steps onto counts as a turn.")]
    [SerializeField] private bool countEveryTileStep = false;

    [Tooltip("Shows the turn number and a countdown to the next disaster at the top of the screen.")]
    [SerializeField] private bool showHud = true;

    public int Turn { get; private set; }

    // Other scripts can listen to this (for example to play a sound). Passes the disaster and its banner sentence.
    public event Action<DisasterDefinition, string> DisasterStruck;

    private class ActiveEffect
    {
        public DisasterDefinition disaster;
        public string summary;
        public float startTime;
    }

    private readonly List<ActiveEffect> active = new List<ActiveEffect>();

    void Awake()
    {
        if (player == null) player = FindObjectOfType<PlayerController>();
        if (grid == null) grid = FindObjectOfType<GridManager>();
    }

    void OnEnable()
    {
        if (player == null) return;
        player.StepTaken += OnStepTaken;
        player.MoveFinished += OnMoveFinished;
        player.WasReset += ResetDisasters;
    }

    void OnDisable()
    {
        if (player == null) return;
        player.StepTaken -= OnStepTaken;
        player.MoveFinished -= OnMoveFinished;
        player.WasReset -= ResetDisasters;
    }

    // ---------- Turns ----------

    void OnStepTaken()
    {
        if (countEveryTileStep) EndTurn();
    }

    void OnMoveFinished()
    {
        if (!countEveryTileStep) EndTurn();
    }

    void EndTurn()
    {
        if (player == null || player.HasWon) return; // reaching the goal beats any disaster

        Turn++;
        foreach (DisasterDefinition disaster in disasters)
            if (disaster != null && disaster.StrikesOnTurn(Turn)) Strike(disaster);
    }

    void Strike(DisasterDefinition disaster)
    {
        string summary = disaster.Trigger(new DisasterContext { player = player, grid = grid, turn = Turn });

        active.Add(new ActiveEffect { disaster = disaster, summary = summary, startTime = Time.unscaledTime });
        if (disaster.visuals.cameraShake > 0f) CameraThump.Thump(disaster.visuals.cameraShake, 0.8f);

        Debug.Log("Disaster: " + disaster.displayName + " struck on turn " + Turn + ". " + summary);
        DisasterStruck?.Invoke(disaster, summary);
    }

    // Back to turn 0 with nothing on screen (used by Restart).
    public void ResetDisasters()
    {
        Turn = 0;
        active.Clear();
    }

    // ---------- Drawing (quick on-screen GUI, no Canvas needed) ----------

    void OnGUI()
    {
        GUI.depth = -100; // draw on top of the cards and everything else

        DrawEffects();
        if (showHud && player != null && !player.HasWon) DrawHud();

        GUI.color = Color.white;
    }

    void DrawHud()
    {
        var style = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 16,
            fontStyle = FontStyle.Bold
        };

        float y = 8f;
        style.normal.textColor = Color.white;
        GUI.Label(new Rect(0f, y, Screen.width, 24f), "Turn " + Turn, style);
        y += 24f;

        int shown = 0;
        foreach (DisasterDefinition disaster in disasters)
        {
            if (disaster == null || shown >= 3) continue;

            int turnsLeft = disaster.TurnsUntilNext(Turn);
            bool imminent = turnsLeft <= 1;

            style.normal.textColor = imminent ? new Color(1f, 0.6f, 0.3f) : new Color(0.85f, 0.92f, 1f);
            string text = imminent
                ? disaster.displayName + " strikes after your next move!"
                : disaster.displayName + " in " + turnsLeft + " turns";
            GUI.Label(new Rect(0f, y, Screen.width, 22f), text, style);

            y += 22f;
            shown++;
        }
    }

    void DrawEffects()
    {
        for (int i = active.Count - 1; i >= 0; i--)
        {
            ActiveEffect effect = active[i];
            DisasterVisuals visuals = effect.disaster.visuals;

            float t = Time.unscaledTime - effect.startTime;
            float duration = Mathf.Max(0.5f, visuals.duration);
            if (t >= duration)
            {
                active.RemoveAt(i);
                continue;
            }

            // Fade in quickly, fade out slowly.
            float envelope = Mathf.Clamp01(t / 0.4f) * Mathf.Clamp01((duration - t) / 1f);

            // Color wash over the whole screen.
            Color tint = visuals.tint;
            GUI.color = new Color(tint.r, tint.g, tint.b, tint.a * envelope);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);

            DrawParticles(visuals, t, envelope);
            DrawBanner(effect, i, envelope);
        }
    }

    // Falling snow, rain, ash... Each particle's position is worked out from the time, so nothing needs to be stored.
    void DrawParticles(DisasterVisuals visuals, float t, float envelope)
    {
        float w = Screen.width;
        float h = Screen.height;
        Color baseColor = visuals.particleColor;

        for (int i = 0; i < visuals.particleCount; i++)
        {
            // Three fixed "random" numbers per particle.
            float r1 = Hash(i * 12.9898f);
            float r2 = Hash(i * 78.233f + 1.7f);
            float r3 = Hash(i * 39.346f + 5.3f);

            float size = Mathf.Lerp(visuals.sizeRange.x, visuals.sizeRange.y, r3);
            float speed = visuals.fallSpeed * Mathf.Lerp(0.6f, 1.2f, r3);          // bigger flakes fall faster
            float sway = Mathf.Sin(t * 1.5f + r2 * 6.283f) * 18f;

            // Wrap around the screen edges so the storm never runs out.
            float x = Mathf.Repeat(r1 * w + visuals.sideDrift * t + sway, w + 40f) - 20f;
            float y = Mathf.Repeat(r2 * h + speed * t, h + 40f) - 20f;

            Rect rect = visuals.streaks
                ? new Rect(x, y, Mathf.Max(1.5f, size * 0.25f), size * 4f)
                : new Rect(x, y, size, size);

            GUI.color = new Color(baseColor.r, baseColor.g, baseColor.b,
                                  baseColor.a * envelope * Mathf.Lerp(0.5f, 1f, r1));
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
        }
    }

    static float Hash(float seed)
    {
        float s = Mathf.Sin(seed) * 43758.5453f;
        return s - Mathf.Floor(s);
    }

    void DrawBanner(ActiveEffect effect, int index, float envelope)
    {
        float y = Screen.height * 0.22f + index * 130f;

        var title = new GUIStyle(GUI.skin.label)
        {
            fontSize = 56,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };
        var subtitle = new GUIStyle(title) { fontSize = 26 };

        DrawShadowedLabel(new Rect(0f, y, Screen.width, 70f), effect.disaster.displayName.ToUpper() + "!", title, envelope);
        DrawShadowedLabel(new Rect(0f, y + 66f, Screen.width, 40f), effect.summary, subtitle, envelope);
    }

    static void DrawShadowedLabel(Rect rect, string text, GUIStyle style, float alpha)
    {
        GUI.color = Color.white;

        style.normal.textColor = new Color(0f, 0f, 0f, 0.6f * alpha);
        GUI.Label(new Rect(rect.x + 3f, rect.y + 3f, rect.width, rect.height), text, style);

        style.normal.textColor = new Color(1f, 1f, 1f, alpha);
        GUI.Label(rect, text, style);
    }
}