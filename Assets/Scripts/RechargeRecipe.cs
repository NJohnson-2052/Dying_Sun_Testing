using UnityEngine;

// Spend materials to get flashlight energy back, wherever you are.
[CreateAssetMenu(menuName = "Path Game/Crafting/Recharge", fileName = "NewRecharge")]
public class RechargeRecipe : CraftingRecipe
{
    [Tooltip("How much energy it gives back. 0 = a full recharge.")]
    [Min(0)] public int restoreAmount = 3;

    protected override string AutoDescription =>
        restoreAmount <= 0 ? "Fully recharge the flashlight, anywhere." : "Get " + restoreAmount + " energy back, anywhere.";

    public override string BlockedReason(CraftContext context)
    {
        if (context.energy == null) return "No flashlight in the scene";
        if (context.energy.Current >= context.energy.Max) return "Flashlight is already full";
        return null;
    }

    public override string Apply(CraftContext context, Vector2Int cell)
    {
        int before = context.energy.Current;
        if (restoreAmount <= 0) context.energy.Recharge();
        else context.energy.Restore(restoreAmount);
        return "+" + (context.energy.Current - before) + " energy";
    }
}