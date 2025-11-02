using System;
using System.Reflection;
using UnityEngine;

namespace ScopeSensitivity
{
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
        
        // 后座力调整的反射信息
        private Type? inputManagerType;
        private FieldInfo? recoilVField;
        private FieldInfo? recoilHField;
        private FieldInfo? recoilGunField;
        private FieldInfo? newRecoilField;
        private bool recoilReflectionCached = false;
        
        // 记录上次调整后座力的时间，确保每次新后座力只调整一次
        private float lastRecoilAdjustTime = 0f;

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
            
            if (recoilVField != null && recoilHField != null && recoilGunField != null && newRecoilField != null)
            {
                recoilReflectionCached = true;
            }
        }

        void Update()
        {
            // 使用 DefaultExecutionOrder(-100) 确保在 CharacterInputControl.Update 之前执行
            // Unity 会按照脚本执行顺序调用
            
            if (!reflectionCached)
            {
                return;
            }
            
            CharacterInputControl inputControl = CharacterInputControl.Instance;
            if (inputControl == null)
            {
                return;
            }
            
            var player = CharacterMainControl.Main;
            if (player == null)
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
            
            // 用户反馈：第一版调整方向对，但近距离后坐力几乎为0（0~6），希望范围是4~6
            // 用户最新需求：近距离再减小20%，远距离不变
            // 需要根据瞄准距离调整，而不是简单地根据倍镜因子
            
            // 获取瞄准距离
            Vector3 aimPoint = inputManager.InputAimPoint;
            Vector3 muzzlePos = gun.muzzle.position;
            float aimDistance = Vector3.Distance(aimPoint, muzzlePos);
            
            const float REFERENCE_FACTOR = 1.5f; // 参考倍镜因子（2倍镜）
            if (adsAimDistanceFactor > REFERENCE_FACTOR)
            {
                float factorRatio = adsAimDistanceFactor / REFERENCE_FACTOR;
                
                // 使用对数缩放作为基础（已验证有效）
                float baseScaleFactor = Mathf.Log(factorRatio, 2f);
                
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
    }
}

