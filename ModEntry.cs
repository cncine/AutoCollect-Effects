#nullable disable
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using System.Collections.Generic;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using ImagePopupMod;
using System.Text.RegularExpressions;
using Il2CppReloaded.Gameplay;
using SC_Tools;

[assembly: MelonInfo(typeof(AutoCollect.ModEntry), "AutoCollect & Effects", "0.65", "XSC")]
namespace AutoCollect;
public class ModEntry : MelonMod
{
    private static MelonLogger.Instance _log;
    private static int _targetPlayer = 0;
    private const string Category = "AutoCollect";
    private const string KeyTarget = "TargetPlayer";
    private static MelonPreferences_Entry<int> _targetEntry;
    
    public static bool gameIsWon = false;
    public static int zombieWaveNum;
    public static int wavecd;

    // 保存wav文件完整路径
    public static List<string> PickSoundPaths = new List<string>();
    public static List<string> PlantSoundPaths = new List<string>();

    public static ImagePopup _popup;
    public static Board depositBoard;
    public static string otherWavPath;
    public static List<KeyValuePair<string, float>> eventList = new List<KeyValuePair<string, float>>();

    public override void OnDeinitializeMelon()
    {
        _popup?.Dispose();
        _popup = null;
        ExternalSfxPlayer.DestroyAudioRoot();
    }

    public override void OnSceneWasInitialized(int buildIndex, string sceneName)
    {
        _popup?.OnSceneChanged();
    }

    public override void OnInitializeMelon()
    {
        _log = LoggerInstance;

        var cat = MelonPreferences.CreateCategory(Category);
        _targetEntry = cat.CreateEntry<int>(KeyTarget, 0,
            "自动收阳光的目标玩家 (0 = 玩家1, 1 = 玩家2)");
        _targetPlayer = _targetEntry.Value;
        if (_targetPlayer != 0 && _targetPlayer != 1) _targetPlayer = 0;

        var harmony = new HarmonyLib.Harmony("AutoCollect.Toggle");
        harmony.PatchAll();

        string modDllPath = Assembly.GetExecutingAssembly().Location;
        string modFolder = Path.GetDirectoryName(modDllPath);
        string resFolder = Path.Combine(modFolder, "res");

        // 自动拾取音效
        string soundFolder = Path.Combine(modFolder, "Sounds");
        PickSoundPaths.Clear();
        if (Directory.Exists(soundFolder))
        {
            string[] wavFiles = Directory.GetFiles(soundFolder, "*.wav");
            _log.Msg($"[AutoCollect] 找到 {wavFiles.Length} 个wav音效");
            foreach (var fpath in wavFiles)
            {
                PickSoundPaths.Add(fpath);
            }
        }
        else
        {
            _log.Warning($"[AutoCollect] Sounds文件夹不存在：{soundFolder}");
        }

        // 放置音效
        string plantSoundFolder = Path.Combine(modFolder, "Sounds", "Plant");
        PlantSoundPaths.Clear();
        if (Directory.Exists(plantSoundFolder))
        {
            string[] plantWavs = Directory.GetFiles(plantSoundFolder, "*.wav");
            _log.Msg($"[AutoCollect] 找到 {plantWavs.Length} 个放置植物音效");
            foreach (var f in plantWavs)
            {
                PlantSoundPaths.Add(f);
            }

        } else
        {
            _log.Warning($"[AutoCollect] Sounds/Plant文件夹不存在：{plantSoundFolder}");
        }

        // 其他音效
        List<string> allAudioFile = new List<string>();
        string otherSoundFolder = Path.Combine(modFolder, "Sounds", "Other");
        if (Directory.Exists(otherSoundFolder))
        {
            string[] otherWavFiles = Directory.GetFiles(otherSoundFolder, "*.wav");
            _log.Msg($"[AutoCollect] 找到 {otherWavFiles.Length} 个其他音效");
            foreach (var fpath in otherWavFiles)
            {
                allAudioFile.Add(fpath);
            }
        }
        else
        {
            _log.Warning($"[AutoCollect] Sounds/Other文件夹不存在：{otherSoundFolder}");
        }
        allAudioFile = allAudioFile.Union(PickSoundPaths).ToList();
        allAudioFile = allAudioFile.Union(PlantSoundPaths).ToList();
        // 加载音频播放组件
        ExternalSfxPlayer.InitAudioRoot();
        ExternalSfxPlayer.PreLoadSamples(allAudioFile);

        _popup = new ImagePopup();
        _popup.Init();
        // 批量加载图片资源
        _popup.LoadImages(new[]
        {
            resFolder + "\\XTT.png",
            resFolder + "\\fozu.png",
            resFolder + "\\nice1.png",

            resFolder + "\\chengjieyong.png",
            resFolder + "\\DaLi.png",
            resFolder + "\\dys.png",
            resFolder + "\\fg.png",
            resFolder + "\\GeWa.png",
            resFolder + "\\HeiShou.png",
            resFolder + "\\jf.png",
            resFolder + "\\ZQD.png",

            resFolder + "\\bmall.png",
            resFolder + "\\jpm.png",
            resFolder + "\\bbbomb.png",
            resFolder + "\\woman.png",
            resFolder + "\\bomb_all.png",
            resFolder + "\\aowalk.png",
            resFolder + "\\awei.png",
            resFolder + "\\joker.png"
        });

        _log.Msg($"[AutoCollect] 目标玩家 = {(_targetPlayer == 0 ? "玩家1" : "玩家2")}  (按 F9 或点左上角按钮切换)");
    }

    /// <summary>随机播放外部wav</summary>
    public static void PlayRandomPickSound(Vector3 pos, float volume = 0.7f)
    {
        if (PickSoundPaths == null || PickSoundPaths.Count == 0) return;
        if (!TimeManager.GetCanAction("MainSound", 5f)) return;

        int randomIdx = UnityEngine.Random.Range(0, PickSoundPaths.Count);
        string wavPath = PickSoundPaths[randomIdx];

        ExternalSfxPlayer.PlayWav(wavPath);
        string pattern = @"[^\\/]+$";
        string fileName = Regex.Match(wavPath, pattern).Value;
        switch (fileName)
        {
            case "pvz_au0.wav":
                _popup?.ShowAt(5, pos.x, pos.y);
                break;
            case "pvz_au5.wav":
                _popup?.ShowAt(7, pos.x, pos.y);
                break;
            case "pvz_au6.wav":
                _popup?.ShowAt(3, pos.x, pos.y);
                break;
            case "pvz_au7.wav":
                _popup?.ShowAt(4, pos.x, pos.y);
                break;
            case "pvz_au8.wav":
                _popup?.ShowAt(8, pos.x, pos.y);
                break;
            case "pvz_au12.wav":
                _popup?.ShowAt(9, pos.x, pos.y);
                break;
            case "pvz_au13.wav":
                _popup?.ShowAt(10, pos.x, pos.y);
                break;
            case "pvz_au14.wav":
                _popup?.ShowAt(6, pos.x, pos.y);
                break;
        }
    }

    // 新增：放置音效
    public static void PlayPlantPlaceSound()
    {
        if (PlantSoundPaths == null || PlantSoundPaths.Count == 0) return;
        if (!TimeManager.GetCanAction("MainSound", 5f)) return;

        int idx = UnityEngine.Random.Range(0, PlantSoundPaths.Count);
        string path = PlantSoundPaths[idx];

        ExternalSfxPlayer.PlayWav(path);
        // 显示动画
        string pattern = @"[^\\/]+$";
        string fileName = Regex.Match(path, pattern).Value;
        switch (fileName)
        {
            case "pvz_aup0.wav":
                _popup?.Show(0);
                break;
            case "pvz_aup2.wav":
                _popup?.Show(1);
                break;
            case "pvz_aup3.wav":
                _popup?.Show(2);
                break;
            case "pvz_aup4.wav":
                _popup?.Show(17);
                break;
        }
    }

    public override void OnUpdate()
    {
        _popup?.Update();
        if (Input.GetKeyDown(KeyCode.F9))
        {
            ToggleTarget();
        }
        // 延迟事件列表
        if (depositBoard != null)
        {
            for (int i = eventList.Count - 1; i >= 0; i--)
            {
                if (eventList[i].Value <= Time.time)
                {
                    OnDelayEvent(eventList[i].Key);
                    eventList.RemoveAt(i);
                }
            }
        }
    }

    public static void OnDelayEvent(string key)
    {
        if (key == "old-lady" && TimeManager.GetCanAction("OldLadySound", 0.1f))
        {
            PlayOtherSoundByFile("yoooo.wav");
        } else if (key.StartsWith("boom1"))
        {
            string pattern = @"^[^|]+\|(-?\d+(?:\.\d+)?)\|(-?\d+(?:\.\d+)?)$";
            Match m = Regex.Match(key, pattern);
            if (m.Success)
            {
                string num1 = m.Groups[1].Value;
                string num2 = m.Groups[2].Value;

                float d1 = float.Parse(num1);
                float d2 = float.Parse(num2);

                _popup?.PlaySequenceAt(15, 160, 160, 11, 0.04f, d1, d2);
                _popup?.Flash("#FF0000", 0.05f, 0f);
                PlayOtherSoundByFile("020.wav");
            }
        }
    }

    public static void OnGameWinSc()
    {
        _popup?.PlaySequence(11, 640, 360, 34, 0.1f);
        PlayOtherSoundByFile("pvz_ohhhh.wav");
        TimeManager.ResetCooldown("MainSound", 5f);
    }

    public static void setGameWinState(bool isWon)
    {
        if (isWon && !gameIsWon && TimeManager.GetCanAction("GameWin", 0.1f))
        {
            OnGameWinSc();
        }
        gameIsWon = isWon;
    }

    public static void setZombieWaveNum(int cWave, int maxWave, int waveTipsCd)
    {
        if (zombieWaveNum != cWave)
        {
            if (cWave > zombieWaveNum && cWave == 1)
            {
                startSound();
            }
            zombieWaveNum = cWave;
        }
        if (wavecd != waveTipsCd)
        {
            if (waveTipsCd > wavecd && cWave >= maxWave - 1)
            {
                barrage();// 最后一波了，“放鞭炮”
            } else if (waveTipsCd > wavecd)
            {
                showAo();// 他来了
            }
            wavecd = waveTipsCd;
        }
    }

    public static void selfDestruct(float x, float y)
    {
        if (!TimeManager.GetCanAction("SelfDestructSound", 0.5f)) return;
        _popup?.ShowAt(12, x, y);
        PlayOtherSoundByFile("Complete.wav");
    }

    public static void kleeBomb(float x, float y)
    {
        if (!TimeManager.GetCanAction("KleeBombSound", 0.5f)) return;
        _popup?.ShowAt(13, x, y);
        PlayOtherSoundByFile("bbbomb.wav");
    }

    public static void kleeBomb()
    {
        if (!TimeManager.GetCanAction("KleeBombSound", 0.5f)) return;
        _popup?.Show(13);
        PlayOtherSoundByFile("bbbomb.wav");
    }

    public static void showWoman(float x, float y)
    {
        if (!TimeManager.GetCanAction("WomanSound", 1f)) return;
        _popup?.ShowAt(14, x, y);
        PlayOtherSoundByFile("wow.wav");
    }

    public static void squash()
    {
        if (!TimeManager.GetCanAction("SquashSound", 0.5f)) return;
        PlayOtherSoundByFile("spmf.wav");
    }

    public static void tanglekelp()
    {
        if (!TimeManager.GetCanAction("TanglekelpSound", 0.5f)) return;
        PlayOtherSoundByFile("water.wav");
    }

    public static void cattail()
    {
        if (!TimeManager.GetCanAction("CattailSound", 0.5f)) return;
        PlayOtherSoundByFile("mwc.wav");
    }

    public static void mabaoguo()
    {
        if (!TimeManager.GetCanAction("MabaoguoSound", 0.1f)) return;
        PlayOtherSoundByFile("mbg.wav");
    }

    public static void song1()
    {
        if (!TimeManager.GetCanAction("Song1Sound", 0.1f)) return;
        PlayOtherSoundByFile("htgp.wav");
    }

    public static void barrage()
    {
        if (!TimeManager.GetCanAction("BarrageSound", 1f)) return;
        for (int i = 0; i < 10; i++)
        {
            float boomX = 720f * UnityEngine.Random.value;
            float boomY = 300f * UnityEngine.Random.value;
            eventList.Add(new KeyValuePair<string, float>("boom1|" + boomX + "|" + boomY, Time.time + i * 0.1f));
        }
    }

    public static void startSound()
    {
        if (!TimeManager.GetCanAction("StartSound", 1f)) return;
        PlayOtherSoundByFile("dontcome.wav");
    }

    public static void showAo()
    {
        if (!TimeManager.GetCanAction("Ao", 1f)) return;
        float driftValue = UnityEngine.Random.Range(-200f, 200f);
        _popup?.PlaySequenceAt(16, 64, 110, 4, 0.2f, 720f, 300f + driftValue, 0f, 300f + driftValue, true, 3f, 5.3f);
        PlayOtherSoundByFile("aowalk.wav");
    }

    public static void nuts()
    {
        if (!TimeManager.GetCanAction("NutsSound", 0.1f)) return;
        if (UnityEngine.Random.value > 0.5f)
        {
            PlayOtherSoundByFile("nuts1.wav");
        } else
        {
            PlayOtherSoundByFile("nuts2.wav");
        }
    }

    public static void wjz()
    {
        if (!TimeManager.GetCanAction("WjzSound", 0.1f)) return;
        PlayOtherSoundByFile("zhenxiang.wav");
    }

    public static void joker(float x, float y)
    {
        if (!TimeManager.GetCanAction("JokerSound", 0.1f)) return;
        _popup?.ShowAt(18, x, y);
        PlayOtherSoundByFile("joker.wav");
    }

    public static void shoot()
    {
        if (!TimeManager.GetCanAction("ShootSound", 0.05f)) return;
        PlayOtherSoundByFile("gun-type1.wav", 1f, 0.6f + UnityEngine.Random.value * 0.8f);
    }

    public static void PlayOtherSoundByFile(string path, float vom = 1f, float pitch = 1f)
    {
        if (otherWavPath == null)
        {
            string modDllPath = Assembly.GetExecutingAssembly().Location;
            string modFolder = Path.GetDirectoryName(modDllPath);
            otherWavPath = Path.Combine(modFolder, "Sounds", "Other");
        }
        ExternalSfxPlayer.PlayWav(otherWavPath + "\\" + path, vom, pitch);
    }

    public override void OnGUI()
    {
        var label = _targetPlayer == 0
            ? "自动收阳光 -> 玩家1"
            : "自动收阳光 -> 玩家2";
        if (GUI.Button(new Rect(10, 10, 230, 30), label))
        {
            ToggleTarget();
        }
        GUI.Label(new Rect(10, 44, 300, 20), "按 F9 也可切换目标玩家");
    }

    public static void reShowAt(int index, float screenX, float screenY)
    {
        _popup?.ShowAt(index, screenX, screenY);
    }

    private static void ToggleTarget()
    {
        _targetPlayer = _targetPlayer == 0 ? 1 : 0;
        if (_targetEntry != null) _targetEntry.Value = _targetPlayer;
        _log?.Msg($"[AutoCollect] 已切换 -> {(_targetPlayer == 0 ? "玩家1" : "玩家2")}");
    }

    public static int TargetPlayer => _targetPlayer;
}
