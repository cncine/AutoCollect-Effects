using MelonLoader;
using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SC_Tools
{
	public static class ExternalSfxPlayer
	{
		// 持久父物体
		private static GameObject _audioRoot;

		// 采样缓存数据结构
		private class AudioSampleData
		{
			public float[] Samples;
			public int Channels;
			public int SampleRate;
			public DateTime LastAccessTime; // LRU：最后访问时间
		}

		private static readonly Dictionary<string, AudioSampleData> _sampleCache = new();
		private const int MaxCacheCount = 64;

		/// <summary>
		/// Mod初始化调用一次
		/// </summary>
		public static void InitAudioRoot()
		{
			if (_audioRoot != null) return;
			_audioRoot = new GameObject("ModAudioRoot");
			UnityEngine.Object.DontDestroyOnLoad(_audioRoot);
			MelonLogger.Msg("ModAudioRoot 持久化对象已创建");
		}

		/// <summary>
		/// 批量预加载音效采样数据（阻塞加载；适合Mod启动阶段调用）
		/// </summary>
		/// <param name="paths">完整WAV路径列表</param>
		public static void PreLoadSamples(List<string> paths)
		{
			if (paths == null || paths.Count == 0)
			{
				MelonLogger.Warning("预加载路径列表为空");
				return;
			}

			int success = 0;
			int fail = 0;
			foreach (string filePath in paths)
			{
				// 已经缓存就跳过
				if (_sampleCache.ContainsKey(filePath))
				{
					success++;
					continue;
				}

				if (!File.Exists(filePath))
				{
					MelonLogger.Error($"[预加载] 文件不存在：{filePath}");
					fail++;
					continue;
				}

				AudioSampleData data = LoadWavToSamples(filePath);
				if (data == null)
				{
					fail++;
					continue;
				}

				// 缓存满了，淘汰旧数据
				if (_sampleCache.Count >= MaxCacheCount)
				{
					EvictLRU();
				}

				_sampleCache.Add(filePath, data);
				success++;
			}
			MelonLogger.Msg($"预加载完成：成功 {success} 个，失败 {fail} 个");
		}

		/// <summary>
		/// 播放外部PCM WAV
		/// </summary>
		public static void PlayWav(string fullPath, float volume = 1f, float pitch = 1f, Vector3 position = default)
		{
			if (_audioRoot == null)
			{
				MelonLogger.Warning("AudioRoot未初始化，自动调用InitAudioRoot");
				InitAudioRoot();
			}

			if (!File.Exists(fullPath))
			{
				MelonLogger.Error($"音效文件不存在：{fullPath}");
				return;
			}

			AudioSampleData sampleData = GetOrLoadSampleData(fullPath);
			if (sampleData == null)
			{
				MelonLogger.Error($"加载WAV采样失败：{fullPath}");
				return;
			}

			// 每次播放新建AudioClip
			int sampleCount = sampleData.Samples.Length / sampleData.Channels;
			AudioClip clip = AudioClip.Create(Path.GetFileName(fullPath), sampleCount, sampleData.Channels, sampleData.SampleRate, false);
			clip.SetData(sampleData.Samples, 0);

			GameObject go = new GameObject("ModSfx_" + Path.GetFileNameWithoutExtension(fullPath));
			go.transform.SetParent(_audioRoot.transform);
			go.transform.position = position;

			AudioSource src = go.AddComponent<AudioSource>();
			src.clip = clip;
			src.volume = volume;
			src.pitch = pitch;
			src.spatialBlend = position == Vector3.zero ? 0f : 1f;
			src.Play();

			MelonCoroutines.Start(CleanupSfx(go, clip, clip.length));
		}

		/// <summary>
		/// 获取采样，不存在则加载；更新LRU访问时间
		/// </summary>
		private static AudioSampleData GetOrLoadSampleData(string filePath)
		{
			if (_sampleCache.TryGetValue(filePath, out var data))
			{
				data.LastAccessTime = DateTime.Now;
				return data;
			}

			if (_sampleCache.Count >= MaxCacheCount)
			{
				EvictLRU();
			}

			AudioSampleData newData = LoadWavToSamples(filePath);
			if (newData == null) return null;

			_sampleCache.Add(filePath, newData);
			return newData;
		}

		/// <summary>
		/// 读取WAV，解析成float[]采样（兼容带INFO元信息，仅16bit PCM）
		/// </summary>
		private static AudioSampleData LoadWavToSamples(string filePath)
		{
			try
			{
				byte[] raw = File.ReadAllBytes(filePath);
				int pos = 12;

				ushort channels = 0;
				int sampleRate = 0;
				ushort bitDepth = 0;
				int dataOffset = 0;
				uint dataSize = 0;

				while (pos + 8 <= raw.Length)
				{
					string chunkId = System.Text.Encoding.ASCII.GetString(raw, pos, 4);
					uint chunkSize = BitConverter.ToUInt32(raw, pos + 4);

					if (chunkId == "fmt ")
					{
						channels = BitConverter.ToUInt16(raw, pos + 8 + 2);
						sampleRate = BitConverter.ToInt32(raw, pos + 8 + 4);
						bitDepth = BitConverter.ToUInt16(raw, pos + 8 + 14);
					}
					else if (chunkId == "data")
					{
						dataOffset = pos + 8;
						dataSize = chunkSize;
						break;
					}
					pos += (int)chunkSize + 8;
					if ((chunkSize & 1) != 0) pos++;
				}

				if (dataOffset <= 0 || dataSize <= 0)
				{
					MelonLogger.Error($"[{filePath}] 找不到wav data音频块");
					return null;
				}
				if (bitDepth != 16)
				{
					MelonLogger.Error($"[{filePath}] 仅支持16bit PCM WAV，当前bit:{bitDepth}");
					return null;
				}

				int bytesPerSample = bitDepth / 8;
				int totalSampleCount = (int)dataSize / bytesPerSample / channels;
				float[] samples = new float[totalSampleCount * channels];

				int sampleIndex = 0;
				for (int i = dataOffset; i < dataOffset + dataSize; i += 2)
				{
					short pcmValue = BitConverter.ToInt16(raw, i);
					samples[sampleIndex++] = pcmValue / 32768f;
				}

				return new AudioSampleData
				{
					Samples = samples,
					Channels = channels,
					SampleRate = sampleRate,
					LastAccessTime = DateTime.Now
				};
			}
			catch (Exception ex)
			{
				MelonLogger.Error($"[{filePath}] 读取WAV异常: {ex.Message}\n{ex.StackTrace}");
				return null;
			}
		}

		/// <summary>
		/// LRU淘汰：移除最久未访问的一条采样缓存
		/// </summary>
		private static void EvictLRU()
		{
			var oldest = _sampleCache.OrderBy(kvp => kvp.Value.LastAccessTime).First();
			_sampleCache.Remove(oldest.Key);
			MelonLogger.Msg($"LRU淘汰采样缓存：{Path.GetFileName(oldest.Key)}");
		}

		/// <summary>
		/// 播放结束清理AudioSource物体和临时AudioClip
		/// </summary>
		private static IEnumerator CleanupSfx(GameObject go, AudioClip clip, float duration)
		{
			yield return new WaitForSeconds(duration + 0.1f);
			UnityEngine.Object.Destroy(go);
			clip.UnloadAudioData();
		}

		/// <summary>
		/// 清空全部采样缓存
		/// </summary>
		public static void ClearSampleCache()
		{
			_sampleCache.Clear();
			MelonLogger.Msg("全部音频采样缓存已清空");
		}

		/// <summary>
		/// Mod卸载销毁持久对象
		/// </summary>
		public static void DestroyAudioRoot()
		{
			if (_audioRoot != null)
			{
				UnityEngine.Object.Destroy(_audioRoot);
				_audioRoot = null;
			}
			ClearSampleCache();
		}
	}
}
