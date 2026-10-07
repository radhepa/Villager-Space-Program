// Turns the Sun, Kerbin and the Mun into pixel-art blocks (see CubeWorld.cs) as soon as the
// solar system exists, and switches off KSP's sun glare: a Minecraft sun doesn't glow.
using System.Collections.Generic;
using UnityEngine;

namespace VillagerSpaceProgram
{
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class BlockSky : MonoBehaviour
    {
        float next;

        void Awake()
        {
            DontDestroyOnLoad(gameObject);
            Camera.onPreCull += CubeBody.OnAnyPreCull;
        }

        void OnDestroy() { Camera.onPreCull -= CubeBody.OnAnyPreCull; }

        void Update()
        {
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + 0.25f;
            List<CelestialBody> bodies = FlightGlobals.fetch != null ? FlightGlobals.Bodies : null;
            if (bodies == null) return;
            for (int i = 0; i < bodies.Count; i++)
            {
                CelestialBody b = bodies[i];
                if (b == null || b.scaledBody == null) continue;
                if (b.bodyName == "Sun") CubeWorld.Make(b, CubeStyle.Sun);
                else if (b.bodyName == "Kerbin") CubeWorld.Make(b, CubeStyle.Earth);
                else if (b.bodyName == "Mun") CubeWorld.Make(b, CubeStyle.Moon);
            }
            if (Sun.Instance != null && Sun.Instance.sunFlare != null && Sun.Instance.sunFlare.enabled)
                Sun.Instance.sunFlare.enabled = false;
            if (SunFlare.Instance != null)
            {
                if (SunFlare.Instance.sunFlare != null && SunFlare.Instance.sunFlare.enabled)
                    SunFlare.Instance.sunFlare.enabled = false;
                if (SunFlare.Instance.enabled) SunFlare.Instance.enabled = false;
            }
        }
    }
}
