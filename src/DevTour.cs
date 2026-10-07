// Developer tour, used to check the mod without clicking through the game by hand.
// Only runs when GameData/VillagerSpaceProgram/PluginData/devtour.txt exists (never shipped).
// It makes its own sandbox save "VSP_Test" and never touches other saves. It then:
// main menu -> VAB with a stock rocket -> launch pad -> villager EVA -> 100 km orbit -> quit,
// writing screenshots and rig dumps to PluginData/tour/.
using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

namespace VillagerSpaceProgram
{
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class DevTour : MonoBehaviour
    {
        const string SaveName = "VSP_Test";
        string outDir, craft;
        bool started;
        string steps = "";

        void Awake()
        {
            if (!VSP.DevMode) { Destroy(this); return; }
            DontDestroyOnLoad(gameObject);
            outDir = VSP.PathIn("tour");
            Directory.CreateDirectory(outDir);
            string cfg = File.ReadAllText(VSP.PathIn("devtour.txt")).Trim();
            foreach (string line in cfg.Split('\n'))
            {
                string l = line.Trim();
                if (l.StartsWith("craft=")) craft = l.Substring(6);
                if (l.StartsWith("steps=")) steps = l.Substring(6);
            }
            if (string.IsNullOrEmpty(craft)) craft = "Kerbal X";
            GameEvents.onLevelWasLoadedGUIReady.Add(OnScene);
            Note("armed, craft=" + craft);
        }

        void Note(string s)
        {
            VSP.Log("tour: " + s);
            File.AppendAllText(Path.Combine(outDir, "tour.log"), DateTime.Now.ToString("HH:mm:ss") + " " + s + "\n");
        }

        void Windowed()
        {
            if (Screen.fullScreen) Screen.SetResolution(1280, 720, false);
        }

        IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            string p = Path.Combine(outDir, name + ".png");
            ScreenCapture.CaptureScreenshot(p);
            yield return null;
            yield return null;
            Note("shot " + name);
        }

        void OnScene(GameScenes scene)
        {
            Windowed();
            Note("scene " + scene);
            if (scene == GameScenes.MAINMENU && !started) { started = true; StartCoroutine(MainMenu()); }
            else if (scene == GameScenes.SPACECENTER) StartCoroutine(SpaceCenter());
            else if (scene == GameScenes.EDITOR) StartCoroutine(Editor());
            else if (scene == GameScenes.FLIGHT) StartCoroutine(Flight());
        }

        string CraftPath { get { return Path.Combine(KSPUtil.ApplicationRootPath, "Ships/VAB/" + craft + ".craft"); } }

        IEnumerator MainMenu()
        {
            yield return new WaitForSecondsRealtime(5f);
            try { DumpEvaPrefabs(); } catch (Exception e) { Note("eva dump failed " + e); }
            yield return StartCoroutine(Shot("01_mainmenu"));
            yield return new WaitForSecondsRealtime(1f);

            string dir = Path.Combine(KSPUtil.ApplicationRootPath, "saves/" + SaveName);
            Directory.CreateDirectory(dir);
            Game g = GamePersistence.CreateNewGame(SaveName, Game.Modes.SANDBOX,
                GameParameters.GetDefaultParameters(Game.Modes.SANDBOX, GameParameters.Preset.Normal),
                "Squad/Flags/default", GameScenes.SPACECENTER, EditorFacility.VAB);
            HighLogic.SaveFolder = SaveName;
            GamePersistence.SaveGame(g, "persistent", SaveName, SaveMode.OVERWRITE);
            HighLogic.CurrentGame = g;
            Note("starting test game");
            g.Start();
        }

        bool editorDone;

        IEnumerator SpaceCenter()
        {
            yield return new WaitForSecondsRealtime(6f);
            yield return StartCoroutine(Shot("02_ksc"));
            if (editorDone) yield break;
            editorDone = true;
            Note("editor with " + CraftPath);
            EditorDriver.StartAndLoadVessel(CraftPath, EditorFacility.VAB);
        }

        bool launched, flown;

        IEnumerator Editor()
        {
            yield return new WaitForSecondsRealtime(6f);
            yield return StartCoroutine(Shot("03_vab"));
            if (launched) yield break;
            launched = true;
            ConfigNode n = ConfigNode.Load(CraftPath);
            VesselCrewManifest crew = HighLogic.CurrentGame.CrewRoster.DefaultCrewForVessel(n, null, true, false);
            Note("launching, crew " + crew.CrewCount);
            FlightDriver.StartWithNewLaunch(CraftPath, "Squad/Flags/default", "LaunchPad", crew);
        }

        static bool InFlight { get { return HighLogic.LoadedScene == GameScenes.FLIGHT; } }
        Vector3 spaceDir, spaceUp;

        IEnumerator Flight()
        {
            if (flown) yield break;
            flown = true;
            yield return new WaitForSecondsRealtime(10f);
            Vessel v = FlightGlobals.ActiveVessel;
            if (v == null) { Note("no vessel"); yield break; }
            FlightCamera.fetch.SetDistance(Mathf.Max(18f, v.vesselSize.magnitude * 1.4f));
            yield return new WaitForSecondsRealtime(2f);
            yield return StartCoroutine(Shot("04_pad"));
            try { DumpKerbals("dump_iva.txt"); } catch (Exception e) { Note("iva dump failed " + e); }

            yield return StartCoroutine(Eva(v, "05_eva_pad"));
            if (!InFlight) { Note("left flight, stopping"); yield break; }

            // Into a 100 km orbit of Kerbin (the stock cheat menu's Set Orbit).
            // Try spots around the orbit and keep the one on Kerbin's day side, measured
            // from where the ship really ends up.
            CelestialBody kb = FlightGlobals.GetBodyByName("Kerbin");
            CelestialBody sun = FlightGlobals.GetBodyByName("Sun");
            int kerbin = FlightGlobals.Bodies.IndexOf(kb);
            // Aim for the Sun about 45 degrees up: overhead light only grazes the rocket.
            double bestM = 0, bestErr = 9;
            for (int i = 0; i < 8 && InFlight; i++)
            {
                double m = i * Math.PI / 4;
                FlightGlobals.fetch.SetShipOrbit(kerbin, 0.0, 700000.0, 0.0, 90.0, m, 0.0, Planetarium.GetUniversalTime());
                yield return new WaitForSecondsRealtime(2f);
                Vector3d toSun = (sun.position - kb.position).normalized;
                double d = Vector3d.Dot((v.GetWorldPos3D() - kb.position).normalized, toSun);
                Note("orbit try m=" + m.ToString("F2") + " sun dot " + d.ToString("F2"));
                if (Math.Abs(d - 0.7) < bestErr) { bestErr = Math.Abs(d - 0.7); bestM = m; }
            }
            FlightGlobals.fetch.SetShipOrbit(kerbin, 0.0, 700000.0, 0.0, 90.0, bestM, 0.0, Planetarium.GetUniversalTime());
            yield return new WaitForSecondsRealtime(5f);
            if (!InFlight) { Note("left flight, stopping"); yield break; }

            // Lay the rocket along the horizon, broadside to the Sun, and look from the lit side.
            Vector3 radial = ((Vector3)(v.GetWorldPos3D() - kb.position)).normalized;
            Vector3 toSunW = ((Vector3)(sun.position - v.GetWorldPos3D())).normalized;
            Vector3 sunFlat = Vector3.ProjectOnPlane(toSunW, radial).normalized;
            Vector3 axis = Vector3.Cross(radial, sunFlat).normalized;
            v.SetRotation(Quaternion.LookRotation(sunFlat, axis));
            if (v.rootPart != null && v.rootPart.Rigidbody != null) v.rootPart.Rigidbody.angularVelocity = Vector3.zero;
            yield return new WaitForSecondsRealtime(3f);
            FlightCamera.SetTarget(v);
            float d0 = Mathf.Max(20f, v.vesselSize.magnitude * 1.5f);
            FlightCamera.fetch.SetDistance(d0);
            for (int i = 0; i < 45 && InFlight; i++)
            {
                FlightCamera.fetch.SetCamCoordsFromPosition(FlightCamera.fetch.GetPivot().position
                    + (sunFlat + radial * 0.3f + axis * 0.45f).normalized * d0);
                yield return null;
            }
            yield return new WaitForSecondsRealtime(1f);
            yield return StartCoroutine(Shot("06_orbit"));

            spaceDir = sunFlat;
            spaceUp = radial;
            yield return StartCoroutine(Eva(v, "07_eva_space"));
            FlightCamera.SetTarget(v);
            if (!InFlight) { Note("left flight, stopping"); yield break; }

            yield return StartCoroutine(LookAt("Mun", "08_mun"));
            yield return StartCoroutine(LookAt("Sun", "09_sun"));

            // Map view, zoomed out: cube Kerbin.
            if (InFlight)
            {
                MapView.EnterMapView();
                yield return new WaitForSecondsRealtime(2f);
                PlanetariumCamera pc = PlanetariumCamera.fetch;
                pc.SetTarget(kb);
                yield return new WaitForSecondsRealtime(1f);
                pc.SetDistance(Mathf.Min(pc.maxDistance, (float)kb.Radius / 6000f * 6f));
                pc.camPitch = 0.45f;
                pc.camHdg = 0.6f;
                yield return new WaitForSecondsRealtime(3f);
                yield return StartCoroutine(Shot("10_map_kerbin"));
                pc.SetDistance(Mathf.Min(pc.maxDistance, (float)kb.Radius / 6000f * 40f));
                yield return new WaitForSecondsRealtime(3f);
                yield return StartCoroutine(Shot("11_map_far"));
                MapView.ExitMapView();
            }
            string names = "";
            foreach (ProtoCrewMember pcm in HighLogic.CurrentGame.CrewRoster.Crew) names += pcm.name + ", ";
            Note("crew: " + names);

            Note("done");
            File.WriteAllText(Path.Combine(outDir, "done.txt"), "ok");
            yield return new WaitForSecondsRealtime(1f);
            Application.Quit();
        }

        // A villager steps out, lets go of the ladder and is placed a few metres from the
        // rocket facing away from it, so the camera sees its face with the rocket behind.
        // The villager stays behind (on the pad); the next EVA uses the next crew member.
        IEnumerator Eva(Vessel v, string shot)
        {
            Part pod = null;
            foreach (Part p in v.parts) if (p.protoModuleCrew.Count > 0) { pod = p; break; }
            if (pod == null) { Note("no crew for eva"); yield break; }
            KerbalEVA k = FlightEVA.fetch.spawnEVA(pod.protoModuleCrew[0], pod, pod.airlock, true);
            Note("eva " + (k != null));
            if (k == null) yield break;
            yield return new WaitForSecondsRealtime(3f);
            FlightGlobals.ForceSetActiveVessel(k.vessel);
            yield return new WaitForSecondsRealtime(2f);
            if (k.OnALadder) k.fsm.RunEvent(k.On_ladderLetGo);
            yield return new WaitForSecondsRealtime(0.5f);

            Vector3 up = ((Vector3)(k.vessel.CoMD - v.mainBody.position)).normalized;
            Vector3 axis = pod.transform.position;
            Vector3 outDir = Vector3.ProjectOnPlane(k.transform.position - axis, up);
            if (outDir.sqrMagnitude < 1e-4f) outDir = Vector3.ProjectOnPlane(pod.transform.forward, up);
            outDir.Normalize();
            if (!v.LandedOrSplashed && spaceDir.sqrMagnitude > 0.5f)
            {
                // In orbit: float out on the sunny side, facing the Sun.
                outDir = spaceDir;
                up = spaceUp;
            }
            VillagerRig rig = k.GetComponent<VillagerRig>();
            Transform body = rig != null && rig.Facing != null ? rig.Facing : k.transform;
            if (v.LandedOrSplashed)
            {
                // Put it down beside the rocket and wait until it is standing idle.
                float low = float.MaxValue;
                foreach (Part p in v.parts) low = Mathf.Min(low, Vector3.Dot(p.transform.position - axis, up));
                Vector3 spot = axis + up * low + outDir * 6f;
                RaycastHit hit;
                if (Physics.Raycast(spot + up * 8f, -up, out hit, 30f, (1 << 15) | (1 << 28)))
                    spot = hit.point;
                Note("ground at " + Vector3.Dot(spot - axis, up).ToString("F1") + " m below pod (lowest part " + low.ToString("F1") + ")");
                k.vessel.SetPosition(spot + up * 0.15f);
                if (k.part.Rigidbody != null) k.part.Rigidbody.velocity = Vector3.zero;
                for (int i = 0; i < 40 && InFlight; i++)
                {
                    yield return new WaitForSecondsRealtime(0.5f);
                    if (k.fsm.currentStateName != null && k.fsm.currentStateName.StartsWith("Idle (Grounded")) break;
                }
                Note("eva state " + k.fsm.currentStateName);
            }
            else
            {
                // In orbit: drift gently out from the hatch towards the Sun.
                if (k.part.Rigidbody != null && pod.Rigidbody != null)
                {
                    k.part.Rigidbody.velocity = pod.Rigidbody.velocity + outDir * 1.2f;
                    k.part.Rigidbody.angularVelocity = Vector3.zero;
                }
                yield return new WaitForSecondsRealtime(3f);
                if (k.part.Rigidbody != null && pod.Rigidbody != null) k.part.Rigidbody.velocity = pod.Rigidbody.velocity;
            }
            // The villager's face is +Z of the Kerbal body mesh; turn that away from the rocket.
            Quaternion now = Quaternion.LookRotation(body.forward, body.up);
            Quaternion want = Quaternion.LookRotation(outDir, up);
            k.vessel.SetRotation(want * Quaternion.Inverse(now) * k.vessel.transform.rotation);
            if (k.part.Rigidbody != null) k.part.Rigidbody.angularVelocity = Vector3.zero;
            yield return new WaitForSecondsRealtime(1.5f);
            Note("eva " + shot + ": " + (k.transform.position - axis).magnitude.ToString("F1") + " m from pod, facing dot "
                + Vector3.Dot(body.forward, outDir).ToString("F2") + ", state " + k.fsm.currentStateName);

            // On the ground KSP lifts a low camera off the floor, so stand further back.
            FlightCamera cam = FlightCamera.fetch;
            bool ground = v.LandedOrSplashed;
            yield return StartCoroutine(Frame(cam, k, body, up, 0.25f, ground ? 3.6f : 2.8f));
            yield return StartCoroutine(Shot(shot));
            yield return StartCoroutine(Frame(cam, k, body, up, 0.7f, ground ? 12f : 9f));
            yield return StartCoroutine(Shot(shot + "_wide"));
            try { DumpKerbals("dump_" + shot + ".txt"); } catch (Exception e) { Note("eva dump failed " + e); }
            if (!InFlight) yield break;
            FlightGlobals.ForceSetActiveVessel(v);
            yield return new WaitForSecondsRealtime(3f);
        }

        // Camera in front of the villager's face (following it if it turns), `side` swings
        // it round to one side for a three-quarter view.
        IEnumerator Frame(FlightCamera cam, KerbalEVA k, Transform body, Vector3 up, float side, float dist)
        {
            cam.SetDistance(dist);
            for (int i = 0; i < 45; i++)
            {
                if (!InFlight || k == null || body == null) yield break;
                Vector3 face = body.forward;
                Vector3 from = (face + Vector3.Cross(up, face) * side).normalized;
                cam.SetCamCoordsFromPosition(k.transform.position + from * dist + up * dist * 0.12f);
                yield return null;
            }
            yield return new WaitForSecondsRealtime(0.5f);
        }

        // Points the flight camera at a body with the vessel in front, for the sky shots.
        IEnumerator LookAt(string body, string shot)
        {
            CelestialBody b = FlightGlobals.GetBodyByName(body);
            FlightCamera cam = FlightCamera.fetch;
            if (b == null || cam == null) yield break;
            for (int i = 0; i < 40; i++)
            {
                if (!InFlight) yield break;
                // Rocket off to one side so it doesn't hide the body behind it.
                Vector3 target = cam.GetPivot().position;
                Vector3 dir = ((Vector3)b.position - target).normalized;
                Vector3 side = Vector3.Cross(dir, cam.transform.up).normalized;
                cam.SetCamCoordsFromPosition(target - dir * cam.Distance + side * cam.Distance * 0.55f);
                yield return null;
            }
            yield return new WaitForSecondsRealtime(1f);
            yield return StartCoroutine(Shot(shot));
        }

        void DumpEvaPrefabs()
        {
            StringBuilder sb = new StringBuilder();
            foreach (string name in new[] { "kerbalEVA", "kerbalEVAfemale" })
            {
                AvailablePart ap = PartLoader.getPartInfoByName(name);
                if (ap == null || ap.partPrefab == null) { sb.AppendLine(name + " missing"); continue; }
                sb.AppendLine("==== " + name);
                Tree(ap.partPrefab.transform, sb, 0);
                Skins(ap.partPrefab.transform, sb);
            }
            File.WriteAllText(Path.Combine(outDir, "dump_eva_prefab.txt"), sb.ToString());
        }

        void DumpKerbals(string file)
        {
            StringBuilder sb = new StringBuilder();
            foreach (Kerbal k in FindObjectsOfType<Kerbal>())
            {
                sb.AppendLine("==== Kerbal " + k.crewMemberName + " up=" + k.transform.up + " fwd=" + k.transform.forward);
                Tree(k.transform, sb, 0);
                Skins(k.transform, sb);
            }
            foreach (KerbalEVA k in FindObjectsOfType<KerbalEVA>())
            {
                sb.AppendLine("==== EVA " + k.name + " up=" + k.transform.up + " fwd=" + k.transform.forward
                    + " scale=" + k.transform.lossyScale + " state=" + k.fsm.currentStateName);
                foreach (MeshRenderer r in k.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (!r.name.StartsWith("VSP_")) continue;
                    sb.AppendLine("  piece " + r.name + " enabled=" + r.enabled + " visible=" + r.isVisible
                        + " active=" + r.gameObject.activeInHierarchy + " dist=" + (r.bounds.center - k.transform.position).magnitude.ToString("F2")
                        + " size=" + r.bounds.size.ToString("F2") + " shader=" + (r.sharedMaterial != null ? r.sharedMaterial.shader.name : "none")
                        + " tex=" + (r.sharedMaterial != null && r.sharedMaterial.mainTexture != null ? r.sharedMaterial.mainTexture.name : "none"));
                }
                Tree(k.transform, sb, 0);
            }
            File.WriteAllText(Path.Combine(outDir, file), sb.ToString());
        }

        static void Tree(Transform t, StringBuilder sb, int depth)
        {
            if (depth > 14) return;
            StringBuilder c = new StringBuilder();
            foreach (Component comp in t.GetComponents<Component>())
            {
                if (comp == null || comp is Transform) continue;
                c.Append(comp.GetType().Name);
                Renderer r = comp as Renderer;
                if (r != null) c.Append(r.enabled ? "(on" : "(off").Append(r.forceRenderingOff ? ",hidden)" : ")");
                c.Append(' ');
            }
            sb.Append(new string(' ', depth * 2)).Append(t.name)
              .Append(t.gameObject.activeSelf ? "" : " [inactive]")
              .Append(" L").Append(t.gameObject.layer)
              .Append(" p=").Append(t.localPosition.ToString("F3"))
              .Append(" ").Append(c).AppendLine();
            for (int i = 0; i < t.childCount; i++) Tree(t.GetChild(i), sb, depth + 1);
        }

        static void Skins(Transform root, StringBuilder sb)
        {
            foreach (SkinnedMeshRenderer s in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (s.sharedMesh == null) continue;
                sb.AppendLine("  SMR " + s.name + " mesh=" + s.sharedMesh.name + " verts=" + s.sharedMesh.vertexCount
                    + " bounds=" + s.sharedMesh.bounds + " bones=" + s.bones.Length);
                Matrix4x4[] bp = s.sharedMesh.bindposes;
                for (int i = 0; i < s.bones.Length && i < bp.Length; i++)
                {
                    if (s.bones[i] == null) continue;
                    sb.AppendLine("    bone " + s.bones[i].name + " bind=" + bp[i].inverse.MultiplyPoint3x4(Vector3.zero).ToString("F3"));
                }
            }
        }
    }
}
