using UnityEngine;
using System.Collections.Generic;
using Color = UnityEngine.Color;

namespace HKSS.ShowHitbox;

public static class HitboxColors
{
    // Color variation offsets for overlapping same-category hitboxes
    private static readonly Color[] ColorVariations = new Color[]
    {
        new Color(0f, 0f, 0f, 0f),         // No change (first)
        new Color(0.2f, -0.1f, 0.3f, 0f),  // Shift towards purple/magenta
        new Color(-0.2f, 0.2f, -0.1f, 0f), // Shift towards green
        new Color(0.3f, 0.1f, -0.2f, 0f),  // Shift towards orange/yellow
        new Color(-0.1f, -0.2f, 0.3f, 0f), // Shift towards blue
        new Color(0.1f, 0.3f, 0.1f, 0f),   // Shift towards lime
    };

    // Cache colors per instance ID per frame
    private static readonly Dictionary<int, Color> _colorCache = new Dictionary<int, Color>();

    // Track hitboxes for variation calculation
    private static readonly Dictionary<int, List<HitboxInfo>> _hitboxesThisFrame = new Dictionary<int, List<HitboxInfo>>();
    private static int _lastFrameCount = -1;

    private struct HitboxInfo
    {
        public Vector3 WorldPosition;
        public int InstanceId;
    }

    private static void EnsureFrameCleared()
    {
        if (Time.frameCount != _lastFrameCount)
        {
            _hitboxesThisFrame.Clear();
            _colorCache.Clear();
            _lastFrameCount = Time.frameCount;
        }
    }

    public static int GetColorCategory(GameObject go, DebugDrawColliderRuntime.ColorType type)
    {
        // Check for special categories first
        var damageEnemies = go.GetComponent<DamageEnemies>();
        if (damageEnemies != null)
            return 100; // PlayerAttack category

        var healthManager = go.GetComponent<HealthManager>();
        if (healthManager != null)
            return 101; // Enemy/Health category

        var damageHero = go.GetComponent<DamageHero>();
        if (damageHero != null)
            return 102; // EnemyAttack category

        // Use ColorType as category
        return (int)type;
    }

    public static Color GetBaseColor(GameObject go, DebugDrawColliderRuntime.ColorType type)
    {
        // Check for special categories
        var damageEnemies = go.GetComponent<DamageEnemies>();
        if (damageEnemies != null)
            return new Color(0f, 1f, 1f); // Cyan for player attacks

        var healthManager = go.GetComponent<HealthManager>();
        if (healthManager != null)
        {
            // Check if both Danger and Enemy are selected - use red for actual enemies
            if (Configs.FillDanger && Configs.FillEnemy)
                return Color.red;
            return new Color(1f, 0.7f, 0f); // Orange
        }

        var damageHero = go.GetComponent<DamageHero>();
        if (damageHero != null)
            return Color.red; // Enemy attacks

        return type switch
        {
            DebugDrawColliderRuntime.ColorType.Tilemap => new Color(0.0f, 0.44f, 0.0f),
            DebugDrawColliderRuntime.ColorType.TerrainCollider => Color.green,
            DebugDrawColliderRuntime.ColorType.Danger => Color.red,
            DebugDrawColliderRuntime.ColorType.Roof => new Color(0.8f, 1f, 0.0f),
            DebugDrawColliderRuntime.ColorType.Region => new Color(0.4f, 0.75f, 1f),
            DebugDrawColliderRuntime.ColorType.Enemy => new Color(1f, 0.7f, 0.0f),
            DebugDrawColliderRuntime.ColorType.Water => new Color(0.2f, 0.5f, 1f),
            DebugDrawColliderRuntime.ColorType.TransitionPoint => Color.magenta,
            DebugDrawColliderRuntime.ColorType.SandRegion => new Color(1f, 0.7f, 0.7f),
            DebugDrawColliderRuntime.ColorType.ShardRegion => Color.grey,
            DebugDrawColliderRuntime.ColorType.CameraLock => new Color(0.16f, 0.17f, 0.28f),
            _ => Color.white
        };
    }

    public static Color ApplyVariation(Color baseColor, int variationIndex)
    {
        if (variationIndex <= 0 || variationIndex >= ColorVariations.Length)
            return baseColor;

        Color variation = ColorVariations[variationIndex];

        return new Color(
            Mathf.Clamp01(baseColor.r + variation.r),
            Mathf.Clamp01(baseColor.g + variation.g),
            Mathf.Clamp01(baseColor.b + variation.b),
            baseColor.a
        );
    }

    public static Color GetHitboxColor(GameObject go, DebugDrawColliderRuntime.ColorType type, float alpha)
    {
        EnsureFrameCleared();

        int instanceId = go.GetInstanceID();

        // Check cache first - return same color if already calculated this frame
        if (_colorCache.TryGetValue(instanceId, out Color cachedColor))
        {
            cachedColor.a = alpha;
            return cachedColor;
        }

        // Calculate color
        int colorCategory = GetColorCategory(go, type);
        Vector3 worldPos = go.transform.position;

        // Count nearby same-category hitboxes
        int nearbyCount = 0;
        if (_hitboxesThisFrame.TryGetValue(colorCategory, out var categoryList))
        {
            foreach (var info in categoryList)
            {
                if (Vector3.Distance(worldPos, info.WorldPosition) < 3f)
                {
                    nearbyCount++;
                }
            }
        }

        // Register this hitbox
        if (!_hitboxesThisFrame.ContainsKey(colorCategory))
        {
            _hitboxesThisFrame[colorCategory] = new List<HitboxInfo>();
        }
        _hitboxesThisFrame[colorCategory].Add(new HitboxInfo
        {
            WorldPosition = worldPos,
            InstanceId = instanceId
        });

        // Calculate final color
        Color baseColor = GetBaseColor(go, type);
        Color finalColor = ApplyVariation(baseColor, nearbyCount);

        // Cache it (without alpha, alpha is applied per-call)
        _colorCache[instanceId] = finalColor;

        finalColor.a = alpha;
        return finalColor;
    }
}