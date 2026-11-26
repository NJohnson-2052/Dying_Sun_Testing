using UnityEngine;

// Spend materials to remove a tile you placed. After choosing it, click the tile to remove.
// (Right-click removal is turned off by default, so this is how you get rid of tiles.)
[CreateAssetMenu(menuName = "Path Game/Crafting/Demolish Tile", fileName = "NewDemolish")]
public class DemolishRecipe : CraftingRecipe
{
    protected override string AutoDescription => "Remove any tile you placed.";

    public override bool NeedsTarget => true;
    public override string TargetPrompt => "Demolish: click a highlighted tile to remove it.";

    public override bool IsValidTarget(CraftContext context, Vector2Int cell)
    {
        // Any placed tile, except the one the player is standing on.
        return context.grid.IsOccupied(cell) && cell != context.player.CurrentCell;
    }

    public override string BlockedReason(CraftContext context)
    {
        for (int x = 0; x < context.grid.width; x++)
        for (int y = 0; y < context.grid.height; y++)
            if (IsValidTarget(context, new Vector2Int(x, y))) return null;

        return "No tile to remove";
    }

    public override string Apply(CraftContext context, Vector2Int cell)
    {
        if (!context.grid.Remove(cell)) return "Nothing to remove";

        // Removing a tile changes where things can go, so make sure the hand still has a legal card.
        if (context.deck != null) context.deck.EnsurePlayable();
        return "Tile removed";
    }
}