using UnityEngine;

// Spend materials to jump straight to a dome you have already visited. After choosing it,
// click one of the highlighted domes. Arriving at a dome recharges the flashlight, as usual.
[CreateAssetMenu(menuName = "Path Game/Crafting/Teleport", fileName = "NewTeleport")]
public class TeleportRecipe : CraftingRecipe
{
    [Tooltip("On: you can also teleport back to the start tile.")]
    public bool includeStart = false;

    protected override string AutoDescription => includeStart
        ? "Jump to the start or any dome you've visited."
        : "Jump to any dome you've visited.";

    public override bool NeedsTarget => true;
    public override string TargetPrompt => "Teleport: click a highlighted dome.";

    public override bool IsValidTarget(CraftContext context, Vector2Int cell)
    {
        if (cell == context.player.CurrentCell) return false;
        if (includeStart && cell == context.grid.startCell) return true;
        return context.grid.IsGoal(cell) && context.player.HasVisited(cell);
    }

    public override string BlockedReason(CraftContext context)
    {
        for (int x = 0; x < context.grid.width; x++)
        for (int y = 0; y < context.grid.height; y++)
            if (IsValidTarget(context, new Vector2Int(x, y))) return null;

        return includeStart ? "Nowhere to teleport to" : "Visit a dome first";
    }

    public override string Apply(CraftContext context, Vector2Int cell)
    {
        return context.player.TeleportTo(cell) ? "Teleported!" : "Couldn't teleport";
    }
}