using System;
using UnityEngine;

// What a disaster looks like on screen. Every disaster has one of these, so a brand new disaster
// gets a storm effect for free just by choosing colors and speeds in the Inspector.
[Serializable]
public class DisasterVisuals
{
    [Tooltip("Color laid over the whole screen. Its alpha is how strong it gets at its peak.")]
    public Color tint = new Color(0.75f, 0.88f, 1f, 0.35f);

    [Tooltip("Color of the falling particles (snowflakes, raindrops, embers...).")]
    public Color particleColor = Color.white;

    [Range(0, 600)] public int particleCount = 220;

    [Tooltip("How fast particles fall, in screen pixels per second.")]
    public float fallSpeed = 260f;

    [Tooltip("How fast the wind pushes particles sideways, in pixels per second. Negative = to the left.")]
    public float sideDrift = -160f;

    [Tooltip("Smallest and largest particle size, in pixels.")]
    public Vector2 sizeRange = new Vector2(3f, 9f);

    [Tooltip("On: long thin streaks (rain, meteors). Off: round dots (snow).")]
    public bool streaks = false;

    [Tooltip("How long the effect and the banner stay on screen, in seconds.")]
    public float duration = 3.5f;

    [Tooltip("How hard the camera shakes when it strikes. 0 = no shake.")]
    public float cameraShake = 0.15f;
}

// How a disaster decides when to strike.
public enum DisasterTiming
{
    // The game picks a disaster at random from the pool every few turns (see the Disaster Manager's Pool Gap).
    RandomPool,
    // It follows its own rhythm: First Turn, then every Repeat Every turns after that.
    OwnSchedule
}

// When in the run a disaster becomes possible.
public enum DisasterStage
{
    FromStart,
    AfterAllDomesVisited,
    AfterCellarVisited
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
// Then choose WHEN it can happen (Active From) and HOW it is picked (Timing, Weight).
//
// To invent a brand new KIND of disaster (one that does something other than take resources):
//   1. Copy ResourceLossDisaster.cs, rename the file and the class.
//   2. Change what Trigger() does. It receives the player and the grid, and returns a short sentence for the banner.
//   3. Make an asset of it (the CreateAssetMenu line at the top of the class adds it to the Create menu).
// Nothing else needs to change: the turn counter, the warnings, the banner, the storm effect and the random
// picking are shared.
public abstract class DisasterDefinition : ScriptableObject
{
    public string displayName = "Disaster";

    [Header("When it can happen")]
    [Tooltip("The point in the run where this disaster becomes possible. For example, After Cellar Visited means it never happens before you reach the Cellar.")]
    public DisasterStage activeFrom = DisasterStage.FromStart;

    [Tooltip("Random Pool: the game picks one disaster at random every few turns, and this one can be picked. Own Schedule: this disaster strikes on its own rhythm instead (First Turn, Repeat Every below).")]
    public DisasterTiming timing = DisasterTiming.RandomPool;

    [Header("Random Pool only")]
    [Tooltip("How likely it is to be picked compared to the others in the pool. 2 is twice as likely as 1. 0 = never.")]
    [Min(0f)] public float weight = 1f;

    [Header("Own Schedule only")]
    [Tooltip("The first turn it strikes on, counted from the moment it becomes possible (Active From).")]
    [Min(1)] public int firstTurn = 4;

    [Tooltip("After that, it strikes again every this many turns.")]
    [Min(1)] public int repeatEvery = 4;

    [Header("Look")]
    public DisasterVisuals visuals = new DisasterVisuals();

    // Do the damage. Return a short sentence for the banner, for example "-2 Scrap, -1 Bio".
    public abstract string Trigger(DisasterContext context);

    // Own Schedule: does it strike at the end of this turn? turnsActive = turns since it became possible.
    public bool StrikesOnTurn(int turnsActive)
    {
        int every = Mathf.Max(1, repeatEvery);
        return turnsActive >= firstTurn && (turnsActive - firstTurn) % every == 0;
    }

    // Own Schedule: how many turns until it next strikes (1 = on the very next turn).
    public int TurnsUntilNext(int turnsActive)
    {
        int every = Mathf.Max(1, repeatEvery);
        if (turnsActive < firstTurn) return firstTurn - turnsActive;
        return every - ((turnsActive - firstTurn) % every);
    }
}