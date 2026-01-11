using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System;

namespace HKSS.ShowHitbox.Behaviour;

// [ Hitbox Scanner ]--
// Scans for damage objects that might not call AddOrUpdate

public class ColliderScanner : MonoBehaviour
{
    private static ColliderScanner? _instance;

    // timing
    private const float QuickScanInterval = 0.1f;
    private const float FullScanInterval = 3f;
    
    private float _lastQuickScanTime;
    private float _lastFullScanTime;
    private bool _needsRescan = true;

    // caches
    private readonly HashSet<int> _processedObjects = new();
    private readonly HashSet<int> _playerObjects = new();
    
    // cached references
    private HeroController? _cachedHero;
    private bool _heroSearched;
    
    // cached arrays to reduce FindObjectsByType calls
    private HealthManager[]? _cachedHealthManagers;
    private DamageHero[]? _cachedDamageHeroes;
    private float _lastHealthManagerCacheTime;
    private float _lastDamageHeroCacheTime;
    private const float CacheInterval = 0.5f;

    private static readonly string[] ExcludeKeywords =
    {
        "camera", "lock", "region", "trigger", "detector", "respawn",
        "transition", "gate", "bounds", "wall", "enviro",
        "terrain", "tilemap", "ground", "roof", "particle", "clamber",
        "inspect", "npc", "dialogue", "scene", "appearance", "boss scene",
        "cage", "trapbench", "enemy collider"
    };
    
    // environmental objects (excluded unless ShowEnvironmental is enabled)
    private static readonly string[] EnvironmentalKeywords =
    {
        "slashwind", "attack force", "grass", "coral", "wind"
    };
    
    // attack/projectile layers
    private const int AttackLayer1 = 11;  // Enemies
    private const int AttackLayer2 = 12;  // Projectiles
    private const int AttackLayer3 = 17;  // Attack
    private const int AttackLayer4 = 22;  // Enemy Attack

    public static void Initialize()
    {
        if (_instance != null) return;
        var go = new GameObject("HKSS_ColliderScanner");
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<ColliderScanner>();
    }

    private void OnEnable() => SceneManager.sceneLoaded += OnSceneLoaded;
    private void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        _needsRescan = true;
        _processedObjects.Clear();
        _playerObjects.Clear();
        _cachedHero = null;
        _heroSearched = false;
        _cachedHealthManagers = null;
        _cachedDamageHeroes = null;
    }

    private void Update()
    {
        if (!DebugDrawColliderRuntime.IsShowing) return;

        float time = Time.unscaledTime;

        if (_needsRescan || time - _lastFullScanTime >= FullScanInterval)
        {
            _lastFullScanTime = time;
            _lastQuickScanTime = time;
            _needsRescan = false;
            FullScan();
        }
        else if (time - _lastQuickScanTime >= QuickScanInterval)
        {
            _lastQuickScanTime = time;
            QuickScan();
        }
    }
    
    private HealthManager[] GetHealthManagers()
    {
        float time = Time.unscaledTime;
        if (_cachedHealthManagers == null || time - _lastHealthManagerCacheTime >= CacheInterval)
        {
            _cachedHealthManagers = FindObjectsByType<HealthManager>(FindObjectsSortMode.None);
            _lastHealthManagerCacheTime = time;
        }
        return _cachedHealthManagers;
    }
    
    private DamageHero[] GetDamageHeroes()
    {
        float time = Time.unscaledTime;
        if (_cachedDamageHeroes == null || time - _lastDamageHeroCacheTime >= CacheInterval)
        {
            _cachedDamageHeroes = FindObjectsByType<DamageHero>(FindObjectsSortMode.None);
            _lastDamageHeroCacheTime = time;
        }
        return _cachedDamageHeroes;
    }

    private void FullScan()
    {
        CachePlayerHierarchy();
        
        // scan HealthManager children (enemies/bosses)
        var healthManagers = GetHealthManagers();
        for (int i = 0; i < healthManagers.Length; i++)
        {
            var hm = healthManagers[i];
            if (hm != null)
                ScanChildren(hm.transform, DebugDrawColliderRuntime.ColorType.Danger);
        }

        // scan DamageHero objects (things that damage the player)
        var damageHeroes = GetDamageHeroes();
        for (int i = 0; i < damageHeroes.Length; i++)
        {
            var dh = damageHeroes[i];
            if (dh != null)
                TryAddDebugCollider(dh.gameObject, DebugDrawColliderRuntime.ColorType.Danger);
        }

        // scan DamageEnemies objects (player attacks)
        var damageEnemies = FindObjectsByType<DamageEnemies>(FindObjectsSortMode.None);
        for (int i = 0; i < damageEnemies.Length; i++)
        {
            var de = damageEnemies[i];
            if (de != null)
                TryAddDebugCollider(de.gameObject, DebugDrawColliderRuntime.ColorType.Enemy);
        }
        
        // scan attack layer colliders (for objects without DamageHero like Lost Lace cross slash)
        ScanAttackLayerColliders();

        // player highlight
        if (Configs.HighlightPlayer && _cachedHero != null)
            ScanChildren(_cachedHero.transform, DebugDrawColliderRuntime.ColorType.Enemy);
    }
    
    private void ScanAttackLayerColliders()
    {
        var colliders = FindObjectsByType<Collider2D>(FindObjectsSortMode.None);
        for (int i = 0; i < colliders.Length; i++)
        {
            var col = colliders[i];
            if (col == null || !col.enabled || !col.gameObject.activeInHierarchy) continue;
            
            int layer = col.gameObject.layer;
            // layers: 11 (Enemies), 12 (Projectiles), 17 (Attack), 22 (Enemy Attack)
            if (layer != AttackLayer1 && layer != AttackLayer2 && layer != AttackLayer3 && layer != AttackLayer4)
                continue;
            
            TryAddDebugCollider(col.gameObject, DebugDrawColliderRuntime.ColorType.Danger);
        }
    }
    
    private void CachePlayerHierarchy()
    {
        if (_cachedHero == null && !_heroSearched)
        {
            _cachedHero = FindAnyObjectByType<HeroController>();
            _heroSearched = true;
        }
        
        if (_cachedHero != null && _playerObjects.Count == 0)
            CacheChildrenIds(_cachedHero.transform);
    }
    
    private void CacheChildrenIds(Transform parent)
    {
        _playerObjects.Add(parent.gameObject.GetInstanceID());
        foreach (Transform child in parent)
            CacheChildrenIds(child);
    }

    private float _lastAttackLayerScanTime;
    private const float AttackLayerScanInterval = 0.15f;

    private void QuickScan()
    {
        // scan DamageHero objects
        var damageHeroes = GetDamageHeroes();
        for (int i = 0; i < damageHeroes.Length; i++)
        {
            var dh = damageHeroes[i];
            if (dh == null) continue;
            
            int instanceId = dh.gameObject.GetInstanceID();
            if (_processedObjects.Contains(instanceId)) continue;
            if (_playerObjects.Contains(instanceId)) continue;
            
            TryAddDebugCollider(dh.gameObject, DebugDrawColliderRuntime.ColorType.Danger);
        }
        
        // throttled attack layer scan for objects without DamageHero
        float time = Time.unscaledTime;
        if (time - _lastAttackLayerScanTime >= AttackLayerScanInterval)
        {
            _lastAttackLayerScanTime = time;
            ScanAttackLayerColliders();
        }
    }

    private static bool IsExcluded(string name)
    {
        foreach (var exclude in ExcludeKeywords)
        {
            if (name.IndexOf(exclude, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }
        
        // check environmental keywords (excluded unless ShowEnvironmental is enabled)
        if (!Configs.ShowEnvironmental)
        {
            foreach (var env in EnvironmentalKeywords)
            {
                if (name.IndexOf(env, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
        }
        
        return false;
    }
    
    private static bool ContainsAnyIgnoreCase(string text, params string[] keywords)
    {
        foreach (var keyword in keywords)
        {
            if (text.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }
        return false;
    }

    private void ScanChildren(Transform parent, DebugDrawColliderRuntime.ColorType type)
    {
        foreach (Transform child in parent)
        {
            TryAddDebugCollider(child.gameObject, type, skipCache: true);
            if (child.childCount > 0)
                ScanChildren(child, type);
        }
    }

    private void TryAddDebugCollider(GameObject go, DebugDrawColliderRuntime.ColorType type, bool skipCache = false)
    {
        int instanceId = go.GetInstanceID();
        
        if (!skipCache && _processedObjects.Contains(instanceId)) return;
        
        if (type == DebugDrawColliderRuntime.ColorType.Danger && _playerObjects.Contains(instanceId))
        {
            _processedObjects.Add(instanceId);
            return;
        }

        string goName = go.name;
        
        bool isDetectionZone = ContainsAnyIgnoreCase(goName, "range", "alert", "sense", "detect");
        
        if (!isDetectionZone && IsExcluded(goName))
        {
            _processedObjects.Add(instanceId);
            return;
        }

        if (go.TryGetComponent<HealthManager>(out _))
        {
            _processedObjects.Add(instanceId);
            return;
        }

        if (go.TryGetComponent<CameraLockArea>(out _))
        {
            _processedObjects.Add(instanceId);
            return;
        }
        
        if (type == DebugDrawColliderRuntime.ColorType.Danger && go.TryGetComponent<DamageEnemies>(out _))
        {
            _processedObjects.Add(instanceId);
            return;
        }
        
        if (!skipCache && go.TryGetComponent<DebugDrawColliderRuntime>(out _))
        {
            _processedObjects.Add(instanceId);
            return;
        }

        if (!go.TryGetComponent<Collider2D>(out _)) return;

        DebugDrawColliderRuntime.AddOrUpdate(go, type, true);
        
        if (!skipCache)
            _processedObjects.Add(instanceId);
    }

    public static void ClearCache()
    {
        if (_instance != null)
        {
            _instance._processedObjects.Clear();
            _instance._playerObjects.Clear();
            _instance._needsRescan = true;
            _instance._cachedHealthManagers = null;
            _instance._cachedDamageHeroes = null;
        }
    }

    private void OnDestroy() => _instance = null;
}
