using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace SomeThingsFloat;

[StaticConstructorOnStartup]
public class SomeThingsFloat
{
    public static readonly List<ThingDef> ThingsToCreate;

    public static readonly Dictionary<Map, FloatingThings_MapComponent> FloatingMapComponents;

    public static readonly DesignationDef HaulUrgentlyDef;

    public static readonly List<ThingDef> ApparelThatPreventDrowning;

    public static readonly List<ThingDef> PawnsThatBreathe;

    public static readonly List<ThingDef> PawnsThatFloat;

    public static readonly List<TerrainDef> ShallowTerrainDefs;

    public static readonly List<ThingDef> AquaticRaces;

    public static readonly bool SwimmingKitLoaded;

    static SomeThingsFloat()
    {
        new Harmony("Mlie.SomeThingsFloat").PatchAll(Assembly.GetExecutingAssembly());
        ThingsToCreate = DefDatabase<ThingDef>.AllDefsListForReading
            .Where(def => TryGetSpecialFloatingValue(def, out var floatingValue, out var minimized) &&
                          floatingValue > 0 && !minimized)
            .ToList();
        ApparelThatPreventDrowning = DefDatabase<ThingDef>.AllDefsListForReading.Where(def =>
            def.IsApparel && def.apparel.tags.Contains("EVA") &&
            def.apparel.layers.Contains(ApparelLayerDefOf.Overhead)).ToList();
        PawnsThatBreathe = DefDatabase<ThingDef>.AllDefsListForReading.Where(def =>
            def.race is { IsFlesh: true } &&
            def.race.body.HasPartWithTag(BodyPartTagDefOf.BreathingSource)).ToList();
        PawnsThatFloat = DefDatabase<ThingDef>.AllDefsListForReading.Where(def =>
            def.race is { IsFlesh: true }).ToList();
        FloatingMapComponents = new Dictionary<Map, FloatingThings_MapComponent>();
        HaulUrgentlyDef = DefDatabase<DesignationDef>.GetNamedSilentFail("HaulUrgentlyDesignation");
        SwimmingKitLoaded = ModLister.GetActiveModWithIdentifier("pyrce.swimming.modkit") != null;
        ShallowTerrainDefs = DefDatabase<TerrainDef>.AllDefsListForReading.Where(def =>
            def.IsWater && (def.defName.ToLower().Contains("shallow") || def.driesTo != null)).ToList();
        AquaticRaces = new List<ThingDef>();
        if (ModLister.GetActiveModWithIdentifier("BiomesTeam.BiomesIslands") == null)
        {
            return;
        }

        foreach (var possibleAquaticAnimal in DefDatabase<ThingDef>.AllDefsListForReading.Where(def =>
                     def.race != null && def.modExtensions?.Any() == true &&
                     def.modExtensions.Any(extension => extension.GetType().Name == "AquaticExtension")))
        {
            var modExtension =
                possibleAquaticAnimal.modExtensions.First(extension =>
                    extension.GetType().Name == "AquaticExtension");
            if ((bool)modExtension.GetType().GetField("aquatic").GetValue(modExtension))
            {
                AquaticRaces.Add(possibleAquaticAnimal);
            }
        }

        LogMessage($"Found {AquaticRaces.Count} aquatic races: {string.Join(", ", AquaticRaces)}", true);
    }

    public static float GetFloatingValue(Thing thing)
    {
        var actualThing = thing;
        switch (thing)
        {
            case null:
                return 0;
            case Corpse corpse when corpse.InnerPawn == null || corpse.InnerPawn.RaceProps.IsFlesh:
                return 0.75f;
            case Pawn pawn:
                if (!SomeThingsFloatMod.instance.Settings.DownedPawnsFloat ||
                    PawnsThatFloat?.Contains(pawn.def) == false ||
                    !pawn.Downed || !pawn.Awake())
                {
                    return 0;
                }

                if (!SwimmingKitLoaded || !pawn.def.statBases.Any(modifier => modifier.stat.defName == "SwimSpeed"))
                {
                    return 0.5f;
                }

                var swimspeed = pawn.def.statBases.First(modifier => modifier.stat == StatDef.Named("SwimSpeed"))
                    .value;
                var moveSpeed = pawn.def.statBases.First(modifier => modifier.stat == StatDefOf.MoveSpeed)
                    .value;

                return 0.5f * (moveSpeed / Math.Max(swimspeed, 0.01f));

            case Building:
                return 0;
            case MinifiedThing minifiedThing:
                actualThing = minifiedThing.InnerThing;
                break;
        }

        // Manually defined items
        if (thing.def.HasModExtension<FloatingThing_ModExtension>())
        {
            return thing.def.GetModExtension<FloatingThing_ModExtension>().floatingValue;
        }

        // Check if its a special thing
        if (TryGetSpecialFloatingValue(actualThing.def, out var floatingValue, out _))
        {
            return floatingValue;
        }

        // Check floatability based on ingredients
        if (actualThing.def.CostList.NullOrEmpty() && actualThing.def.stuffCategories.NullOrEmpty())
        {
            return 0;
        }

        var totalIngredients = 0f;
        var totalValue = 0f;
        if (!actualThing.def.CostList.NullOrEmpty())
        {
            foreach (var thingDefCountClass in actualThing.def.CostList)
            {
                TryGetSpecialFloatingValue(thingDefCountClass.thingDef, out floatingValue, out _);
                totalIngredients += thingDefCountClass.count;
                totalValue += thingDefCountClass.count * floatingValue;
            }
        }

        if (!actualThing.def.stuffCategories.NullOrEmpty())
        {
            TryGetSpecialFloatingValue(actualThing.Stuff, out floatingValue, out _);
            totalIngredients += actualThing.def.CostStuffCount;
            totalValue += actualThing.def.CostStuffCount * floatingValue;
        }

        totalValue /= totalIngredients;
        return totalValue;
    }

    public static IEnumerable<Thing> GetThingsAndPawns(IntVec3 c, Map map)
    {
        var thingList = map.thingGrid.ThingsListAt(c);
        int num;
        for (var i = 0; i < thingList.Count; i = num + 1)
        {
            if (thingList[i].def.category is ThingCategory.Item or ThingCategory.Pawn)
            {
                yield return thingList[i];
            }

            num = i;
        }
    }

    public static bool IsLargeThing(Thing thing)
    {
        switch (thing)
        {
            case null:
                return false;
            case Corpse:
            case Pawn:
            case Building:
            case MinifiedThing:
                return true;
        }

        return thing.def.IsWeapon || thing.def.IsApparel;
    }


    private static bool TryGetSpecialFloatingValue(ThingDef thingDef, out float floatingValue, out bool onlyIfMinimized)
    {
        floatingValue = 1;
        onlyIfMinimized = false;

        if (thingDef.IsStuff)
        {
            if (thingDef.stuffProps.categories?.Contains(StuffCategoryDefOf.Woody) == true)
            {
                return true;
            }

            if (thingDef.stuffProps.categories?.Contains(StuffCategoryDefOf.Fabric) == true)
            {
                floatingValue = 0.5f;
                return true;
            }

            if (thingDef.stuffProps.categories?.Contains(StuffCategoryDefOf.Leathery) == true)
            {
                floatingValue = 0.5f;
                return true;
            }

            floatingValue = 0;
            return true;
        }

        if (thingDef.IsMeat)
        {
            floatingValue = 0.75f;
            return true;
        }

        if (thingDef.IsDrug || thingDef.IsMedicine)
        {
            return true;
        }

        if (thingDef.IsLeather || thingDef.IsWool)
        {
            floatingValue = 0.5f;
            return true;
        }

        if (thingDef.ingestible?.HumanEdible == true)
        {
            floatingValue = 0.75f;
            return true;
        }

        if (thingDef.IsPlant)
        {
            onlyIfMinimized = true;
            return true;
        }

        floatingValue = 0;
        return false;
    }


    public static float CalculateDrowningValue(Pawn pawn)
    {
        var breathing = pawn.health?.capacities?.GetLevel(PawnCapacityDefOf.Breathing) ?? 1;
        var manipulation = pawn.health?.capacities?.GetLevel(PawnCapacityDefOf.Manipulation) ?? 1;
        var minimumCapacity = 0.1;
        var hediffBaseValue = 0.025f;
        var breathingFactor = 0.6f;
        var manipulationFactor = 0.4f;
        var capacties = (breathingFactor * (float)Math.Max(breathing, minimumCapacity)) +
                        (manipulationFactor * (float)Math.Max(manipulation, minimumCapacity));
        var drownValue = hediffBaseValue * (1 / capacties);
        LogMessage(
            $"Drowning value for {pawn}: {drownValue}. Breathing: {breathing}, Manipulation: {manipulation}, Capacities: {capacties}");
        return drownValue;
    }

    public static int GetWastepacksFloated()
    {
        var returnValue = 0;
        foreach (var floatingThingsMapComponent in FloatingMapComponents)
        {
            returnValue += floatingThingsMapComponent.Value.WastePacksFloated;
        }

        return returnValue;
    }

    public static int GetEnemyPawnsDrowned()
    {
        var returnValue = 0;
        foreach (var floatingThingsMapComponent in FloatingMapComponents)
        {
            returnValue += floatingThingsMapComponent.Value.EnemyPawnsDrowned;
        }

        return returnValue;
    }

    public static void LogMessage(string message, bool force = false)
    {
        if (SomeThingsFloatMod.instance.Settings.VerboseLogging || force)
        {
            Log.Message($"[SomeThingsFloat]: {message}");
        }
    }
}