using System;
using System.Collections.Generic;
using UnityEngine;

// Holds the pool of 12 tiles and the player's hand (the "river").
// Draws are random with replacement, but the hand is always checked so that
// at least one card can actually be placed somewhere (whenever any tile in the pool can).
public class TileDeck : MonoBehaviour
{
    [Tooltip("Needed for the 'always something playable' guarantee.")]
    public GridManager grid;

    [Tooltip("Your 12 TileDefinitions (3 shapes x [plain + 3 resources]). Leave empty to auto-generate from the generic prefab.")]
    public TileDefinition[] pool;

    [Tooltip("Only used when the pool above is empty, so you can test before real assets exist.")]
    public GameObject genericPrefab;

    public int handSize = 3;

    [Tooltip("On: taking a card slides the rest forward and a new card enters at the end (a river). Off: the taken slot is simply refilled.")]
    public bool slideRiver = true;

    [Tooltip("On: after every placement the whole hand is shuffled and a fresh hand of different cards is dealt. Off: only the used card is replaced (see Slide River).")]
    public bool redealAfterPlacement = true;

    public TileDefinition[] Hand { get; private set; }

    // True when NO tile in the pool can be placed anywhere (the board is boxed in).
    public bool NoMovesLeft { get; private set; }

    public event Action HandChanged;

    // Fired whenever a fresh hand is dealt, so the UI can play the shuffle animation.
    public event Action Shuffled;

    void Awake()
    {
        if ((pool == null || pool.Length == 0) && genericPrefab != null)
            pool = BuildPlaceholderPool();

        Hand = new TileDefinition[handSize];
        for (int i = 0; i < handSize; i++) Hand[i] = DrawRandom();
    }

    // Start runs after every Awake, so the grid is guaranteed to be ready here.
    void Start()
    {
        if (grid == null) Debug.LogWarning("TileDeck: assign the Grid object, otherwise the playable-card guarantee is off.");
        EnsurePlayable();
        HandChanged?.Invoke();
    }

    TileDefinition DrawRandom()
    {
        if (pool == null || pool.Length == 0) return null;
        return pool[UnityEngine.Random.Range(0, pool.Length)];
    }

    // Call after a tile from this slot has been placed.
    public void ReplaceSlot(int slot)
    {
        if (redealAfterPlacement)
        {
            DealNewHand();
            return;
        }

        if (slideRiver)
        {
            // River style: cards after the taken one slide forward, a new card enters at the end.
            for (int i = slot; i < Hand.Length - 1; i++) Hand[i] = Hand[i + 1];
            Hand[Hand.Length - 1] = DrawRandom();
        }
        else
        {
            Hand[slot] = DrawRandom();
        }

        EnsurePlayable();
        HandChanged?.Invoke();
    }

    // Redraws the whole hand (used by Restart).
    public void ResetDeck()
    {
        DealNewHand();
    }

    // Shuffles the whole pool and deals a fresh hand of different tiles.
    void DealNewHand()
    {
        if (pool == null || pool.Length == 0) return;

        var shuffled = new List<TileDefinition>(pool);
        for (int i = shuffled.Count - 1; i > 0; i--) // Fisher-Yates shuffle
        {
            int j = UnityEngine.Random.Range(0, i + 1);
            TileDefinition temp = shuffled[i];
            shuffled[i] = shuffled[j];
            shuffled[j] = temp;
        }

        for (int i = 0; i < Hand.Length; i++) Hand[i] = shuffled[i % shuffled.Count];

        EnsurePlayable();
        HandChanged?.Invoke();
        Shuffled?.Invoke();
    }

    // If none of the cards in hand can be placed anywhere, swap the newest card for one that can.
    // Call this after anything that changes the board (placing OR removing a tile).
    public void EnsurePlayable()
    {
        if (grid == null || pool == null || pool.Length == 0 || Hand == null) return;

        foreach (var card in Hand)
        {
            if (card != null && grid.HasAnyPlacement(card))
            {
                NoMovesLeft = false;
                return;
            }
        }

        var playable = new List<TileDefinition>();
        foreach (var def in pool)
            if (grid.HasAnyPlacement(def)) playable.Add(def);

        if (playable.Count == 0)
        {
            NoMovesLeft = true; // genuinely boxed in: no tile fits anywhere
            return;
        }

        NoMovesLeft = false;
        Hand[Hand.Length - 1] = playable[UnityEngine.Random.Range(0, playable.Count)];
        HandChanged?.Invoke();
    }

    // 3 shapes x 4 resource options = 12 placeholder definitions.
    TileDefinition[] BuildPlaceholderPool()
    {
        var list = new List<TileDefinition>();
        foreach (PathType shape in Enum.GetValues(typeof(PathType)))
        foreach (ResourceType res in Enum.GetValues(typeof(ResourceType)))
        {
            var def = ScriptableObject.CreateInstance<TileDefinition>();
            def.displayName = shape + (res == ResourceType.None ? " (plain)" : " - " + res);
            def.pathType = shape;
            def.resource = res;
            def.prefab = genericPrefab;
            list.Add(def);
        }
        return list.ToArray();
    }
}