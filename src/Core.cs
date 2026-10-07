// Shared helpers: logging, texture/material loading and the box mesh builder.
// Written for the C# 5 compiler that ships with Windows (no $"", ?. or =>).
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace VillagerSpaceProgram
{
    public static class VSP
    {
        public const string Folder = "VillagerSpaceProgram";

        public static void Log(string msg) { Debug.Log("[VSP] " + msg); }
        public static void Warn(string msg) { Debug.LogWarning("[VSP] " + msg); }

        // PluginData is skipped by KSP's GameDatabase, so our PNGs are only loaded by us,
        // unfiltered and uncompressed, which keeps the pixels crisp.
        public static string PluginData
        {
            get { return Path.Combine(KSPUtil.ApplicationRootPath, "GameData/" + Folder + "/PluginData"); }
        }

        public static string PathIn(string rel) { return Path.Combine(PluginData, rel); }

        // The dev tour only runs when this file exists. It is never shipped.
        public static bool DevMode { get { return File.Exists(PathIn("devtour.txt")); } }
    }

    public static class VSPAssets
    {
        static readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>();
        static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
        static Shader opaque, translucent, unlit;

        public static Texture2D Tex(string name)
        {
            Texture2D t;
            if (textures.TryGetValue(name, out t)) return t;
            string path = VSP.PathIn("Textures/" + name + ".png");
            if (!File.Exists(path))
            {
                VSP.Warn("missing texture " + path);
                textures[name] = null;
                return null;
            }
            t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            ImageConversion.LoadImage(t, File.ReadAllBytes(path), false);
            t.filterMode = FilterMode.Point;
            t.wrapMode = TextureWrapMode.Clamp;
            t.anisoLevel = 0;
            t.name = "VSP_" + name;
            t.hideFlags = HideFlags.DontUnloadUnusedAsset;
            textures[name] = t;
            return t;
        }

        static Shader Find(params string[] names)
        {
            foreach (string n in names)
            {
                Shader s = Shader.Find(n);
                if (s != null) return s;
            }
            return null;
        }

        public static Shader Opaque
        {
            get
            {
                if (opaque == null) opaque = Find("KSP/Diffuse", "Legacy Shaders/Diffuse", "Diffuse", "Standard");
                return opaque;
            }
        }

        public static Shader Translucent
        {
            get
            {
                if (translucent == null) translucent = Find("KSP/Alpha/Translucent", "Legacy Shaders/Transparent/Diffuse", "Transparent/Diffuse");
                return translucent;
            }
        }

        public static Shader Unlit
        {
            get
            {
                if (unlit == null) unlit = Find("KSP/Unlit", "Unlit/Texture", "KSP/Emissive/Diffuse");
                return unlit;
            }
        }

        public enum Kind { Opaque, Translucent, Unlit }

        public static Material Mat(string texName, Kind kind)
        {
            string key = texName + "|" + kind;
            Material m;
            if (materials.TryGetValue(key, out m) && m != null) return m;
            Shader s = kind == Kind.Opaque ? Opaque : kind == Kind.Translucent ? Translucent : Unlit;
            m = new Material(s);
            m.name = "VSP_" + key;
            m.mainTexture = Tex(texName);
            if (m.HasProperty("_Color")) m.SetColor("_Color", Color.white);
            m.hideFlags = HideFlags.DontUnloadUnusedAsset;
            materials[key] = m;
            return m;
        }

        // A copy of an existing material (keeping its shader and settings) with our texture.
        public static Material IconMat(string texName, Material template)
        {
            string key = texName + "|icon|" + template.shader.name;
            Material m;
            if (materials.TryGetValue(key, out m) && m != null) return m;
            m = new Material(template);
            m.name = "VSP_" + key;
            m.mainTexture = Tex(texName);
            if (m.HasProperty("_Color")) m.SetColor("_Color", Color.white);
            if (m.HasProperty("_BumpMap")) m.SetTexture("_BumpMap", null);
            m.hideFlags = HideFlags.DontUnloadUnusedAsset;
            materials[key] = m;
            return m;
        }

        public static string ShaderReport()
        {
            return "opaque=" + Name(Opaque) + " translucent=" + Name(Translucent) + " unlit=" + Name(Unlit);
        }

        static string Name(Shader s) { return s == null ? "NULL" : s.name; }
    }

    // Faces are indexed 0 +X, 1 -X, 2 +Y (top), 3 -Y (bottom), 4 +Z (front), 5 -Z (back).
    public static class BoxMesh
    {
        static readonly Vector3[] Normals =
        {
            Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back
        };
        // "Right" and "up" of each face as seen from outside, so textures stand upright.
        static readonly Vector3[] Us =
        {
            Vector3.forward, Vector3.back, Vector3.right, Vector3.left, Vector3.left, Vector3.right
        };
        static readonly Vector3[] Vs =
        {
            Vector3.up, Vector3.up, Vector3.forward, Vector3.forward, Vector3.up, Vector3.up
        };

        // cell > 0 tiles each face into roughly cell-sized squares, each showing the whole
        // texture rect, so a tall tank reads as a column of blocks. A face shorter than one
        // cell shows only the matching part of the texture, like a slab.
        public static Mesh Build(Vector3 center, Vector3 size, float cell, Rect[] faceUV)
        {
            List<Vector3> verts = new List<Vector3>();
            List<Vector3> norms = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<int> tris = new List<int>();
            for (int f = 0; f < 6; f++)
            {
                Vector3 n = Normals[f], u = Us[f], v = Vs[f];
                float lu = Mathf.Abs(Vector3.Dot(size, u));
                float lv = Mathf.Abs(Vector3.Dot(size, v));
                Vector3 faceCenter = center + Vector3.Scale(n, size) * 0.5f;
                int nu = 1, nv = 1;
                float fu = 1f, fv = 1f;
                if (cell > 0f)
                {
                    nu = Mathf.Clamp(Mathf.RoundToInt(lu / cell), 1, 32);
                    nv = Mathf.Clamp(Mathf.RoundToInt(lv / cell), 1, 32);
                    if (lu < cell) fu = Mathf.Max(lu / cell, 0.0625f);
                    if (lv < cell) fv = Mathf.Max(lv / cell, 0.0625f);
                }
                Rect r = faceUV[f];
                for (int j = 0; j < nv; j++)
                {
                    for (int i = 0; i < nu; i++)
                    {
                        float a0 = -0.5f + (float)i / nu, a1 = -0.5f + (float)(i + 1) / nu;
                        float b0 = -0.5f + (float)j / nv, b1 = -0.5f + (float)(j + 1) / nv;
                        int start = verts.Count;
                        verts.Add(faceCenter + u * (a0 * lu) + v * (b0 * lv));
                        verts.Add(faceCenter + u * (a1 * lu) + v * (b0 * lv));
                        verts.Add(faceCenter + u * (a1 * lu) + v * (b1 * lv));
                        verts.Add(faceCenter + u * (a0 * lu) + v * (b1 * lv));
                        for (int k = 0; k < 4; k++) norms.Add(n);
                        // Slabs keep the top of the texture, like a half block.
                        float vTop = r.yMax, vBot = r.yMax - r.height * fv;
                        float uL = r.xMin, uR = r.xMin + r.width * fu;
                        uvs.Add(new Vector2(uL, vBot));
                        uvs.Add(new Vector2(uR, vBot));
                        uvs.Add(new Vector2(uR, vTop));
                        uvs.Add(new Vector2(uL, vTop));
                        // Clockwise seen from outside = front face in Unity.
                        tris.Add(start); tris.Add(start + 3); tris.Add(start + 2);
                        tris.Add(start); tris.Add(start + 2); tris.Add(start + 1);
                    }
                }
            }
            Mesh m = new Mesh();
            m.name = "VSP_Box";
            if (verts.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(verts);
            m.SetNormals(norms);
            m.SetUVs(0, uvs);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            m.RecalculateTangents();
            return m;
        }
    }

    public static class Atlas
    {
        // 48x16 block atlas: side | top | bottom, inset half a texel against bleeding.
        public static Rect[] Block()
        {
            Rect side = Cell(0, 0, 3, 1, 48, 16);
            Rect top = Cell(1, 0, 3, 1, 48, 16);
            Rect bottom = Cell(2, 0, 3, 1, 48, 16);
            return new Rect[] { side, side, top, bottom, side, side };
        }

        // Cell (col,row) of a cols x rows grid, row 0 at the top of the image.
        public static Rect Cell(int col, int row, int cols, int rows, int texW, int texH)
        {
            float w = 1f / cols, h = 1f / rows;
            float ix = 0.5f / texW, iy = 0.5f / texH;
            return new Rect(col * w + ix, 1f - (row + 1) * h + iy, w - 2 * ix, h - 2 * iy);
        }
    }
}
