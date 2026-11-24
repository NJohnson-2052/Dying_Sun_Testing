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
            deck.recipe = TileDeck.BuildDefaultRecipe(created); // the default 80-card deck, editable in the Inspector
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

    // Fills the TileDeck's recipe list with the default 80-card deck so you can edit the numbers in the Inspector.
    // Run it again any time to put the recipe back to the defaults.
    [MenuItem("Path Game/Reset Deck Recipe To Default")]
    static void ResetDeckRecipe()
    {
        TileDeck deck = Object.FindObjectOfType<TileDeck>();
        if (deck == null || deck.pool == null || deck.pool.Length == 0)
        {
            Debug.LogWarning("Path Game: the TileDeck needs its Pool filled first (run Path Game > Create 12 Tile Definitions).");
            return;
        }

        Undo.RecordObject(deck, "Reset deck recipe");
        deck.recipe = TileDeck.BuildDefaultRecipe(deck.pool);
        EditorUtility.SetDirty(deck);
        EditorSceneManager.MarkSceneDirty(deck.gameObject.scene);

        int total = 0;
        foreach (DeckEntry entry in deck.recipe) total += entry.copies;
        Debug.Log("Path Game: deck recipe reset to the default (" + total + " cards). Select the TileDeck to edit the copies of each tile.");
    }

    // One click: creates the Blizzard disaster (every 4 turns), adds a Disaster Manager to the scene,
    // and wires the player, the grid and the Blizzard into it.
    // Safe to run again: it reuses what already exists and only adds what's missing.
    [MenuItem("Path Game/Add Disaster System To Scene")]
    static void AddDisasterSystem()
    {
        PlayerController player = Object.FindObjectOfType<PlayerController>();
        GridManager grid = Object.FindObjectOfType<GridManager>();
        if (player == null)
        {
            Debug.LogWarning("Path Game: no Player (PlayerController) found in the open scene.");
            return;
        }

        // 1. The Blizzard asset. Only created once, so your tuning is never overwritten.
        EnsureFolder("Assets", "Data");
        EnsureFolder("Assets/Data", "Disasters");
        const string path = "Assets/Data/Disasters/Blizzard.asset";

        ResourceLossDisaster blizzard = AssetDatabase.LoadAssetAtPath<ResourceLossDisaster>(path);
        if (blizzard == null)
        {
            blizzard = ScriptableObject.CreateInstance<ResourceLossDisaster>();
            blizzard.displayName = "Blizzard";
            blizzard.firstTurn = 4;
            blizzard.repeatEvery = 4;
            blizzard.minLoss = 1;
            blizzard.maxLoss = 2;
            AssetDatabase.CreateAsset(blizzard, path);
            AssetDatabase.SaveAssets();
        }

        // 2. The manager object.
        DisasterManager manager = Object.FindObjectOfType<DisasterManager>();
        if (manager == null)
        {
            GameObject go = new GameObject("Disasters");
            Undo.RegisterCreatedObjectUndo(go, "Add disaster system");
            manager = go.AddComponent<DisasterManager>();
        }

        // 3. Wire it up (the fields are private, so go through the serialized object).
        var so = new SerializedObject(manager);
        so.FindProperty("player").objectReferenceValue = player;
        so.FindProperty("grid").objectReferenceValue = grid;

        SerializedProperty list = so.FindProperty("disasters");
        bool alreadyListed = false;
        for (int i = 0; i < list.arraySize; i++)
            if (list.GetArrayElementAtIndex(i).objectReferenceValue == blizzard) alreadyListed = true;

        if (!alreadyListed)
        {
            list.arraySize++;
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = blizzard;
        }

        so.ApplyModifiedProperties();
        EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);

        Selection.activeObject = blizzard; // so you can tune it right away
        Debug.Log("Path Game: disaster system ready. The Blizzard asset is in Assets/Data/Disasters. " +
                  "Make more with right-click > Create > Path Game > Disaster, then add them to the Disasters object's list.");
    }

    // One click: adds the flashlight energy and the chase camera, and wires them up.
    // Safe to run again: it reuses what already exists.
    [MenuItem("Path Game/Add Energy And Chase Camera To Scene")]
    static void AddEnergyAndChaseCamera()
    {
        PlayerController player = Object.FindObjectOfType<PlayerController>();
        GridManager grid = Object.FindObjectOfType<GridManager>();
        PlacementSystem placement = Object.FindObjectOfType<PlacementSystem>();
        Camera cam = Camera.main;

        if (player == null)
        {
            Debug.LogWarning("Path Game: no Player (PlayerController) found in the open scene.");
            return;
        }

        // 1. The flashlight energy object.
        FlashlightEnergy energy = Object.FindObjectOfType<FlashlightEnergy>();
        if (energy == null)
        {
            GameObject go = new GameObject("Flashlight Energy");
            Undo.RegisterCreatedObjectUndo(go, "Add flashlight energy");
            energy = go.AddComponent<FlashlightEnergy>();
        }

        var energySO = new SerializedObject(energy);
        energySO.FindProperty("player").objectReferenceValue = player;
        energySO.FindProperty("grid").objectReferenceValue = grid;
        energySO.ApplyModifiedProperties();
        EditorSceneManager.MarkSceneDirty(energy.gameObject.scene);

        // 2. Tell the placement system about it.
        if (placement != null)
        {
            var placementSO = new SerializedObject(placement);
            SerializedProperty energyProp = placementSO.FindProperty("energy");
            if (energyProp != null) energyProp.objectReferenceValue = energy;
            placementSO.ApplyModifiedProperties();
            EditorSceneManager.MarkSceneDirty(placement.gameObject.scene);
        }
        else
        {
            Debug.LogWarning("Path Game: no PlacementSystem found, so tiles won't cost energy yet.");
        }

        // 3. The chase camera goes on the Main Camera (the one tagged MainCamera).
        if (cam == null)
        {
            Debug.LogWarning("Path Game: no camera tagged MainCamera found, so the chase camera was not added.");
        }
        else
        {
            CameraFollow follow = cam.GetComponent<CameraFollow>();
            if (follow == null) follow = Undo.AddComponent<CameraFollow>(cam.gameObject);

            var followSO = new SerializedObject(follow);
            followSO.FindProperty("player").objectReferenceValue = player;
            followSO.ApplyModifiedProperties();
            EditorSceneManager.MarkSceneDirty(cam.gameObject.scene);
        }

        Selection.activeObject = energy.gameObject;
        Debug.Log("Path Game: energy and chase camera ready. Tune them on the Flashlight Energy object and the Main Camera.");
    }

    static void EnsureFolder(string parent, string name)
    {
        if (!AssetDatabase.IsValidFolder(parent + "/" + name)) AssetDatabase.CreateFolder(parent, name);
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