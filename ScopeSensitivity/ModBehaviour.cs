using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

namespace ScopeSensitivity
{
    // Update 在 CharacterInputControl 之前执行（处理灵敏度和后座力）
    // LateUpdate 在 GameCamera 之后执行（处理开镜漂移）
    [DefaultExecutionOrder(-100)]
    public class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        // 2倍镜的 ADSAimDistanceFactor 阈值（假设 2倍镜约为 1.0-1.5）
        // 如果武器的 ADSAimDistanceFactor 超过这个值，说明是 4倍镜或 8倍镜，需要降低灵敏度
        private const float TWO_SCOPE_FACTOR_THRESHOLD = 1.5f;
        
        // 高倍镜灵敏度缩放参数：0.4 = 高倍镜灵敏度降低到约 40%
        private const float HIGH_SCOPE_SENSITIVITY_SCALE = 0.4f;
        
        // 缓存反射信息
        private Type? characterInputControlType;
        private FieldInfo? mouseDeltaField;
        private bool reflectionCached = false;
        
        // 后座力调整与瞄准同步的反射信息
        private Type? inputManagerType;
        private FieldInfo? recoilVField;
        private FieldInfo? recoilHField;
        private FieldInfo? recoilGunField;
        private FieldInfo? newRecoilField;
        private FieldInfo? inputAimPointField;
        private FieldInfo? aimScreenPointField;
        private FieldInfo? aimMousePosCacheField;
        private bool recoilReflectionCached = false;
        
        // 记录上次调整后座力的时间，确保每次新后座力只调整一次
        private float lastRecoilAdjustTime = 0f;
        
        // GameCamera 反射信息（用于取消开镜漂移）
        private Type? gameCameraType;
        private FieldInfo? offsetFromTargetXField;
        private FieldInfo? offsetFromTargetZField;
        private FieldInfo? cameraRightVectorField;
        private FieldInfo? cameraForwardVectorField;
        private FieldInfo? maxAimOffsetField;
        private FieldInfo? aimOffsetDistanceFactorField;
        private FieldInfo? lerpSpeedField;
        private MethodInfo? screenPointToCharacterPlaneMethod;
        private FieldInfo? mianCameraArmField;
        private bool gameCameraReflectionCached = false;
        
        // 保存原始 lerpSpeed，用于恢复
        private float originalLerpSpeed = 12f;
        
        // 记录上次的 AdsValue，用于检测开镜开始
        private float lastAdsValue = 0f;
        
        // 标记是否正在开镜过程中（用于持续强制设置偏移）
        private bool isAdsTransitioning = false;
        
        // 开镜时的目标偏移值（开镜开始时计算一次，然后持续应用）
        private float adsTargetOffsetX = 0f;
        private float adsTargetOffsetZ = 0f;
        private float adsStartOffsetX = 0f;
        private float adsStartOffsetZ = 0f;
        
        // 开镜开始时的瞄准点（用于保持退出开镜时的瞄准方向）
        private Vector3 adsStartAimPoint = Vector3.zero;
        private Vector2 adsStartAimScreenPoint = Vector2.zero;

        // 调试辅助
        private bool adsEnterLogged = false;
        private bool adsExitLogged = false;
        private int adsLogFrameCount = 0;
        // 长期日志计数（每 60 帧输出一次，便于观察连续拖拽）
        private int adsLongLogFrameCount = 0;

        // 在产生新后座力后的短时间内抑制边缘滚动（用秒计，帧率无关）
        private float recoilSuppressTimer = 0f;
        private const float RECOIL_SUPPRESS_DURATION = 0.25f; // 0.25 秒

        // 边缘滚动在连续射击时快速衰减；并限制整个 ADS 期间因边缘滚动产生的位移不超过 10m
        private const float EDGE_SCROLL_RECOIL_DAMP = 0.1f;   // 后座期衰减系数
        private const float EDGE_SCROLL_MAX_ACCUM = 3f;       // 最大累计位移（世界单位）
        private float edgeScrollAccum = 0f;                    // 本次 ADS 已累计的边缘滚动位移
        
        // 边缘滚动配置
        private float edgeScrollThreshold = 0.8f;   // 距离中心多远开始滚动
        private float edgeScrollSpeed = 50f;        // 边缘滚动速度（恢复到50）

        void Start()
        {
            // 缓存反射信息
            CacheReflectionInfo();

            Debug.Log("[ScopeSensitivity] ModBehaviour.Start executed. reflectionCached=" + reflectionCached + ", gameCameraReflectionCached=" + gameCameraReflectionCached);
        }
        

        private void CacheReflectionInfo()
        {
            // 获取 CharacterInputControl 类型
            characterInputControlType = typeof(CharacterInputControl);
            
            // 获取 mouseDelta 字段
            mouseDeltaField = characterInputControlType.GetField("mouseDelta", 
                BindingFlags.NonPublic | BindingFlags.Instance);
            
            if (mouseDeltaField != null)
            {
                reflectionCached = true;
            }
            
            // 获取 InputManager 类型
            inputManagerType = typeof(InputManager);
            
            // 获取后座力相关字段
            recoilVField = inputManagerType.GetField("recoilV", 
                BindingFlags.NonPublic | BindingFlags.Instance);
            recoilHField = inputManagerType.GetField("recoilH", 
                BindingFlags.NonPublic | BindingFlags.Instance);
            recoilGunField = inputManagerType.GetField("recoilGun", 
                BindingFlags.NonPublic | BindingFlags.Instance);
            newRecoilField = inputManagerType.GetField("newRecoil", 
                BindingFlags.NonPublic | BindingFlags.Instance);
            inputAimPointField = inputManagerType.GetField("inputAimPoint",
                BindingFlags.NonPublic | BindingFlags.Instance);
            aimScreenPointField = inputManagerType.GetField("aimScreenPoint",
                BindingFlags.NonPublic | BindingFlags.Instance);
            aimMousePosCacheField = inputManagerType.GetField("_aimMousePosCache",
                BindingFlags.NonPublic | BindingFlags.Instance);
            
            if (recoilVField != null && recoilHField != null && recoilGunField != null && newRecoilField != null)
            {
                recoilReflectionCached = true;
            }
            
            // 获取 GameCamera 类型
            gameCameraType = typeof(GameCamera);
            
            // 获取相机偏移相关字段
            offsetFromTargetXField = gameCameraType.GetField("offsetFromTargetX", 
                BindingFlags.NonPublic | BindingFlags.Instance);
            
            offsetFromTargetZField = gameCameraType.GetField("offsetFromTargetZ", 
                BindingFlags.NonPublic | BindingFlags.Instance);
            
            cameraRightVectorField = gameCameraType.GetField("cameraRightVector", 
                BindingFlags.NonPublic | BindingFlags.Instance);
            
            cameraForwardVectorField = gameCameraType.GetField("cameraForwardVector", 
                BindingFlags.NonPublic | BindingFlags.Instance);
            
            maxAimOffsetField = gameCameraType.GetField("maxAimOffset", 
                BindingFlags.NonPublic | BindingFlags.Instance);
            
            aimOffsetDistanceFactorField = gameCameraType.GetField("aimOffsetDistanceFactor", 
                BindingFlags.NonPublic | BindingFlags.Instance);
            
            mianCameraArmField = gameCameraType.GetField("mianCameraArm", 
                BindingFlags.Public | BindingFlags.Instance);
            
            lerpSpeedField = gameCameraType.GetField("lerpSpeed", 
                BindingFlags.NonPublic | BindingFlags.Instance);
            
            // 获取 ScreenPointToCharacterPlane 方法
            screenPointToCharacterPlaneMethod = gameCameraType.GetMethod("ScreenPointToCharacterPlane", 
                BindingFlags.NonPublic | BindingFlags.Instance);
            
            if (offsetFromTargetXField != null && offsetFromTargetZField != null && 
                cameraRightVectorField != null && cameraForwardVectorField != null &&
                maxAimOffsetField != null && aimOffsetDistanceFactorField != null &&
                lerpSpeedField != null && screenPointToCharacterPlaneMethod != null && mianCameraArmField != null)
            {
                gameCameraReflectionCached = true;
            }
            else
            {
                Debug.LogError("[ScopeSensitivity] GameCamera 反射缓存失败！");
            }
        }

        void Update()
        {
            // 使用 DefaultExecutionOrder(-100) 确保在 CharacterInputControl.Update 之前执行
            // Unity 会按照脚本执行顺序调用
            
            var player = CharacterMainControl.Main;
            if (player == null)
            {
                return;
            }
            
            // 优先处理开镜漂移控制（在游戏计算之前）
            if (gameCameraReflectionCached)
            {
                CheckAndUpdateAdsState(player);
                
                // 如果正在接管，禁用游戏的 lerp（让游戏无法平滑到计算的偏移）
                if (isAdsTransitioning)
                {
                    GameCamera? gameCamera = LevelManager.Instance?.GameCamera;
                    if (gameCamera != null && lerpSpeedField != null)
                    {
                        lerpSpeedField.SetValue(gameCamera, 0f);
                    }
                    
                    SetAdsOffset(player);
                }
                else
                {
                    // 恢复 lerpSpeed
                    if (originalLerpSpeed > 0)
                    {
                        GameCamera? gameCamera = LevelManager.Instance?.GameCamera;
                        if (gameCamera != null && lerpSpeedField != null)
                        {
                            lerpSpeedField.SetValue(gameCamera, originalLerpSpeed);
                        }
                    }
                }
            }
            
            if (!reflectionCached)
            {
                return;
            }
            
            CharacterInputControl inputControl = CharacterInputControl.Instance;
            if (inputControl == null)
            {
                return;
            }
            
            // 检查是否在 ADS 状态
            if (player.AdsValue < 0.01f)
            {
                return; // 未开镜，不处理
            }
            
            // 获取当前武器
            var gun = player.GetGun();
            if (gun == null)
            {
                return;
            }
            
            // 获取 ADSAimDistanceFactor，判断倍镜类型
            float adsAimDistanceFactor = gun.ADSAimDistanceFactor;
            
            // 如果 ADSAimDistanceFactor 较大（高倍镜），需要降低灵敏度
            if (adsAimDistanceFactor > TWO_SCOPE_FACTOR_THRESHOLD)
            {
                // 获取当前 mouseDelta
                Vector2 currentMouseDelta = (Vector2)(mouseDeltaField?.GetValue(inputControl) ?? Vector2.zero);
                
                // 检查是否是边缘漂移（鼠标在屏幕边缘且 mouseDelta 很大）
                // 如果是边缘漂移，不修改（保持第一阶段速度）
                bool isEdgeDrift = IsEdgeDrift(currentMouseDelta);
                
                if (!isEdgeDrift)
                {
                    // 第二阶段：正常移动准星，应用灵敏度缩放
                    Vector2 scaledMouseDelta = currentMouseDelta * HIGH_SCOPE_SENSITIVITY_SCALE;
                    mouseDeltaField?.SetValue(inputControl, scaledMouseDelta);
                }
            }
            
            // 后座力距离调整
            if (recoilReflectionCached)
            {
                AdjustRecoilByDistance(player, gun);
            }
        }
        
        
        private void AdjustRecoilByDistance(CharacterMainControl player, ItemAgent_Gun gun)
        {
            InputManager? inputManager = LevelManager.Instance?.InputManager;
            if (inputManager == null || gun == null)
            {
                return;
            }
            
            // 检查是否有新的后座力
            bool newRecoil = (bool)(newRecoilField?.GetValue(inputManager) ?? false);
            if (!newRecoil)
            {
                return;
            }
            
            // 产生新后座力：在接下来一小段时间暂停边缘滚动
            recoilSuppressTimer = RECOIL_SUPPRESS_DURATION;

            // 获取当前后座力值
            float recoilV = (float)(recoilVField?.GetValue(inputManager) ?? 0f);
            float recoilH = (float)(recoilHField?.GetValue(inputManager) ?? 0f);
            
            if (recoilV == 0f && recoilH == 0f)
            {
                return;
            }
            
            // 检查是否已经调整过这次后座力（避免重复调整）
            float currentTime = Time.time;
            if (Mathf.Abs(currentTime - lastRecoilAdjustTime) < 0.01f)
            {
                return; // 同一帧已经调整过
            }
            
            // 获取倍镜的 ADSAimDistanceFactor
            float adsAimDistanceFactor = gun.ADSAimDistanceFactor;
            
            // 获取瞄准距离
            Vector3 aimPoint = inputManager.InputAimPoint;
            Vector3 muzzlePos = gun.muzzle.position;
            float aimDistance = Vector3.Distance(aimPoint, muzzlePos);
            
            const float LOW_SCOPE_FACTOR = 1.5f; // 低于此值不调整
            
            if (adsAimDistanceFactor > LOW_SCOPE_FACTOR)
            {
                // 使用线性插值：factor 在 1.5 到 4.24 之间，scale 从 1.0 到 1.5
                // 这样 4.24 时和原方案一致，且渐进过渡
                const float HIGH_SCOPE_FACTOR = 4.24f;
                const float MIN_SCALE = 1.0f;
                const float MAX_SCALE = 1.5f;
                
                float t = Mathf.InverseLerp(LOW_SCOPE_FACTOR, HIGH_SCOPE_FACTOR, adsAimDistanceFactor);
                float baseScaleFactor = Mathf.Lerp(MIN_SCALE, MAX_SCALE, t);
                
                // 根据瞄准距离额外调整：近距离再减小20%，远距离不变
                // 定义近距离和远距离的阈值（可以根据实际游戏调整）
                const float CLOSE_DISTANCE = 10f; // 近距离阈值
                const float FAR_DISTANCE = 50f;   // 远距离阈值
                
                // 计算距离系数：近距离时接近0.8，远距离时接近1.0
                // 注意：distanceScale 是直接用于后座力的乘数，不是用于 scaleFactor 的
                float distanceScale = Mathf.Lerp(0.8f, 1.0f, Mathf.InverseLerp(CLOSE_DISTANCE, FAR_DISTANCE, aimDistance));
                
                // 先用 baseScaleFactor 调整后座力
                float adjustedRecoilV = recoilV / baseScaleFactor;
                float adjustedRecoilH = recoilH / baseScaleFactor;
                
                // 再根据距离进一步调整：近距离再减小20%（乘以0.8）
                adjustedRecoilV *= distanceScale;
                adjustedRecoilH *= distanceScale;
                
                // 应用调整后的后座力
                recoilVField?.SetValue(inputManager, adjustedRecoilV);
                recoilHField?.SetValue(inputManager, adjustedRecoilH);
                
                lastRecoilAdjustTime = currentTime;
            }
        }
        
        private bool IsEdgeDrift(Vector2 mouseDelta)
        {
            // 判断是否是边缘漂移：
            // 1. mouseDelta 的幅度很大（超过阈值）
            // 2. 鼠标位置接近屏幕边缘
            
            InputManager? inputManager = LevelManager.Instance?.InputManager;
            if (inputManager == null)
            {
                return false;
            }
            
            Vector2 mousePos = inputManager.MousePos;
            float edgeThreshold = 50f; // 屏幕边缘阈值（像素）
            
            // 检查鼠标是否在屏幕边缘附近
            bool isNearEdge = mousePos.x < edgeThreshold || 
                             mousePos.x > Screen.width - edgeThreshold ||
                             mousePos.y < edgeThreshold || 
                             mousePos.y > Screen.height - edgeThreshold;
            
            // 如果鼠标在边缘附近且 mouseDelta 很大，认为是边缘漂移
            if (isNearEdge && mouseDelta.magnitude > 10f)
            {
                return true;
            }
            
            return false;
        }
        
        private void CheckAndUpdateAdsState(CharacterMainControl player)
        {
            GameCamera? gameCamera = LevelManager.Instance?.GameCamera;
            InputManager? inputManager = LevelManager.Instance?.InputManager;
            if (gameCamera == null || inputManager == null)
            {
                return;
            }
            
            float currentAdsValue = player.AdsValue;
            
            // 检测开镜开始：AdsValue 从 0 开始增加
            if (lastAdsValue < 0.01f && currentAdsValue > 0.01f && !isAdsTransitioning)
            {
                // 记录开镜开始时的参数
                try
                {
                    adsTargetOffsetX = (float)(offsetFromTargetXField?.GetValue(gameCamera) ?? 0f);
                    adsTargetOffsetZ = (float)(offsetFromTargetZField?.GetValue(gameCamera) ?? 0f);
                    adsStartOffsetX = adsTargetOffsetX;
                    adsStartOffsetZ = adsTargetOffsetZ;
                    
                    // 保存原始 lerpSpeed
                    if (lerpSpeedField != null)
                    {
                        float currentLerpSpeed = (float)(lerpSpeedField.GetValue(gameCamera) ?? 0f);
                        if (currentLerpSpeed > 0)
                        {
                            originalLerpSpeed = currentLerpSpeed;
                        }
                    }

                    adsStartAimPoint = inputManager.InputAimPoint;
                    adsStartAimScreenPoint = inputManager.AimScreenPoint;

                    if (!adsEnterLogged)
                    {
                        var logGun = player.GetGun();
                        string gunName = logGun != null ? logGun.name : "null";
                        float gunFactor = logGun != null ? logGun.ADSAimDistanceFactor : -1f;
                        float currentMaxOffset = (float)(maxAimOffsetField?.GetValue(gameCamera) ?? 0f);
                        float currentDistanceFactor = (float)(aimOffsetDistanceFactorField?.GetValue(gameCamera) ?? 0f);
                        float currentLerpSpeed = (float)(lerpSpeedField?.GetValue(gameCamera) ?? -1f);
                        LogAdsDebug($"进入ADS: Gun={gunName}, Factor={gunFactor:F2}, StartOffset=({adsTargetOffsetX:F2},{adsTargetOffsetZ:F2}), AimPoint={adsStartAimPoint}, AimScreen={adsStartAimScreenPoint}, LerpSpeed={currentLerpSpeed}, CamMaxOffset={currentMaxOffset:F2}, CamDistanceFactor={currentDistanceFactor:F2}");
                        LogGameCameraState(gameCamera, inputManager, "进入ADS");
                        adsEnterLogged = true;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[ScopeSensitivity调试] 获取开镜参数时出错: {ex.Message}");
                }
                
                isAdsTransitioning = true;
                adsExitLogged = false;
                adsLogFrameCount = 0;

                // 进入 ADS 时重置边缘滚动累计量
                edgeScrollAccum = 0f;
            }
            
            // 检测关闭瞄准镜：AdsValue 回到接近 0
            if (isAdsTransitioning && currentAdsValue < 0.01f)
            {
                if (!adsExitLogged)
                {
                    try
                    {
                        GameCamera? gameCameraExit = LevelManager.Instance?.GameCamera;
                        InputManager? inputManagerExit = LevelManager.Instance?.InputManager;
                        var gun = player.GetGun();
                        string gunName = gun != null ? gun.name : "null";
                        float currentOffsetX = gameCameraExit != null ? (float)(offsetFromTargetXField?.GetValue(gameCameraExit) ?? 0f) : 0f;
                        float currentOffsetZ = gameCameraExit != null ? (float)(offsetFromTargetZField?.GetValue(gameCameraExit) ?? 0f) : 0f;
                        Vector3 currentAimPoint = player.GetCurrentAimPoint();
                        LogAdsDebug($"退出ADS: Gun={gunName}, Offset=({currentOffsetX:F2},{currentOffsetZ:F2}), AimPoint={currentAimPoint}");
                        if (gameCameraExit != null && inputManagerExit != null)
                        {
                            LogGameCameraState(gameCameraExit, inputManagerExit, "退出ADS");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[ScopeSensitivity调试] 记录 ADS 结束信息失败: {ex.Message}");
                    }
                    adsExitLogged = true;
                }

                isAdsTransitioning = false;
                adsEnterLogged = false;
                adsLogFrameCount = 0;
            }
            
            lastAdsValue = currentAdsValue;
        }
        
        private void SetAdsOffset(CharacterMainControl player)
        {
            GameCamera? gameCamera = LevelManager.Instance?.GameCamera;
            InputManager? inputManager = LevelManager.Instance?.InputManager;
            if (gameCamera == null || inputManager == null || !isAdsTransitioning)
            {
                if (isAdsTransitioning && !adsEnterLogged)
                {
                    LogAdsDebug("SetAdsOffset: GameCamera 或 InputManager 未准备好，无法更新");
                }
                return;
            }
            
            float currentAdsValue = player.AdsValue;
            if (currentAdsValue <= 0.01f)
            {
                if (adsLogFrameCount == 0)
                {
                    LogAdsDebug($"SetAdsOffset: AdsValue={currentAdsValue:F3}，跳过更新");
                }
                return;
            }
            
            try
            {
                // 读取设置前的值
                float beforeOffsetX = (float)(offsetFromTargetXField?.GetValue(gameCamera) ?? 0f);
                float beforeOffsetZ = (float)(offsetFromTargetZField?.GetValue(gameCamera) ?? 0f);
                float beforeMaxAimOffset = (float)(maxAimOffsetField?.GetValue(gameCamera) ?? 0f);
                float beforeDistanceFactor = (float)(aimOffsetDistanceFactorField?.GetValue(gameCamera) ?? 0f);
                
                // 使用游戏归一化的增量
                CharacterInputControl inputControl = CharacterInputControl.Instance;
                Vector2 gameMouseDelta = (Vector2)(mouseDeltaField?.GetValue(inputControl) ?? Vector2.zero);
                float currentDistanceFactor = (float)(aimOffsetDistanceFactorField?.GetValue(gameCamera) ?? 1f);
                if (Mathf.Abs(currentDistanceFactor) < 0.0001f)
                {
                    currentDistanceFactor = 0.0001f;
                }
                float velocityScale = 0.01f * currentDistanceFactor;
                
                adsTargetOffsetX += gameMouseDelta.x * velocityScale;
                adsTargetOffsetZ += gameMouseDelta.y * velocityScale;
                
                // 边缘滚动：当鼠标靠近屏幕边缘时，自动扩展镜头
                Vector2 mousePos = inputManager.MousePos;
                Vector2 screenSize = new Vector2(Screen.width, Screen.height);
                Vector2 screenCenter = screenSize / 2f;
                Vector2 normalizedPos = (mousePos - screenCenter) / (screenSize / 2f); // -1 to 1

                // 计算边缘滚动增量（使用平方曲线，让速度更平滑递增）
                float adsSpeedMultiplier = 1.0f;

                float ApplyScrollWithLimit(float delta)
                {
                    float remaining = EDGE_SCROLL_MAX_ACCUM - edgeScrollAccum;
                    if (remaining <= 0f)
                    {
                        return 0f; // 已达上限
                    }
                    if (Mathf.Abs(delta) > remaining)
                    {
                        delta = Mathf.Sign(delta) * remaining;
                    }
                    edgeScrollAccum += Mathf.Abs(delta);
                    return delta;
                }

                bool suppressEdgeByRecoil = recoilSuppressTimer > 0f;

                if (Mathf.Abs(normalizedPos.x) > edgeScrollThreshold)
                {
                    float excess = (Mathf.Abs(normalizedPos.x) - edgeScrollThreshold) / (1f - edgeScrollThreshold);
                    excess = excess * excess;
                    float scrollDelta = Mathf.Sign(normalizedPos.x) * excess * edgeScrollSpeed * adsSpeedMultiplier * Time.deltaTime;
                    if (suppressEdgeByRecoil)
                    {
                        scrollDelta *= EDGE_SCROLL_RECOIL_DAMP; // 强力衰减
                        scrollDelta = ApplyScrollWithLimit(scrollDelta);
                    }
                    adsTargetOffsetX += scrollDelta;
                }

                if (Mathf.Abs(normalizedPos.y) > edgeScrollThreshold)
                {
                    float excess = (Mathf.Abs(normalizedPos.y) - edgeScrollThreshold) / (1f - edgeScrollThreshold);
                    excess = excess * excess;
                    float scrollDelta = Mathf.Sign(normalizedPos.y) * excess * edgeScrollSpeed * adsSpeedMultiplier * Time.deltaTime;
                    if (suppressEdgeByRecoil)
                    {
                        scrollDelta *= EDGE_SCROLL_RECOIL_DAMP;
                        scrollDelta = ApplyScrollWithLimit(scrollDelta);
                    }
                    adsTargetOffsetZ += scrollDelta;
                }
                
                // 直接使用基准偏移（已包含瞬时鼠标移动）
                float targetOffsetX = adsTargetOffsetX;
                float targetOffsetZ = adsTargetOffsetZ;
                
                // 限制偏移距离在最大范围内（保持和游戏原本的视野范围一致）
                float currentMaxOffset = (float)(maxAimOffsetField?.GetValue(gameCamera) ?? 25f);
                // 通过插值到固定上限 (HighScopeMaxOffset=24)，让 4.24 倍镜约 50m，低倍镜保持原值
                const float HIGH_SCOPE_MAX_OFFSET = 22f; // 经验值: ~52m
                var currentGun = player.GetGun();
                float gunFactorLocal = currentGun != null ? currentGun.ADSAimDistanceFactor : 1f;
                float tHigh = Mathf.InverseLerp(TWO_SCOPE_FACTOR_THRESHOLD, 4.24f, gunFactorLocal);
                float extendedMaxOffset = Mathf.Lerp(currentMaxOffset, HIGH_SCOPE_MAX_OFFSET, tHigh);
                if (Mathf.Abs(extendedMaxOffset - currentMaxOffset) > 0.001f && maxAimOffsetField != null)
                {
                    maxAimOffsetField.SetValue(gameCamera, extendedMaxOffset);
                }
                currentMaxOffset = extendedMaxOffset;
                if (currentMaxOffset < 0.0001f)
                {
                    currentMaxOffset = Mathf.Max(Mathf.Abs(beforeMaxAimOffset), 0.0001f);
                }
                // 分轴独立钳制：各轴各自限制到 ±currentMaxOffset，解决对角抖动
                if (Mathf.Abs(targetOffsetX) > currentMaxOffset)
                {
                    targetOffsetX = Mathf.Clamp(targetOffsetX, -currentMaxOffset, currentMaxOffset);
                    adsTargetOffsetX = targetOffsetX;
                }
                if (Mathf.Abs(targetOffsetZ) > currentMaxOffset)
                {
                    targetOffsetZ = Mathf.Clamp(targetOffsetZ, -currentMaxOffset, currentMaxOffset);
                    adsTargetOffsetZ = targetOffsetZ;
                }
                
                // 强制设置偏移（游戏无法 lerp 覆盖，因为 lerpSpeed=0）
                offsetFromTargetXField?.SetValue(gameCamera, targetOffsetX);
                offsetFromTargetZField?.SetValue(gameCamera, targetOffsetZ);

                // 同步 InputManager 与角色的瞄准点，避免退出开镜时发生回弹
                float distanceFactor = currentDistanceFactor;
                Vector3 aimPoint = adsStartAimPoint;

                if (cameraRightVectorField != null && cameraForwardVectorField != null)
                {
                    Vector3 cameraRight = (Vector3)(cameraRightVectorField.GetValue(gameCamera) ?? Vector3.right);
                    Vector3 cameraForward = (Vector3)(cameraForwardVectorField.GetValue(gameCamera) ?? Vector3.forward);
                    Vector3 aimOffset = cameraRight * (targetOffsetX - adsStartOffsetX) +
                                        cameraForward * (targetOffsetZ - adsStartOffsetZ);

                    // 保持瞄准平面高度一致
                    aimPoint = adsStartAimPoint + aimOffset;
                    aimPoint.y = adsStartAimPoint.y;
                }

                if (inputAimPointField != null)
                {
                    inputAimPointField.SetValue(inputManager, aimPoint);
                }

                player.SetAimPoint(aimPoint);

                Camera? renderCamera = gameCamera.renderCamera;
                if (renderCamera != null)
                {
                    Vector3 aimScreenPoint = renderCamera.WorldToScreenPoint(aimPoint);
                    Vector2 aimScreenPoint2D = new Vector2(aimScreenPoint.x, aimScreenPoint.y);

                    aimScreenPointField?.SetValue(inputManager, aimScreenPoint2D);
                }

                // 额外每 60 帧记录一次关键参数，便于持续观察最远瞄准距离限制情况
                adsLongLogFrameCount++;
                if (adsLongLogFrameCount % 60 == 0)
                {
                    float offsetMagnitude = Mathf.Sqrt(targetOffsetX * targetOffsetX + targetOffsetZ * targetOffsetZ);
                    float worldOffsetMagnitude = Vector3.Distance(aimPoint, adsStartAimPoint);
                    var gunLong = player.GetGun();
                    float gunFactorLong = gunLong != null ? gunLong.ADSAimDistanceFactor : -1f;
                    Vector3 muzzlePos = gunLong != null ? gunLong.muzzle.position : Vector3.zero;
                    float aimDistance = Vector3.Distance(aimPoint, muzzlePos);
                    LogAdsDebug($"[LongLog] Frame={adsLongLogFrameCount}, GunFactor={gunFactorLong:F2}, OffsetMag={offsetMagnitude:F3}/{currentMaxOffset:F3}, WorldOffsetMag={worldOffsetMagnitude:F3}, AimDist={aimDistance:F3}, DistanceFactor={distanceFactor:F3}");
                }

                if (adsLogFrameCount < 5)
                {
                    Vector3 currentAimPoint = inputManager.InputAimPoint;
                    Vector2 currentAimScreen = inputManager.AimScreenPoint;
                    Vector2 currentMousePos = inputManager.MousePos;
                    var gun = player.GetGun();
                    string gunName = gun != null ? gun.name : "null";
                    float gunFactor = gun != null ? gun.ADSAimDistanceFactor : -1f;
                    LogAdsDebug($"SetAdsOffset[{adsLogFrameCount}]: Gun={gunName}, Factor={gunFactor:F2}, AdsValue={currentAdsValue:F3}, MouseDelta={gameMouseDelta}, TargetOffset=({targetOffsetX:F3},{targetOffsetZ:F3}), StartOffset=({adsStartOffsetX:F3},{adsStartOffsetZ:F3}), MaxOffset={currentMaxOffset:F3}, DistanceFactor={distanceFactor:F3}, AimPoint={currentAimPoint}, AimScreen={currentAimScreen}, MousePos={currentMousePos}");
                    LogGameCameraState(gameCamera, inputManager, $"SetAdsOffset[{adsLogFrameCount}]");
                    adsLogFrameCount++;
                }

                // 每帧递减抑制计数器
                if (recoilSuppressTimer > 0f)
                {
                    recoilSuppressTimer -= Time.deltaTime;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ScopeSensitivity调试] 接管准星时出错: {ex.Message}");
            }
        }

        private void LogGameCameraState(GameCamera gameCamera, InputManager inputManager, string tag)
        {
            // logging disabled for release build
            return;
        }

        private void LogAdsDebug(string message)
        {
            // logging disabled for release build
            return;
        }
    }
}

