using System;
using System.Collections.Generic;
using System.IO;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;

namespace ImagePopupMod
{
	/// <summary>
	/// 普通类，不继承 MelonMod。由外部类调用 Init / LoadImages / Show。
	/// 常驻 Canvas，每张纹理对应一个常驻 RawImage 槽位，防止纹理随场景卸载。
	/// </summary>
	public class ImagePopup
	{
		// ---- 动画参数 ----
		private const float FadeTime = 0.2f;
		private const float HoldTime = 1f;

		private enum AnimState { Idle, FadeIn, Hold, FadeOut }

		// ---- 内部状态 ----
		private Canvas _canvas;
		private GameObject _canvasObj;
		private bool _initialized;

		private readonly List<Texture2D> _textures = new();
		private readonly List<RawImage> _slots = new();   // 每张纹理一个常驻槽位

		private RawImage _activeSlot;   // 当前正在显示的槽位

		private AnimState _animState = AnimState.Idle;
		private float _alpha;
		private float _timer;

		// 屏幕尺寸的 1/3 作为最长边
		private float TargetSize => Mathf.Min(Screen.width, Screen.height) / 3f;

        // ---- 帧动画状态 ----
        private RawImage _seqSlot;              // 专门用于帧序列的槽位
        private Texture2D _seqTex;
        private int _seqFrameW, _seqFrameH;     // 单帧像素尺寸
        private int _seqCols, _seqRows;         // 大图的行列数
        private int _seqTotalFrames;            // 总帧数
        private int _seqCurrentFrame;
        private float _seqFrameDelay;           // 每帧延迟秒
        private float _seqTimer;
        private bool _seqPlaying;

        /// <summary>
        /// 初始化 Canvas。只需调用一次。Canvas 常驻，不随场景销毁。
        /// </summary>
        public void Init()
		{
			if (_initialized) return;
			_initialized = true;

			_canvasObj = new GameObject("SC-Pictures_Canvas");
			_canvas = _canvasObj.AddComponent<Canvas>();
			_canvas.renderMode = RenderMode.ScreenSpaceOverlay;
			_canvas.sortingOrder = 32001;
			_canvasObj.AddComponent<CanvasScaler>();
			// 不需要 GraphicRaycaster，因为图片不交互

			UnityEngine.Object.DontDestroyOnLoad(_canvasObj);
		}

		/// <summary>
		/// 批量加载图片。每张成功加载的纹理会创建一个常驻槽位。
		/// 返回成功加载的数量。
		/// </summary>
		public int LoadImages(IEnumerable<string> filePaths)
		{
			Init();

			// 清掉旧的槽位和纹理
			ReleaseAll();

			int ok = 0;
			foreach (var path in filePaths)
			{
				var tex = LoadSingleTexture(path);
				if (tex == null) continue;

				_textures.Add(tex);
				_slots.Add(CreateSlot(tex));
				ok++;
			}

			MelonLogger.Msg($"🖼️ 已加载 {ok} 张图片");
			return ok;
		}

		/// <summary>
		/// 为一张纹理创建一个常驻 RawImage 槽位。默认隐藏。
		/// 槽位的 texture 字段始终持有纹理引用，防止纹理被场景卸载。
		/// </summary>
		private RawImage CreateSlot(Texture2D tex)
		{
			var obj = new GameObject("Slot_" + tex.name);
			obj.transform.SetParent(_canvasObj.transform, false);

			var img = obj.AddComponent<RawImage>();
			img.raycastTarget = false;
			img.texture = tex;
			img.color = new Color(1, 1, 1, 0);
			img.enabled = false;

			var rect = obj.GetComponent<RectTransform>();
			rect.anchorMin = new Vector2(0.5f, 0.5f);
			rect.anchorMax = new Vector2(0.5f, 0.5f);
			rect.pivot = new Vector2(0.5f, 0.5f);
			rect.anchoredPosition = Vector2.zero;

			return img;
		}

		/// <summary>
		/// 加载单张图片，失败返回 null。
		/// </summary>
		private Texture2D LoadSingleTexture(string filePath)
		{
			if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
			{
				MelonLogger.Error($"❌ 图片不存在：{filePath}");
				return null;
			}

			try
			{
				byte[] bytes = File.ReadAllBytes(filePath);
				var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
				tex.name = Path.GetFileName(filePath);

				if (!tex.LoadImage(bytes))
				{
					MelonLogger.Error($"❌ 解析失败：{filePath}");
					UnityEngine.Object.Destroy(tex);
					return null;
				}

				tex.filterMode = FilterMode.Point;
				return tex;
			}
			catch (Exception ex)
			{
				MelonLogger.Error($"❌ 加载异常 {filePath}: {ex.Message}");
				return null;
			}
		}

		/// <summary>
		/// 显示指定索引的图片。隐藏其他槽位，激活目标槽位。
		/// 缩放到屏幕 1/3 并保持比例，中心跟随鼠标，重新触发淡入淡出。
		/// </summary>
		public void Show(int index)
		{
			Init();

			if (index < 0 || index >= _slots.Count)
			{
				MelonLogger.Warning($"⚠️ 图片索引越界：{index}（共 {_slots.Count} 张）");
				return;
			}

			var tex = _textures[index];
			var slot = _slots[index];
			if (tex == null || slot == null) return;

			// 隐藏其他槽位
			for (int i = 0; i < _slots.Count; i++)
			{
				if (_slots[i] != null) _slots[i].enabled = false;
			}

			// 激活目标槽位
			slot.enabled = true;
			slot.texture = tex;
			_activeSlot = slot;

			ApplySizeAndPosition(slot, tex);

			// 重启动画
			_animState = AnimState.FadeIn;
			_timer = 0f;
			_alpha = 0f;
			slot.color = new Color(1, 1, 1, 0);
		}

		/// <summary>
		/// 根据纹理尺寸和鼠标位置，设置槽位大小和位置。
		/// </summary>
		private void ApplySizeAndPosition(RawImage slot, Texture2D tex)
		{
			if (slot == null || tex == null) return;

			var rect = slot.rectTransform;

			// 保持比例，最长边 = 屏幕 1/3
			float aspect = (float)tex.width / tex.height;
			float w, h;
			if (tex.width >= tex.height)
			{
				w = TargetSize;
				h = TargetSize / aspect;
			}
			else
			{
				h = TargetSize;
				w = TargetSize * aspect;
			}
			rect.sizeDelta = new Vector2(w, h);

			// 中心跟随鼠标
			Vector2 mouse = Input.mousePosition;
			rect.anchoredPosition = new Vector2(
				mouse.x - Screen.width * 0.5f,
				mouse.y - Screen.height * 0.5f);
		}

		/// <summary>
		/// 每帧调用，驱动动画。外部类在自己的 OnUpdate 里转发即可。
		/// </summary>
		public void Update()
		{
            // 帧序列动画
            if (_seqPlaying && _seqSlot != null)
            {
                _seqTimer += Time.deltaTime;
                if (_seqTimer >= _seqFrameDelay)
                {
                    _seqTimer -= _seqFrameDelay;
                    _seqCurrentFrame++;
                    if (_seqCurrentFrame >= _seqTotalFrames)
                    {
                        _seqPlaying = false;
                        _seqSlot.enabled = false;
                    }
                    else
                    {
                        ApplySequenceUv();
                    }
                }
            }

            if (!_initialized || _animState == AnimState.Idle) return;
			if (_activeSlot == null) return;

			_timer += Time.deltaTime;

			switch (_animState)
			{
				case AnimState.FadeIn:
					_alpha = Mathf.Clamp01(_timer / FadeTime);
					if (_timer >= FadeTime)
					{
						_animState = AnimState.Hold;
						_timer = 0f;
					}
					break;

				case AnimState.Hold:
					_alpha = 1f;
					if (_timer >= HoldTime)
					{
						_animState = AnimState.FadeOut;
						_timer = 0f;
					}
					break;

				case AnimState.FadeOut:
					_alpha = 1 - Mathf.Clamp01(_timer / FadeTime);
					if (_timer >= FadeTime)
					{
						_animState = AnimState.Idle;
						_alpha = 0f;
						// 动画结束，隐藏槽位
						if (_activeSlot != null) _activeSlot.enabled = false;
						_activeSlot = null;
					}
					break;
			}

			if (_activeSlot != null)
				_activeSlot.color = new Color(1, 1, 1, _alpha);
		}

		/// <summary>
		/// 释放所有纹理和 UI。外部类在卸载时调用。
		/// </summary>
		public void Dispose()
		{
			ReleaseAll();

			if (_canvasObj != null)
			{
				UnityEngine.Object.Destroy(_canvasObj);
				_canvasObj = null;
			}
			_canvas = null;
			_activeSlot = null;
			_initialized = false;
		}

		/// <summary>
		/// 清掉所有槽位和纹理。
		/// </summary>
		private void ReleaseAll()
		{
			foreach (var slot in _slots)
			{
				if (slot != null) UnityEngine.Object.Destroy(slot.gameObject);
			}
			_slots.Clear();

			foreach (var tex in _textures)
			{
				if (tex != null) UnityEngine.Object.Destroy(tex);
			}
			_textures.Clear();

			_activeSlot = null;
			_animState = AnimState.Idle;
			_alpha = 0f;
		}

        public void ShowAt(int index, float screenX, float screenY)
        {
            Show(index);
            if (_activeSlot == null) return;

			float xMargin = (Screen.width - 16f / 9f * Screen.height) / 2f;
            float xSplit = (Screen.width - xMargin * 2f) / 9f;
            float setX = xSplit * 2 + screenX / 720f * (xSplit * 5) + xMargin;

            float ySplit = Screen.height / 12f;
            float setY = ySplit * 3 + screenY / 600f * (ySplit * 8);

            // 你的坐标是左上角原点、Y 向下
            // Unity 屏幕坐标是左下角原点、Y 向上，所以翻转 Y
            float unityY = Screen.height - setY;

            _activeSlot.rectTransform.anchoredPosition = new Vector2(
                setX - Screen.width * 0.5f,
                unityY - Screen.height * 0.5f);
        }

        /// <summary>
        /// 播放全屏帧序列动画。
        /// </summary>
        /// <param name="textureIndex">_textures 中的索引</param>
        /// <param name="frameWidth">单帧宽度（像素）</param>
        /// <param name="frameHeight">单帧高度（像素）</param>
        /// <param name="frameCount">总帧数</param>
        /// <param name="frameDelay">每帧延迟（秒）</param>
        /// <param name="loop">是否循环</param>
        public void PlaySequence(int textureIndex, int frameWidth, int frameHeight,
                                 int frameCount, float frameDelay, bool loop = false)
        {
            Init();

            if (textureIndex < 0 || textureIndex >= _textures.Count)
            {
                MelonLogger.Warning($"⚠️ 帧序列纹理索引越界：{textureIndex}");
                return;
            }

            var tex = _textures[textureIndex];
            if (tex == null) return;

            // 隐藏其他槽位
            for (int i = 0; i < _slots.Count; i++)
                if (_slots[i] != null) _slots[i].enabled = false;

            // 创建或复用一个全屏槽位
            if (_seqSlot == null)
            {
                var obj = new GameObject("SequenceSlot");
                obj.transform.SetParent(_canvasObj.transform, false);
                _seqSlot = obj.AddComponent<RawImage>();
                _seqSlot.raycastTarget = false;

                var rect = obj.GetComponent<RectTransform>();
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = Vector2.zero;
                rect.sizeDelta = Vector2.zero;   // 铺满父级（Canvas）
            }

            _seqSlot.enabled = true;
            _seqSlot.texture = tex;
            _seqSlot.color = Color.white;

            _seqTex = tex;
            _seqFrameW = frameWidth;
            _seqFrameH = frameHeight;
            _seqCols = tex.width / frameWidth;
            _seqRows = tex.height / frameHeight;
            _seqTotalFrames = Mathf.Min(frameCount, _seqCols * _seqRows);
            _seqFrameDelay = frameDelay;
            _seqCurrentFrame = 0;
            _seqTimer = 0f;
            _seqPlaying = true;

            ApplySequenceUv();
        }

        private void ApplySequenceUv()
        {
            if (_seqSlot == null || _seqTex == null) return;

            int col = _seqCurrentFrame % _seqCols;
            int row = _seqCurrentFrame / _seqCols;

            // uvRect 原点在左下角，行从下往上数，所以要翻转行
            float u = (float)col / _seqCols;
            float v = 1f - (float)(row + 1) / _seqRows;
            float w = 1f / _seqCols;
            float h = 1f / _seqRows;

            _seqSlot.uvRect = new Rect(u, v, w, h);
        }
    }
}