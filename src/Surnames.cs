// Villagers don't say "Kerman", they say "Hmm": every crew member's last name becomes Hmm.
// Jebediah Kerman -> Jebediah Hmm. First names are never touched.
//
// KSP stores names in the save, so this is a real rename. Set rename_surnames = false in
// PluginData/settings.cfg and load the save again to turn everyone back into Kermans.
using System;
using System.IO;
using UnityEngine;

namespace VillagerSpaceProgram
{
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class Surnames : MonoBehaviour
    {
        public const string Old = " Kerman";
        public const string New = " Hmm";
        float next;

        void Awake() { DontDestroyOnLoad(gameObject); }

        public static bool Enabled
        {
            get
            {
                string path = VSP.PathIn("settings.cfg");
                if (!File.Exists(path)) return true;
                foreach (string line in File.ReadAllLines(path))
                {
                    string l = line.Trim().ToLowerInvariant().Replace(" ", "");
                    if (l.StartsWith("rename_surnames=")) return !l.EndsWith("false");
                }
                return true;
            }
        }

        void Update()
        {
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + 1f;
            Game g = HighLogic.CurrentGame;
            if (g == null || g.CrewRoster == null) return;
            bool on = Enabled;
            string from = on ? Old : New, to = on ? New : Old;
            KerbalRoster roster = g.CrewRoster;
            int changed = 0;
            for (int i = 0; i < roster.Count; i++)
            {
                ProtoCrewMember pcm = roster[i];
                if (pcm == null || pcm.name == null || !pcm.name.EndsWith(from)) continue;
                string first = pcm.name.Substring(0, pcm.name.Length - from.Length);
                try
                {
                    if (pcm.ChangeName(first + to)) changed++;
                }
                catch (Exception e)
                {
                    VSP.Warn("rename " + pcm.name + ": " + e.Message);
                }
            }
            if (changed > 0) VSP.Log("renamed " + changed + " crew to *" + to);
        }
    }
}
