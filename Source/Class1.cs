using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace WorkbenchAreas
{
    [StaticConstructorOnStartup]
    public static class ModInitializer
    {
        static ModInitializer()
        {
            try
            {
                var harmony = new Harmony("pixelbara.workbenchareas");

                // 1. Context patch: store active Bill during ingredient search
                var methodTryFind = AccessTools.Method(typeof(WorkGiver_DoBill), "TryFindBestBillIngredients");
                if (methodTryFind != null)
                {
                    harmony.Patch(methodTryFind,
                        prefix: new HarmonyMethod(typeof(Patch_TryFindBestBillIngredients), nameof(Patch_TryFindBestBillIngredients.Prefix)),
                        finalizer: new HarmonyMethod(typeof(Patch_TryFindBestBillIngredients), nameof(Patch_TryFindBestBillIngredients.Finalizer)));
                }
                else
                {
                    Log.Error("[WorkbenchAreas] Could not find target method WorkGiver_DoBill.TryFindBestBillIngredients!");
                }

                // 2. Filter patch: override item filter during evaluation
                var methodAllows = AccessTools.Method(typeof(ThingFilter), "Allows", new Type[] { typeof(Thing) });
                if (methodAllows != null)
                {
                    harmony.Patch(methodAllows, postfix: new HarmonyMethod(typeof(Patch_ThingFilter_Allows), nameof(Patch_ThingFilter_Allows.Postfix)));
                }
                else
                {
                    Log.Error("[WorkbenchAreas] Could not find target method ThingFilter.Allows!");
                }

                // 3. UI patch: render target area selector button
                var methodUI = AccessTools.Method(typeof(Dialog_BillConfig), nameof(Dialog_BillConfig.DoWindowContents));
                if (methodUI != null)
                {
                    harmony.Patch(methodUI, postfix: new HarmonyMethod(typeof(Patch_Dialog_BillConfig), nameof(Patch_Dialog_BillConfig.Postfix)));
                }

                Log.Message("[WorkbenchAreas] Successfully initialized!");
            }
            catch (Exception ex)
            {
                Log.Error("[WorkbenchAreas] Critical error during patching: " + ex);
            }
        }
    }

    // Helper for RimWorld localization with fallback strings
    public static class TranslationHelper
    {
        public static string TranslateWithFallback(string key, string fallback)
        {
            return key.CanTranslate() ? key.Translate().ToString() : fallback;
        }
    }

    // Storage for bill area associations and active evaluation context
    public static class BillAreaData
    {
        public static Dictionary<Bill, Area> targetAreas = new Dictionary<Bill, Area>();
        public static Bill CurrentEvaluatingBill = null;

        public static Area GetTargetArea(this Bill bill)
        {
            if (bill != null && targetAreas.TryGetValue(bill, out var area))
                return area;
            return null;
        }

        public static void SetTargetArea(this Bill bill, Area area)
        {
            if (bill == null) return;

            if (area == null)
                targetAreas.Remove(bill);
            else
                targetAreas[bill] = area;
        }
    }

    // Set active bill context during ingredient search
    public static class Patch_TryFindBestBillIngredients
    {
        public static void Prefix(Bill bill)
        {
            BillAreaData.CurrentEvaluatingBill = bill;
        }

        public static void Finalizer()
        {
            BillAreaData.CurrentEvaluatingBill = null;
        }
    }

    // Filter out items outside the selected target area during search
    public static class Patch_ThingFilter_Allows
    {
        public static void Postfix(Thing t, ref bool __result)
        {
            if (!__result || t == null) return;

            Bill activeBill = BillAreaData.CurrentEvaluatingBill;
            if (activeBill != null)
            {
                Area targetArea = activeBill.GetTargetArea();
                if (targetArea != null)
                {
                    // Check if the item's position falls within the target Area grid
                    IntVec3 pos = t.PositionHeld;
                    if (!targetArea[pos])
                    {
                        __result = false;
                    }
                }
            }
        }
    }

    // UI Rendering for Dialog_BillConfig
    public static class Patch_Dialog_BillConfig
    {
        public static void Postfix(Dialog_BillConfig __instance, Rect inRect)
        {
            Bill bill = AccessTools.Field(typeof(Dialog_BillConfig), "bill")?.GetValue(__instance) as Bill;
            if (bill == null) return;

            // Position button directly under the search radius slider
            float width = 280f;
            float height = 30f;
            float x = inRect.width - width - 10f;
            float y = inRect.height - 40f;

            Rect buttonRect = new Rect(x, y, width, height);

            Area currentArea = bill.GetTargetArea();

            string defaultLabel = TranslationHelper.TranslateWithFallback("WorkbenchAreas.AllAreas", "All Areas (Default)");
            string labelPrefix = TranslationHelper.TranslateWithFallback("WorkbenchAreas.TargetArea", "Area: ");

            string currentLabel = currentArea != null ? currentArea.Label : defaultLabel;

            if (Widgets.ButtonText(buttonRect, labelPrefix + currentLabel))
            {
                List<FloatMenuOption> options = new List<FloatMenuOption>();

                // Default / Reset option
                options.Add(new FloatMenuOption(defaultLabel, () => bill.SetTargetArea(null)));

                Map map = Find.CurrentMap;
                if (map != null)
                {
                    List<Area> allAreas = map.areaManager.AllAreas;
                    for (int i = 0; i < allAreas.Count; i++)
                    {
                        Area area = allAreas[i];

                        // Filter out utility areas: keep only Home Area and player-created allowed areas
                        if (area is Area_Home || area is Area_Allowed)
                        {
                            options.Add(new FloatMenuOption(area.Label, () => bill.SetTargetArea(area)));
                        }
                    }
                }

                Find.WindowStack.Add(new FloatMenu(options));
            }
        }
    }
}