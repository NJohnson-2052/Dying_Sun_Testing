using System;
using System.Collections.Generic;
using UnityEngine;

// Counts the player's turns and sets off disasters.
// A "turn" is one completed move by the player (one click-to-walk), or every single tile stepped
// if you turn on Count Every Tile Step. Placing tiles does not count as a turn.
//
// Two ways a disaster can be triggered (set per disaster, on its asset):
//   Random Pool  - every few turns the game picks one disaster from the pool at random.
//   Own Schedule - the disaster strikes on its own fixed rhythm.
// Each disaster also has an "Active From" stage, so something like the Meteor can wait until the Cellar.
//
// This also ends the run: if a disaster leaves the player with no resources at all, it is Game Over.
//
// Setup: click Path Game > Add Disaster System To Scene. It makes this object, wires it up,
// and creates the Blizzard and Meteor disasters for you.
public class DisasterManager : MonoBehaviour
{
    [SerializeField] private PlayerController player;
    [SerializeField] private GridManager grid;

    [Tooltip("Every disaster in this list is considered. Drag more disaster assets in here.")]
    [SerializeField] private List<DisasterDefinition> disasters = new List<DisasterDefinition>();

    [Tooltip("Off: a turn is one completed walk (one click). On: every tile the player steps onto counts as a turn.")]
    [SerializeField] private bool countEveryTileStep = false;

    [Header("Random Pool")]
    [Tooltip("The shortest wait, in turns, before the next random disaster.")]
    [Min(1)] [SerializeField] private int poolGapMin = 3;

    [Tooltip("The longest wait, in turns, before the next random disaster. Make it the same as the shortest for a fixed rhythm.")]
    [Min(1)] [SerializeField] private int poolGapMax = 5;

    [Tooltip("On: the warning names the disaster that is coming next. Off: it just says 'A disaster'.")]
    [SerializeField] private bool showNextDisaster = true;

    [Header("Game Over")]
    [Tooltip("On: running out of resources ends the run and you have to Restart.")]
    [SerializeField] private bool gameOverWhenNoResources = true;

    [Tooltip("On: only a disaster that leaves you with nothing ends the run. Off: ANY way of reaching zero resources does (for example spending them all on crafting).")]
    [SerializeField] private bool onlyFromDisasters = true;

    [Header("Display")]
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

    // The turn each late-starting disaster first became possible (used to count its schedule from then).
    private readonly Dictionary<DisasterDefinition, int> activeSince = new Dictionary<DisasterDefinition, int>();

    // The random pool: when the next pool disaster strikes, and which one it will be.
    private int nextPoolTurn;
    private DisasterDefinition nextPool;

    private bool hadResources; // you can only "run out" after having had some

    void Awake()
    {
        if (player == null) player = FindObjectOfType<PlayerController>();
        if (grid == null) grid = FindObjectOfType<GridManager>();
        ScheduleNextPool();
    }

    void OnEnable()
    {
        if (player == null) return;
        player.StepTaken += OnStepTaken;
        player.MoveFinished += OnMoveFinished;
        player.WasReset += ResetDisasters;
        player.ResourcesChanged += OnResourcesChanged;
    }

    void OnDisable()
    {
        if (player == null) return;
        player.StepTaken -= OnStepTaken;
        player.MoveFinished -= OnMoveFinished;
        player.WasReset -= ResetDisasters;
        player.ResourcesChanged -= OnResourcesChanged;
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
        if (player == null || player.HasWon || player.GameOver) return; // the run is over

        Turn++;
        bool unlocked = UpdateActivation();

        // 1. Disasters on their own fixed schedule.
        foreach (DisasterDefinition disaster in disasters)
        {
            if (disaster == null || disaster.timing != DisasterTiming.OwnSchedule || !IsActive(disaster)) continue;

            if (disaster.StrikesOnTurn(TurnsActive(disaster)))
            {
                Strike(disaster);
                if (player.GameOver) return;
            }
        }

        // 2. The random pool.
        if (Turn >= nextPoolTurn)
        {
            DisasterDefinition pick = IsPoolEligible(nextPool) ? nextPool : PickFromPool();
            if (pick != null) Strike(pick);
            ScheduleNextPool();
        }
        else if (unlocked || !IsPoolEligible(nextPool))
        {
            // Something new became possible (the Meteor after the Cellar, say), so it can be the next one.
            nextPool = PickFromPool();
        }
    }

    // ---------- Which disasters are possible right now ----------

    bool IsActive(DisasterDefinition disaster)
    {
        switch (disaster.activeFrom)
        {
            case DisasterStage.AfterAllDomesVisited:
                return player != null && grid != null && player.GoalsVisited >= grid.Goals.Count;
            case DisasterStage.AfterCellarVisited:
                return player != null && player.CellarFound;
            default:
                return true;
        }
    }

    // Notes the turn each late-starting disaster first became possible. Returns true if one just did.
    bool UpdateActivation()
    {
        bool newlyActive = false;
        foreach (DisasterDefinition disaster in disasters)
        {
            if (disaster == null || disaster.activeFrom == DisasterStage.FromStart) continue;

            if (IsActive(disaster) && !activeSince.ContainsKey(disaster))
            {
                activeSince[disaster] = Turn;
                newlyActive = true;
            }
        }
        return newlyActive;
    }

    // How many turns it has been possible for (0 on the turn it unlocks).
    int TurnsActive(DisasterDefinition disaster)
    {
        if (disaster.activeFrom == DisasterStage.FromStart) return Turn;
        return activeSince.TryGetValue(disaster, out int since) ? Turn - since : 0;
    }

    // ---------- The random pool ----------

    bool IsPoolEligible(DisasterDefinition disaster)
    {
        return disaster != null && disaster.timing == DisasterTiming.RandomPool && disaster.weight > 0f && IsActive(disaster);
    }

    // A weighted random pick among the disasters that can happen right now. Null if there are none.
    DisasterDefinition PickFromPool()
    {
        float total = 0f;
        foreach (DisasterDefinition disaster in disasters)
            if (IsPoolEligible(disaster)) total += disaster.weight;

        if (total <= 0f) return null;

        float roll = UnityEngine.Random.value * total;
        DisasterDefinition last = null;
        foreach (DisasterDefinition disaster in disasters)
        {
            if (!IsPoolEligible(disaster)) continue;
            last = disaster;
            roll -= disaster.weight;
            if (roll <= 0f) return disaster;
        }
        return last;
    }

    void ScheduleNextPool()
    {
        int low = Mathf.Max(1, Mathf.Min(poolGapMin, poolGapMax));
        int high = Mathf.Max(low, Mathf.Max(poolGapMin, poolGapMax));

        nextPoolTurn = Turn + UnityEngine.Random.Range(low, high + 1);
        nextPool = PickFromPool();
    }

    // ---------- Striking ----------

    void Strike(DisasterDefinition disaster)
    {
        int before = player.TotalResources;
        string summary = disaster.Trigger(new DisasterContext { player = player, grid = grid, turn = Turn });
        int after = player.TotalResources;

        active.Add(new ActiveEffect { disaster = disaster, summary = summary, startTime = Time.unscaledTime });
        if (disaster.visuals.cameraShake > 0f) CameraThump.Thump(disaster.visuals.cameraShake, 0.8f);

        Debug.Log("Disaster: " + disaster.displayName + " struck on turn " + Turn + ". " + summary);
        DisasterStruck?.Invoke(disaster, summary);

        // Wiped out: you had resources and now you have none.
        if (gameOverWhenNoResources && before > 0 && after == 0)
            player.FailRun("The " + disaster.displayName + " left you with nothing.");
    }

    // Only used when "Only From Disasters" is off: any way of hitting zero ends the run.
    void OnResourcesChanged()
    {
        if (player == null) return;

        int total = player.TotalResources;
        if (total > 0)
        {
            hadResources = true;
        }
        else if (hadResources && gameOverWhenNoResources && !onlyFromDisasters && !player.GameOver && !player.HasWon)
        {
            player.FailRun("You ran out of resources.");
        }
    }

    // Back to turn 0 with nothing on screen (used by Restart).
    public void ResetDisasters()
    {
        Turn = 0;
        active.Clear();
        activeSince.Clear();
        hadResources = false;
        ScheduleNextPool();
    }

    // ---------- Drawing (quick on-screen GUI, no Canvas needed) ----------

    void OnGUI()
    {
        GUI.depth = -100; // draw on top of the cards and everything else

        DrawEffects();
        if (showHud && player != null && !player.HasWon && !player.GameOver) DrawHud();

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

        // The next random disaster.
        if (IsPoolEligible(nextPool))
        {
            string name = showNextDisaster ? nextPool.displayName : "A disaster";
            DrawCountdown(style, ref y, name, Mathf.Max(1, nextPoolTurn - Turn));
            shown++;
        }

        // Disasters on their own schedule.
        foreach (DisasterDefinition disaster in disasters)
        {
            if (shown >= 3) break;
            if (disaster == null || disaster.timing != DisasterTiming.OwnSchedule || !IsActive(disaster)) continue;

            DrawCountdown(style, ref y, disaster.displayName, disaster.TurnsUntilNext(TurnsActive(disaster)));
            shown++;
        }
    }

    void DrawCountdown(GUIStyle style, ref float y, string name, int turnsLeft)
    {
        bool imminent = turnsLeft <= 1;
        style.normal.textColor = imminent ? new Color(1f, 0.6f, 0.3f) : new Color(0.85f, 0.92f, 1f);

        string text = imminent
            ? name + " strikes after your next move!"
            : name + " in " + turnsLeft + " turns";
        GUI.Label(new Rect(0f, y, Screen.width, 22f), text, style);
        y += 22f;
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

    // Falling snow, rain, embers... Each particle's position is worked out from the time, so nothing needs to be stored.
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