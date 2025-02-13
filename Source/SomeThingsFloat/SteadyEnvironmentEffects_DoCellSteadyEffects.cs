using HarmonyLib;
using RimWorld;
using Verse;

namespace SomeThingsFloat;

[HarmonyPatch(typeof(SteadyEnvironmentEffects), "DoCellSteadyEffects")]
public static class SteadyEnvironmentEffects_DoCellSteadyEffects
{
    public static void Postfix(IntVec3 c, Map ___map)
    {
        if (SomeThingsFloat.FloatingMapComponents.TryGetValue(___map, out var component))
        {
            component.UnSpawnedDeterioration(c);
        }
    }
}