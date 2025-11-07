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
        
        // 开镜开始时的 aimOffsetDistanceFactor（固定使用，避免开镜过程中变化导致漂移）
        private float adsStartDistanceFactor = 1f;
        
        // 开镜开始时的最大偏移距离（固定使用，避免开镜过程中变化导致限制不一致）
        private float adsStartMaxOffset = 25f;

        // 开镜开始时的瞄准点（用于保持退出开镜时的瞄准方向）
        private Vector3 adsStartAimPoint = Vector3.zero;
        private Vector2 adsStartAimScreenPoint = Vector2.zero;
        
        // 边缘滚动配置
        private float edgeScrollThreshold = 0.8f;   // 距离中心多远开始滚动
        private float edgeScrollSpeed = 50f;        // 边缘滚动速度（恢复到50）

        void Start()
        {
            // 缓存反射信息
            CacheReflectionInfo();
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
                    
                    // 记录开镜开始时的 aimOffsetDistanceFactor（防止开镜过程中变化）
                    adsStartDistanceFactor = (float)(aimOffsetDistanceFactorField?.GetValue(gameCamera) ?? 1f);
                    
                    // 计算开镜完成后的最大偏移距离（基于武器的 ADSAimDistanceFactor）
                    // 游戏公式：maxAimOffset = defaultAimOffset * gun.ADSAimDistanceFactor（完全开镜时）
                    var gun = player.GetGun();
                    if (gun != null)
                    {
                        const float defaultAimOffset = 5f; // 游戏中的默认值
                        adsStartMaxOffset = defaultAimOffset * gun.ADSAimDistanceFactor;
                    }
                    else
                    {
                        adsStartMaxOffset = 25f; // 无武器时的默认值
                    }
                    
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
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[ScopeSensitivity调试] 获取开镜参数时出错: {ex.Message}");
                }
                
                isAdsTransitioning = true;
            }
            
            // 检测关闭瞄准镜：AdsValue 回到接近 0
            if (isAdsTransitioning && currentAdsValue < 0.01f)
            {
                isAdsTransitioning = false;
            }
            
            lastAdsValue = currentAdsValue;
        }
        
        private void SetAdsOffset(CharacterMainControl player)
        {
            GameCamera? gameCamera = LevelManager.Instance?.GameCamera;
            InputManager? inputManager = LevelManager.Instance?.InputManager;
            if (gameCamera == null || inputManager == null || !isAdsTransitioning)
            {
                return;
            }
            
            float currentAdsValue = player.AdsValue;
            if (currentAdsValue <= 0.01f)
            {
                return;
            }
            
            try
            {
                // 读取设置前的值
                float beforeOffsetX = (float)(offsetFromTargetXField?.GetValue(gameCamera) ?? 0f);
                float beforeOffsetZ = (float)(offsetFromTargetZField?.GetValue(gameCamera) ?? 0f);
                float beforeMaxAimOffset = (float)(maxAimOffsetField?.GetValue(gameCamera) ?? 0f);
                float beforeDistanceFactor = (float)(aimOffsetDistanceFactorField?.GetValue(gameCamera) ?? 0f);
                
                // 使用开镜开始时计算的固定最大偏移距离（不再动态读取，避免开镜过程中变化导致限制问题）
                
                // 使用游戏归一化的增量
                CharacterInputControl inputControl = CharacterInputControl.Instance;
                Vector2 gameMouseDelta = (Vector2)(mouseDeltaField?.GetValue(inputControl) ?? Vector2.zero);
                float velocityScale = 0.01f * adsStartDistanceFactor;
                
                adsTargetOffsetX += gameMouseDelta.x * velocityScale;
                adsTargetOffsetZ += gameMouseDelta.y * velocityScale;
                
                // 边缘滚动：当鼠标靠近屏幕边缘时，自动扩展镜头
                Vector2 mousePos = inputManager.MousePos;
                Vector2 screenSize = new Vector2(Screen.width, Screen.height);
                Vector2 screenCenter = screenSize / 2f;
                Vector2 normalizedPos = (mousePos - screenCenter) / (screenSize / 2f); // -1 to 1
                
                // 计算边缘滚动增量（使用平方曲线，让速度更平滑递增）
                // 取消蓄力倍率，保持一致的边缘滚动速度
                float adsSpeedMultiplier = 1.0f;
                
                if (Mathf.Abs(normalizedPos.x) > edgeScrollThreshold)
                {
                    float excess = (Mathf.Abs(normalizedPos.x) - edgeScrollThreshold) / (1f - edgeScrollThreshold);
                    excess = excess * excess; // 平方曲线，让加速更平滑
                    float scrollDelta = Mathf.Sign(normalizedPos.x) * excess * edgeScrollSpeed * adsSpeedMultiplier * Time.deltaTime;
                    adsTargetOffsetX += scrollDelta;
                }
                
                if (Mathf.Abs(normalizedPos.y) > edgeScrollThreshold)
                {
                    float excess = (Mathf.Abs(normalizedPos.y) - edgeScrollThreshold) / (1f - edgeScrollThreshold);
                    excess = excess * excess; // 平方曲线，让加速更平滑
                    float scrollDelta = Mathf.Sign(normalizedPos.y) * excess * edgeScrollSpeed * adsSpeedMultiplier * Time.deltaTime;
                    adsTargetOffsetZ += scrollDelta;
                }
                
                // 直接使用基准偏移（已包含瞬时鼠标移动）
                float targetOffsetX = adsTargetOffsetX;
                float targetOffsetZ = adsTargetOffsetZ;
                
                // 限制偏移距离在最大范围内（保持和游戏原本的视野范围一致）
                float currentOffsetMagnitude = Mathf.Sqrt(targetOffsetX * targetOffsetX + targetOffsetZ * targetOffsetZ);
                if (currentOffsetMagnitude > adsStartMaxOffset)
                {
                    float scale = adsStartMaxOffset / currentOffsetMagnitude;
                    targetOffsetX *= scale;
                    targetOffsetZ *= scale;
                    
                    // 同时也更新基准偏移，避免累积超出范围
                    adsTargetOffsetX = targetOffsetX;
                    adsTargetOffsetZ = targetOffsetZ;
                }
                
                // 强制设置偏移（游戏无法 lerp 覆盖，因为 lerpSpeed=0）
                offsetFromTargetXField?.SetValue(gameCamera, targetOffsetX);
                offsetFromTargetZField?.SetValue(gameCamera, targetOffsetZ);

                // 同步 InputManager 与角色的瞄准点，避免退出开镜时发生回弹
                float distanceFactor = Mathf.Max(adsStartDistanceFactor, 0.0001f);
                Vector3 aimPoint = adsStartAimPoint;

                if (cameraRightVectorField != null && cameraForwardVectorField != null)
                {
                    Vector3 cameraRight = (Vector3)(cameraRightVectorField.GetValue(gameCamera) ?? Vector3.right);
                    Vector3 cameraForward = (Vector3)(cameraForwardVectorField.GetValue(gameCamera) ?? Vector3.forward);
                    Vector3 aimOffset = cameraRight * ((targetOffsetX - adsStartOffsetX) / distanceFactor) +
                                        cameraForward * ((targetOffsetZ - adsStartOffsetZ) / distanceFactor);

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
                    aimMousePosCacheField?.SetValue(inputManager, aimScreenPoint2D);
                    inputManager.SetMousePosition(aimScreenPoint2D);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ScopeSensitivity调试] 接管准星时出错: {ex.Message}");
            }
        }
    }
}

