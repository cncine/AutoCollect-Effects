#nullable disable
using UnityEngine;
using AutoCollect;
using HarmonyLib;
using Il2CppReloaded.Gameplay;
using Il2CppInterop.Runtime.InteropTypes;
namespace AutoCollect.Framework;
[HarmonyPatch(typeof(Board), "Update")]
public class BoardUpdatePatch
{
    private static void Postfix(Board __instance)
    {
        var board = __instance;
        ModEntry.setGameWinState(board.mLevelAwardSpawned);
        ModEntry.setZombieWaveNum(board.mCurrentWave, board.mNumWaves, board.mHugeWaveCountDown);
        if (board.mChallenge?.mChallengeState > ChallengeState.Normal)
        {
            return;
        }
        var flag6 = board.mLevelComplete || board.mLevelAwardSpawned || board.mBoardFadeOutCounter > 0;
        if (flag6) return;
        DataArray<Coin> coins = board.m_coins;
        if (coins == null) return;
        for (var i = 0; i < coins.Count; i++)
        {
            var coin = coins[i];
            if (coin is not { mIsBeingCollected: false, mDead: false }) continue;
            if (coin.mType is CoinType.UsableSeedPacket or CoinType.PresentPlant)
            {
                continue;
            }
            ModEntry.PlayRandomPickSound(new Vector3(coin.X, coin.Y, 0), 1f);
            coin.Collect(ModEntry.TargetPlayer);
        }
    }
}