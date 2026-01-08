using UnityEngine;
using System.Collections.Generic;
using Color = UnityEngine.Color;

namespace HKSS.ShowHitbox;

public static class HitboxColors
{
    // Color variation offsets for overlapping same-category hitboxes
    private static readonly Color[] ColorVariations = new Color[]
    {
        new Color(0f, 0f, 0f, 0f),
        new Color(0.35f, -0.15f, 0.4f, 0f),
        new Color(-0.3f, 0.35f, -0.15f, 0f),
        new Color(0.4f, 0.2f, -0.35f, 0f),
        new Color(-0.2f, -0.3f, 0.4f, 0f),
        new Color(0.2f, 0.4f, 0.2f, 0f),
    };

    // detection zones (range, alert, sense) - purple/violet color
    public static readonly Color DetectionZoneColor = new Color(0.6f, 0.3f, 0.9f, 1f);

    // Cache colors per instance ID per frame
    private static readonly Dictionary<int, Color> _colorCache = new Dictionary<int, Color>();

    // Track unique hitbox names per category for variation
    private static readonly Dictionary<int, Dictionary<string, int>> _nameVariationIndex = new Dictionary<int, Dictionary<string, int>>();

    private static int _lastFrameCount = -1;

    private static void EnsureFrameCleared()
    {
        if (Time.frameCount != _lastFrameCount)
        {
            _colorCache.Clear();
            _nameVariationIndex.Clear();
            _lastFrameCount = Time.frameCount;
        }
    }

    public static bool IsPlayer(GameObject go)
    {
        // Check if this is the player by looking for HeroController or name
        var heroController = go.GetComponentInParent<HeroController>();
        if (heroController != null) return true;

        // Fallback: check name
        string fullName = go.name;
        Transform parent = go.transform.parent;
        while (parent != null)
        {
            fullName = parent.name + "/" + fullName;
            parent = parent.parent;
        }
        return fullName.Contains("Hero_Hornet");
    }

    public static int GetColorCategory(GameObject go, DebugDrawColliderRuntime.ColorType type)
    {
        var damageEnemies = go.GetComponent<DamageEnemies>();
        if (damageEnemies != null)
            return 100; // PlayerAttack category

        var healthManager = go.GetComponent<HealthManager>();
        if (healthManager != null)
            return 101; // Enemy/Health category

        var damageHero = go.GetComponent<DamageHero>();
        if (damageHero != null)
            return 102; // EnemyAttack category

        return (int)type;
    }

    public static Color GetBaseColor(GameObject go, DebugDrawColliderRuntime.ColorType type)
    {
        // Check if player should be highlighted
        if (Configs.HighlightPlayer && IsPlayer(go))
            return Color.red;

        // Check for special categories
        var damageEnemies = go.GetComponent<DamageEnemies>();
        if (damageEnemies != null)
            return new Color(0f, 1f, 1f); // Cyan for player attacks

        var healthManager = go.GetComponent<HealthManager>();
        if (healthManager != null)
        {
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

    private static string GetHitboxIdentifier(GameObject go)
    {
        string fullName = go.name;

        if (go.transform.parent != null)
        {
            fullName = go.transform.parent.name + "/" + go.name;
        }

        return fullName;
    }

    public static Color GetHitboxColor(GameObject go, DebugDrawColliderRuntime.ColorType type, float alpha)
    {
        EnsureFrameCleared();

        int instanceId = go.GetInstanceID();

        if (_colorCache.TryGetValue(instanceId, out Color cachedColor))
        {
            cachedColor.a = alpha;
            return cachedColor;
        }

        int colorCategory = GetColorCategory(go, type);
        string hitboxId = GetHitboxIdentifier(go);

        if (!_nameVariationIndex.ContainsKey(colorCategory))
        {
            _nameVariationIndex[colorCategory] = new Dictionary<string, int>();
        }

        var categoryNames = _nameVariationIndex[colorCategory];

        int variationIndex;
        if (categoryNames.TryGetValue(hitboxId, out int existingIndex))
        {
            variationIndex = existingIndex;
        }
        else
        {
            variationIndex = categoryNames.Count;
            categoryNames[hitboxId] = variationIndex;
        }

        Color baseColor = GetBaseColor(go, type);
        Color finalColor = ApplyVariation(baseColor, variationIndex);

        _colorCache[instanceId] = finalColor;

        finalColor.a = alpha;
        return finalColor;
    }
}