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

    // The run: visit every dome -> the Cellar appears -> visit the Cellar -> return to the ship (the start tile).
    // HasWon becomes true when you get back to the ship after the Cellar.
    public bool HasWon { get; private set; }
    public event Action ReachedGoal;          // fired once, when the run is complete

    // Fired the first time the player steps onto each dome. Passes that dome's cell.
    public event Action<Vector2Int> GoalVisited;

    // Fired when the last dome is visited and the Cellar has just appeared.
    public event Action AllDomesVisited;

    // Fired the first time the player steps onto the Cellar. Passes its cell.
    public event Action<Vector2Int> CellarVisited;

    // The run can also be lost: see FailRun. After that nothing works until Restart.
    public bool GameOver { get; private set; }
    public string GameOverReason { get; private set; }
    public float GameOverTime { get; private set; }   // Time.unscaledTime when it happened
    public event Action<string> GameFailed;

    // Ends the run as a failure (for example when a disaster takes your last resource).
    public void FailRun(string reason)
    {
        if (GameOver || HasWon) return;

        GameOver = true;
        GameOverReason = reason;
        GameOverTime = Time.unscaledTime;

        if (moveRoutine != null) StopCoroutine(moveRoutine);
        moveRoutine = null;

        GameFailed?.Invoke(reason);
    }

    private readonly HashSet<Vector2Int> visitedGoals = new HashSet<Vector2Int>();   // domes only
    private bool cellarVisited;

    public int GoalsVisited => visitedGoals.Count;                                   // domes only
    public bool CellarFound => cellarVisited;
    public bool HasVisited(Vector2Int cell) => visitedGoals.Contains(cell) || (cellarVisited && grid.IsCellar(cell));

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

    // Bio + Scrap + Rations all added together.
    public int TotalResources
    {
        get
        {
            int total = 0;
            foreach (int amount in resources.Values) total += amount;
            return total;
        }
    }

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
        if (HasWon || GameOver || IsMoving || target == CurrentCell) return false;

        List<Vector2Int> path = FindPath(CurrentCell, target);
        if (path == null) return false; // no connected route

        moveRoutine = StartCoroutine(WalkPath(path));
        return true;
    }

    // Jumps straight to a cell (used by the Teleport crafting option). Counts as a move, so it is one turn.
    public bool TeleportTo(Vector2Int cell)
    {
        if (HasWon || GameOver || IsMoving || !grid.IsInBounds(cell)) return false;

        PickupBurst.Spawn(transform.position + Vector3.up * 0.3f, new Color(0.5f, 0.9f, 1f), 16);
        CurrentCell = cell;
        transform.position = CellToStandPoint(cell);
        PickupBurst.Spawn(transform.position + Vector3.up * 0.3f, new Color(0.5f, 0.9f, 1f), 16);

        if (HandleArrival(cell)) return true; // teleporting back to the ship after the Cellar completes the run

        StepTaken?.Invoke();     // lets the flashlight recharge if it's the start or a dome
        MoveFinished?.Invoke();  // and counts as a turn for disasters
        return true;
    }

    IEnumerator WalkPath(List<Vector2Int> path)
    {
        int steps = 0;

        foreach (Vector2Int cell in path)
        {
            // Stop if the tile ahead was removed while walking.
            // (The start cell and the domes never have a placed tile, so they are always allowed.)
            if (cell != grid.startCell && !grid.IsGoal(cell) && grid.GetTile(cell) == null) break;

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

            // Domes, the Cellar and the ship are handled here. The walk only ends early if the run was just completed.
            if (HandleArrival(cell)) break;

            StepTaken?.Invoke();
        }
        moveRoutine = null;

        // The walk is over. This counts as one turn.
        if (steps > 0) MoveFinished?.Invoke();
    }

    // Runs every time the player arrives on a cell. Returns true if that just completed the run.
    bool HandleArrival(Vector2Int cell)
    {
        if (grid.IsCellar(cell))
        {
            if (!cellarVisited)
            {
                cellarVisited = true;
                grid.MarkGoalVisited(cell); // lets you build out of the Cellar from now on
                CellarVisited?.Invoke(cell);
            }
        }
        else if (grid.IsDome(cell) && visitedGoals.Add(cell))
        {
            grid.MarkGoalVisited(cell); // lets you build out of this dome from now on
            GoalVisited?.Invoke(cell);

            // The last dome makes the Cellar appear somewhere on the board.
            if (visitedGoals.Count >= grid.Goals.Count && !grid.HasCellar && grid.SpawnCellar(cell))
                AllDomesVisited?.Invoke();
        }

        // Back at the ship after visiting the Cellar: the run is complete.
        if (cellarVisited && cell == grid.startCell)
        {
            HasWon = true;
            ReachedGoal?.Invoke();
            return true;
        }

        return false;
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
        visitedGoals.Clear();
        cellarVisited = false;
        HasWon = false;
        GameOver = false;
        GameOverReason = null;

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