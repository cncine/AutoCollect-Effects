#nullable disable
using HarmonyLib;
using Il2CppReloaded.Gameplay;
using AutoCollect;
using System.Text;
using UnityEngine;
using System.Reflection;
using MelonLoader;

[HarmonyPatch(typeof(Board), nameof(Board.AddPlant), new[] { typeof(int), typeof(int), typeof(SeedType), typeof(SeedType) })]
public class PlantPlacePatch
{
    static void Postfix(Plant __result, Board __instance, int __0, int __1, SeedType __2, SeedType __3)
    {
        // __result != null = 植物成功添加到棋盘（放置成功）
        if (__result == null)
            return;
        switch (__2)
        {
            case SeedType.Doomshroom:
                if (!__instance.StageIsNight())
                {
                    break;
                }
                return;
            case SeedType.Cherrybomb:
            case SeedType.Jalapeno:
                return;
            case SeedType.ExplodeONut:
                ModEntry.kleeBomb();
                return;
        }
        // 调用我们已经写好的放置植物音效函数
        ModEntry.PlayPlantPlaceSound();
    }
}

[HarmonyPatch(typeof(Plant), nameof(Plant.Update))]
public class PlantUpdateSpecialCountdownPatch
{
    static void Postfix(Plant __instance)
    {
        switch (__instance.mSeedType)
        {
            case SeedType.Doomshroom:
            case SeedType.Cherrybomb:
            case SeedType.Jalapeno:
                break;
            default: return;
        }
        int cd = __instance.mDoSpecialCountdown;
        if (cd == 99)
        { // 刚开始爆炸计时
            switch (__instance.mSeedType)
            {
                case SeedType.Doomshroom:
                case SeedType.Cherrybomb:
                    ModEntry.selfDestruct(
                        ModEntry.depositBoard.GridToPixelX(__instance.mPlantCol, __instance.mStartRow),
                        ModEntry.depositBoard.GridToPixelY(__instance.mPlantCol, __instance.mStartRow)
                    );
                    break;
                case SeedType.Jalapeno:
                    ModEntry.kleeBomb(
                        ModEntry.depositBoard.GridToPixelX(__instance.mPlantCol, __instance.mStartRow),
                        ModEntry.depositBoard.GridToPixelY(__instance.mPlantCol, __instance.mStartRow)
                    );
                    break;
            }
        }
    }
}

[HarmonyPatch(typeof(Board))]
public class BoardPatchClass
{
    [HarmonyPatch("InitLevel")]
    [HarmonyPostfix]
    static void InitLevel(Board __instance)
    {
        // MelonLogger.Msg();
        ModEntry.depositBoard = __instance;
    }

    [HarmonyPatch("DisposeBoard")]
    [HarmonyPrefix]
    static void DisposeBoardPrefix(Board __instance)
    {
        ModEntry.depositBoard = null;
    }
}

[HarmonyPatch(typeof(Zombie), nameof(Zombie.EatPlant))]
public class ZombieEatPlantPatch
{
    static void Prefix(Plant thePlant)
    {
        if (thePlant == null) return;

        if (thePlant.mSeedType == SeedType.Hypnoshroom)
        {
            int col = thePlant.mPlantCol;
            int row = thePlant.mStartRow;
            bool isSleeping = thePlant.mIsAsleep; // ✅读取睡眠状态

            if (!isSleeping)
            {
                ModEntry.showWoman(
                    ModEntry.depositBoard.GridToPixelX(col, row),
                    ModEntry.depositBoard.GridToPixelY(col, row)
                );
            }
        }
    }
}
