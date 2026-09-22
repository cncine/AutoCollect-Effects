using System;
using System.Collections.Generic;
using System.IO;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;

namespace ImagePopupMod
{
    public class ImagePopup
    {
        // ---- 常量 ----
        private const float DefaultFadeTime = 0.2f;
        private const float DefaultHoldTime = 1f;
        private const float DefaultTargetSizeDiv = 3f;

        // ---- 内部状态 ----
        private Canvas _canvas;
        private GameObject _canvasObj;
        private GameObject _flashCanvasObj;   // Flash 专用 Canvas（层级更低）
        private bool _initialized;

        private readonly List<Texture2D> _textures = new();
        private readonly List<RawImage> _slots = new();

        // 正在播放的实例
        private readonly List<PopupInstance> _instances = new();
        private readonly List<SequenceInstance> _sequences = new();
        private readonly List<FlashInstance> _flashes = new();

        private float TargetSize(float div) => Mathf.Min(Screen.width, Screen.height) / div;

        // =========================================================
        //  初始化
        // =========================================================
        public void Init()
        {
            if (_initialized) return;
            _initialized = true;

            _canvasObj = new GameObject("SC-Pictures_Canvas");
            _canvas = _canvasObj.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 32001;
            _canvasObj.AddComponent<CanvasScaler>();

            UnityEngine.Object.DontDestroyOnLoad(_canvasObj);
        }

        private void EnsureFlashCanvas()
        {
            if (_flashCanvasObj != null) return;

            _flashCanvasObj = new GameObject("SC-Pictures_FlashCanvas");
            var fc = _flashCanvasObj.AddComponent<Canvas>();
            fc.renderMode = RenderMode.ScreenSpaceOverlay;
            fc.sortingOrder = 32000;   // 主 Canvas 是 32001，低一层
            _flashCanvasObj.AddComponent<CanvasScaler>();

            UnityEngine.Object.DontDestroyOnLoad(_flashCanvasObj);
        }

        // =========================================================
        //  加载纹理
        // =========================================================
        public int LoadImages(IEnumerable<string> filePaths)
        {
            Init();
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

        // =========================================================
        //  单图显示 Show / ShowAt
        // =========================================================
        public void Show(int index, float fadeTime = DefaultFadeTime,
                         float holdTime = DefaultHoldTime, float targetSizeDiv = DefaultTargetSizeDiv)
        {
            ShowInternal(index, null, null, fadeTime, holdTime, targetSizeDiv, null, null);
        }

        public void ShowAt(int index, float screenX, float screenY,
                           float fadeTime = DefaultFadeTime,
                           float holdTime = DefaultHoldTime,
                           float targetSizeDiv = DefaultTargetSizeDiv)
        {
            ShowInternal(index, screenX, screenY, fadeTime, holdTime, targetSizeDiv, null, null);
        }

        /// <summary>
        /// 带终点移动的 ShowAt。移动贯穿 FadeIn + Hold + FadeOut 全过程。
        /// </summary>
        public void ShowAt(int index, float screenX, float screenY,
                           float endX, float endY,
                           float fadeTime = DefaultFadeTime,
                           float holdTime = DefaultHoldTime,
                           float targetSizeDiv = DefaultTargetSizeDiv)
        {
            ShowInternal(index, screenX, screenY, fadeTime, holdTime, targetSizeDiv, endX, endY);
        }

        private void ShowInternal(int index, float? startX, float? startY,
                                  float fadeTime, float holdTime, float targetSizeDiv,
                                  float? endX, float? endY)
        {
            Init();

            if (index < 0 || index >= _textures.Count)
            {
                MelonLogger.Warning($"⚠️ 图片索引越界：{index}");
                return;
            }

            var tex = _textures[index];
            if (tex == null) return;

            var slot = CreateSlot(tex);
            slot.enabled = true;
            slot.texture = tex;
            slot.color = new Color(1, 1, 1, 0);

            ApplySize(slot, tex, targetSizeDiv);

            Vector2 startPos;
            if (startX.HasValue && startY.HasValue)
            {
                startPos = LogicalToAnchored(startX.Value, startY.Value);
                slot.rectTransform.anchoredPosition = startPos;
            }
            else
            {
                Vector2 mouse = Input.mousePosition;
                startPos = new Vector2(mouse.x - Screen.width * 0.5f,
                                       mouse.y - Screen.height * 0.5f);
                slot.rectTransform.anchoredPosition = startPos;
            }

            var inst = new PopupInstance
            {
                Slot = slot,
                FadeTime = fadeTime,
                HoldTime = holdTime,
                Alpha = 0f,
                Timer = 0f,
                TotalElapsed = 0f,
                State = AnimState.FadeIn,
            };

            if (endX.HasValue && endY.HasValue)
            {
                inst.StartPos = startPos;
                inst.EndPos = LogicalToAnchored(endX.Value, endY.Value);
                inst.HasMove = true;
            }

            _instances.Add(inst);
        }

        // =========================================================
        //  帧序列 PlaySequence / PlaySequenceAt
        // =========================================================
        /// <summary>
        /// 播放帧序列。targetSizeDiv 不传（0）时全屏。
        /// </summary>
        public void PlaySequence(int textureIndex, int frameWidth, int frameHeight,
                                 int frameCount, float frameDelay, bool loop = false,
                                 float targetSizeDiv = 0f)
        {
            PlaySequenceInternal(textureIndex, frameWidth, frameHeight, frameCount,
                                 frameDelay, loop, null, null, targetSizeDiv, null, null);
        }

        public void PlaySequenceAt(int textureIndex, int frameWidth, int frameHeight,
                                   int frameCount, float frameDelay,
                                   float screenX, float screenY,
                                   bool loop = false,
                                   float targetSizeDiv = DefaultTargetSizeDiv)
        {
            PlaySequenceInternal(textureIndex, frameWidth, frameHeight, frameCount,
                                 frameDelay, loop, screenX, screenY, targetSizeDiv, null, null);
        }

        /// <summary>
        /// 带终点移动的 PlaySequenceAt。移动贯穿整个帧序列播放过程。
        /// </summary>
        public void PlaySequenceAt(int textureIndex, int frameWidth, int frameHeight,
                                   int frameCount, float frameDelay,
                                   float screenX, float screenY,
                                   float endX, float endY,
                                   bool loop = false,
                                   float targetSizeDiv = DefaultTargetSizeDiv)
        {
            PlaySequenceInternal(textureIndex, frameWidth, frameHeight, frameCount,
                                 frameDelay, loop, screenX, screenY, targetSizeDiv, endX, endY);
        }

        private void PlaySequenceInternal(int textureIndex, int frameWidth, int frameHeight,
                                  int frameCount, float frameDelay, bool loop,
                                  float? screenX, float? screenY,
                                  float targetSizeDiv,
                                  float? endX, float? endY)
        {
            Init();

            if (textureIndex < 0 || textureIndex >= _textures.Count)
            {
                MelonLogger.Warning($"⚠️ 帧序列纹理索引越界：{textureIndex}");
                return;
            }

            var tex = _textures[textureIndex];
            if (tex == null) return;

            var obj = new GameObject("SeqSlot");
            obj.transform.SetParent(_canvasObj.transform, false);
            var slot = obj.AddComponent<RawImage>();
            slot.raycastTarget = false;
            slot.texture = tex;
            slot.color = Color.white;

            var rect = obj.GetComponent<RectTransform>();
            bool isFullscreen = targetSizeDiv <= 0f;

            Vector2 startPos = Vector2.zero;

            if (isFullscreen)
            {
                // 全屏：铺满 Canvas
                rect.anchorMin = Vector2.zero;   // (0, 0)
                rect.anchorMax = Vector2.one;    // (1, 1)
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = Vector2.zero;
                rect.sizeDelta = Vector2.zero;
            }
            else
            {
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);

                float aspect = (float)frameWidth / frameHeight;
                float target = TargetSize(targetSizeDiv);
                float w, h;
                if (frameWidth >= frameHeight) { w = target; h = target / aspect; }
                else { h = target; w = target * aspect; }
                rect.sizeDelta = new Vector2(w, h);

                if (screenX.HasValue && screenY.HasValue)
                {
                    startPos = LogicalToAnchored(screenX.Value, screenY.Value);
                }
                else
                {
                    Vector2 mouse = Input.mousePosition;
                    startPos = new Vector2(mouse.x - Screen.width * 0.5f,
                                           mouse.y - Screen.height * 0.5f);
                }
                rect.anchoredPosition = startPos;
            }

            int cols = tex.width / frameWidth;
            int rows = tex.height / frameHeight;

            var seq = new SequenceInstance
            {
                Slot = slot,
                FrameW = frameWidth,
                FrameH = frameHeight,
                Cols = cols,
                Rows = rows,
                TotalFrames = Mathf.Min(frameCount, cols * rows),
                FrameDelay = frameDelay,
                CurrentFrame = 0,
                Timer = 0f,
                TotalElapsed = 0f,
                Playing = true,
                Loop = loop,
                IsFullscreen = isFullscreen,
                StartPos = startPos,
            };

            if (endX.HasValue && endY.HasValue)
            {
                seq.EndPos = LogicalToAnchored(endX.Value, endY.Value);
                seq.HasMove = true;
            }

            _sequences.Add(seq);
            ApplySequenceUv(seq);
        }

        private void ApplySequenceUv(SequenceInstance seq)
        {
            if (seq?.Slot == null) return;
            int col = seq.CurrentFrame % seq.Cols;
            int row = seq.CurrentFrame / seq.Cols;

            float u = (float)col / seq.Cols;
            float v = 1f - (float)(row + 1) / seq.Rows;
            float w = 1f / seq.Cols;
            float h = 1f / seq.Rows;

            seq.Slot.uvRect = new Rect(u, v, w, h);
        }

        // =========================================================
        //  全屏纯色闪烁（独立 Canvas，层级在主 Canvas 之下）
        // =========================================================
        /// <summary>
        /// 全屏纯色闪烁。colorHex 形如 "FF0000" 或 "#FF0000"。
        /// </summary>
        public void Flash(string colorHex,
                          float fadeTime = 0.2f,
                          float holdTime = 0.5f)
        {
            Init();
            EnsureFlashCanvas();

            Color color;
            if (!ColorUtility.TryParseHtmlString(
                    colorHex.StartsWith("#") ? colorHex : "#" + colorHex, out color))
            {
                MelonLogger.Warning($"⚠️ 无法解析颜色：{colorHex}");
                return;
            }

            var obj = new GameObject("FlashSlot");
            obj.transform.SetParent(_flashCanvasObj.transform, false);
            var img = obj.AddComponent<RawImage>();
            img.raycastTarget = false;
            img.texture = Texture2D.whiteTexture;
            img.color = new Color(color.r, color.g, color.b, 0f);

            var rect = obj.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;

            _flashes.Add(new FlashInstance
            {
                Slot = img,
                Color = color,
                FadeTime = fadeTime,
                HoldTime = holdTime,
                Alpha = 0f,
                Timer = 0f,
                State = AnimState.FadeIn,
            });
        }

        // =========================================================
        //  坐标转换
        // =========================================================
        private Vector2 LogicalToAnchored(float screenX, float screenY)
        {
            float xMargin = (Screen.width - 16f / 9f * Screen.height) / 2f;
            float xSplit = (Screen.width - xMargin * 2f) / 9f;
            float setX = xSplit * 2 + screenX / 720f * (xSplit * 5) + xMargin;

            float ySplit = Screen.height / 12f;
            float setY = ySplit * 3 + screenY / 600f * (ySplit * 8);

            float unityY = Screen.height - setY;

            return new Vector2(setX - Screen.width * 0.5f,
                               unityY - Screen.height * 0.5f);
        }

        // =========================================================
        //  尺寸
        // =========================================================
        private void ApplySize(RawImage slot, Texture2D tex, float targetSizeDiv)
        {
            if (slot == null || tex == null) return;
            var rect = slot.rectTransform;

            float aspect = (float)tex.width / tex.height;
            float target = TargetSize(targetSizeDiv);
            float w, h;
            if (tex.width >= tex.height) { w = target; h = target / aspect; }
            else { h = target; w = target * aspect; }
            rect.sizeDelta = new Vector2(w, h);
        }

        // =========================================================
        //  每帧更新
        // =========================================================
        public void Update()
        {
            if (!_initialized) return;
            float dt = Time.deltaTime;

            for (int i = _instances.Count - 1; i >= 0; i--)
            {
                var inst = _instances[i];
                if (inst.Slot == null) { _instances.RemoveAt(i); continue; }
                UpdatePopupInstance(inst, dt);
                if (inst.State == AnimState.Idle)
                {
                    if (inst.Slot != null) UnityEngine.Object.Destroy(inst.Slot.gameObject);
                    _instances.RemoveAt(i);
                }
            }

            for (int i = _sequences.Count - 1; i >= 0; i--)
            {
                var seq = _sequences[i];
                if (seq.Slot == null) { _sequences.RemoveAt(i); continue; }
                UpdateSequence(seq, dt);
                if (!seq.Playing)
                {
                    if (seq.Slot != null) UnityEngine.Object.Destroy(seq.Slot.gameObject);
                    _sequences.RemoveAt(i);
                }
            }

            for (int i = _flashes.Count - 1; i >= 0; i--)
            {
                var f = _flashes[i];
                if (f.Slot == null) { _flashes.RemoveAt(i); continue; }
                UpdateFlash(f, dt);
                if (f.State == AnimState.Idle)
                {
                    if (f.Slot != null) UnityEngine.Object.Destroy(f.Slot.gameObject);
                    _flashes.RemoveAt(i);
                }
            }
        }

        private void UpdatePopupInstance(PopupInstance inst, float dt)
        {
            inst.Timer += dt;
            inst.TotalElapsed += dt;

            // 移动：贯穿整个生命周期
            if (inst.HasMove && inst.Slot != null)
            {
                float total = inst.FadeTime * 2f + inst.HoldTime;
                float t = total > 0f ? Mathf.Clamp01(inst.TotalElapsed / total) : 1f;
                inst.Slot.rectTransform.anchoredPosition =
                    Vector2.Lerp(inst.StartPos, inst.EndPos, t);
            }

            switch (inst.State)
            {
                case AnimState.FadeIn:
                    inst.Alpha = Mathf.Clamp01(inst.Timer / inst.FadeTime);
                    if (inst.Timer >= inst.FadeTime)
                    {
                        inst.State = AnimState.Hold;
                        inst.Timer = 0f;
                    }
                    break;
                case AnimState.Hold:
                    inst.Alpha = 1f;
                    if (inst.Timer >= inst.HoldTime)
                    {
                        inst.State = AnimState.FadeOut;
                        inst.Timer = 0f;
                    }
                    break;
                case AnimState.FadeOut:
                    inst.Alpha = 1 - Mathf.Clamp01(inst.Timer / inst.FadeTime);
                    if (inst.Timer >= inst.FadeTime)
                    {
                        inst.State = AnimState.Idle;
                        inst.Alpha = 0f;
                    }
                    break;
            }

            if (inst.Slot != null)
                inst.Slot.color = new Color(1, 1, 1, inst.Alpha);
        }

        private void UpdateSequence(SequenceInstance seq, float dt)
        {
            seq.TotalElapsed += dt;

            // 移动：贯穿整个序列（全屏时不移动）
            if (seq.HasMove && !seq.IsFullscreen && seq.Slot != null && seq.TotalFrames > 0)
            {
                float total = seq.FrameDelay * seq.TotalFrames;
                float t = total > 0f ? Mathf.Clamp01(seq.TotalElapsed / total) : 1f;
                seq.Slot.rectTransform.anchoredPosition =
                    Vector2.Lerp(seq.StartPos, seq.EndPos, t);
            }

            seq.Timer += dt;
            if (seq.Timer >= seq.FrameDelay)
            {
                seq.Timer -= seq.FrameDelay;
                seq.CurrentFrame++;
                if (seq.CurrentFrame >= seq.TotalFrames)
                {
                    if (seq.Loop) seq.CurrentFrame = 0;
                    else { seq.Playing = false; return; }
                }
                ApplySequenceUv(seq);
            }
        }

        private void UpdateFlash(FlashInstance f, float dt)
        {
            f.Timer += dt;
            switch (f.State)
            {
                case AnimState.FadeIn:
                    f.Alpha = Mathf.Clamp01(f.Timer / f.FadeTime);
                    if (f.Timer >= f.FadeTime) { f.State = AnimState.Hold; f.Timer = 0f; }
                    break;
                case AnimState.Hold:
                    f.Alpha = 1f;
                    if (f.Timer >= f.HoldTime) { f.State = AnimState.FadeOut; f.Timer = 0f; }
                    break;
                case AnimState.FadeOut:
                    f.Alpha = 1 - Mathf.Clamp01(f.Timer / f.FadeTime);
                    if (f.Timer >= f.FadeTime) { f.State = AnimState.Idle; f.Alpha = 0f; }
                    break;
            }
            if (f.Slot != null)
                f.Slot.color = new Color(f.Color.r, f.Color.g, f.Color.b, f.Alpha);
        }

        // =========================================================
        //  场景切换
        // =========================================================
        public void OnSceneChanged()
        {
            foreach (var inst in _instances)
                if (inst.Slot != null) UnityEngine.Object.Destroy(inst.Slot.gameObject);
            _instances.Clear();

            foreach (var seq in _sequences)
                if (seq.Slot != null) UnityEngine.Object.Destroy(seq.Slot.gameObject);
            _sequences.Clear();

            foreach (var f in _flashes)
                if (f.Slot != null) UnityEngine.Object.Destroy(f.Slot.gameObject);
            _flashes.Clear();
        }

        // =========================================================
        //  释放
        // =========================================================
        public void Dispose()
        {
            ReleaseAll();

            if (_canvasObj != null) { UnityEngine.Object.Destroy(_canvasObj); _canvasObj = null; }
            if (_flashCanvasObj != null) { UnityEngine.Object.Destroy(_flashCanvasObj); _flashCanvasObj = null; }
            _canvas = null;
            _initialized = false;
        }

        private void ReleaseAll()
        {
            OnSceneChanged();

            foreach (var slot in _slots)
                if (slot != null) UnityEngine.Object.Destroy(slot.gameObject);
            _slots.Clear();

            foreach (var tex in _textures)
                if (tex != null) UnityEngine.Object.Destroy(tex);
            _textures.Clear();
        }

        // =========================================================
        //  内部数据结构
        // =========================================================
        private enum AnimState { Idle, FadeIn, Hold, FadeOut }

        private class PopupInstance
        {
            public RawImage Slot;
            public float FadeTime;
            public float HoldTime;
            public float Alpha;
            public float Timer;
            public float TotalElapsed;
            public AnimState State;
            public bool HasMove;
            public Vector2 StartPos;
            public Vector2 EndPos;
        }

        private class SequenceInstance
        {
            public RawImage Slot;
            public int FrameW, FrameH;
            public int Cols, Rows;
            public int TotalFrames;
            public int CurrentFrame;
            public float FrameDelay;
            public float Timer;
            public float TotalElapsed;
            public bool Playing;
            public bool Loop;
            public bool IsFullscreen;
            public bool HasMove;
            public Vector2 StartPos;
            public Vector2 EndPos;
        }

        private class FlashInstance
        {
            public RawImage Slot;
            public Color Color;
            public float FadeTime;
            public float HoldTime;
            public float Alpha;
            public float Timer;
            public AnimState State;
        }
    }
}