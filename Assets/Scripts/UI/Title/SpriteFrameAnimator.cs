using UnityEngine;
using UnityEngine.UI;

namespace RunningGame.UI
{
    /// <summary>
    /// 序列帧动画：按给定帧率循环播放一组 Sprite，驱动同一物体上的
    /// UI Image（优先）或 SpriteRenderer。标题页会大量使用 unscaled 时间，
    /// 因此默认不随 timeScale 暂停。可在 Inspector 里直接增删 frames、调帧率。
    /// </summary>
    [DisallowMultipleComponent]
    public class SpriteFrameAnimator : MonoBehaviour
    {
        [Header("帧序列（按顺序播放）")]
        public Sprite[] frames;

        [Header("播放参数")]
        [Tooltip("每秒播放的帧数。<= 0 时保持第一帧不动。")]
        public float framesPerSecond = 12f;
        public bool loop = true;
        public bool playOnAwake = true;
        [Tooltip("开启后从随机一帧开始，多个同类动画不会完全同步。")]
        public bool randomizeStartFrame = false;
        [Tooltip("使用 unscaled 时间，暂停(timeScale=0)时仍然播放。标题页应为 true。")]
        public bool useUnscaledTime = true;

        private Image _image;
        private SpriteRenderer _spriteRenderer;
        private float _timer;
        private int _index;
        private bool _playing;

        public int CurrentFrame => _index;
        public bool IsPlaying => _playing;

        private void Awake()
        {
            _image = GetComponent<Image>();
            _spriteRenderer = GetComponent<SpriteRenderer>();
            _index = 0;
            _timer = 0f;
            _playing = playOnAwake;

            if (randomizeStartFrame && frames != null && frames.Length > 0)
                _index = Random.Range(0, frames.Length);

            ApplyFrame(_index);
        }

        private void OnEnable()
        {
            if (playOnAwake) _playing = true;
        }

        private void Update()
        {
            if (!_playing) return;
            if (frames == null || frames.Length == 0) return;
            if (framesPerSecond <= 0f) return;

            if (frames.Length == 1)
            {
                ApplyFrame(0);
                return;
            }

            float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            _timer += dt * framesPerSecond;

            while (_timer >= 1f)
            {
                _timer -= 1f;
                Advance();
            }
        }

        private void Advance()
        {
            _index++;
            if (_index >= frames.Length)
            {
                if (loop)
                {
                    _index = 0;
                }
                else
                {
                    _index = frames.Length - 1;
                    _playing = false;
                }
            }
            ApplyFrame(_index);
        }

        private void ApplyFrame(int index)
        {
            if (frames == null || index < 0 || index >= frames.Length) return;
            var sprite = frames[index];
            if (sprite == null) return;

            if (_image != null) _image.sprite = sprite;
            else if (_spriteRenderer != null) _spriteRenderer.sprite = sprite;
        }

        /// <summary>从头开始播放。</summary>
        public void Restart()
        {
            _index = 0;
            _timer = 0f;
            _playing = true;
            ApplyFrame(_index);
        }

        /// <summary>暂停在当前帧。</summary>
        public void Stop() => _playing = false;

        /// <summary>继续播放（若已到末尾且不循环，会在 Restart 后继续）。</summary>
        public void Play() => _playing = true;

        public void SetFrame(int index)
        {
            if (frames == null || frames.Length == 0) return;
            _index = Mathf.Clamp(index, 0, frames.Length - 1);
            _timer = 0f;
            ApplyFrame(_index);
        }
    }
}
