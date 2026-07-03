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
            var count = path.Count;
            var vertices = new List<Vector3>(count * 2 + 2);
            var uv = new List<Vector2>(count * 2 + 2);
            var triangles = new List<int>(count * 12);
            var bottomY = topY - thickness;

            vertices.Add(new Vector3(0f, topY, 0f));
            uv.Add(new Vector2(0.5f, 0.5f));
            vertices.Add(new Vector3(0f, bottomY, 0f));
            uv.Add(new Vector2(0.5f, 0.5f));

            for (var i = 0; i < count; i++)
            {
                var p = path[i];
                vertices.Add(new Vector3(p.x, topY, p.y));
                vertices.Add(new Vector3(p.x, bottomY, p.y));
                uv.Add(new Vector2(p.x, p.y));
                uv.Add(new Vector2(p.x, p.y));
            }

            for (var i = 0; i < count; i++)
            {
                var next = (i + 1) % count;
                var top = 2 + i * 2;
                var bottom = top + 1;
                var nextTop = 2 + next * 2;
                var nextBottom = nextTop + 1;

                triangles.Add(0);
                triangles.Add(nextTop);
                triangles.Add(top);
                triangles.Add(1);
                triangles.Add(bottom);
                triangles.Add(nextBottom);

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
