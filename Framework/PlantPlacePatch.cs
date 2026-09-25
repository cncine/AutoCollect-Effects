#nullable disable
using HarmonyLib;
using Il2CppReloaded.Gameplay;
using AutoCollect;
using System.Text;
using UnityEngine;
using System.Reflection;
using MelonLoader;
using System.Runtime.CompilerServices;

// MelonLogger.Msg();
// 植物行为
public static class PlantAction
{
    private static readonly ConditionalWeakTable<Plant, PhaseHolder> _phases = new();

    private class PhaseHolder
    {
        public int LastPhase = -1;
    }

    public static void Tallnut(Plant thePlant)
    {
        // 只看高坚果
        if (thePlant.mSeedType != SeedType.Tallnut) return;

        // 死亡不触发
        if (thePlant.mDead || thePlant.mPlantHealth <= 0) return;

        int max = thePlant.mPlantMaxHealth;
        if (max <= 0) return;

        float ratio = (float)thePlant.mPlantHealth / max;

        // 阶段：2=完好, 1=裂纹, 0=破损
        int phase = ratio > 0.66f ? 2 : (ratio > 0.33f ? 1 : 0);

        var holder = _phases.GetOrCreateValue(thePlant);

        if (holder.LastPhase == -1)
        {
            holder.LastPhase = phase;
            return;
        }

        if (phase != holder.LastPhase)
        {
            int last = holder.LastPhase;
            holder.LastPhase = phase;

            ModEntry.nuts();
        }
    }

    public static void Chomper(Plant thePlant)
    {
        if (thePlant == null) return;
        if (thePlant.mSeedType != SeedType.Chomper) return;
        if (thePlant.mState != PlantState.ChomperDigesting) return;
        int cd = thePlant.mStateCountdown;
        if (cd == 4000)
        {
            ModEntry.wjz();
        }
    }
    public static void Kernelpult(Projectile projectile)
    {
        if (projectile == null) return;

        // 仅黄油
        if (projectile.mProjectileType != ProjectileType.Butter) return;

        // 老太太叫
        ModEntry.eventList.Add(new KeyValuePair<string, float>("old-lady", Time.time));// 暂时关闭延迟

    }
    public static void Tanglekelp(Plant thePlant)
    {
        if (thePlant == null) return;
        if (thePlant.mSeedType != SeedType.Tanglekelp) return;
        PlantState plantsState = thePlant.mState;
        int cd = thePlant.mStateCountdown;
        if (plantsState == PlantState.TanglekelpGrabbing && cd == 100)
        {
            ModEntry.tanglekelp();
        }
    }
    public static void Squash(Plant thePlant)
    {
        if (thePlant == null) return;
        if (thePlant.mSeedType != SeedType.Squash) return;

        PlantState plantsState = thePlant.mState;
        int cd = thePlant.mStateCountdown;
        if (plantsState == PlantState.SquashLook && cd == 80)
        {
            // 窝瓜发现敌人
            int col = thePlant.mPlantCol;
            int row = thePlant.mStartRow;
            ModEntry.squash();
        }
    }
    public static void Hypnoshroom(Plant thePlant)
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
    public static void Aashes(Plant thePlant)
    {
        int cd = thePlant.mDoSpecialCountdown;
        if (cd == 99)
        { // 刚开始爆炸计时
            switch (thePlant.mSeedType)
            {
                case SeedType.Doomshroom:
                case SeedType.Cherrybomb:
                    ModEntry.selfDestruct(
                        ModEntry.depositBoard.GridToPixelX(thePlant.mPlantCol, thePlant.mStartRow),
                        ModEntry.depositBoard.GridToPixelY(thePlant.mPlantCol, thePlant.mStartRow)
                    );
                    break;
                case SeedType.Jalapeno:
                    ModEntry.kleeBomb(
                        ModEntry.depositBoard.GridToPixelX(thePlant.mPlantCol, thePlant.mStartRow),
                        ModEntry.depositBoard.GridToPixelY(thePlant.mPlantCol, thePlant.mStartRow)
                    );
                    break;
            }
        }
    }
}

// 放置钩子
[HarmonyPatch(typeof(Board), nameof(Board.AddPlant))]
public class PlantPlacePatch
{
    static void Postfix(Board __instance, int __0, int __1, SeedType __2, Plant __result)
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
            case SeedType.Cattail:
                ModEntry.cattail();
                return;
        }

        ModEntry.PlayPlantPlaceSound();
    }
}

// 植物更新钩子
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
                PlantAction.Aashes(__instance);
                break;
            case SeedType.Tanglekelp:
                PlantAction.Tanglekelp(__instance);
                break;
            case SeedType.Chomper:
                PlantAction.Chomper(__instance);
                break;
            case SeedType.Tallnut:
                PlantAction.Tallnut(__instance);
                break;
        }
    }
}

// 缓存Board
[HarmonyPatch(typeof(Board))]
public class BoardPatchClass
{
    [HarmonyPatch("InitLevel")]
    [HarmonyPostfix]
    static void InitLevel(Board __instance)
    {
        ModEntry.depositBoard = __instance;
        // 保护和复位
        SC_Tools.TimeManager.ResetCooldown("GameWin", 0.1f);
        ModEntry.zombieWaveNum = 1;
    }

    [HarmonyPatch("DisposeBoard")]
    [HarmonyPrefix]
    static void DisposeBoardPrefix(Board __instance)
    {
        ModEntry.depositBoard = null;
        ModEntry.eventList.Clear();
    }
}

// 魅惑菇钩子
[HarmonyPatch(typeof(Zombie), nameof(Zombie.EatPlant))]
public class ZombieEatPlantPatch
{
    static void Prefix(Plant thePlant)
    {
        PlantAction.Hypnoshroom(thePlant);
    }
}

// 窝瓜钩子
[HarmonyPatch(typeof(Plant), nameof(Plant.UpdateSquash))]
public class PlantUpdateSquashPatch
{
    static void Postfix(Plant __instance)
    {
        PlantAction.Squash(__instance);
    }
}

// 植物发射钩子
[HarmonyPatch(typeof(Board), nameof(Board.AddProjectile), new Type[]
{
    typeof(float),
    typeof(float),
    typeof(int),
    typeof(int),
    typeof(ProjectileType)
})]
public class bulletPatch
{
    static void Postfix(Projectile __result)
    {
        PlantAction.Kernelpult(__result);
    }
}

// 玉米加农炮发射
[HarmonyPatch(typeof(Plant), nameof(Plant.CobCannonFire))]
public static class CobCannonFirePatch
{
    static void Postfix(Plant __instance, int theTargetX, int theTargetY)
    {
        // 校验一下，确保是玉米加农炮，避免其他植物同名方法（安全判断）
        if (__instance.mSeedType == SeedType.Cobcannon)
        {
            ModEntry.song1();
        }
    }
}

// 小推车触发
[HarmonyPatch(typeof(LawnMower), nameof(LawnMower.Update))]
public static class LawnMowerUpdatePatch
{
    private static readonly ConditionalWeakTable<LawnMower, StateHolder> _states = new();

    private class StateHolder
    {
        public LawnMowerState LastState;
    }
    static void Postfix(LawnMower __instance)
    {
        LawnMowerState current = __instance.mMowerState;

        if (!_states.TryGetValue(__instance, out var holder))
        {
            // 第一次见到，记录初始状态
            _states.Add(__instance, new StateHolder { LastState = current });
            return;
        }

        if (current != holder.LastState)
        {
            var last = holder.LastState;
            holder.LastState = current;

            if (last == LawnMowerState.Ready && current == LawnMowerState.Triggered)
            {
                ModEntry.mabaoguo();
            }
        }
    }
}