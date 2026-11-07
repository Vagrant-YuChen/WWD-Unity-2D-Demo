using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RunningGame.UI
{
    /// <summary>
    /// 入场演出：Logo 缩放弹入，随后子按钮按顺序错峰从右侧滑入淡入。
    /// 自动收集子元素，无需手动逐项配置。使用 unscaled 时间。
    /// </summary>
    public class PlaySequenceReveal : MonoBehaviour
    {
        [Header("Logo")]
        public RectTransform logo;
        public float logoDelay = 0.05f;
        public float logoDuration = 0.45f;
        public float logoStartScale = 0.8f;

        [Header("Buttons")]
        public float stagger = 0.08f;
        public float itemDuration = 0.3f;
        public float itemSlideX = 45f;

        private struct Item
        {
            public RectTransform rect;
            public CanvasGroup group;
            public Vector2 target;
        }

        private readonly List<Item> items = new List<Item>();
        private CanvasGroup logoGroup;
        private Vector3 logoBaseScale;
        private bool ready;

        private void Awake()
        {
            logoBaseScale = Vector3.one;

            if (logo != null)
            {
                logoGroup = EnsureGroup(logo.gameObject);
                logoBaseScale = logo.localScale == Vector3.zero ? Vector3.one : logo.localScale;
            }

            var children = new List<RectTransform>();
            for (int i = 0; i < transform.childCount; i++)
                children.Add(transform.GetChild(i) as RectTransform);

            foreach (var rt in children)
            {
                if (rt == null) continue;
                if (rt == logo) continue;
                if (rt.name.StartsWith("Logo")) continue;
                if (rt.name.Contains("Pointer")) continue;
                if (rt.GetComponent<UnityEngine.UI.Button>() == null) continue;

                items.Add(new Item
                {
                    rect = rt,
                    group = EnsureGroup(rt.gameObject),
                    target = rt.anchoredPosition
                });
            }

            SetHidden();
            ready = true;
        }

        private void OnEnable()
        {
            if (!ready) return;
            StartCoroutine(Play());
        }

        private void SetHidden()
        {
            if (logoGroup != null) logoGroup.alpha = 0f;
            if (logo != null) logo.localScale = logoBaseScale * logoStartScale;

            foreach (var it in items)
            {
                if (it.group != null) it.group.alpha = 0f;
                if (it.rect != null) it.rect.anchoredPosition = it.target + new Vector2(itemSlideX, 0f);
            }
        }

        private IEnumerator Play()
        {
            if (logo != null)
            {
                if (logoDelay > 0f) yield return new WaitForSecondsRealtime(logoDelay);
                float e = 0f;
                while (e < logoDuration)
                {
                    e += Time.unscaledDeltaTime;
                    float k = EaseOut(Mathf.Clamp01(e / logoDuration));
                    if (logoGroup != null) logoGroup.alpha = k;
                    if (logo != null) logo.localScale = logoBaseScale * Mathf.LerpUnclamped(logoStartScale, 1f, k);
                    yield return null;
                }
                if (logoGroup != null) logoGroup.alpha = 1f;
                if (logo != null) logo.localScale = logoBaseScale;
            }

            for (int i = 0; i < items.Count; i++)
            {
                StartCoroutine(RevealItem(items[i], i));
            }
        }

        private IEnumerator RevealItem(Item it, int index)
        {
            if (stagger > 0f && index > 0)
                yield return new WaitForSecondsRealtime(stagger * index);

            float e = 0f;
            while (e < itemDuration)
            {
                e += Time.unscaledDeltaTime;
                float k = EaseOut(Mathf.Clamp01(e / itemDuration));
                if (it.group != null) it.group.alpha = k;
                if (it.rect != null) it.rect.anchoredPosition = Vector2.LerpUnclamped(it.target + new Vector2(itemSlideX, 0f), it.target, k);
                yield return null;
            }
            if (it.group != null) it.group.alpha = 1f;
            if (it.rect != null) it.rect.anchoredPosition = it.target;
        }

        private static float EaseOut(float t) => 1f - Mathf.Pow(1f - t, 3f);

        private static CanvasGroup EnsureGroup(GameObject go)
        {
            var cg = go.GetComponent<CanvasGroup>();
            if (cg == null) cg = go.AddComponent<CanvasGroup>();
            return cg;
        }
    }
}
