#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Editor-only helpers. This file MUST live in a folder named "Editor" (Assets/Editor/PathGameTools.cs).
// They appear in the top menu under "Path Game".
public static class PathGameTools
{
    // ---------------------------------------------------------------------------------
    // NAMING RULES THIS TOOL ASSUMES - edit here if your letters mean something else.
    //   Shape letters:   S = Straight, C = Cross, V = Curve (the V in "curVe")
    //   Resource prefix: B = Bio, S = Scrap, R = Rations, (none) = plain
    //   Example: Tile_B_V = Bio Curve, Tile_S = plain Straight, Tile_S_S = Scrap Straight
    // ---------------------------------------------------------------------------------
    static readonly (string letter, PathType shape, string name)[] Shapes =
    {
        ("S", PathType.Straight, "Straight"),
        ("C", PathType.Cross,    "Cross"),
        ("V", PathType.Corner,   "Curve"),
    };

    static readonly (string prefix, ResourceType resource, string name)[] Resources =
    {
        ("",  ResourceType.None,    "Plain"),
        ("B", ResourceType.Bio,     "Bio"),
        ("S", ResourceType.Scrap,   "Scrap"),
        ("R", ResourceType.Rations, "Rations"),
    };

    [MenuItem("Path Game/Create 12 Tile Definitions")]
    static void CreateTileDefinitions()
    {
        const string folder = "Assets/Data";
        if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets", "Data");

        var created = new List<TileDefinition>();

        foreach (var res in Resources)
        foreach (var shape in Shapes)
        {
            string prefabName = "Tile_" + (res.prefix == "" ? "" : res.prefix + "_") + shape.letter;
            GameObject prefab = FindPrefab(prefabName);
            if (prefab == null)
            {
                Debug.LogWarning("Path Game: couldn't find prefab '" + prefabName + "'. Skipped.");
                continue;
            }

            string label = res.name + " " + shape.name;
            string path = folder + "/" + label + ".asset";

            TileDefinition def = AssetDatabase.LoadAssetAtPath<TileDefinition>(path);
            if (def == null)
            {
                def = ScriptableObject.CreateInstance<TileDefinition>();
                AssetDatabase.CreateAsset(def, path);
            }

            def.displayName = label;
            def.pathType = shape.shape;
            def.resource = res.resource;
            def.prefab = prefab;
            EditorUtility.SetDirty(def);
            created.Add(def);
        }

        AssetDatabase.SaveAssets();

        // Put them straight into the TileDeck's pool.
        TileDeck deck = Object.FindObjectOfType<TileDeck>();
        if (deck != null)
        {
            Undo.RecordObject(deck, "Assign tile pool");
            deck.pool = created.ToArray();
            EditorUtility.SetDirty(deck);
            EditorSceneManager.MarkSceneDirty(deck.gameObject.scene);
        }
        else
        {
            Debug.LogWarning("Path Game: no TileDeck found in the open scene. Drag the definitions into its Pool by hand.");
        }

        Debug.Log("Path Game: created/updated " + created.Count + " tile definitions in " + folder + ".");
    }

    // Logs each tile prefab's size and how far its visual center is from its pivot.
    // Size X/Z should match your Grid's Cell Size; center offset should be about 0.
    [MenuItem("Path Game/Check Tile Prefabs")]
    static void CheckTilePrefabs()
    {
        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs/Tiles" });
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);

            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                Debug.Log(prefab.name + ": no renderers found.");
            }
            else
            {
                Bounds b = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
                Vector3 offset = b.center - instance.transform.position;

                Debug.Log(string.Format(
                    "{0}: size X {1:F2}  Y {2:F2}  Z {3:F2} | center offset X {4:F2}  Z {5:F2} | bottom Y {6:F2} | root scale {7} rotation {8}",
                    prefab.name, b.size.x, b.size.y, b.size.z, offset.x, offset.z, b.min.y - instance.transform.position.y,
                    instance.transform.localScale, instance.transform.eulerAngles));
            }

            Object.DestroyImmediate(instance);
        }
    }

    // Select an object in the scene (like your map), then run this to see its corners and top surface height.
    [MenuItem("Path Game/Log Selected Object Bounds")]
    static void LogSelectedBounds()
    {
        GameObject go = Selection.activeGameObject;
        if (go == null) { Debug.LogWarning("Path Game: select an object in the scene first."); return; }

        Renderer[] renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) { Debug.LogWarning("Path Game: " + go.name + " has no renderers."); return; }

        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);

        Debug.Log(string.Format("{0}: min {1} max {2} size {3}  (top surface Y = {4:F3})",
            go.name, b.min, b.max, b.size, b.max.y));
    }

    struct Region
    {
        public int count, minX, maxX, minZ, maxZ;
        public float heightSum;
    }

    // Finds the sunken recess ("divot") in the map and moves the Grid to sit in it.
    // Method: fire rays straight down over the map, find the lower areas, pick the one closest to the grid's size.
    [MenuItem("Path Game/Snap Grid To Map Divot")]
    static void SnapGridToMapDivot()
    {
        GridManager grid = Object.FindObjectOfType<GridManager>();
        if (grid == null) { Debug.LogWarning("Path Game: no GridManager in the open scene."); return; }

        // Use the selected object if it has a collider, otherwise look for the map by name.
        Collider col = null;
        if (Selection.activeGameObject != null) col = Selection.activeGameObject.GetComponentInChildren<Collider>();
        if (col == null)
        {
            GameObject map = GameObject.Find("Agricolony Map");
            if (map != null) col = map.GetComponentInChildren<Collider>();
        }
        if (col == null) { Debug.LogWarning("Path Game: select the map object (it needs a Mesh Collider) and try again."); return; }

        Physics.SyncTransforms();

        // 1. Sample the surface height on a lattice of downward rays.
        Bounds b = col.bounds;
        float step = Mathf.Max(0.05f, Mathf.Max(b.size.x, b.size.z) / 500f);
        int nx = Mathf.Max(1, Mathf.CeilToInt(b.size.x / step));
        int nz = Mathf.Max(1, Mathf.CeilToInt(b.size.z / step));

        var heights = new float[nx, nz];
        var hit = new bool[nx, nz];
        float top = float.MinValue;

        for (int ix = 0; ix < nx; ix++)
        for (int iz = 0; iz < nz; iz++)
        {
            var origin = new Vector3(b.min.x + (ix + 0.5f) * step, b.max.y + 1f, b.min.z + (iz + 0.5f) * step);
            if (col.Raycast(new Ray(origin, Vector3.down), out RaycastHit h, b.size.y + 2f))
            {
                heights[ix, iz] = h.point.y;
                hit[ix, iz] = true;
                top = Mathf.Max(top, h.point.y);
            }
        }

        if (top == float.MinValue) { Debug.LogWarning("Path Game: the rays didn't hit the map's collider."); return; }

        // 2. Group the sunken samples into connected regions.
        var low = new bool[nx, nz];
        for (int ix = 0; ix < nx; ix++)
        for (int iz = 0; iz < nz; iz++)
            low[ix, iz] = hit[ix, iz] && heights[ix, iz] < top - 0.02f;

        var regions = new List<Region>();
        var visited = new bool[nx, nz];
        var stack = new Stack<Vector2Int>();

        for (int sx = 0; sx < nx; sx++)
        for (int sz = 0; sz < nz; sz++)
        {
            if (visited[sx, sz] || !low[sx, sz]) continue;

            var region = new Region { minX = sx, maxX = sx, minZ = sz, maxZ = sz };
            visited[sx, sz] = true;
            stack.Push(new Vector2Int(sx, sz));

            while (stack.Count > 0)
            {
                Vector2Int p = stack.Pop();
                region.count++;
                region.heightSum += heights[p.x, p.y];
                region.minX = Mathf.Min(region.minX, p.x);
                region.maxX = Mathf.Max(region.maxX, p.x);
                region.minZ = Mathf.Min(region.minZ, p.y);
                region.maxZ = Mathf.Max(region.maxZ, p.y);

                for (int d = 0; d < 4; d++)
                {
                    int qx = p.x + (d == 0 ? 1 : d == 1 ? -1 : 0);
                    int qz = p.y + (d == 2 ? 1 : d == 3 ? -1 : 0);
                    if (qx < 0 || qz < 0 || qx >= nx || qz >= nz) continue;
                    if (visited[qx, qz] || !low[qx, qz]) continue;
                    visited[qx, qz] = true;
                    stack.Push(new Vector2Int(qx, qz));
                }
            }

            if (region.count >= 25) regions.Add(region);
        }

        if (regions.Count == 0)
        {
            Debug.LogWarning("Path Game: no recess found. The map's top surface is at Y = " + top.ToString("F3") +
                             " and nothing is lower than that by more than 0.02.");
            return;
        }

        // 3. Pick the region closest in size to the grid (the recess is meant to hold it).
        float gridW = grid.width * grid.cellSize;
        float gridH = grid.height * grid.cellSize;
        int best = 0;
        float bestScore = float.MaxValue;

        for (int i = 0; i < regions.Count; i++)
        {
            Region r = regions[i];
            float sizeX = (r.maxX - r.minX + 1) * step;
            float sizeZ = (r.maxZ - r.minZ + 1) * step;
            float floorY = r.heightSum / r.count;
            Debug.Log("Path Game: recess " + i + ": " + sizeX.ToString("F2") + " x " + sizeZ.ToString("F2") +
                      ", floor Y " + floorY.ToString("F3") + " (map top Y " + top.ToString("F3") + ")");

            float score = Mathf.Abs(sizeX - gridW) + Mathf.Abs(sizeZ - gridH);
            if (score < bestScore) { bestScore = score; best = i; }
        }

        // 4. Center the grid in it, at floor height.
        Region pick = regions[best];
        float minXw = b.min.x + pick.minX * step;
        float maxXw = b.min.x + (pick.maxX + 1) * step;
        float minZw = b.min.z + pick.minZ * step;
        float maxZw = b.min.z + (pick.maxZ + 1) * step;
        float centerX = (minXw + maxXw) * 0.5f;
        float centerZ = (minZw + maxZw) * 0.5f;
        float floor = pick.heightSum / pick.count;

        Undo.RecordObject(grid.transform, "Snap grid to map divot");
        grid.transform.position = new Vector3(centerX - gridW * 0.5f, floor, centerZ - gridH * 0.5f);
        EditorUtility.SetDirty(grid.transform);
        EditorSceneManager.MarkSceneDirty(grid.gameObject.scene);

        float pickW = maxXw - minXw;
        float pickH = maxZw - minZw;
        Debug.Log("Path Game: moved the Grid into recess " + best + " (" + pickW.ToString("F2") + " x " + pickH.ToString("F2") +
                  "). The grid is " + gridW.ToString("F2") + " x " + gridH.ToString("F2") + ". New Grid position: " + grid.transform.position);

        if (Mathf.Abs(pickW - gridW) > step * 3f || Mathf.Abs(pickH - gridH) > step * 3f)
            Debug.LogWarning("Path Game: the recess and the grid aren't the same size. Adjust the Grid's Width/Height (cell size is set by your tile).");
    }

    static GameObject FindPrefab(string name)
    {
        foreach (string guid in AssetDatabase.FindAssets(name + " t:Prefab"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (Path.GetFileNameWithoutExtension(path) == name)
                return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }
        return null;
    }
}
#endif