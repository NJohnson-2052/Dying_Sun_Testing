using System;
using UnityEngine;

// The flashlight. Placing a tile lights up new ground, so every tile costs energy.
// Walking the player back onto the start tile recharges it to full (later this will be the ship).
//
// Setup: click Path Game > Add Energy And Chase Camera To Scene. It makes this object and wires it up.
public class FlashlightEnergy : MonoBehaviour
{
    [SerializeField] private PlayerController player;
    [SerializeField] private GridManager grid;

    [Header("Energy")]
    [Tooltip("How much energy a full flashlight holds.")]
    [Min(1)] [SerializeField] private int maxEnergy = 10;

    [Tooltip("Energy spent each time a tile is placed.")]
    [Min(0)] [SerializeField] private int costPerTile = 1;

    [Tooltip("Shows the energy bar on screen.")]
    [SerializeField] private bool showHud = true;

    public int Current { get; private set; }
    public int Max => maxEnergy;

    // True while there is enough energy to place at least one tile.
    public bool CanPlaceAnything => Current >= costPerTile;

    public event Action EnergyChanged;
    public event Action Recharged;

    private float lowFlashUntil = -1f;   // red shake of the bar when you try to place with too little energy
    private float rechargeFlashUntil = -1f;

    void Awake()
    {
        if (player == null) player = FindObjectOfType<PlayerController>();
        if (grid == null) grid = FindObjectOfType<GridManager>();
        Current = maxEnergy;
    }

    void OnEnable()
    {
        if (player == null) return;
        player.StepTaken += OnPlayerStep;
        player.WasReset += OnPlayerReset;
    }

    void OnDisable()
    {
        if (player == null) return;
        player.StepTaken -= OnPlayerStep;
        player.WasReset -= OnPlayerReset;
    }

    // ---------- Using energy ----------

    // How much a tile costs. Change this if different tiles should cost different amounts,
    // for example: return tile.pathType == PathType.Cross ? 2 : 1;
    public int CostFor(TileDefinition tile) => costPerTile;

    public bool CanAfford(TileDefinition tile) => Current >= CostFor(tile);

    public void Spend(TileDefinition tile)
    {
        Current = Mathf.Max(0, Current - CostFor(tile));
        EnergyChanged?.Invoke();
    }

    // Called when the player tried to place a tile without enough energy.
    public void NotifyNotEnough()
    {
        lowFlashUntil = Time.unscaledTime + 0.45f;
    }

    // ---------- Recharging ----------

    void OnPlayerStep()
    {
        // Stepping onto the start tile recharges the flashlight.
        if (grid != null && player.CurrentCell == grid.startCell && Current < maxEnergy) Recharge();
    }

    void OnPlayerReset()
    {
        Current = maxEnergy;
        EnergyChanged?.Invoke();
    }

    public void Recharge()
    {
        Current = maxEnergy;
        rechargeFlashUntil = Time.unscaledTime + 1.2f;
        EnergyChanged?.Invoke();
        Recharged?.Invoke();
    }

    // ---------- Drawing (quick on-screen GUI, no Canvas needed) ----------

    void OnGUI()
    {
        if (!showHud) return;

        // Sits under the Bio / Scrap / Rations readout in the top-left corner.
        float x = 10f;
        float y = 84f;
        float width = 220f;
        float height = 18f;

        // Shake sideways when you try to place with too little energy.
        if (Time.unscaledTime < lowFlashUntil)
            x += Mathf.Sin(Time.unscaledTime * 70f) * 4f;

        bool lowFlash = Time.unscaledTime < lowFlashUntil;
        bool rechargeFlash = Time.unscaledTime < rechargeFlashUntil;
        bool low = Current <= Mathf.Max(1, maxEnergy / 4);

        // Border: red when you couldn't afford a tile, bright when just recharged.
        Color border = lowFlash ? new Color(1f, 0.25f, 0.2f) : rechargeFlash ? new Color(1f, 1f, 0.7f) : new Color(0f, 0f, 0f, 0.8f);
        Fill(new Rect(x - 3f, y - 3f, width + 6f, height + 6f), border);
        Fill(new Rect(x, y, width, height), new Color(0.08f, 0.08f, 0.1f, 0.95f));

        // One block per point of energy (or one smooth bar if the flashlight holds a lot).
        Color on = low ? new Color(1f, 0.45f, 0.2f) : new Color(1f, 0.88f, 0.35f);
        if (rechargeFlash) on = Color.Lerp(on, Color.white, 0.5f);

        if (maxEnergy <= 24)
        {
            float gap = 2f;
            float block = (width - gap * (maxEnergy + 1)) / maxEnergy;
            for (int i = 0; i < maxEnergy; i++)
            {
                Rect r = new Rect(x + gap + i * (block + gap), y + gap, block, height - gap * 2f);
                Fill(r, i < Current ? on : new Color(1f, 1f, 1f, 0.07f));
            }
        }
        else
        {
            Fill(new Rect(x + 2f, y + 2f, (width - 4f) * Current / maxEnergy, height - 4f), on);
        }

        var label = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold };
        label.normal.textColor = Color.white;
        GUI.Label(new Rect(x + width + 8f, y - 2f, 160f, 22f), rechargeFlash ? "Recharged!" : "Flashlight " + Current + "/" + maxEnergy, label);

        // A hint when the flashlight can't light another tile.
        if (!CanPlaceAnything && player != null && !player.HasWon)
        {
            label.normal.textColor = new Color(1f, 0.6f, 0.3f);
            GUI.Label(new Rect(x, y + height + 6f, 420f, 22f), "Out of energy! Walk back to the start to recharge.", label);
        }

        GUI.color = Color.white;
    }

    static void Fill(Rect rect, Color color)
    {
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = Color.white;
    }
}