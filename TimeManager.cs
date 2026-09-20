using MelonLoader;
using UnityEngine;
using System.Collections.Generic;
using System;

namespace SC_Tools
{
	public static class TimeManager
	{
		public static Dictionary<string, TimeManagerInstance> instanceList = new Dictionary<string, TimeManagerInstance>();
		/// <summary>
		/// 检查是否可以执行动作，满足冷却返回true，并立刻开启冷却
		/// </summary>
		/// <param name="actionName">动作唯一标识</param>
		/// <param name="cooldownTime">冷却秒数</param>
		/// <param name="resetCooldown">true=强制重置冷却并返回true</param>
		/// <returns>是否允许执行</returns>
		public static bool GetCanAction(string actionName, float cooldownTime, bool resetCooldown = false)
		{
			if (!instanceList.TryGetValue(actionName, out var instance))
			{
				instance = new TimeManagerInstance(cooldownTime);
				instanceList.Add(actionName, instance);
				return true;
			}
			// 每次调用GetCanAction同步更新冷却时长，支持动态改cd
			instance.SetCooldown(cooldownTime);
			return instance.CanAction(resetCooldown);
		}

		/// <summary>
		/// 重置冷却
		/// 存在实例：更新冷却时长 + 重置冷却起点
		/// 不存在实例：新建实例，设置冷却时间，并立刻进入冷却
		/// </summary>
		/// <param name="actionName">动作标识</param>
		/// <param name="cooldownTime">冷却秒数</param>
		public static void ResetCooldown(string actionName, float cooldownTime)
		{
			if (!instanceList.TryGetValue(actionName, out var instance))
			{
				// 不存在就新建实例
				instance = new TimeManagerInstance(cooldownTime);
				instanceList.Add(actionName, instance);
			}
			else
			{
				// 存在实例：更新冷却时长
				instance.SetCooldown(cooldownTime);
			}
			// 重置冷却起点，立刻进入冷却
			instance.ResetCooldown();
		}

		// 手动清除某个冷却实例
		public static void RemoveInstance(string actionName)
		{
			instanceList.Remove(actionName);
		}

		// 清空全部
		public static void ClearAll()
		{
			instanceList.Clear();
		}
	}

	public class TimeManagerInstance
	{
		private float TimeNow => Time.time;
		private float _cooldown; // 移除readonly，支持动态修改冷却
		private float _lastTime;

		public TimeManagerInstance(float cooldown)
		{
			_cooldown = cooldown;
			_lastTime = TimeNow;
		}

		public bool CanAction(bool resetCooldown = false)
		{
			if (resetCooldown)
			{
				ResetCooldown();
				return true;
			}

			if (TimeNow - _lastTime < _cooldown)
				return false;

			_lastTime = TimeNow;
			return true;
		}

		public void ResetCooldown()
		{
			_lastTime = TimeNow;
		}

		public void SetCooldown(float newCooldown)
		{
			_cooldown = newCooldown;
		}
	}
}
