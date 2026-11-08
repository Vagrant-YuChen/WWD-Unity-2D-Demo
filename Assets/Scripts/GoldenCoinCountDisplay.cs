using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 金币数量显示（暂停界面里的 GameUI_GoldenCoinCount）。
/// 逻辑：读取 CoinWallet.Total（跨关卡持久化、存在本地存档里的已入账金币总数），
/// 把它拆成一位一位的数字，用 GameUI_0~9 的贴图逐位显示。
/// 该总额只在“某关首次通关结算”时增加（见 CoinWallet / CoinPickup / FlagVictory），
/// 重玩已入账的关卡不会重复累加，因此不会被刷。每逢暂停界面被激活（ESC 打开）会自动刷新。
///
/// 用法：
///  1) 把这个脚本挂在 GameUI_GoldenCoinCount 这个图片物体上；
///  2) digitSprites 会自动填成 GameUI_0~9（编辑器下按路径自动加载，也可手动拖 10 张）；
///  3) 其它（Colon、金币图标）保持不动。
/// 多位数时会在模板右侧自动生成额外的数字图片，单/多位数都能正确显示。
/// </summary>
public class GoldenCoinCountDisplay : MonoBehaviour
{
    [Tooltip("作为数字模板的图片。留空则自动取本物体上的 Image。")]
    public Image digitTemplate;

    [Tooltip("0-9 数字贴图，索引=数字。长度不足时按个位数兜底。")]
    public Sprite[] digitSprites = new Sprite[10];

    [Tooltip("相邻数字之间的额外间距（像素，可为负）。")]
    public float digitSpacing = -4f;

    [Tooltip("多位数时，整串数字以模板位置为中心居中。关闭则从模板位置向右排列。")]
    public bool centerOnTemplate = true;

    // 显示用到的全部数字图片：第 0 个就是模板本身，其余为运行时生成的克隆。
    private readonly List<Image> _digitImages = new List<Image>();
    private Image _template;

    private void Awake()
    {
        _template = digitTemplate != null ? digitTemplate : GetComponent<Image>();
        if (_template != null && _digitImages.Count == 0)
            _digitImages.Add(_template);
    }

    private void OnEnable()
    {
        Refresh();
    }

    private void Start()
    {
        Refresh();
    }

    /// <summary>用一个外部数值刷新（默认不传则读取 CoinWallet.Total——跨关卡存档金币总数）。</summary>
    public void Refresh()
    {
        Refresh(CoinWallet.Total);
    }

    /// <summary>把 value 显示成逐位数字贴图。</summary>
    public void Refresh(int value)
    {
        if (_template == null)
        {
            _template = digitTemplate != null ? digitTemplate : GetComponent<Image>();
            if (_template == null) return;
            if (_digitImages.Count == 0) _digitImages.Add(_template);
        }

        if (value < 0) value = 0;

        string digits = value.ToString();
        int count = digits.Length;

        EnsureDigitImages(count);

        RectTransform templateRt = _template.rectTransform;
        float step = templateRt.rect.width * Mathf.Abs(templateRt.localScale.x) + digitSpacing;
        float totalWidth = step * (count - 1);

        float baseX = templateRt.anchoredPosition.x;
        float startX = centerOnTemplate ? baseX - totalWidth * 0.5f : baseX;
        float y = templateRt.anchoredPosition.y;

        for (int i = 0; i < _digitImages.Count; i++)
        {
            Image img = _digitImages[i];
            bool used = i < count;
            if (img.gameObject.activeSelf != used)
                img.gameObject.SetActive(used);
            if (!used) continue;

            int digit = digits[i] - '0';
            img.sprite = ResolveSprite(digit);

            RectTransform rt = img.rectTransform;
            rt.anchoredPosition = new Vector2(startX + step * i, y);
        }
    }

    private Sprite ResolveSprite(int digit)
    {
        if (digitSprites != null && digit >= 0 && digit < digitSprites.Length && digitSprites[digit] != null)
            return digitSprites[digit];
        // 兜底：素材缺失时保留模板原有贴图，避免变空白。
        return _template != null ? _template.sprite : null;
    }

    private void EnsureDigitImages(int needed)
    {
        while (_digitImages.Count < needed)
        {
            Image clone = CreateDigitImage(_digitImages[_digitImages.Count - 1]);
            _digitImages.Add(clone);
        }
    }

    // 以已有数字图片为模板，生成一个"只含 Image"的新图片（不带本脚本，避免递归）。
    private Image CreateDigitImage(Image source)
    {
        var go = new GameObject("GameUI_GoldenCoinCount_" + _digitImages.Count,
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));

        var rt = (RectTransform)go.transform;
        rt.SetParent(source.rectTransform.parent, false);

        var srcRt = source.rectTransform;
        rt.anchorMin = srcRt.anchorMin;
        rt.anchorMax = srcRt.anchorMax;
        rt.pivot = srcRt.pivot;
        rt.localScale = srcRt.localScale;
        rt.sizeDelta = srcRt.sizeDelta;

        var img = go.GetComponent<Image>();
        img.sprite = source.sprite;
        img.type = source.type;
        img.color = source.color;
        img.preserveAspect = source.preserveAspect;
        img.raycastTarget = false;
        img.material = source.material;

        go.SetActive(false);
        return img;
    }

#if UNITY_EDITOR
    // 编辑器下自动补齐 0-9 数字贴图（走项目里既有的 Assets/Texture 2D/GameUI_x.png 路径），
    // 会被序列化进场景，打包运行时也能用；也可手动替换成任意 0-9 贴图。
    private void OnValidate()
    {
        if (digitSprites == null || digitSprites.Length != 10)
            Array.Resize(ref digitSprites, 10);

        for (int i = 0; i < 10; i++)
        {
            if (digitSprites[i] == null)
            {
                var sp = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(
                    "Assets/Texture 2D/GameUI_" + i + ".png");
                if (sp != null) digitSprites[i] = sp;
            }
        }
    }
#endif
}
