// Turns every part into a Minecraft-style block. Runs once at the main menu, after KSP has
// loaded all parts: each part prefab (and its editor icon) gets a tiled box sized to the
// original model, and the original meshes are hidden. Physics, colliders, drag and FX are
// untouched, so rockets fly exactly as before.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace VillagerSpaceProgram
{
    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public class BlockParts : MonoBehaviour
    {
        static readonly string[] Skip = { "kerbalEVA", "flag", "PotatoRoid", "PotatoComet" };

        void Start()
        {
            VSP.Log("shaders: " + VSPAssets.ShaderReport());
            StringBuilder report = new StringBuilder();
            int done = 0;
            foreach (AvailablePart ap in PartLoader.LoadedPartsList)
            {
                try
                {
                    string block;
                    Vector3 size;
                    if (Blockify(ap, out block, out size))
                    {
                        done++;
                        report.AppendLine(ap.name + "\t" + ap.category + "\t" + block + "\t" + size.ToString("F2"));
                    }
                }
                catch (Exception e)
                {
                    VSP.Warn("part " + ap.name + " failed: " + e);
                }
            }
            VSP.Log("turned " + done + " parts into blocks");
            if (VSP.DevMode)
            {
                Directory.CreateDirectory(VSP.PathIn("tour"));
                File.WriteAllText(VSP.PathIn("tour/parts.txt"), report.ToString());
            }
        }

        static bool Blockify(AvailablePart ap, out string block, out Vector3 size)
        {
            block = null;
            size = Vector3.zero;
            if (ap == null || ap.partPrefab == null) return false;
            foreach (string s in Skip)
                if (ap.name.StartsWith(s, StringComparison.OrdinalIgnoreCase)) return false;

            Part p = ap.partPrefab;
            Transform model = p.transform.Find("model");
            if (model == null) return false;
            block = ChooseBlock(ap, p);
            Bounds b;
            if (!AddBlock(model, p.transform, block, out b)) return false;
            size = b.size;

            if (ap.iconPrefab != null)
            {
                Bounds ib;
                AddBlock(ap.iconPrefab.transform, ap.iconPrefab.transform, block, out ib);
            }
            return true;
        }

        // Adds a VSP_Block under `holder`, sized to the visible meshes measured in `frame`.
        static bool AddBlock(Transform holder, Transform frame, string block, out Bounds bounds)
        {
            bounds = new Bounds();
            bool any = false;
            List<Renderer> hide = new List<Renderer>();
            int layer = holder.gameObject.layer;
            foreach (Renderer r in holder.GetComponentsInChildren<Renderer>(true))
            {
                if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;
                if (r.gameObject.name == "VSP_Block") return false;
                hide.Add(r);
                if (!r.enabled || !ActiveUpTo(r.transform, frame)) continue;
                Mesh mesh = MeshOf(r);
                if (mesh == null) continue;
                Matrix4x4 m = frame.worldToLocalMatrix * r.transform.localToWorldMatrix;
                Bounds mb = mesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 c = mb.center + Vector3.Scale(mb.extents, new Vector3(
                        (i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 w = m.MultiplyPoint3x4(c);
                    if (!any) { bounds = new Bounds(w, Vector3.zero); any = true; layer = r.gameObject.layer; }
                    else bounds.Encapsulate(w);
                }
            }
            if (!any || bounds.size.sqrMagnitude < 1e-6f) return false;

            GameObject go = new GameObject("VSP_Block");
            go.layer = layer;
            go.transform.SetParent(holder, false);
            go.transform.position = frame.position;
            go.transform.rotation = frame.rotation;
            Vector3 hs = holder.lossyScale, fs = frame.lossyScale;
            go.transform.localScale = new Vector3(Div(fs.x, hs.x), Div(fs.y, hs.y), Div(fs.z, hs.z));

            float cell = Median(bounds.size.x, bounds.size.y, bounds.size.z);
            go.AddComponent<MeshFilter>().sharedMesh = BoxMesh.Build(bounds.center, bounds.size, cell, Atlas.Block());
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = VSPAssets.Mat("block_" + block, VSPAssets.Kind.Opaque);
            if (holder == frame && hide.Count > 0 && hide[0].sharedMaterial != null)
            {
                // Editor icons use KSP's clipping icon shader; keep it so icons scroll and
                // light like the rest of the parts list.
                mr.sharedMaterial = VSPAssets.IconMat("block_" + block, hide[0].sharedMaterial);
            }

            HideOriginals h = go.AddComponent<HideOriginals>();
            h.targets = hide.ToArray();
            h.Apply();
            return true;
        }

        static bool ActiveUpTo(Transform t, Transform stop)
        {
            for (Transform c = t; c != null && c != stop; c = c.parent)
                if (!c.gameObject.activeSelf) return false;
            return true;
        }

        static Mesh MeshOf(Renderer r)
        {
            SkinnedMeshRenderer smr = r as SkinnedMeshRenderer;
            if (smr != null) return smr.sharedMesh;
            MeshFilter mf = r.GetComponent<MeshFilter>();
            return mf != null ? mf.sharedMesh : null;
        }

        static float Div(float a, float b) { return Mathf.Abs(b) < 1e-6f ? 1f : a / b; }

        static float Median(float a, float b, float c)
        {
            return Mathf.Max(Mathf.Min(a, b), Mathf.Min(Mathf.Max(a, b), c));
        }

        static bool Has(Part p, string module)
        {
            if (p.Modules == null) return false;
            for (int i = 0; i < p.Modules.Count; i++)
                if (p.Modules[i] != null && p.Modules[i].moduleName == module) return true;
            return false;
        }

        static bool HasRes(Part p, string res)
        {
            if (p.Resources == null) return false;
            for (int i = 0; i < p.Resources.Count; i++)
                if (p.Resources[i].resourceName == res) return true;
            return false;
        }

        // What each part becomes. Behaviour first (what the part does), then category.
        public static string ChooseBlock(AvailablePart ap, Part p)
        {
            bool engine = Has(p, "ModuleEngines") || Has(p, "ModuleEnginesFX");
            if (engine) return HasRes(p, "SolidFuel") ? "tnt" : "furnace";
            if (Has(p, "ModuleAblator")) return "obsidian";
            if (Has(p, "ModuleCommand")) return p.CrewCapacity > 0 ? "oak_planks" : "observer";
            if (Has(p, "ModuleParachute")) return "white_wool";
            if (Has(p, "ModuleDeployableSolarPanel")) return "daylight_detector";
            if (Has(p, "ModuleScienceLab") || Has(p, "ModuleScienceExperiment")) return "bookshelf";
            if (Has(p, "ModuleActiveRadiator") || Has(p, "ModuleDeployableRadiator")) return "packed_ice";
            if (Has(p, "ModuleLight") || Has(p, "ModuleColorChanger") && ap.category == PartCategories.Utility) return "glowstone";
            if (Has(p, "ModuleRCS") || Has(p, "ModuleRCSFX")) return "dispenser";
            if (Has(p, "ModuleReactionWheel")) return "redstone_lamp";
            if (Has(p, "ModuleDataTransmitter")) return "note_block";
            if (Has(p, "ModuleDecouple") || Has(p, "ModuleAnchoredDecoupler") || Has(p, "ModuleDockingNode")) return "smooth_stone";
            if (Has(p, "ModuleLandingLeg") || Has(p, "ModuleWheelBase")) return "oak_log";
            if (Has(p, "ModuleCargoBay") || Has(p, "ModuleInventoryPart") && ap.category == PartCategories.Cargo) return "chest";
            if (Has(p, "ModuleRoboticServoPiston") || Has(p, "ModuleRoboticServoHinge") || Has(p, "ModuleRoboticRotationServo")) return "piston";

            if (HasRes(p, "XenonGas")) return "diamond_block";
            if (HasRes(p, "MonoPropellant")) return "gold_block";
            if (HasRes(p, "Ore")) return "iron_ore";
            if (HasRes(p, "LiquidFuel") && HasRes(p, "Oxidizer")) return "iron_block";
            if (HasRes(p, "LiquidFuel")) return "copper_block";
            if (HasRes(p, "ElectricCharge") && ap.category == PartCategories.Electrical) return "redstone_block";

            switch (ap.category)
            {
                case PartCategories.Aero: return "quartz_block";
                case PartCategories.Structural: return "stone_bricks";
                case PartCategories.Robotics: return "piston";
                case PartCategories.Ground: return "oak_log";
                case PartCategories.Cargo:
                case PartCategories.Payload: return "chest";
                case PartCategories.Thermal: return "obsidian";
                case PartCategories.Science: return "bookshelf";
                case PartCategories.Communication: return "note_block";
                case PartCategories.Electrical: return "redstone_block";
                case PartCategories.Coupling: return "smooth_stone";
                case PartCategories.Control: return "redstone_lamp";
                case PartCategories.Pods: return "oak_planks";
                case PartCategories.FuelTank:
                case PartCategories.Propulsion: return "iron_block";
                default: return "cobblestone";
            }
        }
    }

    // Keeps the original meshes invisible without touching renderer.enabled, which KSP's
    // own modules switch on and off. The array survives Instantiate, so every copy of the
    // part re-applies it on Start, and again every second for meshes KSP re-enables.
    public class HideOriginals : MonoBehaviour
    {
        public Renderer[] targets;
        float next;

        void Start() { Apply(); }

        void Update()
        {
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + 1f;
            Apply();
        }

        public void Apply()
        {
            if (targets == null) return;
            for (int i = 0; i < targets.Length; i++)
                if (targets[i] != null && !targets[i].forceRenderingOff) targets[i].forceRenderingOff = true;
        }
    }
}
