# BunnyController 可优化项（对照 Character.cs）

> 基于 `Assets/Scripts/BunnyController.cs` 与反编译的 `Character.cs` 对比，列出值得代入的优化点。
> 行号均为 `BunnyController.cs` 当前行号（2026-06-16 快照）。

---

## 1. 跳跃弧度：用「跳跃顶点重力过渡曲线」替代固定 `airGravity`

- **Character.cs 参考**：`Fall()` 内的 `loGravity` / `gravityTransitionTime` / `JumpCapTransitionTime` 曲线 + `loGravityMod` / `GravityCurve.Evaluate(vy)`，起跳初期低重力、顶点后渐进到全重力。
- **当前代码**：`BunnyController.cs` 第 44-46 行固定 `groundGravity`/`airGravity`；第 236-242 行 `FixedUpdate` 里 `rb.gravityScale = isGrounded ? groundGravity : airGravity` 一刀切。
- **优化建议**：新增 `AnimationCurve jumpCapTransitionTime`、`float loGravityMod`、`AnimationCurve gravityCurve`、`float gravityTransitionTime`，在 `FixedUpdate` 中按 `timeSinceJump` 插值重力；起跳上升阶段 `loGravity=true` 维持低重力，顶点后 `gravityTransitionTime` MoveTowards 到曲线值。让跳跃弧度更"软"、更有手感。
- **影响位置**：字段区 44-46、`FixedUpdate` 236-242、`Jump()` 576-596（需重置 `timeSinceJump`/`gravityTransitionTime`）、`ApplyVariableJumpHeight` 608-626。

## 2. 空中惯性：加入「惯性修正器」支持墙跳/弹跳后加速

- **Character.cs 参考**：`airInertiaModifier` / `airInertiaModifierL` / `airInertiaModifierR`，墙跳时 `airInertiaModifierL/R = WallJumpInertiaModifier`，并在空中控制时叠加 `num19 += airInertiaModifierL/R`。
- **当前代码**：`BunnyController.cs` 第 19 行单一 `airInertia`；第 472-480 行空中控制 `airControl = airInertia * (isSprinting ? 1.2f : 1f)` 恒定。
- **优化建议**：拆分为 `baseAirInertia` + `airInertiaModifier` + `airInertiaModifierL/R`，在 `ApplySpringBounce`/`ApplyLaunch`/墙跳时临时提升修正器，落地时清零；MoveLogic 空中分支改为 `airControl = (baseAirInertia + modifier) * ...`。避免弹跳后空中操控"发飘"。
- **影响位置**：字段 19、`MoveLogic` 472-480、`ApplySpringBounce` 787-809、`ApplyLaunch` 812-840。

## 3. 二段跳 / 空中跳（Air Jumps）

- **Character.cs 参考**：`AirJumps`、`MaxAirJumps`、`AirJumpGraceTimer`/`AirJumpGraceTime`，在 `!onGround && !onWall && AirJumps < MaxAirJumps` 时允许起跳，落地/贴墙重置。
- **当前代码**：`BunnyController.cs` 第 597-607 `CanJump()` 仅依赖 `coyoteTimer > 0`，无空中跳能力。
- **优化建议**：新增 `int airJumps`、`int maxAirJumps`（默认 0 保持现状）、`float airJumpGraceTimer`；在 `Update()` 跳跃触发分支增加空中跳条件；`Jump()` 中 `airJumps++`；落地时 `airJumps = 0`。可做成可选开关，不影响现有单段跳。
- **影响位置**：字段区 87-92、`Update()` 205-234、`Jump()` 576-596、`CanJump()` 597-607、`UpdateGroundDetection` 369-415（落地重置）。

## 4. 输入响应：加入「模拟量修正」与「响应时间曲线」

- **Character.cs 参考**：`analogueModifier.Evaluate(Mathf.Abs(right - left))` 缩放输入强度；`ResponseTimeModifier.Evaluate(max(leftResponseTime, rightResponseTime))` 让加速随按键时长渐进，避免瞬间满速。
- **当前代码**：`BunnyController.cs` 第 352-357 `UpdateInput()` 直接 `Input.GetAxisRaw`；第 448-485 `MoveLogic` 使用原始输入。
- **优化建议**：新增 `AnimationCurve analogueModifier`、`AnimationCurve responseTimeModifier`，在 MoveLogic 中 `targetSpeed *= analogueModifier.Evaluate(Mathf.Abs(horizontalInput))`，并按按键时长插值 `responseTime` 作为加速度系数。让起步有"重量感"、长时间按住才达满速。
- **影响位置**：字段区 14-20、`UpdateInput` 352-357（记录按键时长）、`MoveLogic` 448-485。

## 5. 落地特效：在「空中→落地」瞬间生成 Dust Cloud

- **Character.cs 参考**：`justLanded` + `lastFallSpeed < -MinSmokeVelocity` 时 `SpawnDustCloud()`；落地瞬间播放特效。
- **当前代码**：`BunnyController.cs` 第 405-412 已检测 `isJustLanded`（带 `LandedSpeedThreshold`），但仅更新动画，无 VFX。
- **优化建议**：在 `UpdateGroundDetection` 中置位 `isJustLanded` 时，若 `rb.velocity.y < LandedSpeedThreshold`（或记录 `lastFallSpeed`），调用 `SmokePool`/粒子生成落地烟尘；可加 `MinSmokeVelocity` 阈值避免小跳也冒烟。
- **影响位置**：字段 91-92、`UpdateGroundDetection` 369-415、`UpdateLandedState` 680-695；新增 `lastFallSpeed` 字段与 `FixedUpdate` 中 `lastFallSpeed = Mathf.Min(lastFallSpeed, v.y)`。

## 6. 相机前视：基于输入方向的 lookahead（Move View）

- **Character.cs 参考**：`moveViewCurrent/Max/Timer`，按 `left2/right2/up2/down2` 输入渐进移动视野中心，松开后 `moveViewSpeedReturn` 回中。
- **当前代码**：`BunnyController.cs` 无此机制，镜头完全跟随角色。
- **优化建议**：新增 `ViewDirection moveViewMax/Current/Timer` 与 `moveViewDelay/Speed/SpeedReturn`，在 `Update()` 中按输入渐进、松开回中，暴露 `MoveViewOffset` 给相机系统。长距离跳跃/冲刺时提前看到前方。
- **影响位置**：新增字段区（建议放在 100-108 附近）、`Update()` 205-234；需相机脚本消费该偏移。

## 7. 跳跃缓冲增强：加入「预跳 Grace」与「空中跳 Grace」

- **Character.cs 参考**：`PreJumpGraceTimer`/`PreJumpGraceTime`（墙跳缓冲）、`AirJumpGraceTimer`/`AirJumpGraceTime`（防止连跳过快）。
- **当前代码**：`BunnyController.cs` 第 89-90 `coyoteTimer`/`jumpBufferTimer`；第 358-363 `UpdateJumpBuffer`；第 597-607 `CanJump`。
- **优化建议**：在现有 Coyote/Buffer 基础上，新增 `preJumpGraceTimer`（用于墙跳/弹跳后的短时起跳窗口）与 `airJumpGraceTimer`（二段跳冷却），避免"起跳瞬间立即再跳"的误触；`Jump()` 中重置计时器。
- **影响位置**：字段 89-90、`Update()` 220-234、`Jump()` 576-596、`CanJump()` 597-607。

## 8. 统一冲击接口：`AddImpulse(force, cooldown, resetAirJumps)`

- **Character.cs 参考**：`AddImpulse(Vector2 force, float cooldown, bool resetAirJumps)`，带 `impulseAdded` 冷却防止连发。
- **当前代码**：`BunnyController.cs` 第 787-809 `ApplySpringBounce`、第 812-840 `ApplyLaunch` 各自重置状态，逻辑分散。
- **优化建议**：抽取通用 `AddImpulse(force, cooldown, resetAirJumps)`，弹簧/大炮/陷阱统一调用；内部处理 `impulseAdded` 冷却、`risingFromJump`、`jumpCutEligible=false`、`isGrounded=false` 等重置，减少重复代码与状态遗漏。
- **影响位置**：新增方法（建议放在 `Jump()` 576 附近）；重构 `ApplySpringBounce` 787-809、`ApplyLaunch` 812-840。

## 9. 脚步音效：基于 `lastFootstep` 计时器的节奏播放

- **Character.cs 参考**：`lastFootstep` 累加 `Time.fixedDeltaTime`，移动时触发 `_Move_Start`/`_Sprint_Start`，停止时触发 `_Move_Stop`。
- **当前代码**：`BunnyController.cs` 无脚步音效节奏控制。
- **优化建议**：新增 `float lastFootstep`、`float footstepTime`，在 `MoveLogic` 地面分支中当 `Mathf.Abs(vx) > 0` 时累加计时器，超过 `footstepTime` 播放脚步音并重置；停止时播"停止"音。配合 `CharacterSFXName` 风格的音效命名。
- **影响位置**：字段区（建议 96 附近）、`MoveLogic` 448-485、`UpdateAnimatorParams` 696-716。

## 10. 跨角色站立继承（多人/幽灵模式，可选）

- **Character.cs 参考**：`StandingOnCharacterNested`、`DuplicateCharacterMotion`、`characterInheritedMotion`，站在另一角色上时继承其运动。
- **当前代码**：`BunnyController.cs` 无此机制（单人控制器）。
- **优化建议**：若后续支持"站人"玩法，可新增 `characterInheritedMotion` 与 `StandingOnCharacter` 检测（复用 `FeetPhysicsCollider` 风格的 OverlapCollider），在 `MoveLogic` 中叠加继承速度。当前单人模式可暂不实现，仅作规划。
- **影响位置**：`MoveLogic` 448-485、`FixedUpdate` 236-258。

---

### 优先级建议

| 优先级 | 项 | 理由 |
|---|---|---|
| P1 | 1 重力曲线 | 直接影响跳跃手感，改动局部 |
| P1 | 5 落地特效 | 提升反馈，改动小 |
| P2 | 4 输入响应曲线 | 提升"重量感" |
| P2 | 7 跳跃缓冲增强 | 鲁棒性 |
| P2 | 8 统一冲击接口 | 代码整洁度 |
| P3 | 2 空中惯性修正 | 仅墙跳/弹跳场景受益 |
| P3 | 3 二段跳 | 新功能，需设计 |
| P3 | 6 相机前视 | 需相机系统配合 |
| P3 | 9 脚步音效 | 音效资产依赖 |
| P4 | 10 跨角色继承 | 多人模式才需要 |
