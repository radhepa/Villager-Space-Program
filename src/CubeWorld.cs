// From space, the Sun, Kerbin and the Mun are giant blocks.
//
// Kerbin and the Mun are painted from their own colour maps (so the continents and craters
// are where they should be), turned into Minecraft pixels: water, grass, sand and snow for
// Kerbin, four shades of stone for the Mun. They spin with the planet, are lit by the Sun,
// and turn back into the real round worlds up close, so terrain, landing and orbits all work
// as normal. The Sun is a glowing pixel-art block that is always shown.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace VillagerSpaceProgram
{
    public enum CubeStyle { Earth, Moon, Sun }

    public static class CubeWorld
    {
        public static void Make(CelestialBody b, CubeStyle style)
        {
            Transform t = b.scaledBody.transform;
            if (t.Find("VSP_Cube") != null) return;
            MeshFilter mf = b.scaledBody.GetComponent<MeshFilter>();
            MeshRenderer sphere = b.scaledBody.GetComponent<MeshRenderer>();
            if (mf == null || mf.sharedMesh == null || sphere == null || sphere.sharedMaterial == null) return;
            float radius = mf.sharedMesh.bounds.extents.x;

            int px = style == CubeStyle.Earth ? 24 : 16;   // pixels along each cube face
            Texture2D tex;
            try
            {
                tex = style == CubeStyle.Sun ? SunFaces() : Paint(mf.sharedMesh, sphere.sharedMaterial.mainTexture, px, style);
            }
            catch (Exception e) { VSP.Warn("cube " + b.bodyName + ": " + e); return; }
            if (tex == null) return;
            px = tex.height;

            if (style == CubeStyle.Sun)
            {
                // The glowing corona billboards would poke out around the block.
                foreach (SunCoronas c in b.scaledBody.GetComponentsInChildren<SunCoronas>(true))
                    c.gameObject.SetActive(false);
            }

            GameObject go = new GameObject("VSP_Cube");
            go.layer = b.scaledBody.layer;
            go.transform.SetParent(t, false);
            Rect[] uv = new Rect[6];
            for (int f = 0; f < 6; f++) uv[f] = Atlas.Cell(f, 0, 6, 1, px * 6, px);
            go.AddComponent<MeshFilter>().sharedMesh = BoxMesh.Build(Vector3.zero, Vector3.one * radius * 2.02f, 0f, uv);
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = style == CubeStyle.Sun ? SunMaterial(tex) : WorldMaterial(tex);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            CubeBody cb = go.AddComponent<CubeBody>();
            cb.localRadius = radius;
            cb.alwaysOn = style == CubeStyle.Sun;
            cb.farRadii = style == CubeStyle.Moon ? 4f : 3f;
            List<Renderer> extras = new List<Renderer>();
            if (style != CubeStyle.Sun)
                foreach (Renderer r in b.scaledBody.GetComponentsInChildren<Renderer>(true))
                    if (r != sphere && !r.name.StartsWith("VSP_")) extras.Add(r);
            cb.atmosphere = extras.ToArray();
            VSP.Log("cubed " + b.bodyName + " (" + style + ", mesh radius " + radius + ")");
        }

        // Lit by the Sun, plus a little glow so the night side still reads as a block.
        static Material WorldMaterial(Texture2D tex)
        {
            Shader emissive = Shader.Find("KSP/Emissive/Diffuse");
            Material m = new Material(emissive != null ? emissive : VSPAssets.Opaque);
            m.mainTexture = tex;
            if (m.HasProperty("_Color")) m.SetColor("_Color", Color.white);
            if (m.HasProperty("_Emissive")) m.SetTexture("_Emissive", tex);
            if (m.HasProperty("_EmissiveColor")) m.SetColor("_EmissiveColor", new Color(0.35f, 0.35f, 0.35f, 1f));
            return m;
        }

        static Material SunMaterial(Texture2D tex)
        {
            Material m = new Material(VSPAssets.Unlit);
            m.mainTexture = tex;
            if (m.HasProperty("_Color")) m.SetColor("_Color", Color.white);
            return m;
        }

        // The Sun's own light doesn't shade it, so bake Minecraft-style face shading into
        // the six faces (top brightest, bottom darkest) to keep the block shape readable.
        static Texture2D SunFaces()
        {
            Texture2D src = VSPAssets.Tex("sky_sun");
            if (src == null) return null;
            int px = src.width;
            float[] shade = { 0.86f, 0.86f, 1f, 0.74f, 0.93f, 0.93f };
            Texture2D outTex = NewFaces(px, "VSP_CubeSun");
            for (int f = 0; f < 6; f++)
                for (int y = 0; y < px; y++)
                    for (int x = 0; x < px; x++)
                    {
                        Color c = src.GetPixel(x, y);
                        outTex.SetPixel(f * px + x, y, new Color(c.r * shade[f], c.g * shade[f], c.b * shade[f], 1f));
                    }
            outTex.Apply(false, false);
            return outTex;
        }

        static Texture2D NewFaces(int px, string name)
        {
            Texture2D t = new Texture2D(px * 6, px, TextureFormat.RGBA32, false);
            t.filterMode = FilterMode.Point;
            t.wrapMode = TextureWrapMode.Clamp;
            t.name = name;
            t.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return t;
        }

        // Reads the body's colour map back from the GPU and paints the six cube faces.
        static Texture2D Paint(Mesh sphere, Texture src, int px, CubeStyle style)
        {
            if (src == null) return null;
            RenderTexture rt = RenderTexture.GetTemporary(512, 256, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(src, rt);
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            Texture2D map = new Texture2D(512, 256, TextureFormat.RGBA32, false);
            map.ReadPixels(new Rect(0, 0, 512, 256), 0, 0);
            map.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);

            // How does the map wrap around this mesh? Fit u = su*lon/2pi + ou, v = 0.5 + sv*lat/pi.
            float su = 1, ou = 0, sv = 1;
            Vector3[] verts = sphere.vertices;
            Vector2[] uvs = sphere.uv;
            if (verts != null && uvs != null && verts.Length == uvs.Length && verts.Length > 50)
                Fit(verts, uvs, out su, out ou, out sv);

            // The Mun's greys are shaded relative to its own average brightness.
            float mean = 0, sd = 1;
            if (style == CubeStyle.Moon) Stats(map, out mean, out sd);

            Texture2D outTex = NewFaces(px, "VSP_Cube" + style);
            System.Random rng = new System.Random(7);
            // Same face order and orientation as BoxMesh.
            Vector3[] n = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            Vector3[] us = { Vector3.forward, Vector3.back, Vector3.right, Vector3.left, Vector3.left, Vector3.right };
            Vector3[] vs = { Vector3.up, Vector3.up, Vector3.forward, Vector3.forward, Vector3.up, Vector3.up };
            for (int f = 0; f < 6; f++)
            {
                for (int y = 0; y < px; y++)
                {
                    for (int x = 0; x < px; x++)
                    {
                        Vector3 d = (n[f] + us[f] * (2f * (x + 0.5f) / px - 1f) + vs[f] * (2f * (y + 0.5f) / px - 1f)).normalized;
                        float lon = Mathf.Atan2(d.z, d.x), lat = Mathf.Asin(Mathf.Clamp(d.y, -1f, 1f));
                        float u = Frac(su * lon / (2 * Mathf.PI) + ou);
                        float v = Mathf.Clamp01(0.5f + sv * lat / Mathf.PI);
                        Color c = map.GetPixelBilinear(u, v);
                        Color o = style == CubeStyle.Moon ? Stone(c, mean, sd) : Earth(c, Mathf.Abs(lat));
                        outTex.SetPixel(f * px + x, y, Jitter(o, rng));
                    }
                }
            }
            outTex.Apply(false, false);
            UnityEngine.Object.Destroy(map);
            return outTex;
        }

        static void Fit(Vector3[] verts, Vector2[] uvs, out float bestSu, out float bestOu, out float bestSv)
        {
            bestSu = 1; bestOu = 0; bestSv = 1;
            int step = Math.Max(1, verts.Length / 400);
            float bestErr = float.MaxValue;
            for (int s = -1; s <= 1; s += 2)
            {
                for (int k = 0; k < 128; k++)
                {
                    float o = k / 128f, err = 0;
                    for (int i = 0; i < verts.Length; i += step)
                    {
                        Vector3 d = verts[i].normalized;
                        if (Mathf.Abs(d.y) > 0.9f) continue;
                        float u = Frac(s * Mathf.Atan2(d.z, d.x) / (2 * Mathf.PI) + o);
                        float e = Mathf.Abs(u - uvs[i].x);
                        err += Mathf.Min(e, 1 - e);
                    }
                    if (err < bestErr) { bestErr = err; bestSu = s; bestOu = o; }
                }
            }
            float e1 = 0, e2 = 0;
            for (int i = 0; i < verts.Length; i += step)
            {
                float lat = Mathf.Asin(Mathf.Clamp(verts[i].normalized.y, -1f, 1f));
                e1 += Mathf.Abs(0.5f + lat / Mathf.PI - uvs[i].y);
                e2 += Mathf.Abs(0.5f - lat / Mathf.PI - uvs[i].y);
            }
            bestSv = e1 <= e2 ? 1 : -1;
        }

        static void Stats(Texture2D map, out float mean, out float sd)
        {
            double sum = 0, sum2 = 0;
            int n = 0;
            for (int y = 0; y < map.height; y += 4)
                for (int x = 0; x < map.width; x += 4)
                {
                    float l = Lum(map.GetPixel(x, y));
                    sum += l;
                    sum2 += l * l;
                    n++;
                }
            mean = (float)(sum / n);
            sd = Mathf.Max(0.02f, Mathf.Sqrt((float)(sum2 / n - (sum / n) * (sum / n))));
        }

        static float Lum(Color c) { return c.r * 0.3f + c.g * 0.59f + c.b * 0.11f; }

        static float Frac(float x) { return x - Mathf.Floor(x); }

        static Color Jitter(Color o, System.Random rng)
        {
            float j = (float)(rng.NextDouble() - 0.5) * 0.06f;
            return new Color(Mathf.Clamp01(o.r + j), Mathf.Clamp01(o.g + j), Mathf.Clamp01(o.b + j), 1f);
        }

        // Real colours -> Minecraft blocks: water, grass, sand, snow, stone.
        static Color Earth(Color c, float absLat)
        {
            float lum = Lum(c);
            if (c.b > c.g * 0.95f && c.b > c.r * 1.1f) return lum < 0.25f ? new Color(0.20f, 0.33f, 0.80f) : new Color(0.25f, 0.42f, 0.88f);
            if (lum > 0.7f || absLat > 1.35f) return new Color(0.95f, 0.97f, 0.98f);
            if (c.r > c.g * 1.05f && lum > 0.45f) return new Color(0.86f, 0.82f, 0.62f);
            if (lum < 0.14f) return new Color(0.45f, 0.45f, 0.45f);
            return lum > 0.38f ? new Color(0.45f, 0.70f, 0.25f) : new Color(0.33f, 0.56f, 0.18f);
        }

        // Real colours -> four shades of stone, darkest in the craters.
        static Color Stone(Color c, float mean, float sd)
        {
            float z = (Lum(c) - mean) / sd;
            if (z < -1f) return new Color(0.36f, 0.36f, 0.38f);
            if (z < 0f) return new Color(0.50f, 0.50f, 0.52f);
            if (z < 1f) return new Color(0.63f, 0.63f, 0.65f);
            return new Color(0.76f, 0.76f, 0.78f);
        }
    }

    // Shows a cube (and hides the body's atmosphere glow) only for cameras far away, so the
    // real round world takes over up close. The Sun's cube is always shown.
    public class CubeBody : MonoBehaviour
    {
        public float localRadius = 1000f;
        public float farRadii = 3f;
        public bool alwaysOn;
        public Renderer[] atmosphere;
        MeshRenderer mr;
        static readonly List<CubeBody> all = new List<CubeBody>();

        void OnEnable() { all.Add(this); mr = GetComponent<MeshRenderer>(); }
        void OnDisable() { all.Remove(this); }

        public static void OnAnyPreCull(Camera cam)
        {
            if (cam == null) return;
            for (int i = 0; i < all.Count; i++)
            {
                CubeBody c = all[i];
                if (c == null || c.mr == null) continue;
                float worldR = c.localRadius * c.transform.lossyScale.x;
                bool far = c.alwaysOn || (cam.transform.position - c.transform.position).magnitude > worldR * c.farRadii;
                c.mr.enabled = far;
                if (c.atmosphere != null)
                    for (int k = 0; k < c.atmosphere.Length; k++)
                        if (c.atmosphere[k] != null) c.atmosphere[k].forceRenderingOff = far;
            }
        }
    }
}
