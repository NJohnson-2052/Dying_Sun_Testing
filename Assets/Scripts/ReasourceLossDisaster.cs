using System.Collections.Generic;
using UnityEngine;

// A disaster that takes resources away from the player. The Blizzard is one of these.
// Make more with right-click > Create > Path Game > Disaster > Resource Loss, for example:
//   a Sandstorm that only takes Scrap every 6 turns, or a Famine that takes 3 Rations every 10 turns.
[CreateAssetMenu(menuName = "Path Game/Disaster/Resource Loss", fileName = "NewDisaster")]
public class ResourceLossDisaster : DisasterDefinition
{
    [Tooltip("Which resources it takes from. Leave empty for all of them.")]
    public ResourceType[] affects = new ResourceType[0];

    [Tooltip("On: it hits only ONE resource, chosen at random from the ones you actually have. Off: it hits every resource listed.")]
    public bool onlyOneRandomType = false;

    [Tooltip("Each resource it hits loses a random amount between these two numbers (never more than you have).")]
    [Min(0)] public int minLoss = 1;
    [Min(0)] public int maxLoss = 2;

    public override string Trigger(DisasterContext context)
    {
        // 1. Which resources can it hit?
        var targets = new List<ResourceType>();
        if (affects != null && affects.Length > 0)
        {
            foreach (ResourceType type in affects)
                if (type != ResourceType.None && !targets.Contains(type)) targets.Add(type);
        }
        else
        {
            targets.Add(ResourceType.Bio);
            targets.Add(ResourceType.Scrap);
            targets.Add(ResourceType.Rations);
        }

        // 2. Optionally narrow it down to a single resource the player really has.
        if (onlyOneRandomType)
        {
            targets.RemoveAll(type => context.player.GetResource(type) <= 0);
            if (targets.Count > 1)
            {
                ResourceType pick = targets[UnityEngine.Random.Range(0, targets.Count)];
                targets.Clear();
                targets.Add(pick);
            }
        }

        // 3. Take the resources and report what was lost.
        int low = Mathf.Max(0, minLoss);
        int high = Mathf.Max(low, maxLoss);

        var lost = new List<string>();
        foreach (ResourceType type in targets)
        {
            int wanted = UnityEngine.Random.Range(low, high + 1);
            int taken = context.player.RemoveResource(type, wanted);
            if (taken > 0) lost.Add("-" + taken + " " + type);
        }

        return lost.Count > 0 ? string.Join(", ", lost) : "You had nothing to lose";
    }
}