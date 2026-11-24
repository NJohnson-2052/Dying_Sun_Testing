using System;
using UnityEngine;

// What a disaster looks like on screen. Every disaster has one of these, so a brand new disaster
// gets a storm effect for free just by choosing colors and speeds in the Inspector.
[Serializable]
public class DisasterVisuals
{
    [Tooltip("Color laid over the whole screen. Its alpha is how strong it gets at its peak.")]
    public Color tint = new Color(0.75f, 0.88f, 1f, 0.35f);

    [Tooltip("Color of the falling particles (snowflakes, raindrops, ash...).")]
    public Color particleColor = Color.white;

    [Range(0, 600)] public int particleCount = 220;

    [Tooltip("How fast particles fall, in screen pixels per second.")]
    public float fallSpeed = 260f;

    [Tooltip("How fast the wind pushes particles sideways, in pixels per second. Negative = to the left.")]
    public float sideDrift = -160f;

    [Tooltip("Smallest and largest particle size, in pixels.")]
    public Vector2 sizeRange = new Vector2(3f, 9f);

    [Tooltip("On: long thin streaks (rain). Off: round dots (snow).")]
    public bool streaks = false;

    [Tooltip("How long the effect and the banner stay on screen, in seconds.")]
    public float duration = 3.5f;

    [Tooltip("How hard the camera shakes when it strikes. 0 = no shake.")]
    public float cameraShake = 0.15f;
}

// Everything a disaster is allowed to touch when it strikes.
public class DisasterContext
{
    public PlayerController player;
    public GridManager grid;
    public int turn;
}

// The base for every disaster. Each disaster is an ASSET in your project, so making another one
// is just right-click > Create > Path Game > Disaster, then dragging it into the Disaster Manager's list.
//
// To invent a brand new KIND of disaster (one that does something other than take resources):
//   1. Copy ResourceLossDisaster.cs, rename the file and the class.
//   2. Change what Trigger() does. It receives the player and the grid, and returns a short sentence for the banner.
//   3. Make an asset of it (the CreateAssetMenu line at the top of the class adds it to the Create menu).
// Nothing else needs to change: the turn counter, the warnings, the banner and the storm effect are shared.
public abstract class DisasterDefinition : ScriptableObject
{
    public string displayName = "Disaster";

    [Tooltip("The first turn it strikes on.")]
    [Min(1)] public int firstTurn = 4;

    [Tooltip("After that, it strikes again every this many turns.")]
    [Min(1)] public int repeatEvery = 4;

    public DisasterVisuals visuals = new DisasterVisuals();

    // Do the damage. Return a short sentence for the banner, for example "-2 Scrap, -1 Bio".
    public abstract string Trigger(DisasterContext context);

    // Does it strike at the end of this turn?
    public bool StrikesOnTurn(int turn)
    {
        int every = Mathf.Max(1, repeatEvery);
        return turn >= firstTurn && (turn - firstTurn) % every == 0;
    }

    // How many turns until it next strikes (1 = on the very next turn).
    public int TurnsUntilNext(int turn)
    {
        int every = Mathf.Max(1, repeatEvery);
        if (turn < firstTurn) return firstTurn - turn;
        return every - ((turn - firstTurn) % every);
    }
}