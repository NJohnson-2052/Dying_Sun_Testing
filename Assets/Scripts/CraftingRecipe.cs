using System;
using UnityEngine;

// One price tag: "2 Scrap". A recipe has a list of these, so it can cost several materials.
[Serializable]
public struct ResourceCost
{
    public ResourceType resource;
    [Min(1)] public int amount;

    public ResourceCost(ResourceType resource, int amount)
    {
        this.resource = resource;
        this.amount = amount;
    }
}

// Everything a recipe is allowed to touch when it is used.
public class CraftContext
{
    public PlayerController player;
    public GridManager grid;
    public FlashlightEnergy energy;
    public TileDeck deck;
}

// The base for every crafting option. Each option is an ASSET in your project, so changing a price
// is just clicking the asset and editing the numbers, and making another one is
// right-click > Create > Path Game > Crafting, then dragging it into the Crafting Manager's list.
//
// To invent a brand new kind of option (something other than recharge / battery / demolish / teleport):
//   1. Copy RechargeRecipe.cs, rename the file and the class.
//   2. Change what Apply() does. It receives the player, grid, flashlight and deck.
//      Return a short sentence to show on screen.
//   3. If it needs the player to click a cell first (like Demolish and Teleport), also override
//      NeedsTarget, IsValidTarget and TargetPrompt.
//   4. Make an asset of it (the CreateAssetMenu line at the top of the class adds it to the Create menu).
public abstract class CraftingRecipe : ScriptableObject
{
    public string displayName = "Recipe";

    [Tooltip("Shown under the name in the crafting panel. Leave empty to use an automatic description.")]
    [TextArea(1, 3)] public string description = "";

    [Tooltip("The materials it costs. Add as many lines as you like, each with its own material and amount.")]
    public ResourceCost[] cost = new ResourceCost[0];

    [Tooltip("Every time you use it in a run, each price goes up by this much. 0 = the price never changes.")]
    [Min(0)] public int extraCostPerUse = 0;

    [Tooltip("The most times it can be used in one run. 0 = unlimited.")]
    [Min(0)] public int maxUses = 0;

    // Does the player have to click a cell after choosing this option?
    public virtual bool NeedsTarget => false;

    // The instruction shown at the top of the screen while the player is choosing a cell.
    public virtual string TargetPrompt => "Click a highlighted cell.";

    // Return a reason it can't be used right now ("Flashlight is already full"), or null if it's fine.
    public virtual string BlockedReason(CraftContext context) => null;

    // For options that need a target: is this cell a legal choice? Legal cells are highlighted on the board.
    public virtual bool IsValidTarget(CraftContext context, Vector2Int cell) => false;

    // Do the thing. For options that need a target, `cell` is the cell that was clicked.
    // Return a short sentence for the on-screen message, for example "+3 energy".
    public abstract string Apply(CraftContext context, Vector2Int cell);

    // The automatic description, used when the Description box is empty.
    protected virtual string AutoDescription => "";

    public string DescriptionText => string.IsNullOrWhiteSpace(description) ? AutoDescription : description;

    // The price of cost line `index` after the recipe has been used `timesUsed` times already.
    public int CostFor(int index, int timesUsed) => cost[index].amount + extraCostPerUse * timesUsed;
}