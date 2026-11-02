using System.Collections.Generic;
using UnityEngine;

public class PlacedTile
{
    public TileDefinition definition;
    public int rotationSteps;
    public Openings openings;
    public GameObject instance;
}

// Owns the grid data: size, cell <-> world conversion, what is placed where, and the goal.
// The grid lies flat on the XZ plane. Put this on an empty GameObject at the grid's corner.
public class GridManager : MonoBehaviour
{
    [Header("Grid")]
    public int width = 8;
    public int height = 8;
    public float cellSize = 1f;

    [Tooltip("Lifts everything on the grid (tiles, blanks, goal, player) above the map surface. Raise it if tiles look sunk into, or level with, the ground.")]
    public float surfaceOffset = 0.05f;

    [Header("Rules")]
    [Tooltip("Where the player starts. Tiles must connect outward from here.")]
    public Vector2Int startCell = new Vector2Int(0, 0);

    [Tooltip("Off: a new tile needs at least one matching connection. On: every neighbouring tile that touches it must also match (stricter).")]
    public bool requireAllNeighborsMatch = false;

    [Header("Goal")]
    [Tooltip("Optional. Leave empty to get a simple gold disc until you have a real model.")]
    public GameObject goalPrefab;

    [Tooltip("The goal is placed at least this many steps (Manhattan distance) from the start.")]
    public int minGoalDistance = 4;

    [Header("Visuals")]
    [Tooltip("Shown on every empty cell until a tile is placed there (your Tile_Blank prefab).")]
    public GameObject blankPrefab;

    [Tooltip("At start, measure the Blank Prefab: use its footprint as the Cell Size, and line every tile up with the cell (center and ground level), even if its pivot isn't centered.")]
    public bool autoFitToBlank = true;

    [Header("Drop animation")]
    [Tooltip("How far above its cell a tile starts when placed. 0 turns the animation off.")]
    public float dropHeight = 3f;

    [Tooltip("How long the drop and bounce takes, in seconds.")]
    public float dropDuration = 0.45f;

    [Tooltip("How hard the camera thumps when a tile lands. 0 turns it off. Try 0.3 if the map is far from the camera.")]
    public float landingThump = 0.15f;

    [Header("Debug")]
    public bool drawGizmos = true;

    public Vector2Int GoalCell { get; private set; }

    private PlacedTile[,] cells;
    private readonly Dictionary<TileDefinition, int> placedCounts = new Dictionary<TileDefinition, int>();
    private GameObject goalMarker;
    private GameObject[,] blanks;

    // Each prefab is measured once: from its pivot to the center of its BASE (x, z) and to its bottom (y).
    // Every tile model can have a different pivot, so each one gets its own offset.
    private readonly Dictionary<GameObject, Vector3> pivotOffsets = new Dictionary<GameObject, Vector3>();

    void Awake()
    {
        MeasureBlank();
        cells = new PlacedTile[width, height];
        SpawnBlanks();
        PlaceGoal();
    }

    // ---------- Fitting tiles to the grid ----------

    // Measures a prefab's BASE: its lowest-lying parts (the tile slab), ignoring trees and props on top.
    bool MeasureBase(GameObject prefab, out Bounds baseBounds, out Vector3 pivotWorld)
    {
        baseBounds = new Bounds();
        GameObject probe = Instantiate(prefab, transform.position, Quaternion.identity);
        pivotWorld = probe.transform.position;

        Renderer[] renderers = probe.GetComponentsInChildren<Renderer>();
        bool found = renderers.Length > 0;

        if (found)
        {
            float lowest = float.MaxValue;
            foreach (Renderer r in renderers) lowest = Mathf.Min(lowest, r.bounds.min.y);

            bool first = true;
            foreach (Renderer r in renderers)
            {
                if (r.bounds.min.y > lowest + 0.05f) continue; // skip things standing on top of the slab
                if (first) { baseBounds = r.bounds; first = false; }
                else baseBounds.Encapsulate(r.bounds);
            }
        }

        probe.SetActive(false);
        Destroy(probe);
        return found;
    }

    // The blank tile decides the Cell Size.
    void MeasureBlank()
    {
        if (!autoFitToBlank || blankPrefab == null) return;
        if (!MeasureBase(blankPrefab, out Bounds b, out Vector3 pivot)) return;

        float size = Mathf.Max(b.size.x, b.size.z);
        if (Mathf.Abs(b.size.x - b.size.z) > 0.05f * size)
            Debug.LogWarning("Grid: the blank tile isn't square (" + b.size.x.ToString("F2") + " x " + b.size.z.ToString("F2") + "). Using the larger side.");

        if (size > 0.01f) cellSize = size;
        Debug.Log("Grid: fitted to the blank tile. Cell Size = " + cellSize.ToString("F3"));
    }

    Vector3 GetPivotOffset(GameObject prefab)
    {
        if (!autoFitToBlank || prefab == null) return Vector3.zero;
        if (pivotOffsets.TryGetValue(prefab, out Vector3 cached)) return cached;

        Vector3 offset = Vector3.zero;
        if (MeasureBase(prefab, out Bounds b, out Vector3 pivot))
        {
            offset = new Vector3(b.center.x - pivot.x, b.min.y - pivot.y, b.center.z - pivot.z);
            Debug.Log("Grid: " + prefab.name + " base " + b.size.x.ToString("F2") + " x " + b.size.z.ToString("F2") +
                      ", pivot offset " + offset.ToString("F3"));
        }

        pivotOffsets[prefab] = offset;
        return offset;
    }

    // Where to put this prefab's pivot so its base is centered on the cell and sits on the ground.
    // visualRotationSteps is how many 90-degree turns the model itself gets.
    public Vector3 PositionFor(GameObject prefab, Vector2Int c, int visualRotationSteps)
    {
        Vector3 offset = GetPivotOffset(prefab);
        Vector3 flat = new Vector3(offset.x, 0f, offset.z);
        Vector3 rotated = Quaternion.Euler(0f, 90f * visualRotationSteps, 0f) * flat;
        return CellToWorld(c) - rotated - new Vector3(0f, offset.y, 0f);
    }

    // ---------- Blank tiles (visual filler for empty cells) ----------

    void SpawnBlanks()
    {
        blanks = new GameObject[width, height];
        if (blankPrefab == null) return;

        for (int x = 0; x < width; x++)
        for (int y = 0; y < height; y++)
            blanks[x, y] = Instantiate(blankPrefab, PositionFor(blankPrefab, new Vector2Int(x, y), 0), Quaternion.identity, transform);
    }

    void SetBlankActive(Vector2Int c, bool active)
    {
        if (blanks == null || !IsInBounds(c) || blanks[c.x, c.y] == null) return;
        blanks[c.x, c.y].SetActive(active);
    }

    void RestoreAllBlanks()
    {
        for (int x = 0; x < width; x++)
        for (int y = 0; y < height; y++)
            SetBlankActive(new Vector2Int(x, y), true);
    }

    // ---------- Coordinate helpers ----------

    public bool IsInBounds(Vector2Int c) => c.x >= 0 && c.y >= 0 && c.x < width && c.y < height;

    public bool IsOccupied(Vector2Int c) => IsInBounds(c) && cells[c.x, c.y] != null;

    public PlacedTile GetTile(Vector2Int c) => IsInBounds(c) ? cells[c.x, c.y] : null;

    // Center of a cell in world space.
    public Vector3 CellToWorld(Vector2Int c)
    {
        return transform.position + new Vector3((c.x + 0.5f) * cellSize, surfaceOffset, (c.y + 0.5f) * cellSize);
    }

    public Vector2Int WorldToCell(Vector3 world)
    {
        Vector3 local = world - transform.position;
        return new Vector2Int(Mathf.FloorToInt(local.x / cellSize), Mathf.FloorToInt(local.z / cellSize));
    }

    // ---------- Goal ----------

    void PlaceGoal()
    {
        var far = new List<Vector2Int>();
        var any = new List<Vector2Int>();

        for (int x = 0; x < width; x++)
        for (int y = 0; y < height; y++)
        {
            var c = new Vector2Int(x, y);
            if (c == startCell) continue;
            any.Add(c);
            int distance = Mathf.Abs(c.x - startCell.x) + Mathf.Abs(c.y - startCell.y);
            if (distance >= minGoalDistance) far.Add(c);
        }

        List<Vector2Int> options = far.Count > 0 ? far : any;
        if (options.Count == 0) return;

        GoalCell = options[Random.Range(0, options.Count)];
        SetBlankActive(GoalCell, false); // the goal tile replaces the blank underneath
        SpawnGoalMarker();
    }

    void SpawnGoalMarker()
    {
        if (goalPrefab != null)
        {
            goalMarker = Instantiate(goalPrefab, PositionFor(goalPrefab, GoalCell, 0), Quaternion.identity, transform);
            return;
        }

        // Placeholder: a flat gold disc.
        goalMarker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        goalMarker.name = "GoalMarker";
        goalMarker.transform.SetParent(transform);
        goalMarker.transform.position = CellToWorld(GoalCell) + Vector3.up * 0.03f;
        goalMarker.transform.localScale = new Vector3(cellSize * 0.8f, 0.03f, cellSize * 0.8f);
        Destroy(goalMarker.GetComponent<Collider>()); // never block mouse raycasts
        goalMarker.GetComponent<Renderer>().material.color = new Color(1f, 0.85f, 0.1f);
    }

    // Clears all tiles and picks a new goal. Used by the Restart button.
    public void ResetBoard()
    {
        for (int x = 0; x < width; x++)
        for (int y = 0; y < height; y++)
        {
            if (cells[x, y] == null) continue;
            Destroy(cells[x, y].instance);
            cells[x, y] = null;
        }
        placedCounts.Clear();
        RestoreAllBlanks();

        if (goalMarker != null) Destroy(goalMarker);
        PlaceGoal();
    }

    // ---------- Placement rules ----------

    public int PlacedCount(TileDefinition def) => placedCounts.TryGetValue(def, out int n) ? n : 0;

    // The four sides and the neighbouring cell each one leads to.
    private static readonly (Openings side, Vector2Int offset)[] Directions =
    {
        (Openings.North, new Vector2Int(0, 1)),
        (Openings.East,  new Vector2Int(1, 0)),
        (Openings.South, new Vector2Int(0, -1)),
        (Openings.West,  new Vector2Int(-1, 0)),
    };

    // Openings of whatever is in a cell. The empty start and goal cells act as crossroads
    // so the player can leave the start and step into the goal.
    public Openings GetOpenings(Vector2Int c)
    {
        PlacedTile tile = GetTile(c);
        if (tile != null) return tile.openings;
        return (c == startCell || c == GoalCell)
            ? Openings.North | Openings.East | Openings.South | Openings.West
            : Openings.None;
    }

    public bool CanPlace(Vector2Int c, TileDefinition def, int rotationSteps)
    {
        if (def == null || !IsInBounds(c) || IsOccupied(c)) return false;
        if (c == startCell || c == GoalCell) return false; // these two cells stay free
        if (def.maxCount > 0 && PlacedCount(def) >= def.maxCount) return false;

        Openings newOpenings = PathUtil.Rotate(PathUtil.BaseOpenings(def.pathType), rotationSteps);
        int connections = 0;

        foreach (var (side, offset) in Directions)
        {
            Vector2Int neighbour = c + offset;
            if (!IsInBounds(neighbour)) continue;

            // The goal is only a destination. It doesn't count as something to build from,
            // otherwise you could start a path out of thin air next to it.
            if (neighbour == GoalCell) continue;

            Openings neighbourOpenings = GetOpenings(neighbour);
            if (neighbourOpenings == Openings.None) continue; // empty cell, nothing to match

            bool newOpensToward = (newOpenings & side) != 0;
            bool neighbourOpensBack = (neighbourOpenings & PathUtil.Rotate(side, 2)) != 0; // opposite side

            if (newOpensToward && neighbourOpensBack) connections++;
            else if (requireAllNeighborsMatch && newOpensToward != neighbourOpensBack) return false;
        }

        return connections > 0;
    }

    // Is there ANY cell and rotation where this tile could legally go right now?
    public bool HasAnyPlacement(TileDefinition def)
    {
        if (cells == null || def == null) return false;

        for (int x = 0; x < width; x++)
        for (int y = 0; y < height; y++)
        for (int r = 0; r < 4; r++)
            if (CanPlace(new Vector2Int(x, y), def, r)) return true;

        return false;
    }

    public bool TryPlace(Vector2Int c, TileDefinition def, int rotationSteps)
    {
        rotationSteps = ((rotationSteps % 4) + 4) % 4;
        if (!CanPlace(c, def, rotationSteps)) return false;

        var instance = Instantiate(def.prefab, PositionFor(def.prefab, c, rotationSteps + def.modelRotationOffset),
            Quaternion.Euler(0f, 90f * (rotationSteps + def.modelRotationOffset), 0f), transform);

        cells[c.x, c.y] = new PlacedTile
        {
            definition = def,
            rotationSteps = rotationSteps,
            openings = PathUtil.Rotate(PathUtil.BaseOpenings(def.pathType), rotationSteps),
            instance = instance
        };
        placedCounts[def] = PlacedCount(def) + 1;
        // Drop the tile in from above. The blank underneath hides at the first impact.
        if (dropHeight > 0f)
            instance.AddComponent<TileDropAnimation>().Begin(dropHeight, dropDuration, () =>
            {
                SetBlankActive(c, false);
                if (landingThump > 0f) CameraThump.Thump(landingThump);
            });
        else
            SetBlankActive(c, false);
        return true;
    }

    public bool Remove(Vector2Int c)
    {
        if (!IsOccupied(c)) return false;
        var tile = cells[c.x, c.y];
        Destroy(tile.instance);
        placedCounts[tile.definition] = PlacedCount(tile.definition) - 1;
        cells[c.x, c.y] = null;
        SetBlankActive(c, true); // show the blank again
        return true;
    }

    // Re-applies each placed tile's model rotation and position. Used after calibrating a Model Rotation Offset,
    // so tiles that are already on the board update too.
    public void RefreshPlacedTiles()
    {
        for (int x = 0; x < width; x++)
        for (int y = 0; y < height; y++)
        {
            PlacedTile t = cells[x, y];
            if (t == null || t.instance == null) continue;

            int visualSteps = t.rotationSteps + t.definition.modelRotationOffset;
            t.instance.transform.rotation = Quaternion.Euler(0f, 90f * visualSteps, 0f);
            t.instance.transform.position = PositionFor(t.definition.prefab, new Vector2Int(x, y), visualSteps);
        }
    }

    // ---------- Gizmos (Scene view) ----------

    void OnDrawGizmos()
    {
        if (!drawGizmos) return;

        Gizmos.color = new Color(1f, 1f, 1f, 0.35f);
        for (int x = 0; x <= width; x++)
            Gizmos.DrawLine(transform.position + new Vector3(x * cellSize, 0, 0),
                            transform.position + new Vector3(x * cellSize, 0, height * cellSize));
        for (int z = 0; z <= height; z++)
            Gizmos.DrawLine(transform.position + new Vector3(0, 0, z * cellSize),
                            transform.position + new Vector3(width * cellSize, 0, z * cellSize));

        // Green marker on the start cell.
        Gizmos.color = new Color(0.2f, 1f, 0.3f, 0.5f);
        Gizmos.DrawCube(CellToWorld(startCell) + Vector3.up * 0.02f,
                        new Vector3(cellSize * 0.9f, 0.02f, cellSize * 0.9f));

        if (cells == null) return;

        // Gold marker on the goal cell (only once the game is running and a goal exists).
        Gizmos.color = new Color(1f, 0.85f, 0.1f, 0.6f);
        Gizmos.DrawCube(CellToWorld(GoalCell) + Vector3.up * 0.02f,
                        new Vector3(cellSize * 0.9f, 0.02f, cellSize * 0.9f));

        // Draw a line out of each open side so you can see rotation working on the generic prefab.
        Gizmos.color = Color.yellow;
        for (int x = 0; x < width; x++)
        for (int y = 0; y < height; y++)
        {
            var t = cells[x, y];
            if (t == null) continue;
            Vector3 center = CellToWorld(new Vector2Int(x, y)) + Vector3.up * 0.05f;
            float half = cellSize * 0.5f;
            if ((t.openings & Openings.North) != 0) Gizmos.DrawLine(center, center + Vector3.forward * half);
            if ((t.openings & Openings.East)  != 0) Gizmos.DrawLine(center, center + Vector3.right * half);
            if ((t.openings & Openings.South) != 0) Gizmos.DrawLine(center, center + Vector3.back * half);
            if ((t.openings & Openings.West)  != 0) Gizmos.DrawLine(center, center + Vector3.left * half);
        }
    }
}