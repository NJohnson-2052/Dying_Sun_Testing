using UnityEngine;

// Spend materials to make the flashlight battery bigger for the rest of the run.
// With "Extra Cost Per Use" above 0, each upgrade costs more than the last.
[CreateAssetMenu(menuName = "Path Game/Crafting/Battery Upgrade", fileName = "NewBatteryUpgrade")]
public class BatteryUpgradeRecipe : CraftingRecipe
{
    [Tooltip("How much bigger the battery gets each time.")]
    [Min(1)] public int extraCapacity = 1;

    [Tooltip("The battery can't grow past this size. 0 = no limit.")]
    [Min(0)] public int capacityLimit = 0;

    protected override string AutoDescription => "Battery +" + extraCapacity + " bigger, and it fills up.";

    public override string BlockedReason(CraftContext context)
    {
        if (context.energy == null) return "No flashlight in the scene";
        if (capacityLimit > 0 && context.energy.Max >= capacityLimit) return "Battery is at its biggest";
        return null;
    }

    public override string Apply(CraftContext context, Vector2Int cell)
    {
        context.energy.UpgradeMax(extraCapacity);
        return "Battery size " + context.energy.Max;
    }
}