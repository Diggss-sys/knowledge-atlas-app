using System.Collections.Generic;
using UnityEngine;

namespace RoomGen.Generation
{
    internal static class GenerationUtil
    {
        public static GameObject CreateBox(
            string name,
            Transform parent,
            Vector3 size,
            Vector3 localPosition,
            Material material,
            int layer,
            Quaternion? localRotation = null)
        {
            var item = GameObject.CreatePrimitive(PrimitiveType.Cube);
            item.name = name;
            item.layer = layer;
            item.transform.SetParent(parent, false);
            item.transform.localPosition = localPosition;
            item.transform.localRotation = localRotation ?? Quaternion.identity;
            item.transform.localScale = size;
            item.GetComponent<MeshRenderer>().sharedMaterial = material;
            return item;
        }

        public static GameObject CreateMeshObject(
            string name,
            Transform parent,
            Mesh mesh,
            Material material,
            int layer,
            bool collider = true)
        {
            var item = new GameObject(name);
            item.layer = layer;
            item.transform.SetParent(parent, false);
            item.AddComponent<MeshFilter>().sharedMesh = mesh;
            item.AddComponent<MeshRenderer>().sharedMaterial = material;
            if (collider)
                item.AddComponent<MeshCollider>().sharedMesh = mesh;
            return item;
        }

        public static void SetLayerRecursively(GameObject root, int layer)
        {
            root.layer = layer;
            foreach (Transform child in root.transform)
                SetLayerRecursively(child.gameObject, layer);
        }

        public static Mesh BuildFootprintPrism(IReadOnlyList<Vector2> path, float topY, float thickness, string name)
        {
            // Caps are EAR-CLIPPED, not center-fanned: a wall bowed INTO the room (wall_bow < 0)
            // makes the footprint non-convex, where a hub fan emits inverted/overlapping triangles
            // (RENDERING_RESEARCH.md §4 / PR-review finding). Ear clipping handles both.
            var count = path.Count;
            var vertices = new List<Vector3>(count * 4);
            var uv = new List<Vector2>(count * 4);
            var triangles = new List<int>(count * 12);
            var bottomY = topY - thickness;

            for (var i = 0; i < count; i++)
            {
                var p = path[i];
                vertices.Add(new Vector3(p.x, topY, p.y));
                vertices.Add(new Vector3(p.x, bottomY, p.y));
                uv.Add(new Vector2(p.x, p.y));
                uv.Add(new Vector2(p.x, p.y));
            }

            var capTriangles = EarClip.Triangulate(path);
            var ccw = EarClip.SignedArea(path) > 0f;
            for (var t = 0; t < capTriangles.Count; t += 3)
            {
                var a = capTriangles[t];
                var b = capTriangles[t + 1];
                var c = capTriangles[t + 2];

                // Top cap must be visible from ABOVE (+Y): CW seen from above = ring order reversed
                // for a CCW ring. EarClip returns triangles in ring orientation; flip as needed.
                if (ccw)
                {
                    triangles.Add(a * 2); triangles.Add(c * 2); triangles.Add(b * 2);
                    triangles.Add(a * 2 + 1); triangles.Add(b * 2 + 1); triangles.Add(c * 2 + 1);
                }
                else
                {
                    triangles.Add(a * 2); triangles.Add(b * 2); triangles.Add(c * 2);
                    triangles.Add(a * 2 + 1); triangles.Add(c * 2 + 1); triangles.Add(b * 2 + 1);
                }
            }

            // Side band around the rim (unchanged behaviour).
            for (var i = 0; i < count; i++)
            {
                var next = (i + 1) % count;
                var top = i * 2;
                var bottom = top + 1;
                var nextTop = next * 2;
                var nextBottom = nextTop + 1;

                triangles.Add(top);
                triangles.Add(nextTop);
                triangles.Add(nextBottom);
                triangles.Add(top);
                triangles.Add(nextBottom);
                triangles.Add(bottom);
            }

            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
