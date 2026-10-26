using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UnderwaterBaseline.Editor
{
    public static class UnderwaterSeabedGeometry
    {
        static Mesh Save(string name, List<Vector3> v, List<Vector2> uv, List<int> tri)
        {
            string path = "Assets/Underwater/Materials/" + name + ".asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (!mesh) { mesh = new Mesh(); AssetDatabase.CreateAsset(mesh, path); } else mesh.Clear();
            mesh.SetVertices(v); mesh.SetUVs(0, uv); mesh.SetTriangles(tri, 0); mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds(); EditorUtility.SetDirty(mesh); return mesh;
        }
        public static Mesh Boulder(int variant)
        {
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            const int rings = 20, slices = 40;
            for (int y = 0; y <= rings; y++) for (int x = 0; x <= slices; x++)
            {
                float lat = y * Mathf.PI / rings, lon = x * 2 * Mathf.PI / slices;
                var p = new Vector3(Mathf.Sin(lat) * Mathf.Cos(lon), Mathf.Cos(lat), Mathf.Sin(lat) * Mathf.Sin(lon));
                float r = 1 + 0.12f * Mathf.Sin(p.x * 4.2f + variant * 2) * Mathf.Cos(p.z * 3 + p.y * 2) + 0.035f * Mathf.Sin(p.z * 12 + p.x * 9);
                p *= r; p.y = Mathf.Max(-0.65f, p.y) * 0.68f + 0.45f;
                v.Add(p); uv.Add(new Vector2((float)x / slices * 2, (float)y / rings));
                if (x == slices || y == rings) continue;
                int i = y * (slices + 1) + x;
                tri.AddRange(new[] {i,i+1,i+slices+1,i+1,i+slices+2,i+slices+1});
            }
            return Save("Boulder" + variant, v, uv, tri);
        }
        public static Mesh Seagrass()
        {
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>(); var rand = new System.Random(71);
            for (int blade = 0; blade < 28; blade++)
            {
                float angle = (float)rand.NextDouble() * Mathf.PI * 2, length = 0.4f + (float)rand.NextDouble() * 0.6f;
                var direction = new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle));
                var side = Vector3.Cross(Vector3.up,direction);
                var root = direction * ((float)rand.NextDouble() * 0.22f); int start = v.Count;
                for (int k = 0; k <= 5; k++)
                {
                    float t = k / 5f;
                    var center = root + Vector3.up * (length * t) + direction * (length * t * t * 0.48f);
                    float width = 0.018f * (1 - t) + 0.001f;
                    v.Add(center-side*width); v.Add(center+side*width); uv.Add(new Vector2(0,t)); uv.Add(new Vector2(1,t));
                    if (k == 5) continue;
                    int i = start + k * 2; tri.AddRange(new[] {i,i+2,i+1,i+1,i+2,i+3});
                }
            }
            return Save("CurvedSeagrass",v,uv,tri);
        }
        public static GameObject Place(string name, Mesh mesh, Material material, Vector3 position, Vector3 scale, float yaw)
        {
            var go = new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer)); go.layer=8;
            go.GetComponent<MeshFilter>().sharedMesh=mesh; go.GetComponent<Renderer>().sharedMaterial=material;
            go.transform.position=position; go.transform.localScale=scale; go.transform.rotation=Quaternion.Euler(0,yaw,0);
            return go;
        }
    }
}
