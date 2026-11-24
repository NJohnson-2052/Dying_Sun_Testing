using System;
using System.Collections.Generic;
using UnityEngine;

// One line of the deck recipe: "put this many copies of this tile into the deck".
[Serializable]
public class DeckEntry
{
    public TileDefinition tile;
    [Min(0)] public int copies = 1;
}

// A real deck of cards.
//   * The deck is built from the recipe (80 cards by default) and shuffled.
//   * You hold a hand of cards drawn from the top of the draw pile.
//   * Played and discarded cards go to the discard pile.
//   * When the draw pile runs out, the discard pile is shuffled back in.
//   * The hand always contains at least one card that can be placed somewhere (if any card in the deck can).
public class TileDeck : MonoBehaviour
{
    [Tooltip("Needed for the 'always something playable' guarantee.")]
    public GridManager grid;

    [Tooltip("Your 12 TileDefinitions (3 shapes x [plain + 3 resources]). Used to build the default deck when the recipe below is empty.")]
    public TileDefinition[] pool;

    [Tooltip("Only used when the pool above is empty, so you can test before real assets exist.")]
    public GameObject genericPrefab;

    [Header("Hand")]
    public int handSize = 3;

    [Tooltip("On: after every placement the whole hand goes to the discard pile and a fresh hand is drawn (the freeze-away and shuffle animations play). Off: only the card you played is replaced, and unused cards stay in your hand.")]
    public bool discardUnusedCards = true;

    [Header("Deck recipe")]
    [Tooltip("How many copies of each tile are in the deck. Leave empty to use the default 80-card deck. Run the menu Path Game > Reset Deck Recipe To Default to fill this list in so you can edit the numbers.")]
    public List<DeckEntry> recipe = new List<DeckEntry>();

    public TileDefinition[] Hand { get; private set; }

    // How many cards are left to draw, and how many are in the discard pile.
    public int DrawCount => drawPile.Count;
    public int DiscardCount => discardPile.Count;

    // True when NO card in the deck can be placed anywhere (the board is boxed in).
    public bool NoMovesLeft { get; private set; }

    public event Action HandChanged;

    // A fresh hand was dealt, so the UI can play the freeze and shuffle animations.
    public event Action Shuffled;

    // The discard pile was shuffled back into the draw pile.
    public event Action Reshuffled;

    // The top of the draw pile is the END of the list.
    private readonly List<TileDefinition> drawPile = new List<TileDefinition>();
    private readonly List<TileDefinition> discardPile = new List<TileDefinition>();

    void Awake()
    {
        if ((pool == null || pool.Length == 0) && genericPrefab != null)
            pool = BuildPlaceholderPool();

        Hand = new TileDefinition[handSize];
        BuildDeck();
        for (int i = 0; i < Hand.Length; i++) Hand[i] = Draw();
    }

    // Start runs after every Awake, so the grid is guaranteed to be ready here.
    void Start()
    {
        if (grid == null) Debug.LogWarning("TileDeck: assign the Grid object, otherwise the playable-card guarantee is off.");
        EnsurePlayable();
        HandChanged?.Invoke();
    }

    // ---------- The default recipe ----------

    // 80 cards in total:
    //   crosses are rare: 2 of each type = 8 cards = 1 in 10
    //   plain paths are the most common: 15 straights + 15 curves + 2 crosses = 32 (40%)
    //   each resource (Bio, Scrap, Rations): 7 straights + 7 curves + 2 crosses = 16 (20%)
    public static int DefaultCopies(TileDefinition def)
    {
        if (def.pathType == PathType.Cross) return 2;
        return def.resource == ResourceType.None ? 15 : 7;
    }

    public static List<DeckEntry> BuildDefaultRecipe(IEnumerable<TileDefinition> tiles)
    {
        var entries = new List<DeckEntry>();
        if (tiles == null) return entries;

        foreach (TileDefinition def in tiles)
        {
            if (def == null) continue;
            entries.Add(new DeckEntry { tile = def, copies = DefaultCopies(def) });
        }
        return entries;
    }

    // ---------- Building and shuffling ----------

    void BuildDeck()
    {
        drawPile.Clear();
        discardPile.Clear();

        List<DeckEntry> entries = (recipe != null && recipe.Count > 0) ? recipe : BuildDefaultRecipe(pool);

        foreach (DeckEntry entry in entries)
        {
            if (entry == null || entry.tile == null) continue;
            for (int i = 0; i < entry.copies; i++) drawPile.Add(entry.tile);
        }

        // Safety net: if the recipe was all zeros, still make a deck out of the pool.
        if (drawPile.Count == 0 && pool != null)
            foreach (TileDefinition def in pool)
                if (def != null) drawPile.Add(def);

        Shuffle(drawPile);
        LogDeckSummary();
    }

    static void Shuffle<T>(List<T> cards)
    {
        for (int i = cards.Count - 1; i > 0; i--) // Fisher-Yates shuffle
        {
            int j = UnityEngine.Random.Range(0, i + 1);
            T temp = cards[i];
            cards[i] = cards[j];
            cards[j] = temp;
        }
    }

    void LogDeckSummary()
    {
        int crosses = 0, plain = 0, bio = 0, scrap = 0, rations = 0;
        foreach (TileDefinition c in drawPile)
        {
            if (c.pathType == PathType.Cross) crosses++;
            switch (c.resource)
            {
                case ResourceType.None:    plain++;   break;
                case ResourceType.Bio:     bio++;     break;
                case ResourceType.Scrap:   scrap++;   break;
                case ResourceType.Rations: rations++; break;
            }
        }

        Debug.Log("Deck built: " + drawPile.Count + " cards (" + crosses + " crosses; " +
                  plain + " plain, " + bio + " Bio, " + scrap + " Scrap, " + rations + " Rations).");
    }

    // ---------- Drawing and discarding ----------

    // Takes the top card of the draw pile. If the pile is empty, the discard pile is shuffled back in first.
    TileDefinition Draw()
    {
        if (drawPile.Count == 0) ReshuffleDiscardIntoDraw();
        if (drawPile.Count == 0) return null;

        int last = drawPile.Count - 1;
        TileDefinition card = drawPile[last];
        drawPile.RemoveAt(last);
        return card;
    }

    void Discard(TileDefinition card)
    {
        if (card != null) discardPile.Add(card);
    }

    void ReshuffleDiscardIntoDraw()
    {
        if (discardPile.Count == 0) return;

        drawPile.AddRange(discardPile);
        discardPile.Clear();
        Shuffle(drawPile);
        Reshuffled?.Invoke();
    }

    // Call after the card in this slot has been placed on the board.
    public void ReplaceSlot(int playedSlot)
    {
        if (Hand == null) return;

        // The card you played goes to the discard pile.
        if (playedSlot >= 0 && playedSlot < Hand.Length)
        {
            Discard(Hand[playedSlot]);
            Hand[playedSlot] = null;
        }

        // Optionally the rest of your hand goes too.
        if (discardUnusedCards)
        {
            for (int i = 0; i < Hand.Length; i++)
            {
                Discard(Hand[i]);
                Hand[i] = null;
            }
        }

        for (int i = 0; i < Hand.Length; i++)
            if (Hand[i] == null) Hand[i] = Draw();

        EnsurePlayable();
        HandChanged?.Invoke();
        if (discardUnusedCards) Shuffled?.Invoke();
    }

    // Starts over with a full, freshly shuffled deck and a new hand (used by Restart).
    public void ResetDeck()
    {
        BuildDeck();
        for (int i = 0; i < Hand.Length; i++) Hand[i] = Draw();

        EnsurePlayable();
        HandChanged?.Invoke();
        Shuffled?.Invoke();
    }

    // ---------- The "always something playable" guarantee ----------

    // If none of the cards in hand can be placed anywhere, swap the newest card for one from the deck that can.
    // Call this after anything that changes the board (placing OR removing a tile).
    public void EnsurePlayable()
    {
        if (grid == null || Hand == null) return;

        // Ask the grid once per tile type, not once per card.
        var cache = new Dictionary<TileDefinition, bool>();
        bool IsPlayable(TileDefinition def)
        {
            if (def == null) return false;
            if (!cache.TryGetValue(def, out bool ok))
            {
                ok = grid.HasAnyPlacement(def);
                cache[def] = ok;
            }
            return ok;
        }

        foreach (TileDefinition card in Hand)
        {
            if (IsPlayable(card))
            {
                NoMovesLeft = false;
                return;
            }
        }

        // Look for a playable card in the draw pile (nearest the top first), shuffling the discard pile in if needed.
        int index = FindPlayableIndex(drawPile, IsPlayable);
        if (index < 0 && discardPile.Count > 0)
        {
            ReshuffleDiscardIntoDraw();
            index = FindPlayableIndex(drawPile, IsPlayable);
        }

        if (index < 0)
        {
            NoMovesLeft = true; // genuinely boxed in: no card in the deck fits anywhere
            return;
        }

        NoMovesLeft = false;

        // Swap it into the newest slot; the card it replaces goes back into the draw pile at a random spot.
        int slot = Hand.Length - 1;
        TileDefinition replaced = Hand[slot];
        Hand[slot] = drawPile[index];
        drawPile.RemoveAt(index);
        if (replaced != null) drawPile.Insert(UnityEngine.Random.Range(0, drawPile.Count + 1), replaced);

        HandChanged?.Invoke();
    }

    static int FindPlayableIndex(List<TileDefinition> cards, Func<TileDefinition, bool> isPlayable)
    {
        for (int i = cards.Count - 1; i >= 0; i--)
            if (isPlayable(cards[i])) return i;
        return -1;
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