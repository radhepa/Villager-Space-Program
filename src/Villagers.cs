// Every Kerbal becomes a Minecraft-style villager.
//
// The villager is a handful of boxes (head, nose, robe, crossed arms, legs, glass helmet),
// sized from the Kerbal's own skeleton: legs reach the hips, the robe runs up to the chin,
// and the head is as big as the Kerbal's head. Each box follows a bone (hips, spine, head
// pivot) every frame using the skinned meshes' bind poses, so it moves with KSP's walk,
// swim, seat and ragdoll animations whatever pose the Kerbal is in when we find it.
//
// Kerbal meshes are hidden with forceRenderingOff, which leaves renderer.enabled to KSP:
// a villager part only shows while the Kerbal mesh it stands in for would (helmet on/off,
// your own head in IVA first person, ...). Jetpacks, chutes and cargo stay visible.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace VillagerSpaceProgram
{
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class Villagers : MonoBehaviour
    {
        float next, nextLoose;

        void Awake() { DontDestroyOnLoad(gameObject); }

        void Update()
        {
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + 0.5f;
            try
            {
                foreach (KerbalEVA eva in FindObjectsOfType<KerbalEVA>())
                    VillagerRig.Ensure(eva.transform, eva.part, null, eva);
                foreach (Kerbal k in FindObjectsOfType<Kerbal>())
                    VillagerRig.Ensure(k.transform, null, k, null);

                // Kerbals with no game logic: the crowd at the KSC and in the VAB, the
                // main menu, the astronaut complex.
                if (Time.unscaledTime >= nextLoose)
                {
                    nextLoose = Time.unscaledTime + 2f;
                    foreach (SkinnedMeshRenderer s in FindObjectsOfType<SkinnedMeshRenderer>())
                    {
                        if (s.sharedMesh == null || !s.name.StartsWith("body", StringComparison.OrdinalIgnoreCase)) continue;
                        if (s.transform.parent == null || s.GetComponentInParent<VillagerRig>() != null) continue;
                        if (s.GetComponentInParent<KerbalEVA>() != null || s.GetComponentInParent<Kerbal>() != null) continue;
                        if (!LooksLikeKerbal(s)) continue;
                        VillagerRig.Ensure(s.transform.parent, null, null, null);
                    }
                }
            }
            catch (Exception e)
            {
                VSP.Warn("villager scan: " + e);
            }
        }

        static bool LooksLikeKerbal(SkinnedMeshRenderer s)
        {
            if (s.bones == null) return false;
            bool hip = false, spine = false;
            foreach (Transform b in s.bones)
            {
                if (b == null) continue;
                if (b.name.Contains("_hip")) hip = true;
                if (b.name.StartsWith("bn_sp")) spine = true;
            }
            return hip && spine;
        }
    }

    public class VillagerSkip : MonoBehaviour { }

    public class VillagerRig : MonoBehaviour
    {
        public Part part;
        public Kerbal kerbal;
        public KerbalEVA eva;
        public Renderer[] originals;
        public Renderer bodySource, headSource, helmetSource;
        public MeshRenderer[] bodyPieces, headPieces;
        public MeshRenderer helmetPiece;
        string job;

        static readonly string[] HideNames =
        {
            "body", "head", "neck", "eye", "pupil", "teeth", "tongue", "helmet", "visor",
            "hair", "lash", "brow", "mouth", "jaw"
        };

        public static VillagerRig Ensure(Transform root, Part part, Kerbal kerbal, KerbalEVA eva)
        {
            VillagerRig rig = root.GetComponent<VillagerRig>();
            if (rig != null || root.GetComponent<VillagerSkip>() != null) return rig;
            rig = root.gameObject.AddComponent<VillagerRig>();
            rig.part = part;
            rig.kerbal = kerbal;
            rig.eva = eva;
            bool ok = false;
            try { ok = rig.Build(); }
            catch (Exception e) { VSP.Warn("villager build failed on " + root.name + ": " + e); }
            if (!ok)
            {
                DestroyImmediate(rig);
                root.gameObject.AddComponent<VillagerSkip>();
                return null;
            }
            return rig;
        }

        static bool IsKerbalPart(string name)
        {
            string n = name.ToLowerInvariant();
            foreach (string h in HideNames)
                if (n.Contains(h)) return true;
            return false;
        }

        // A bone, and the matrix taking body-mesh space to that bone's local space.
        struct Bone
        {
            public Transform t;
            public Matrix4x4 fromBody;
            public Vector3 pos;
        }

        // The way the villager faces (+Z) and its up (+Y), following the skeleton.
        public Transform Facing
        {
            get { return bodyPieces != null && bodyPieces.Length > 2 && bodyPieces[2] != null ? bodyPieces[2].transform : null; }
        }

        bool Build()
        {
            // EVA Kerbals carry several suit models with only one switched on, so build on
            // the active one (and rebuild if KSP swaps suits, see LateUpdate).
            SkinnedMeshRenderer body = null, head = null, helmet = null;
            List<Renderer> hide = new List<Renderer>();
            for (int pass = 0; pass < 2 && body == null; pass++)
            {
                hide.Clear();
                head = null;
                helmet = null;
                foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
                {
                    if (!(r is SkinnedMeshRenderer) && !(r is MeshRenderer)) continue;
                    if (r.name.StartsWith("VSP_") || !IsKerbalPart(r.name)) continue;
                    hide.Add(r);
                    SkinnedMeshRenderer s = r as SkinnedMeshRenderer;
                    if (s == null || s.sharedMesh == null || s.bones == null || s.bones.Length == 0) continue;
                    if (pass == 0 && !s.gameObject.activeInHierarchy) continue;
                    string n = s.name.ToLowerInvariant();
                    if (n.StartsWith("body") && (body == null || s.sharedMesh.vertexCount > body.sharedMesh.vertexCount)) body = s;
                    else if (n.StartsWith("headmesh") && head == null) head = s;
                    else if (n == "helmet" && helmet == null) helmet = s;
                }
            }
            if (eva != null && eva.helmetMesh != null) helmet = eva.helmetMesh;
            if (body == null) return false;
            originals = hide.ToArray();

            // Every bone of every Kerbal mesh in the same suit, expressed against the body
            // mesh's space.
            Dictionary<Transform, Bone> bones = new Dictionary<Transform, Bone>();
            foreach (Renderer r in hide)
            {
                SkinnedMeshRenderer s = r as SkinnedMeshRenderer;
                if (s == null || s.sharedMesh == null || s.bones == null) continue;
                if (s.gameObject.activeInHierarchy != body.gameObject.activeInHierarchy) continue;
                Matrix4x4 toS = s.transform.worldToLocalMatrix * body.transform.localToWorldMatrix;
                Matrix4x4[] bp = s.sharedMesh.bindposes;
                for (int i = 0; i < s.bones.Length && i < bp.Length; i++)
                {
                    Transform t = s.bones[i];
                    if (t == null || bones.ContainsKey(t)) continue;
                    Bone b;
                    b.t = t;
                    b.fromBody = bp[i] * toS;
                    b.pos = b.fromBody.inverse.MultiplyPoint3x4(Vector3.zero);
                    bones[t] = b;
                }
            }

            // Measure the Kerbal (body mesh space: +Y up, +Z the way it faces).
            Bounds bb = body.sharedMesh.bounds;
            float feet = bb.min.y;
            float top = bb.max.y, headBottom = -1f;
            if (head != null)
            {
                Bounds hb = InBodySpace(head, body);
                top = Mathf.Max(top, hb.max.y);
                headBottom = hb.min.y;
            }
            float hipY = 0f, hipZ = 0f;
            int hips = 0;
            foreach (Bone b in bones.Values)
                if (b.t.name.Contains("_hip")) { hipY += b.pos.y; hipZ += b.pos.z; hips++; }
            if (hips > 0) { hipY /= hips; hipZ /= hips; }
            else { hipY = feet + (top - feet) * 0.25f; hipZ = bb.center.z; }
            if (headBottom < 0f || headBottom <= hipY) headBottom = feet + (top - feet) * 0.5f;

            float hh = top - headBottom;           // villager head is 10 px tall
            float px = hh / 10f;
            float z = hipZ;
            float robeBottom = feet + (hipY - feet) * 0.35f;

            Bone hipL, hipR, spine, neck;
            if (!Find(bones, "l_hip", new Vector3(-2 * px, hipY, z), out hipL)) return false;
            Find(bones, "r_hip", new Vector3(2 * px, hipY, z), out hipR);
            Find(bones, "spc", new Vector3(0, (hipY + headBottom) * 0.5f, z), out spine);
            if (!Find(bones, "headpivot_a", new Vector3(0, headBottom, z), out neck))
                Find(bones, "neck", new Vector3(0, headBottom, z), out neck);

            string tex = "villager_" + JobOf();
            job = JobOf();
            int bodyLayer = body.gameObject.layer;
            int headLayer = head != null ? head.gameObject.layer : bodyLayer;

            List<MeshRenderer> bodyList = new List<MeshRenderer>();
            List<MeshRenderer> headList = new List<MeshRenderer>();
            float legH = hipY - feet;
            bodyList.Add(Piece("VSP_LegL", hipL, new Vector3(-2 * px, feet + legH / 2, z), new Vector3(4 * px, legH, 4 * px), 7, 7, 7, bodyLayer, tex));
            bodyList.Add(Piece("VSP_LegR", hipR, new Vector3(2 * px, feet + legH / 2, z), new Vector3(4 * px, legH, 4 * px), 7, 7, 7, bodyLayer, tex));
            float robeH = headBottom - robeBottom;
            bodyList.Add(Piece("VSP_Robe", spine, new Vector3(0, robeBottom + robeH / 2, z), new Vector3(8 * px, robeH, 6 * px), 4, 5, 5, bodyLayer, tex));
            bodyList.Add(Piece("VSP_Arms", spine, new Vector3(0, headBottom - 3.5f * px, z + 4.6f * px), new Vector3(9 * px, 4 * px, 4 * px), 6, 6, 6, bodyLayer, tex));

            Vector3 headC = new Vector3(0, headBottom + hh / 2, z);
            headList.Add(Piece("VSP_Head", neck, headC, new Vector3(8 * px, hh, 8 * px), 0, 1, 2, headLayer, tex));
            headList.Add(Piece("VSP_Nose", neck, new Vector3(0, headBottom + 3 * px, z + 5 * px), new Vector3(2 * px, 4 * px, 2 * px), 3, 3, 3, headLayer, tex));
            helmetPiece = Piece("VSP_Helmet", neck, headC, new Vector3(10.5f * px, hh + 2 * px, 10.5f * px), -1, -1, -1, headLayer, "glass");

            bodyPieces = bodyList.ToArray();
            headPieces = headList.ToArray();
            bodySource = body;
            headSource = head != null ? (Renderer)head : body;
            helmetSource = helmet;
            Refresh();
            return true;
        }

        static Bounds InBodySpace(SkinnedMeshRenderer s, SkinnedMeshRenderer body)
        {
            Matrix4x4 m = body.transform.worldToLocalMatrix * s.transform.localToWorldMatrix;
            Bounds src = s.sharedMesh.bounds;
            Bounds b = new Bounds(m.MultiplyPoint3x4(src.center), Vector3.zero);
            for (int i = 0; i < 8; i++)
                b.Encapsulate(m.MultiplyPoint3x4(src.center + Vector3.Scale(src.extents, new Vector3(
                    (i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
            return b;
        }

        // Prefer a bone whose name contains `key`; otherwise the bone nearest `near`.
        static bool Find(Dictionary<Transform, Bone> bones, string key, Vector3 near, out Bone best)
        {
            best = new Bone();
            bool found = false;
            float bestD = float.MaxValue;
            foreach (Bone b in bones.Values)
            {
                if (b.t.name.ToLowerInvariant().Contains(key))
                {
                    float d = (b.pos - near).sqrMagnitude;
                    if (!found || d < bestD) { best = b; bestD = d; found = true; }
                }
            }
            if (found) return true;
            foreach (Bone b in bones.Values)
            {
                float d = (b.pos - near).sqrMagnitude;
                if (d < bestD) { best = b; bestD = d; found = true; }
            }
            return found;
        }

        string JobOf()
        {
            ProtoCrewMember pcm = null;
            if (kerbal != null) pcm = kerbal.protoCrewMember;
            if (pcm == null && part != null && part.protoModuleCrew != null && part.protoModuleCrew.Count > 0)
                pcm = part.protoModuleCrew[0];
            if (pcm == null || string.IsNullOrEmpty(pcm.trait)) return "villager";
            switch (pcm.trait)
            {
                case "Pilot": return "pilot";
                case "Engineer": return "engineer";
                case "Scientist": return "scientist";
                case "Tourist": return "tourist";
                default: return "villager";
            }
        }

        // Pieces follow bones without being their children: KSP switches the bone objects
        // off whenever the Kerbal isn't ragdolling (the bones carry the ragdoll physics),
        // which would hide anything parented to them.
        readonly List<Transform> pieces = new List<Transform>();
        readonly List<Transform> pieceBones = new List<Transform>();
        readonly List<Matrix4x4> pieceFrom = new List<Matrix4x4>();

        // Builds a box (given in body-mesh space) that follows `bone`.
        MeshRenderer Piece(string name, Bone bone, Vector3 center, Vector3 size, int front, int side, int top, int layer, string tex)
        {
            if (bone.t == null) return null;
            GameObject go = new GameObject(name);
            go.layer = layer;
            go.transform.SetParent(transform, false);
            pieces.Add(go.transform);
            pieceBones.Add(bone.t);
            pieceFrom.Add(bone.fromBody);
            Pose(go.transform, bone.t, bone.fromBody);

            Rect[] uv;
            if (front < 0)
            {
                Rect all = new Rect(0, 0, 1, 1);
                uv = new Rect[] { all, all, all, all, all, all };
            }
            else
            {
                Rect f = Atlas.Cell(front % 4, front / 4, 4, 4, 64, 64);
                Rect s = Atlas.Cell(side % 4, side / 4, 4, 4, 64, 64);
                Rect t = Atlas.Cell(top % 4, top / 4, 4, 4, 64, 64);
                uv = new Rect[] { s, s, t, s, f, s };
            }
            go.AddComponent<MeshFilter>().sharedMesh = BoxMesh.Build(center, size, 0f, uv);
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = VSPAssets.Mat(tex, tex == "glass" ? VSPAssets.Kind.Translucent : VSPAssets.Kind.Opaque);
            return mr;
        }

        float nextSuitCheck;

        void LateUpdate()
        {
            if (Time.unscaledTime >= nextSuitCheck)
            {
                nextSuitCheck = Time.unscaledTime + 1f;
                if (bodySource != null && !bodySource.gameObject.activeInHierarchy && OtherBodyActive())
                {
                    Teardown();
                    if (!Build()) { enabled = false; return; }
                }
            }
            for (int i = 0; i < pieces.Count; i++)
                if (pieces[i] != null && pieceBones[i] != null) Pose(pieces[i], pieceBones[i], pieceFrom[i]);
            Refresh();
        }

        // piece = bone (current pose) * bone-from-body (bind pose) * body-space box.
        static void Pose(Transform piece, Transform bone, Matrix4x4 fromBody)
        {
            Matrix4x4 m = bone.localToWorldMatrix * fromBody;
            piece.position = m.GetColumn(3);
            piece.rotation = Quaternion.LookRotation(m.GetColumn(2), m.GetColumn(1));
            Vector3 world = new Vector3(m.GetColumn(0).magnitude, m.GetColumn(1).magnitude, m.GetColumn(2).magnitude);
            Vector3 ps = piece.parent != null ? piece.parent.lossyScale : Vector3.one;
            piece.localScale = new Vector3(world.x / ps.x, world.y / ps.y, world.z / ps.z);
        }

        bool OtherBodyActive()
        {
            foreach (SkinnedMeshRenderer s in GetComponentsInChildren<SkinnedMeshRenderer>(false))
                if (s != bodySource && s.name.StartsWith("body", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        void Teardown()
        {
            DestroyPieces(bodyPieces);
            DestroyPieces(headPieces);
            if (helmetPiece != null) Destroy(helmetPiece.gameObject);
            bodyPieces = headPieces = null;
            helmetPiece = null;
            pieces.Clear();
            pieceBones.Clear();
            pieceFrom.Clear();
        }

        static void DestroyPieces(MeshRenderer[] rs)
        {
            if (rs == null) return;
            foreach (MeshRenderer r in rs)
                if (r != null) Destroy(r.gameObject);
        }

        void Refresh()
        {
            if (originals != null)
                for (int i = 0; i < originals.Length; i++)
                    if (originals[i] != null && !originals[i].forceRenderingOff) originals[i].forceRenderingOff = true;

            bool bodyOn = Visible(bodySource);
            bool headOn = Visible(headSource);
            bool helmetOn = helmetSource != null && Visible(helmetSource) && headOn;
            if (kerbal != null && !kerbal.showHelmet) helmetOn = false;

            Show(bodyPieces, bodyOn);
            Show(headPieces, headOn);
            if (helmetPiece != null && helmetPiece.enabled != helmetOn) helmetPiece.enabled = helmetOn;

            string j = JobOf();
            if (j != job)
            {
                job = j;
                Material m = VSPAssets.Mat("villager_" + j, VSPAssets.Kind.Opaque);
                SetMat(bodyPieces, m);
                SetMat(headPieces, m);
            }
        }

        static bool Visible(Renderer r)
        {
            return r != null && r.enabled && r.gameObject.activeInHierarchy;
        }

        static void Show(MeshRenderer[] rs, bool on)
        {
            if (rs == null) return;
            for (int i = 0; i < rs.Length; i++)
                if (rs[i] != null && rs[i].enabled != on) rs[i].enabled = on;
        }

        static void SetMat(MeshRenderer[] rs, Material m)
        {
            if (rs == null) return;
            for (int i = 0; i < rs.Length; i++)
                if (rs[i] != null) rs[i].sharedMaterial = m;
        }
    }
}
