using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using ItemStatsSystem;
using UnityEngine;

namespace InfiniteSyringes
{
    public class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        // 被保护的针类物品集合（订阅了 onSetStackCount 事件的物品）
        private HashSet<Item> protectedNeedles = new HashSet<Item>();
        
        // 上个 StackCount 值（用于判断是否减少）
        private Dictionary<int, int> lastStackCount = new Dictionary<int, int>(); // InstanceID -> StackCount
        
        // 标记是否正在进行启动扫描（避免启动时的数量变化被误判）
        private bool isScanning = false;
        
        // 追踪刚使用过的针剂（使用后预期会减少数量）
        private HashSet<int> recentlyUsedNeedles = new HashSet<int>(); // InstanceID

        void Start()
        {
            // 订阅静态物品使用事件（在使用完成时触发）
            UsageUtilities.OnItemUsedStaticEvent += OnItemUsedStatic;
            
            // 订阅场景加载完成事件
            SceneLoader.onFinishedLoadingScene += OnSceneFinishedLoading;
        }
        
        private void OnSceneFinishedLoading(SceneLoadingContext context)
        {
            // 延迟一帧，确保所有背包物品都已加载
            StartCoroutine(ScanExistingNeedles());
        }
        
        private IEnumerator ScanExistingNeedles()
        {
            // 设置扫描标记
            isScanning = true;
            
            // 等待一帧
            yield return new WaitForEndOfFrame();
            
            // 获取主角背包
            var player = CharacterMainControl.Main;
            if (player != null && player.CharacterItem != null && player.CharacterItem.Inventory != null)
            {
                // 遍历背包中所有物品（递归检查子物品，包括收纳包）
                foreach (var item in player.CharacterItem.Inventory.Content)
                {
                    if (item != null)
                    {
                        ScanItemRecursively(item);
                    }
                }
            }
            
            // 扫描完成
            isScanning = false;
        }
        
        private void ScanItemRecursively(Item item)
        {
            // 检查当前物品
            ProtectNeedleIfNeeded(item);
            
            // 递归检查子物品（如收纳包中的物品）
            if (item.Inventory != null)
            {
                foreach (var childItem in item.Inventory.Content)
                {
                    if (childItem != null)
                    {
                        ScanItemRecursively(childItem);
                    }
                }
            }
        }
        
        private void ProtectNeedleIfNeeded(Item item)
        {
            // 检查是否是针类物品：使用游戏原生Tags判断
            bool isNeedle = IsNeedleItem(item) && item.Stackable;
            
            if (isNeedle && !protectedNeedles.Contains(item))
            {
                int instanceID = item.GetInstanceID();
                
                // 订阅数量变化事件
                item.onSetStackCount += OnNeedleStackCountChanged;
                protectedNeedles.Add(item);
                
                // 记录当前数量
                lastStackCount[instanceID] = item.StackCount;
            }
        }
        
        /// <summary>
        /// 判断物品是否为针剂类物品
        /// 根据游戏原生Tags判断：检查是否包含 "Injector" tag
        /// 与游戏针剂包的过滤逻辑完全一致，语言无关，准确可靠
        /// </summary>
        private bool IsNeedleItem(Item item)
        {
            if (item == null || item.Tags == null)
                return false;
            
            // 只检查 Injector tag（与游戏针剂包逻辑一致）
            return item.Tags.Contains("Injector");
        }

        void OnDestroy()
        {
            // 取消订阅
            UsageUtilities.OnItemUsedStaticEvent -= OnItemUsedStatic;
            SceneLoader.onFinishedLoadingScene -= OnSceneFinishedLoading;
            
            // 取消所有针类物品的事件订阅
            foreach (var needle in protectedNeedles)
            {
                if (needle != null)
                {
                    needle.onSetStackCount -= OnNeedleStackCountChanged;
                }
            }
        }

        private void OnItemUsedStatic(Item item)
        {
            // 保护新使用的针类物品
            ProtectNeedleIfNeeded(item);
            
            // 标记为刚使用的针剂
            if (item != null && protectedNeedles.Contains(item))
            {
                int instanceID = item.GetInstanceID();
                recentlyUsedNeedles.Add(instanceID);
            }
        }
        
        private void OnNeedleStackCountChanged(Item item)
        {
            if (item == null || !protectedNeedles.Contains(item))
                return;
            
            int instanceID = item.GetInstanceID();
            int currentCount = item.StackCount;
            
            // 如果正在扫描启动时的物品，只更新记录，不处理数量变化
            if (isScanning)
            {
                lastStackCount[instanceID] = currentCount;
                return;
            }
            
            // 检查是否记录过上次的数量
            if (!lastStackCount.ContainsKey(instanceID))
            {
                lastStackCount[instanceID] = currentCount;
                return;
            }
            
            int previousCount = lastStackCount[instanceID];
            
            // 如果数量减少了
            if (currentCount < previousCount)
            {
                // 只有当该物品刚使用过时才恢复（区分使用和拆分）
                if (!recentlyUsedNeedles.Contains(instanceID))
                {
                    lastStackCount[instanceID] = currentCount;
                    return;
                }
                
                // 移除刚使用标记
                recentlyUsedNeedles.Remove(instanceID);
                
                // 立即恢复（无论数量是否为0）
                RestoreStackCountImmediately(item, instanceID, currentCount);
                return;
            }
            
            // 更新记录的数量
            lastStackCount[instanceID] = currentCount;
        }
        
        private void RestoreStackCountImmediately(Item item, int instanceID, int currentCount)
        {
            // 先暂时取消订阅，避免递归触发
            item.onSetStackCount -= OnNeedleStackCountChanged;
            
            // 直接设置数量为 currentCount + 1
            item.SetInt("Count", currentCount + 1, true);
            
            // 手动触发相关事件，确保UI更新
            try
            {
                Type itemType = typeof(Item);
                MethodInfo notifyChildChangedMethod = itemType.GetMethod("NotifyChildChanged", BindingFlags.NonPublic | BindingFlags.Instance);
                notifyChildChangedMethod?.Invoke(item, null);
                
                if (item.InInventory != null)
                {
                    Type inventoryType = item.InInventory.GetType();
                    MethodInfo notifyContentChangedMethod = inventoryType.GetMethod("NotifyContentChanged", BindingFlags.NonPublic | BindingFlags.Instance);
                    notifyContentChangedMethod?.Invoke(item.InInventory, new object[] { item });
                }
            }
            catch
            {
                // 静默处理异常
            }
            
            // 重新订阅
            item.onSetStackCount += OnNeedleStackCountChanged;
            
            // 更新记录的数量
            lastStackCount[instanceID] = item.StackCount;
        }
    }
}
