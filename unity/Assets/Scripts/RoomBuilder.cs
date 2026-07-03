using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// ============================================================================
//  Parametric Room Builder  —  Unity translation of the web prototype.
//
//  THE CONCEPT (same as the browser version):
//    A room is NOT one fixed model. It is three things:
//      1) a RECIPE  -> the public fields below (width, contour, wall, ...)
//      2) a KIT OF PARTS -> built from primitives, surfaced with PBR textures
//      3) a BUILDER -> the Build() method, which assembles the parts from the recipe
//    Change any field in the Inspector and the room RE-ASSEMBLES itself.
//
//  Materials: drop CC0 textures named plaster/wood/brick/concrete/floor_wood/
//  tile/carpet (.jpg) into  Assets/Resources/RoomTex/  and they are picked up
//  automatically. If a texture is missing, it falls back to a flat color.
// ============================================================================
[ExecuteAlways]               // so it rebuilds live in the Editor, not just in Play mode
public class RoomBuilder : MonoBehaviour
{
    public enum Contour  { Angular, Curved }
    public enum WallMat  { Plaster, Wood, Brick, Concrete }
    public enum FloorMat { Wood, Tile, Carpet }

    [Header("Shell")]
    [Range(3f, 8f)]   public float width  = 5f;
    [Range(3f, 8f)]   public float length = 6f;
    [Range(2.2f, 4f)] public float height = 2.7f;
    public Contour contour = Contour.Angular;

    [Header("Surfaces")]
    public WallMat  wall  = WallMat.Plaster;
    public FloorMat floor = FloorMat.Wood;

    [Header("Openings")]
    [Range(0f, 1f)] public float doorPos = 0.5f;   // slides the door along the front wall
    [Range(0, 3)]   public int   windows = 2;      // add / remove windows on the left wall

    [Header("Light")]
    [Range(0f, 1f)] public float warmth    = 0.5f; // warm <-> cool
    [Range(0f, 2f)] public float intensity = 1f;
    public bool showCeiling = false;

    [Header("Environment")]
    public bool useHDRI = true;    // image-based lighting from Resources/HDRI/sky.hdr

    const float T = 0.12f, DOOR_W = 1.0f, DOOR_H = 2.05f, WIN_W = 1.1f, WIN_BOT = 0.95f, WIN_TOP = 1.95f;

    struct Opening { public float x, width, bottom, top; }

    Transform parts;          // container for everything we build (cleared each rebuild)
    Material  skyMat;         // cached HDRI skybox material
    string lastSig = "~";

    void OnEnable()                       { Build(); lastSig = Sig(); }
    void Update()                         { var s = Sig(); if (s != lastSig) { lastSig = s; Build(); } }
    [ContextMenu("Rebuild Room")] void Rebuild() { Build(); lastSig = Sig(); }

    // a fingerprint of the recipe; when it changes, we rebuild
    string Sig() => $"{width}|{length}|{height}|{contour}|{wall}|{floor}|{doorPos}|{windows}|{warmth}|{intensity}|{showCeiling}|{useHDRI}";

    // ---------- materials ----------
    // Flat-color material (doors, glass, furniture, ceiling). Works in URP + Built-in.
    Material Mat(Color c, float smooth)
    {
        var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var m = new Material(sh);
        if (m.HasProperty("_BaseColor"))  m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Color"))      m.SetColor("_Color", c);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
        if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smooth);
        return m;
    }
    // Textured material: loads Resources/RoomTex/<texName>.jpg, tiles it, color fallback.
    Material MatTex(string texName, Color fallback, float tile, float smooth)
    {
        var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var m = new Material(sh);
        var tex = string.IsNullOrEmpty(texName) ? null : Resources.Load<Texture2D>("RoomTex/" + texName);
        if (tex != null)
        {
            if (m.HasProperty("_BaseMap")) { m.SetTexture("_BaseMap", tex); m.SetTextureScale("_BaseMap", new Vector2(tile, tile)); }
            if (m.HasProperty("_MainTex")) { m.SetTexture("_MainTex", tex); m.SetTextureScale("_MainTex", new Vector2(tile, tile)); }
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
            if (m.HasProperty("_Color"))     m.SetColor("_Color", Color.white);
        }
        else
        {
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", fallback);
            if (m.HasProperty("_Color"))     m.SetColor("_Color", fallback);
        }
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
        if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smooth);
        return m;
    }
    string WallTexName()  => wall  switch { WallMat.Plaster=>"plaster", WallMat.Wood=>"wood", WallMat.Brick=>"brick", _=>"concrete" };
    string FloorTexName() => floor switch { FloorMat.Wood=>"floor_wood", FloorMat.Tile=>"tile", _=>"carpet" };
    Color  WallColor()    => wall  switch { WallMat.Plaster=>new Color(0.93f,0.90f,0.86f), WallMat.Wood=>new Color(0.61f,0.42f,0.24f), WallMat.Brick=>new Color(0.62f,0.35f,0.28f), _=>new Color(0.72f,0.72f,0.70f) };
    Color  FloorColor()   => floor switch { FloorMat.Wood=>new Color(0.55f,0.38f,0.22f), FloorMat.Tile=>new Color(0.88f,0.86f,0.82f), _=>new Color(0.44f,0.49f,0.35f) };

    // ---------- primitive helpers ----------
    void Box(Transform parent, Vector3 size, Vector3 localPos, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.hideFlags = HideFlags.DontSave;          // GameObject, not its Transform — avoids the warning
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale   = size;
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
    }

    // Build one wall as separate piers + sill + header boxes AROUND each opening.
    // (You cannot cheaply "cut a hole" in a mesh at runtime — same in any engine —
    //  so an opening is built as a GAP.)
    void Wall(Transform root, string name, float len, float h, List<Opening> ops, Material mat, Vector3 pos, float yRot)
    {
        var wGO = new GameObject(name);
        wGO.hideFlags = HideFlags.DontSave;         // set on the GameObject (silences the Transform warning)
        var w = wGO.transform;
        w.SetParent(root, false);
        w.localPosition = pos;
        w.localRotation = Quaternion.Euler(0, yRot, 0);

        float cur = -len / 2f;
        void Add(float x0, float x1, float y0, float y1)
        {
            float ww = x1 - x0, hh = y1 - y0;
            if (ww <= 0.001f || hh <= 0.001f) return;
            Box(w, new Vector3(ww, hh, T), new Vector3((x0 + x1) / 2f, (y0 + y1) / 2f, 0), mat);
        }
        foreach (var op in ops.OrderBy(o => o.x))
        {
            float l = op.x - op.width / 2f, r = op.x + op.width / 2f;
            Add(cur, l, 0, h);          // solid pier before the opening
            Add(l, r, 0, op.bottom);    // sill below
            Add(l, r, op.top, h);       // header above
            cur = r;
        }
        Add(cur, len / 2f, 0, h);       // final pier
    }

    // ---------- THE BUILDER ----------
    void Build()
    {
        if (parts != null) { if (Application.isPlaying) Destroy(parts.gameObject); else DestroyImmediate(parts.gameObject); }
        var partsGO = new GameObject("RoomParts");
        partsGO.hideFlags = HideFlags.DontSave;      // set on the GameObject (silences the Transform warning)
        parts = partsGO.transform;
        parts.SetParent(transform, false);

        float W = width, L = length, H = height;
        var wallMat = MatTex(WallTexName(), WallColor(), 2.0f, 0.05f);

        // floor
        Box(parts, new Vector3(W, T, L), new Vector3(0, -T / 2f, 0), MatTex(FloorTexName(), FloorColor(), 3.0f, floor == FloorMat.Tile ? 0.5f : 0.1f));

        // four walls
        Wall(parts, "BackWall", W, H, new List<Opening>(), wallMat, new Vector3(0, 0, -L / 2f), 0);

        float doorX = Mathf.Lerp(-W / 2f + DOOR_W / 2f + 0.1f, W / 2f - DOOR_W / 2f - 0.1f, doorPos);
        Wall(parts, "FrontWall", W, H,
            new List<Opening> { new Opening { x = doorX, width = DOOR_W, bottom = 0, top = DOOR_H } },
            wallMat, new Vector3(0, 0, L / 2f), 0);
        Box(parts, new Vector3(DOOR_W - 0.06f, DOOR_H - 0.05f, 0.04f),
            new Vector3(doorX, (DOOR_H - 0.05f) / 2f, L / 2f - 0.04f), Mat(new Color(0.42f, 0.29f, 0.17f), 0.2f));

        var wins = new List<Opening>();
        for (int i = 0; i < windows; i++)
        {
            float t  = windows == 1 ? 0.5f : (float)i / (windows - 1);
            float xc = Mathf.Lerp(-L / 2f + WIN_W / 2f + 0.3f, L / 2f - WIN_W / 2f - 0.3f, t);
            wins.Add(new Opening { x = xc, width = WIN_W, bottom = WIN_BOT, top = WIN_TOP });
        }
        Wall(parts, "LeftWall", L, H, wins, wallMat, new Vector3(-W / 2f, 0, 0), 90);
        foreach (var wd in wins)
            Box(parts, new Vector3(0.03f, WIN_TOP - WIN_BOT, wd.width - 0.06f),
                new Vector3(-W / 2f, (WIN_BOT + WIN_TOP) / 2f, wd.x), Mat(new Color(0.62f, 0.78f, 0.85f), 0.9f));

        Wall(parts, "RightWall", L, H, new List<Opening>(), wallMat, new Vector3(W / 2f, 0, 0), -90);

        // ceiling
        if (showCeiling)
            Box(parts, new Vector3(W, T, L), new Vector3(0, H + T / 2f, 0), MatTex(WallTexName(), new Color(0.93f, 0.92f, 0.90f), 2.0f, 0.05f));

        // CONTOUR cue: curved == rounded corner columns.
        if (contour == Contour.Curved)
        {
            float r = 0.45f;
            Vector3[] corners = {
                new Vector3(-W/2f, H/2f, -L/2f), new Vector3(W/2f, H/2f, -L/2f),
                new Vector3( W/2f, H/2f,  L/2f), new Vector3(-W/2f, H/2f,  L/2f) };
            foreach (var cpos in corners)
            {
                var col = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                col.hideFlags = HideFlags.DontSave;
                col.transform.SetParent(parts, false);
                col.transform.localPosition = cpos;
                col.transform.localScale = new Vector3(r * 2f, H / 2f, r * 2f);
                col.GetComponent<MeshRenderer>().sharedMaterial = wallMat;
            }
        }

        // furniture: real Poly Haven models, materials overridden for a clean matched look
        PlaceFurniture(parts, W, L);

        // light
        var sun  = FindOrMakeSun();
        var warm = new Color(1.0f, 0.86f, 0.70f);
        var cool = new Color(0.82f, 0.90f, 1.0f);
        sun.color     = Color.Lerp(warm, cool, warmth);
        sun.intensity = 1.1f * intensity;
        sun.shadows   = LightShadows.Soft;

        ApplyEnvironment();
    }

    // HDRI image-based lighting: panoramic skybox + ambient + reflections from Resources/HDRI/sky.hdr
    void ApplyEnvironment()
    {
        if (!useHDRI) return;
        Texture cube = Resources.Load<Cubemap>("HDRI/sky");
        if (cube == null) cube = Resources.Load<Texture>("HDRI/sky");
        var sh   = Shader.Find("Skybox/Cubemap");
        if (cube == null || sh == null) return;
        if (skyMat == null) skyMat = new Material(sh) { hideFlags = HideFlags.DontSave };
        if (skyMat.HasProperty("_Tex"))      skyMat.SetTexture("_Tex", cube);
        if (skyMat.HasProperty("_Exposure")) skyMat.SetFloat("_Exposure", 1.1f);
        RenderSettings.skybox = skyMat;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Skybox;
        RenderSettings.defaultReflectionMode = UnityEngine.Rendering.DefaultReflectionMode.Skybox;
        DynamicGI.UpdateEnvironment();
    }

    // ---------- furniture (real models from Resources/Furniture) ----------
    void PlaceFurniture(Transform parent, float W, float L)
    {
        // sofa against the back wall, facing into the room
        Spawn("Furniture/sofa",  new Vector3(0f, 0f, -L/2f + 0.55f),       0f,  new Color(0.42f,0.47f,0.55f), parent);
        // armchair angled in the far corner
        Spawn("Furniture/chair", new Vector3(W/2f - 0.9f, 0f, -L/2f + 1.0f), -45f, new Color(0.52f,0.55f,0.42f), parent);
        // coffee cart near the middle
        Spawn("Furniture/table", new Vector3(0.2f, 0f, -0.2f),              0f,  new Color(0.55f,0.40f,0.26f), parent);
    }

    void Spawn(string path, Vector3 pos, float yRot, Color col, Transform parent)
    {
        var prefab = Resources.Load<GameObject>(path);
        if (prefab == null) return;
        var go = Instantiate(prefab, parent);
        go.hideFlags = HideFlags.DontSave;
        go.transform.localPosition = pos;
        go.transform.localRotation = Quaternion.Euler(0f, yRot, 0f);
        var mat = Mat(col, 0.2f);
        foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
        {
            var arr = new Material[r.sharedMaterials.Length];
            for (int i = 0; i < arr.Length; i++) arr[i] = mat;
            r.sharedMaterials = arr;
        }
    }

    Light FindOrMakeSun()
    {
        foreach (var l in FindObjectsOfType<Light>())
            if (l.type == LightType.Directional) return l;
        var go = new GameObject("Sun (auto)");
        go.transform.rotation = Quaternion.Euler(50, -30, 0);
        var lt = go.AddComponent<Light>();
        lt.type = LightType.Directional;
        return lt;
    }
}
