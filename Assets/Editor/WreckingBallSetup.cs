using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 摆锤（WreckingBall）一键装配工具：生成三套序列帧动画 + 状态机，并把打开场景里的
/// 身体 / 链条球 / 齿轮接好。Arm 是静态图，不参与动画。
///
/// 帧顺序按美术给出的规律：
///   030-038 → 019 → 001-010 → 011-018 → 019 → 020-030
/// 身体、齿轮和链条共用同一帧号，所以三套动画的播放顺序完全一致，动作天然同步。
///
/// 首次编译会自动执行；也可以随时用菜单手动重跑：
///   Tools/Wrecking Ball/Setup In Open Scene
///   Tools/Wrecking Ball/Rebuild Animation Assets
/// 重复运行是安全的，已有的组件 / 资源不会被删除。
/// </summary>
public static class WreckingBallSetup
{
    private const string SpriteFolder = "Assets/Sprite";
    private const string AnimationFolder = "Assets/Animation";

    private const string ChainClipPath = AnimationFolder + "/WreckingBallChainSwing.anim";
    private const string BodyClipPath = AnimationFolder + "/WreckingBallBodySpin.anim";
    private const string WheelClipPath = AnimationFolder + "/WreckingBallWheelSpin.anim";

    private const string ChainControllerPath = AnimationFolder + "/WreckingBallChain.controller";
    private const string BodyControllerPath = AnimationFolder + "/WreckingBallBody.controller";
    private const string WheelControllerPath = AnimationFolder + "/WreckingBallWheel.controller";

    private const string ChainFamily = "WreckingBall-ChainandBall";
    private const string BodyFamily = "WreckingBall-Body";
    private const string WheelFamily = "WreckingBall-Wheel";

    /// <summary>12fps：39 帧一个完整来回约 3.25 秒，和项目里其它序列帧动画同速。</summary>
    private const float FrameRate = 12f;

    /// <summary>球在贴图里的平均像素半径，用来生成 CircleCollider2D（单位像素）。</summary>
    private const float BallRadiusPixels = 65.5f;

    /// <summary>球碰撞体子物体的名字，动画里按这个路径写球的本地坐标。</summary>
    private const string BallChildName = "Ball";

    private struct FrameData
    {
        public readonly int width;
        public readonly int height;
        public readonly float anchorX;
        public readonly float anchorY;
        public readonly float ballX;
        public readonly float ballY;

        public FrameData(int width, int height, float anchorX, float anchorY, float ballX, float ballY)
        {
            this.width = width;
            this.height = height;
            this.anchorX = anchorX;
            this.anchorY = anchorY;
            this.ballX = ballX;
            this.ballY = ballY;
        }
    }

    // 每帧的链条锚点（链条绕在滑轮上的那一端）和球心，单位：像素，原点在图片左上角。
    // 链条球每帧是一段「绕过齿轮的双股链条」，两条股在滑轮处的端点分得很开（约 60px）。
    // 锚点取两条股端点连线的中点（即链条真正套在齿轮中心的位置），而不是某一股的尖角，
    // 否则只把一条股贴到齿轮上、另一条股会悬空、看起来没连上。
    // 数值通过逐帧图像分析得到：距离变换找球心；离球心最远的一圈像素分成两簇即为两股端点，
    // 取其连线中点作锚点。
    private static readonly FrameData[] Frames =
    {
        new FrameData(245, 446, 71f, 19f, 74f, 334f),  // 000
        new FrameData(180, 434, 147f, 13f, 70f, 324f), // 001
        new FrameData(250, 406, 214f, 26f, 69f, 302f), // 002
        new FrameData(330, 353, 298f, 27f, 81f, 264f), // 003
        new FrameData(378, 299, 352f, 27f, 95f, 220f), // 004
        new FrameData(420, 242, 394f, 31f, 101f, 174f),// 005
        new FrameData(442, 200, 419f, 31f, 108f, 132f),// 006
        new FrameData(464, 156, 438f, 31f, 108f, 88f), // 007
        new FrameData(464, 134, 441f, 37f, 108f, 66f), // 008
        new FrameData(459, 134, 442f, 52f, 109f, 67f), // 009
        // 左侧顶点 010~012：原锚点落到了球的下方（anchorY 88/76/50 > 球心 Y≈67），
        // 且链条长度比其它帧长约 10px，导致摆到最左侧时外股链条离开滑轮。
        // 这里按右侧顶点（030 一侧）镜像出对称锚点：始终在球上方、链长与相邻帧一致（≈337px）。
        new FrameData(470, 135, 444f, 58f, 113f, 67f), // 010
        new FrameData(470, 135, 442f, 52f, 109f, 67f), // 011
        new FrameData(468, 134, 441f, 37f, 106f, 67f), // 012
        new FrameData(450, 176, 429f, 31f, 105f, 109f),// 013
        new FrameData(423, 215, 395f, 34f, 106f, 146f),// 014
        new FrameData(399, 265, 369f, 35f, 98f, 196f), // 015
        new FrameData(365, 315, 333f, 36f, 97f, 243f), // 016
        new FrameData(301, 366, 268f, 30f, 82f, 281f), // 017
        new FrameData(195, 432, 164f, 22f, 80f, 323f), // 018
        new FrameData(159, 447, 92f, 17f, 87f, 335f),  // 019
        new FrameData(171, 441, 32f, 22f, 100f, 331f), // 020
        new FrameData(239, 416, 32f, 28f, 170f, 311f), // 021
        new FrameData(305, 372, 32f, 31f, 226f, 280f), // 022
        new FrameData(370, 315, 30f, 29f, 277f, 235f), // 023
        new FrameData(416, 258, 27f, 28f, 314f, 188f), // 024
        new FrameData(444, 210, 25f, 29f, 336f, 140f), // 025
        new FrameData(457, 166, 22f, 29f, 348f, 97f),  // 026
        new FrameData(461, 136, 19f, 31f, 349f, 68f),  // 027
        new FrameData(463, 132, 20f, 52f, 349f, 65f),  // 028
        new FrameData(461, 135, 18f, 60f, 349f, 67f),  // 029
        new FrameData(464, 133, 20f, 56f, 350f, 66f),  // 030
        new FrameData(461, 135, 20f, 35f, 346f, 67f),  // 031
        new FrameData(453, 169, 22f, 30f, 345f, 101f), // 032
        new FrameData(443, 209, 25f, 28f, 335f, 142f), // 033
        new FrameData(420, 257, 32f, 32f, 316f, 186f), // 034
        new FrameData(380, 303, 30f, 29f, 281f, 234f), // 035
        new FrameData(314, 358, 29f, 34f, 229f, 278f), // 036
        new FrameData(245, 405, 30f, 29f, 172f, 311f), // 037
        new FrameData(192, 438, 33f, 23f, 107f, 328f), // 038
    };

    // 摆荡顺序：从右侧最高点 030 出发，经底部 019 到左侧最高点 010，
    // 再经 019 回到 030。尾部重复的 030 与开头同姿，循环播放时刚好无缝。
    private static readonly int[] SwingOrder =
    {
        30, 31, 32, 33, 34, 35, 36, 37, 38,
        19,
        1, 2, 3, 4, 5, 6, 7, 8, 9, 10,
        11, 12, 13, 14, 15, 16, 17, 18,
        19,
        20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30,
    };

    #region 自动执行
    [InitializeOnLoadMethod]
    private static void ScheduleAutoSetup()
    {
        EditorApplication.delayCall += AutoSetup;
    }

    private static void AutoSetup()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;

        EnsureAssets();
        WireOpenScene(false);
    }
    #endregion

    #region 菜单
    [MenuItem("Tools/Wrecking Ball/Setup In Open Scene", priority = 1)]
    public static void MenuSetupScene()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;

        EnsureAssets();
        WireOpenScene(true);
        AssetDatabase.SaveAssets();
    }

    [MenuItem("Tools/Wrecking Ball/Rebuild Animation Assets", priority = 2)]
    public static void MenuRebuildAssets()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;

        EnsureAssets();
        AssetDatabase.SaveAssets();
        Debug.Log("[WreckingBallSetup] 三套摆锤序列帧动画与状态机已刷新。");
    }
    #endregion

    #region 资源生成
    private static void EnsureAssets()
    {
        Sprite[] chainSprites = LoadSprites(ChainFamily);
        Sprite[] bodySprites = LoadSprites(BodyFamily);
        Sprite[] wheelSprites = LoadSprites(WheelFamily);

        if (chainSprites == null || bodySprites == null || wheelSprites == null) return;

        AnimationClip chainClip = BuildSpriteClip(ChainClipPath, "WreckingBallChainSwing", chainSprites);
        AnimationClip bodyClip = BuildSpriteClip(BodyClipPath, "WreckingBallBodySpin", bodySprites);
        AnimationClip wheelClip = BuildSpriteClip(WheelClipPath, "WreckingBallWheelSpin", wheelSprites);

        SpriteRenderer chainRenderer = FindChainInOpenScene();
        if (chainRenderer != null && chainClip != null)
        {
            ApplyChainCurves(chainClip, chainRenderer);
        }

        EnsureController(ChainControllerPath, "Swing", chainClip);
        EnsureController(BodyControllerPath, "BodyLoop", bodyClip);
        EnsureController(WheelControllerPath, "WheelLoop", wheelClip);

        AssetDatabase.SaveAssets();
    }

    private static AnimationClip BuildSpriteClip(string path, string clipName, Sprite[] sprites)
    {
        AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip == null)
        {
            clip = new AnimationClip { name = clipName };
            AssetDatabase.CreateAsset(clip, path);
        }

        clip.frameRate = FrameRate;

        ObjectReferenceKeyframe[] keys = new ObjectReferenceKeyframe[SwingOrder.Length];
        for (int i = 0; i < SwingOrder.Length; i++)
        {
            keys[i] = new ObjectReferenceKeyframe
            {
                time = i / FrameRate,
                value = sprites[SwingOrder[i]]
            };
        }

        EditorCurveBinding binding = EditorCurveBinding.PPtrCurve(string.Empty, typeof(SpriteRenderer), "m_Sprite");
        AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);

        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.startTime = 0f;
        settings.stopTime = keys[keys.Length - 1].time;
        settings.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(clip, settings);

        EditorUtility.SetDirty(clip);
        return clip;
    }

    /// <summary>
    /// 链条球每帧的精灵是单独裁剪的，锚点在图片里的位置各不相同。
    /// 这里把「让锚点固定在世界上的同一根轴上」所需的位移写进动画：
    ///   1) 链条物体的本地坐标 —— 让锚点（滑轮那一端）始终停在 pivotWorld；
    ///   2) Ball 子物体的本地坐标 —— 就是球心相对精灵 pivot 的位置，随帧变化，
    ///      保证球碰撞体始终贴在画面里那颗球上。
    /// 注意：Ball 的父物体就是链条物体，而链条物体的原点在精灵中心，
    /// 所以球的本地坐标就是「球心像素 - 精灵 pivot」，不要再减锚点。
    /// </summary>
    private static void ApplyChainCurves(AnimationClip clip, SpriteRenderer chainRenderer)
    {
        Transform tr = chainRenderer.transform;
        Sprite currentSprite = chainRenderer.sprite;
        int currentFrame = ParseFrame(currentSprite != null ? currentSprite.name : string.Empty, 30);

        if (currentSprite == null)
        {
            currentSprite = LoadSprite(ChainFamily, currentFrame);
            if (currentSprite == null) return;
        }

        // 当前摆放姿势对应的世界锚点，整个摆动都围绕它进行。
        Vector3 currentAnchorLocal = PixelToSpriteLocal(currentSprite, Frames[currentFrame].anchorX, Frames[currentFrame].anchorY);
        Vector3 pivotWorld = tr.position + tr.rotation * Vector3.Scale(tr.lossyScale, currentAnchorLocal);

        int count = SwingOrder.Length;
        Vector3[] chainLocals = new Vector3[count];
        Vector2[] ballLocals = new Vector2[count];

        for (int i = 0; i < count; i++)
        {
            int frame = SwingOrder[i];
            Sprite sprite = LoadSprite(ChainFamily, frame);
            if (sprite == null) return;

            Vector3 anchorLocal = PixelToSpriteLocal(sprite, Frames[frame].anchorX, Frames[frame].anchorY);
            Vector3 centerWorld = pivotWorld - tr.rotation * Vector3.Scale(tr.lossyScale, anchorLocal);
            chainLocals[i] = tr.parent != null ? tr.parent.InverseTransformPoint(centerWorld) : centerWorld;

            Vector3 ballLocal = PixelToSpriteLocal(sprite, Frames[frame].ballX, Frames[frame].ballY);
            ballLocals[i] = ballLocal;
        }

        SetPositionCurves(clip, string.Empty, chainLocals);
        SetPositionCurves(clip, BallChildName, ballLocals);

        EditorUtility.SetDirty(clip);
    }

    private static void SetPositionCurves(AnimationClip clip, string path, Vector3[] positions)
    {
        SetPositionCurves(clip, path, positions.Select(p => new Vector2(p.x, p.y)).ToArray());
    }

    private static void SetPositionCurves(AnimationClip clip, string path, Vector2[] positions)
    {
        Keyframe[] xKeys = new Keyframe[positions.Length];
        Keyframe[] yKeys = new Keyframe[positions.Length];

        for (int i = 0; i < positions.Length; i++)
        {
            float time = i / FrameRate;
            xKeys[i] = new Keyframe(time, positions[i].x) { inTangent = float.PositiveInfinity, outTangent = float.PositiveInfinity };
            yKeys[i] = new Keyframe(time, positions[i].y) { inTangent = float.PositiveInfinity, outTangent = float.PositiveInfinity };
        }

        AnimationUtility.SetEditorCurve(clip,
            EditorCurveBinding.FloatCurve(path, typeof(Transform), "m_LocalPosition.x"),
            new AnimationCurve(xKeys));
        AnimationUtility.SetEditorCurve(clip,
            EditorCurveBinding.FloatCurve(path, typeof(Transform), "m_LocalPosition.y"),
            new AnimationCurve(yKeys));
    }

    private static void EnsureController(string path, string stateName, AnimationClip clip)
    {
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
        if (controller == null)
        {
            controller = AnimatorController.CreateAnimatorControllerAtPath(path);
        }
        if (controller == null || clip == null) return;

        if (controller.layers == null || controller.layers.Length == 0)
        {
            AnimatorStateMachine stateMachine = new AnimatorStateMachine { name = "Base Layer" };
            stateMachine.hideFlags = HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(stateMachine, controller);

            AnimatorControllerLayer layer = new AnimatorControllerLayer
            {
                name = "Base Layer",
                defaultWeight = 1f,
                stateMachine = stateMachine
            };
            controller.AddLayer(layer);
        }

        AnimatorControllerLayer[] layers = controller.layers;
        if (!Mathf.Approximately(layers[0].defaultWeight, 1f))
        {
            layers[0].defaultWeight = 1f;
            controller.layers = layers;
        }

        AnimatorStateMachine machine = controller.layers[0].stateMachine;

        AnimatorState state = machine.states
            .Where(s => s.state != null && s.state.name == stateName)
            .Select(s => s.state)
            .FirstOrDefault();

        if (state == null)
        {
            state = machine.AddState(stateName);
            state.hideFlags = HideFlags.HideInHierarchy;
            if (!AssetDatabase.Contains(state))
                AssetDatabase.AddObjectToAsset(state, controller);
        }

        state.motion = clip;
        state.speed = 1f;

        if (machine.defaultState != state)
        {
            machine.defaultState = state;
        }

        EditorUtility.SetDirty(controller);
    }
    #endregion

    #region 场景装配
    private static void WireOpenScene(bool verbose)
    {
        SpriteRenderer chain = null;
        SpriteRenderer body = null;
        SpriteRenderer wheel = null;

        foreach (SpriteRenderer renderer in UnityEngine.Object.FindObjectsOfType<SpriteRenderer>(true))
        {
            if (renderer.sprite == null) continue;
            string spriteName = renderer.sprite.name;

            if (chain == null && spriteName.StartsWith(ChainFamily, StringComparison.Ordinal)) chain = renderer;
            else if (body == null && spriteName.StartsWith(BodyFamily, StringComparison.Ordinal)) body = renderer;
            else if (wheel == null && spriteName.StartsWith(WheelFamily, StringComparison.Ordinal)) wheel = renderer;
        }

        if (chain == null)
        {
            if (verbose) Debug.LogWarning("[WreckingBallSetup] 当前打开的场景里没有找到 WreckingBall-ChainandBall 精灵。");
            return;
        }

        bool changed = false;

        AnimationClip chainClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ChainClipPath);
        if (chainClip != null)
        {
            ApplyChainCurves(chainClip, chain);
        }

        changed |= EnsureAnimator(chain, ChainControllerPath);
        if (body != null) changed |= EnsureAnimator(body, BodyControllerPath);
        if (wheel != null) changed |= EnsureAnimator(wheel, WheelControllerPath);

        changed |= EnsureBall(chain);

        if (changed)
        {
            EditorSceneManager.MarkSceneDirty(chain.gameObject.scene);
            if (verbose) Debug.Log("[WreckingBallSetup] 摆锤已在当前场景装配完成。", chain.gameObject);
        }
        else if (verbose)
        {
            Debug.Log("[WreckingBallSetup] 当前场景的摆锤已经是装配好的状态，无需修改。", chain.gameObject);
        }
    }

    private static bool EnsureAnimator(SpriteRenderer renderer, string controllerPath)
    {
        bool changed = false;
        Animator animator = renderer.GetComponent<Animator>();

        if (animator == null)
        {
            animator = Undo.AddComponent<Animator>(renderer.gameObject);
            changed = true;
        }

        RuntimeAnimatorController controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(controllerPath);
        if (controller != null && animator.runtimeAnimatorController != controller)
        {
            animator.runtimeAnimatorController = controller;
            changed = true;
        }

        // 位置由动画曲线直接驱动，不能交给 Root Motion 提取。
        if (animator.applyRootMotion)
        {
            animator.applyRootMotion = false;
            changed = true;
        }

        // 屏幕外也继续播，保证摆锤永远不停。
        if (animator.cullingMode != AnimatorCullingMode.AlwaysAnimate)
        {
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            changed = true;
        }

        if (changed) EditorUtility.SetDirty(animator);
        return changed;
    }

    private static bool EnsureBall(SpriteRenderer chain)
    {
        bool changed = false;

        Transform ball = chain.transform.Find(BallChildName);
        if (ball == null)
        {
            GameObject ballObject = new GameObject(BallChildName);
            Undo.RegisterCreatedObjectUndo(ballObject, "Create Wrecking Ball Collider");
            ball = ballObject.transform;
            ball.SetParent(chain.transform, false);
            ball.localRotation = Quaternion.identity;
            ball.localScale = Vector3.one;
            ball.localPosition = BallLocalOffset(SwingOrder[0]);
            changed = true;
        }

        // 如果用户把 CircleCollider2D 直接加在了链条物体上，沿用它的半径并禁用旧碰撞体，
        // 因为链条物体自己的原点在精灵中心，不会跟着球走。
        CircleCollider2D legacyCollider = chain.GetComponent<CircleCollider2D>();
        float radius = BallRadiusPixels * 0.01f;
        if (legacyCollider != null && legacyCollider.radius > 0.01f && legacyCollider.radius < 3f)
        {
            radius = legacyCollider.radius;
        }

        CircleCollider2D collider = ball.GetComponent<CircleCollider2D>();
        if (collider == null)
        {
            collider = Undo.AddComponent<CircleCollider2D>(ball.gameObject);
            changed = true;
        }

        if (!collider.isTrigger) { collider.isTrigger = true; changed = true; }
        if (collider.offset != Vector2.zero) { collider.offset = Vector2.zero; changed = true; }
        if (!Mathf.Approximately(collider.radius, radius)) { collider.radius = radius; changed = true; }
        if (changed) EditorUtility.SetDirty(collider);

        if (legacyCollider != null && legacyCollider.enabled)
        {
            legacyCollider.enabled = false;
            EditorUtility.SetDirty(legacyCollider);
            changed = true;
            Debug.Log("[WreckingBallSetup] 已把链条物体上的 CircleCollider2D 禁用，改由 Ball 子物体负责击杀判定。", chain.gameObject);
        }

        Rigidbody2D body = ball.GetComponent<Rigidbody2D>();
        if (body == null)
        {
            body = Undo.AddComponent<Rigidbody2D>(ball.gameObject);
            changed = true;
        }

        if (body.bodyType != RigidbodyType2D.Kinematic) { body.bodyType = RigidbodyType2D.Kinematic; changed = true; }
        if (!body.simulated) { body.simulated = true; changed = true; }
        if (!Mathf.Approximately(body.gravityScale, 0f)) { body.gravityScale = 0f; changed = true; }
        if (!body.useFullKinematicContacts) { body.useFullKinematicContacts = true; changed = true; }
        if (body.collisionDetectionMode != CollisionDetectionMode2D.Continuous) { body.collisionDetectionMode = CollisionDetectionMode2D.Continuous; changed = true; }
        if (body.interpolation != RigidbodyInterpolation2D.Interpolate) { body.interpolation = RigidbodyInterpolation2D.Interpolate; changed = true; }
        if (changed) EditorUtility.SetDirty(body);

        if (ball.GetComponent<WreckingBallKill>() == null)
        {
            Undo.AddComponent<WreckingBallKill>(ball.gameObject);
            changed = true;
        }

        return changed;
    }
    #endregion

    #region 工具
    private static SpriteRenderer FindChainInOpenScene()
    {
        foreach (SpriteRenderer renderer in UnityEngine.Object.FindObjectsOfType<SpriteRenderer>(true))
        {
            if (renderer.sprite != null && renderer.sprite.name.StartsWith(ChainFamily, StringComparison.Ordinal))
                return renderer;
        }
        return null;
    }

    private static Sprite[] LoadSprites(string family)
    {
        Sprite[] sprites = new Sprite[Frames.Length];
        for (int i = 0; i < sprites.Length; i++)
        {
            sprites[i] = LoadSprite(family, i);
            if (sprites[i] == null) return null;
        }
        return sprites;
    }

    private static Sprite LoadSprite(string family, int frame)
    {
        string path = string.Format("{0}/{1}.{2:D3}.png", SpriteFolder, family, frame);
        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (sprite == null)
        {
            Debug.LogError("[WreckingBallSetup] 找不到精灵：" + path);
        }
        return sprite;
    }

    /// <summary>
    /// 球碰撞体子物体相对链条物体（精灵 pivot）的本地坐标。
    /// 动画里用的是同一套数值，创建子物体时直接复用，避免首帧跳动。
    /// </summary>
    private static Vector2 BallLocalOffset(int frame)
    {
        Sprite sprite = LoadSprite(ChainFamily, frame);
        if (sprite == null) return Vector2.zero;

        Vector3 ballLocal = PixelToSpriteLocal(sprite, Frames[frame].ballX, Frames[frame].ballY);
        return ballLocal;
    }

    /// <summary>把贴图像素坐标换算成精灵本地坐标（以 pivot 为原点、Y 向上）。</summary>
    private static Vector3 PixelToSpriteLocal(Sprite sprite, float pixelX, float pixelY)
    {
        float unitsPerPixel = sprite.bounds.size.x / sprite.rect.width;
        Vector2 pivot = sprite.pivot;
        return new Vector3(
            (pixelX - pivot.x) * unitsPerPixel,
            ((sprite.rect.height - pixelY) - pivot.y) * unitsPerPixel,
            0f);
    }

    private static int ParseFrame(string spriteName, int fallback)
    {
        int dot = spriteName.LastIndexOf('.');
        if (dot >= 0)
        {
            int frame;
            if (int.TryParse(spriteName.Substring(dot + 1), out frame) && frame >= 0 && frame < Frames.Length)
                return frame;
        }
        return fallback;
    }
    #endregion
}