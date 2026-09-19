using System;
using System.Runtime.InteropServices;

public static class WinmmSound
{
    [DllImport("winmm.dll")]
    private static extern bool PlaySound(string pszSound, IntPtr hmod, uint fdwSound);

    private const uint SND_FILENAME = 0x00020000;
    private const uint SND_ASYNC = 0x0001;

    /// <summary>
    /// 异步播放wav文件（Windows系统API）
    /// </summary>
    /// <param name="filePath">wav完整路径</param>
    public static void PlayWavFile(string filePath)
    {
        try
        {
            PlaySound(filePath, IntPtr.Zero, SND_FILENAME | SND_ASYNC);
        }
        catch
        {
            // 播放失败静默忽略
        }
    }
}
