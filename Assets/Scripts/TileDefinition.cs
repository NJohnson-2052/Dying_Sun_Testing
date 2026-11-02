using UnityEngine;

public enum ResourceType { None, Bio, Scrap, Rations }

// Right-click in Project > Create > Path Game > Tile Definition.
// Later, when your real assets arrive, make one of these per tile and assign the real prefab.
[CreateAssetMenu(menuName = "Path Game/Tile Definition", fileName = "NewTileDefinition")]
public class TileDefinition : ScriptableObject
{
    public string displayName = "Tile";
    public PathType pathType = PathType.Straight;
    [Tooltip("None = plain path with no benefit.")]
    public ResourceType resource = ResourceType.None;
    public GameObject prefab;

    [Tooltip("If the model's path points the wrong way compared to the game's rules, turn it here. " +
             "Each step turns the model 90 degrees clockwise (seen from above).")]
    [Range(0, 3)] public int modelRotationOffset = 0;

    [Tooltip("How many of this tile the player may place. 0 = unlimited.")]
    public int maxCount = 0;
}