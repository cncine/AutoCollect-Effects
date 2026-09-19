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

[assembly: MelonInfo(typeof(AutoCollect.ModEntry), "AutoCollect & Effects", "1.0.3", "XSC")]
namespace AutoCollect;
public class ModEntry : MelonMod
{
    private static MelonLogger.Instance _log;
    private static int _targetPlayer = 0;
    private const string Category = "AutoCollect";
    private const string KeyTarget = "TargetPlayer";
    private static MelonPreferences_Entry<int> _targetEntry;
    
    public static bool gameIsWon = false;

    // 保存wav文件完整路径，不再存AudioClip
    public static List<string> PickSoundPaths = new List<string>();
    public static List<string> PlantSoundPaths = new List<string>();
    public static float LastSoundTime;
    public static float LastBombTime;
    public const float SoundCooldown = 5f;
    public static ImagePopup _popup;
    public static Board depositBoard;
    // public static GameplayActivity depositGameplayActivity;

    public override void OnDeinitializeMelon()
    {
        _popup?.Dispose();
        _popup = null;
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
        string soundFolder = Path.Combine(modFolder, "Sounds");
        string resFolder = Path.Combine(modFolder, "res");

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

        // 新增：放置音效
        // 在 OnInitializeMelon 里面，读取植物音效文件夹
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

        _log.Msg($"[AutoCollect] 音效加载完成，可用数量：{PickSoundPaths.Count}");
        _log.Msg($"[AutoCollect] 目标玩家 = {(_targetPlayer == 0 ? "玩家1" : "玩家2")}  (按 F9 或点左上角按钮切换)");

        _popup = new ImagePopup();
        _popup.Init();

        // 批量加载
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
            resFolder + "\\woman.png"
        });
    }

    /// <summary>随机播放外部wav，Windows winmm异步播放，无3D空间音效</summary>
    public static void PlayRandomPickSound(Vector3 pos, float volume = 0.7f)
    {
        if (PickSoundPaths == null || PickSoundPaths.Count == 0) return;
        if (Time.time - LastSoundTime < SoundCooldown) return;

        int randomIdx = UnityEngine.Random.Range(0, PickSoundPaths.Count);
        string wavPath = PickSoundPaths[randomIdx];

        LastSoundTime = Time.time;
        WinmmSound.PlayWavFile(wavPath);
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
        if (Time.time - LastSoundTime < SoundCooldown) return;

        int idx = UnityEngine.Random.Range(0, PlantSoundPaths.Count);
        string path = PlantSoundPaths[idx];

        LastSoundTime = Time.time;
        WinmmSound.PlayWavFile(path);
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
        }
    }

    public override void OnUpdate()
    {
        _popup?.Update();
        // if (Input.GetKeyDown(KeyCode.T)) OnGameWinSc();
        if (Input.GetKeyDown(KeyCode.F9))
        {
            ToggleTarget();
        }
    }

    public static void OnGameWinSc()
    {
        _popup?.PlaySequence(11, 640, 360, 34, 0.1f);
        string modDllPath = Assembly.GetExecutingAssembly().Location;
        string modFolder = Path.GetDirectoryName(modDllPath);
        string winWaveFile = Path.Combine(modFolder, "Sounds", "Other\\pvz_ohhhh.wav");
        WinmmSound.PlayWavFile(winWaveFile);
        LastSoundTime = Time.time;
    }

    public static void setGameWinState(bool isWon)
    {
        if (isWon && !gameIsWon)
        {
            OnGameWinSc();
        }
        gameIsWon = isWon;
    }

    public static void selfDestruct(float x, float y)
    {
        if (Time.time - LastBombTime < 0.5f) return;
        LastBombTime = Time.time;
        _popup?.ShowAt(12, x, y);
        string modDllPath = Assembly.GetExecutingAssembly().Location;
        string modFolder = Path.GetDirectoryName(modDllPath);
        string winWaveFile = Path.Combine(modFolder, "Sounds", "Other\\Complete.wav");
        WinmmSound.PlayWavFile(winWaveFile);
        LastSoundTime = Time.time;
    }

    public static void kleeBomb(float x, float y)
    {
        if (Time.time - LastBombTime < 0.5f) return;
        LastBombTime = Time.time;
        _popup?.ShowAt(13, x, y);
        string modDllPath = Assembly.GetExecutingAssembly().Location;
        string modFolder = Path.GetDirectoryName(modDllPath);
        string winWaveFile = Path.Combine(modFolder, "Sounds", "Other\\bbbomb.wav");
        WinmmSound.PlayWavFile(winWaveFile);
        LastSoundTime = Time.time;
    }

    public static void kleeBomb()
    {
        if (Time.time - LastBombTime < 0.5f) return;
        LastBombTime = Time.time;
        _popup?.Show(13);
        string modDllPath = Assembly.GetExecutingAssembly().Location;
        string modFolder = Path.GetDirectoryName(modDllPath);
        string winWaveFile = Path.Combine(modFolder, "Sounds", "Other\\bbbomb.wav");
        WinmmSound.PlayWavFile(winWaveFile);
        LastSoundTime = Time.time;
    }

    public static void showWoman(float x, float y)
    {
        if (Time.time - LastBombTime < 0.5f) return;
        LastBombTime = Time.time;
        _popup?.ShowAt(14, x, y);
        string modDllPath = Assembly.GetExecutingAssembly().Location;
        string modFolder = Path.GetDirectoryName(modDllPath);
        string winWaveFile = Path.Combine(modFolder, "Sounds", "Other\\wow.wav");
        WinmmSound.PlayWavFile(winWaveFile);
        LastSoundTime = Time.time;
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
