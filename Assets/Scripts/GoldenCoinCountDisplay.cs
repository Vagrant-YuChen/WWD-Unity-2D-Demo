using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 金币数量显示（暂停界面里的 GameUI_GoldenCoinCount）。
/// 显示口径：CoinWallet.Total（已入账总额）+ 本局尚未入账的正差额，
/// 即“若此刻通关结算，钱包里会有的金币数”。所以玩家在关卡里每捡到一枚金币，
/// 这个数字都会实时增长；而重玩已入账关卡时，只有超过该关历史最高收集数才会继续增加，
/// 保持“每关只按最高收集数入账、无法刷金币”的设计（见 CoinWallet / CoinPickup / FlagVictory）。
/// 数字用 GameUI_0~9 的贴图逐位显示，每逢暂停界面被激活（ESC 打开）都会刷新。
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
    private int _lastDisplayed = int.MinValue;

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

    private void Update()
    {
        // 只有值真正变化时才重绘，避免每帧重复拆字/生成图片。
        // 暂停界面在游戏中通常是关闭的，这里的 Update 只在其可见时运行；
        // 若把它当成常驻 HUD 使用，也能实时跟着拾取数增长。
        int value = ComputeDisplayValue();
        if (value != _lastDisplayed)
            Refresh(value);
    }

    /// <summary>
    /// 刷新暂停界面里的金币数量。
    /// 显示的是“若此刻结算”的金币总额 = 已入账总额 + 本局尚未入账的正差额，
    /// 因此玩家在关卡里每捡到一枚金币，这里都会实时增加（不会把已入账的部分重复计入）。
    /// </summary>
    public void Refresh()
    {
        Refresh(ComputeDisplayValue());
    }

    /// <summary>
    /// 计算要显示的金币数：CoinWallet.Total（已入账总额）+ 本局收集数超过“该关已入账数”的差额。
    /// 既能在捡金币时实时增长，又保持“每关只按最高收集数入账、无法刷金币”的设计。
    /// </summary>
    public static int ComputeDisplayValue()
    {
        int total = CoinWallet.Total;

        string sceneName = SceneManager.GetActiveScene().name;
        int banked = CoinWallet.GetBankedAmount(sceneName);

        int pendingGain = CoinPickup.TotalCollected - banked;
        if (pendingGain < 0) pendingGain = 0;

        return total + pendingGain;
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
        _lastDisplayed = value;

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
