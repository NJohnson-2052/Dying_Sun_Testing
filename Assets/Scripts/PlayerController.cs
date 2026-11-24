using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// The pill-shaped player. Click a placed tile and the player walks there along connected paths.
// A step between two tiles is only allowed if BOTH tiles have an opening facing each other.
public class PlayerController : MonoBehaviour
{
    [SerializeField] private GridManager grid;

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 3f;
    [Tooltip("How high the capsule's center sits above the grid surface.")]
    [SerializeField] private float heightOffset = 0.5f;
    [Tooltip("How high the player hops between tiles. 0 = glide.")]
    [SerializeField] private float hopHeight = 0.25f;

    [Header("Resources")]
    [Tooltip("If true, each tile pays out only the first time you step on it.")]
    [SerializeField] private bool collectOnce = true;
    [Tooltip("A small burst of colored sparks when you collect a resource.")]
    [SerializeField] private bool sparkles = true;

    public Vector2Int CurrentCell { get; private set; }
    public bool IsMoving => moveRoutine != null;
    public event Action ResourcesChanged;

    public bool HasWon { get; private set; }
    public event Action ReachedGoal;

    // Used by the disaster system to count turns.
    // Fired after each tile the player steps onto (not the goal tile, which ends the game).
    public event Action StepTaken;
    // Fired when a walk ends, as long as the player moved at least one tile.
    public event Action MoveFinished;
    // Fired when Restart puts the player back at the start.
    public event Action WasReset;

    private Coroutine moveRoutine;
    private readonly Dictionary<ResourceType, int> resources = new Dictionary<ResourceType, int>();
    private readonly HashSet<Vector2Int> collectedCells = new HashSet<Vector2Int>();

    // The four directions: which side of the tile, and which neighbouring cell that leads to.
    private static readonly (Openings side, Vector2Int offset)[] Directions =
    {
        (Openings.North, new Vector2Int(0, 1)),
        (Openings.East,  new Vector2Int(1, 0)),
        (Openings.South, new Vector2Int(0, -1)),
        (Openings.West,  new Vector2Int(-1, 0)),
    };

    void Start()
    {
        CurrentCell = grid.startCell;
        transform.position = CellToStandPoint(CurrentCell);
    }

    public int GetResource(ResourceType type) => resources.TryGetValue(type, out int n) ? n : 0;

    // Takes resources away (a disaster, a cost...). Never goes below zero.
    // Returns how many were actually taken, which can be less than asked if the player had fewer.
    public int RemoveResource(ResourceType type, int amount)
    {
        int have = GetResource(type);
        int taken = Mathf.Clamp(amount, 0, have);
        if (taken > 0)
        {
            resources[type] = have - taken;
            ResourcesChanged?.Invoke();
        }
        return taken;
    }

    // Called by PlacementSystem when the player clicks a placed tile.
    public bool TryMoveTo(Vector2Int target)
    {
        if (HasWon || IsMoving || target == CurrentCell) return false;

        List<Vector2Int> path = FindPath(CurrentCell, target);
        if (path == null) return false; // no connected route

        moveRoutine = StartCoroutine(WalkPath(path));
        return true;
    }

    IEnumerator WalkPath(List<Vector2Int> path)
    {
        int steps = 0;

        foreach (Vector2Int cell in path)
        {
            // Stop if the tile ahead was removed while walking.
            // (The start cell never has a placed tile, so it is always allowed.)
            if (cell != grid.startCell && grid.GetTile(cell) == null) break;

            Vector3 start = transform.position;
            Vector3 destination = CellToStandPoint(cell);
            float distance = Vector3.Distance(start, destination);

            Vector3 flatDirection = destination - start;
            flatDirection.y = 0f;

            float travelled = 0f;
            while (travelled < distance)
            {
                travelled = Mathf.Min(distance, travelled + moveSpeed * Time.deltaTime);
                float progress = distance > 0.0001f ? travelled / distance : 1f;

                // Glide toward the next tile with one little hop per step.
                Vector3 p = Vector3.Lerp(start, destination, progress);
                p.y += Mathf.Sin(progress * Mathf.PI) * hopHeight;
                transform.position = p;

                // Turn to face the way we're going.
                if (flatDirection.sqrMagnitude > 0.0001f)
                    transform.rotation = Quaternion.Slerp(transform.rotation,
                        Quaternion.LookRotation(flatDirection), 12f * Time.deltaTime);

                yield return null;
            }
            transform.position = destination;

            CurrentCell = cell;
            steps++;
            Collect(cell);

            // Stepping onto the goal ends the walk and wins the demo.
            if (cell == grid.GoalCell)
            {
                HasWon = true;
                ReachedGoal?.Invoke();
                break;
            }

            StepTaken?.Invoke();
        }
        moveRoutine = null;

        // The walk is over. This counts as one turn.
        if (steps > 0) MoveFinished?.Invoke();
    }

    void Collect(Vector2Int cell)
    {
        PlacedTile tile = grid.GetTile(cell);
        if (tile == null || tile.definition.resource == ResourceType.None) return;
        if (collectOnce && !collectedCells.Add(cell)) return;

        ResourceType type = tile.definition.resource;
        resources[type] = GetResource(type) + 1;
        if (sparkles) PickupBurst.Spawn(transform.position + Vector3.up * 0.3f, ResourceColor(type));
        ResourcesChanged?.Invoke();
    }

    // Puts the player back at the start with empty resources (used by Restart).
    public void ResetPlayer()
    {
        if (moveRoutine != null) StopCoroutine(moveRoutine);
        moveRoutine = null;

        resources.Clear();
        collectedCells.Clear();
        HasWon = false;

        CurrentCell = grid.startCell;
        transform.position = CellToStandPoint(CurrentCell);
        ResourcesChanged?.Invoke();
        WasReset?.Invoke();
    }

    // The grid decides what each cell opens to (the start and goal cells act like crossroads).
    Openings OpeningsAt(Vector2Int cell) => grid.GetOpenings(cell);

    // Breadth-first search over tiles whose openings connect to each other.
    List<Vector2Int> FindPath(Vector2Int from, Vector2Int to)
    {
        var cameFrom = new Dictionary<Vector2Int, Vector2Int>();
        var queue = new Queue<Vector2Int>();
        queue.Enqueue(from);
        cameFrom[from] = from;

        while (queue.Count > 0)
        {
            Vector2Int current = queue.Dequeue();
            if (current == to) break;

            foreach (var (side, offset) in Directions)
            {
                Vector2Int next = current + offset;
                if (!grid.IsInBounds(next) || cameFrom.ContainsKey(next)) continue;

                bool currentOpens = (OpeningsAt(current) & side) != 0;
                bool nextOpensBack = (OpeningsAt(next) & PathUtil.Rotate(side, 2)) != 0; // opposite side
                if (!currentOpens || !nextOpensBack) continue;

                cameFrom[next] = current;
                queue.Enqueue(next);
            }
        }

        if (!cameFrom.ContainsKey(to)) return null;

        // Walk backwards from the target to build the route.
        var path = new List<Vector2Int>();
        for (Vector2Int c = to; c != from; c = cameFrom[c]) path.Add(c);
        path.Reverse();
        return path;
    }

    Vector3 CellToStandPoint(Vector2Int cell) => grid.CellToWorld(cell) + Vector3.up * heightOffset;

    static Color ResourceColor(ResourceType r)
    {
        switch (r)
        {
            case ResourceType.Bio:     return new Color(0.3f, 0.8f, 0.4f);
            case ResourceType.Scrap:   return new Color(0.85f, 0.6f, 0.25f);
            case ResourceType.Rations: return new Color(0.9f, 0.35f, 0.35f);
            default:                   return Color.white;
        }
    }

    // Temporary resource readout under the hand display.
    void OnGUI()
    {
        GUI.Label(new Rect(10, 10, 300, 22), "Bio: " + GetResource(ResourceType.Bio));
        GUI.Label(new Rect(10, 32, 300, 22), "Scrap: " + GetResource(ResourceType.Scrap));
        GUI.Label(new Rect(10, 54, 300, 22), "Rations: " + GetResource(ResourceType.Rations));
    }
}