# ScopeSensitivity Mod

倍镜优化 Mod for 《逃离鸭科夫》（Escape from Duckov）

## 功能说明

本 Mod 提供两项倍镜优化功能：
1. **降低高倍镜开镜灵敏度**：降低 4倍镜、8倍镜开镜时的鼠标灵敏度，使其与 2倍镜接近
2. **优化后座力分布**：根据瞄准距离调整后座力，使近距离和远距离的后座相对视野比例更一致

### 功能 1：倍镜灵敏度调整

游戏中的开镜操作分为两个阶段：

1. **第一阶段（边缘漂移）**：将鼠标拉到屏幕边缘时，地图会快速漂移到瞄准点。本 Mod **不修改**此阶段的速度。
2. **第二阶段（准星移动）**：开镜状态下移动准星。本 Mod **只降低**高倍镜在此阶段的灵敏度。

**实现细节**：
- 使用反射访问 `CharacterInputControl` 的私有字段 `mouseDelta`（鼠标增量）
- 在 `Update()` 中检测 ADS 状态和倍镜类型（通过 `gun.ADSAimDistanceFactor`）
- 如果 `ADSAimDistanceFactor > 1.5`（判断为高倍镜），且不是边缘漂移，则缩小 `mouseDelta`
- 边缘漂移判断：鼠标在屏幕边缘附近且 `mouseDelta` 幅度较大

**倍镜类型判断**：
- 通过 `ItemAgent_Gun.ADSAimDistanceFactor` 判断倍镜类型
- 假设 2倍镜的 `ADSAimDistanceFactor` 约为 1.0-1.5
- 如果超过阈值（1.5），判断为 4倍镜或 8倍镜，需要降低灵敏度

### 功能 2：后座力距离优化

**问题**：原版游戏中，狙击枪近距离瞄准时后座力过大（镜头几乎飞出屏幕外），而远距离后座力相对较小。这是因为后座力是基于屏幕空间计算的，在放大视野下显得更大。

**解决方案**：
- 使用线性缩放：根据倍镜因子（`ADSAimDistanceFactor`）线性调整后座力，使不同倍镜的后座相对视野范围更接近
- 距离微调：根据瞄准距离额外调整，近距离再减小 20%，远距离保持不变
- 平滑过渡：在 10m 到 50m 之间平滑过渡

**计算方式**：
1. 基础缩放：`调整后座 = 原始后座 / baseScaleFactor`
   - `baseScaleFactor` 在 1.5 倍镜时为 1.0（不调整）
   - `baseScaleFactor` 在 4.24 倍镜时为 1.5（后座降低到 67%）
   - 中间倍镜线性插值
2. 距离调整：`最终后座 = 调整后座 × distanceScale`
   - 近距离（<10m）：`distanceScale = 0.8`（再减小 20%）
   - 远距离（>50m）：`distanceScale = 1.0`（保持不变）
   - 中间距离：平滑插值

## 当前参数

**灵敏度调整**：
- **2倍镜阈值**：1.5（超过此值判断为高倍镜）
- **高倍镜灵敏度缩放**：0.5（高倍镜灵敏度降低到 50%）
- **屏幕边缘阈值**：50 像素（判断边缘漂移）
- **边缘漂移 mouseDelta 阈值**：10（判断是否为边缘漂移）

**后座力优化**：
- **低倍镜因子**：1.5（不调整）
- **高倍镜因子**：4.24（后座降低到 67%）
- **近距离阈值**：10m
- **远距离阈值**：50m
- **近距离缩放**：0.8（减小 20%）
- **远距离缩放**：1.0（不变）

## 技术细节

### 灵敏度调整实现

**游戏内部逻辑**：
1. **鼠标输入处理**：
   - `CharacterInputControl.OnPlayerMouseDelta()` 接收鼠标增量，存储到 `mouseDelta` 字段
   - `CharacterInputControl.Update()` 调用 `InputManager.SetAimInputUsingMouse(this.mouseDelta)`
   - `SetAimInputUsingMouse()` 将 `mouseDelta` 转换为屏幕坐标增量

2. **瞄准偏移计算**：
   - `GameCamera.UpdateAimOffsetNormal()` 使用 `aimOffsetDistanceFactor` 计算瞄准偏移
   - ADS 状态下，`aimOffsetDistanceFactor` 被 `gun.ADSAimDistanceFactor` 放大
   - 高倍镜的 `ADSAimDistanceFactor` 较大，导致灵敏度更高

**Mod 实现**：
- 使用 `[DefaultExecutionOrder(-100)]` 确保在 `CharacterInputControl.Update()` 之前执行
- 通过反射访问和修改 `CharacterInputControl.Instance.mouseDelta`
- 只对高倍镜且非边缘漂移的情况应用缩放，保持边缘漂移速度不变

### 后座力优化实现

**游戏内部逻辑**：
1. **后座力计算**：
   - `InputManager.AddRecoil()` 计算原始后座力值（`recoilV`, `recoilH`）
   - 后座力基于屏幕空间，在高倍镜放大视野下显得更大
   - `InputManager.ProcessMousePosViaRecoil()` 应用后座力到瞄准位置

2. **当前问题**：
   - 后座力值固定，不考虑瞄准距离
   - 近距离时，相同的屏幕空间后座力显得更大（几乎飞出屏幕外）
   - 远距离时，相对较小

**Mod 实现**：
- 使用反射访问 `InputManager.recoilV` 和 `recoilH`
- 在每次新的后座力事件（`InputManager.newRecoil = true`）时调整
- 使用线性插值方式平滑调整后座力：倍镜越大，后座降低越多
- 通过 `InputManager.InputAimPoint` 和 `gun.muzzle.position` 计算瞄准距离
- 后座力基于屏幕空间，在放大视野下显得更大，因此通过调整后座值使相对视野比例更一致

