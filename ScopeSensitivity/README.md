# ScopeSensitivity Mod

倍镜优化 Mod for 《逃离鸭科夫》（Escape from Duckov）

## 功能说明

本 Mod 提供三项倍镜优化功能：
1. **降低高倍镜开镜灵敏度**：降低 4倍镜、8倍镜开镜时的鼠标灵敏度，使其与 2倍镜接近
2. **优化后座力分布**：根据瞄准距离调整后座力，使近距离和远距离的后座相对视野比例更一致
3. **重做倍镜操作方式**：取消开镜自动漂移，改为鼠标跟随+边缘滚动的控制方式

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

### 功能 3：重做倍镜操作方式

**原版问题**：开镜时准星会自动沿鼠标位置向外漂移，难以精确控制瞄准点，且无法自由调整视野。

**本 Mod 方案**：完全重做开镜操作逻辑，提供更灵活的镜头控制方式。

#### 3.1 核心改变

**取消自动开镜漂移**：
- 原版游戏通过 `GameCamera.lerpSpeed` 让镜头平滑移动到计算的偏移位置
- 本 Mod 将 `lerpSpeed` 设为 0，完全接管镜头偏移控制
- 开镜时镜头不再自动漂移，保持稳定

#### 3.2 鼠标跟随镜头移动

开镜状态下，移动鼠标会实时调整镜头偏移：
- 使用瞬时鼠标速度（`mouseVelocity`）更新偏移，响应灵敏
- 基础速度系数：`0.005 × adsStartDistanceFactor`
- 支持二维方向自由移动

#### 3.3 边缘滚动

当鼠标靠近屏幕边缘时，镜头自动向外扩展视野：
- **触发条件**：鼠标归一化位置 > 0.8（约屏幕边缘 10% 区域）
- **速度曲线**：使用平方曲线（`excess²`）让加速更平滑
- **滚动速度**：50 units/秒
- **独立计算**：X轴和Z轴独立计算，支持对角线滚动

**计算公式**：
```
normalizedPos = (mousePos - screenCenter) / (screenSize / 2)  // -1 到 1
excess = (|normalizedPos| - 0.8) / 0.2  // 超出阈值的比例
scrollDelta = sign(normalizedPos) × excess² × 50 × deltaTime
```

#### 3.5 镜头移动范围限制

限制镜头偏移在圆形区域内，与原游戏不同倍镜的视野范围保持一致：
- **最大偏移**：`maxAimOffset = 5 × gun.ADSAimDistanceFactor`
- **2倍镜**：约 5-7.5 范围
- **4倍镜**：约 10-15 范围
- **8倍镜**：约 20-25 范围
- 开镜开始时计算一次固定值，避免开镜过程中动态变化导致的限制问题

**限制方式**：
```
currentMagnitude = sqrt(offsetX² + offsetZ²)
if (currentMagnitude > maxAimOffset) {
    scale = maxAimOffset / currentMagnitude
    offsetX *= scale
    offsetZ *= scale
}
```

#### 3.6 开镜状态管理

**开镜检测**：
- 监测 `AdsValue` 从 0 → >0.01 判断开镜开始
- 记录开镜初始参数：偏移、鼠标位置、距离因子、最大偏移等

**接管控制**：
- 设置 `lerpSpeed = 0` 禁用游戏的自动平滑
- 每帧强制设置 `offsetFromTargetX/Z` 完全控制镜头偏移

**退出接管**：
- 检测 `AdsValue` → <0.01 判断关闭瞄准镜
- 恢复原始 `lerpSpeed`，交还控制权给游戏

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

**倍镜操作方式**：
- **鼠标跟随速度系数**：0.005
- **边缘滚动阈值**：0.8（归一化位置）
- **边缘滚动速度**：50 units/秒
- **最大偏移计算**：5 × ADSAimDistanceFactor

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

### 倍镜操作方式实现

**游戏内部逻辑**：
1. **开镜偏移计算**：
   - `GameCamera.UpdateAimOffsetNormal()` 计算准星到鼠标的偏移向量
   - 使用 `offsetFromTargetX/Z` 存储偏移值
   - 通过 `lerpSpeed` 平滑移动到目标偏移（默认 12）

2. **原版问题**：
   - 开镜时准星自动向鼠标位置漂移，无法精确控制
   - 偏移值由游戏自动计算，无法自由调整视野范围

**Mod 实现**：

1. **完全接管镜头控制**：
   - 使用反射访问 `GameCamera.offsetFromTargetX/Z` 强制设置偏移
   - 设置 `lerpSpeed = 0` 禁用游戏的自动平滑
   - 每帧直接设置偏移值，完全控制镜头位置

2. **鼠标跟随**：
   - 监测鼠标帧间位移（`mouseVelocity`）
   - 累加到偏移目标值：`adsTargetOffsetX += mouseVelocity.x × velocityScale`
   - 速度系数考虑倍镜因子：`velocityScale = 0.005 × adsStartDistanceFactor`

3. **边缘滚动**：
   - 计算鼠标归一化位置：`normalizedPos = (mousePos - center) / (size / 2)`
   - 超出阈值时计算增量：`scrollDelta = sign × excess² × speed × deltaTime`
   - 使用平方曲线让加速更自然

5. **范围限制**：
   - 开镜开始时预计算最大偏移：`maxOffset = 5 × gun.ADSAimDistanceFactor`
   - 使用圆形区域限制：`if (magnitude > maxOffset) { scale and clamp }`
   - 同时更新基准偏移，防止累积溢出

6. **状态管理**：
   - 监测 `AdsValue` 变化检测开镜/关镜
   - 开镜时记录初始参数（偏移、鼠标位置、距离因子等）
   - 关镜时恢复 `lerpSpeed`，交还控制权

**执行顺序**：
- 使用 `[DefaultExecutionOrder(-100)]` 确保在游戏更新之前执行
- `CheckAndUpdateAdsState()` 检测开镜状态
- `SetAdsOffset()` 每帧更新偏移值
- 在 `GameCamera` 更新之前设置偏移，避免被游戏覆盖

