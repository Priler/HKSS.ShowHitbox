using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using System;

namespace HKSS.ShowHitbox.Behaviour;

// [ Hitbox Scanner ]--
// Scans for damage objects that might not call AddOrUpdate

public class ColliderScanner : MonoBehaviour
{
    private static ColliderScanner? _instance;

    // timing
    private const float QuickScanInterval = 0.05f;
    private const float FullScanInterval = 3f;
    private const float KeywordScanInterval = 0.5f;
    
    private float _lastQuickScanTime;
    private float _lastFullScanTime;
    private float _lastKeywordScanTime;
    private bool _needsRescan = true;
    private bool _didInitialColliderScan;

    // caches
    private readonly HashSet<int> _processedObjects = new();
    private readonly HashSet<int> _loggedObjects = new();
    private readonly HashSet<int> _playerObjects = new();
    
    // cached references
    private HeroController? _cachedHero;
    private bool _heroSearched;
    
    // cached arrays to reduce FindObjectsByType calls
    private HealthManager[]? _cachedHealthManagers;
    private DamageHero[]? _cachedDamageHeroes;
    private Transform[]? _cachedBossLikeObjects;
    private float _lastHealthManagerCacheTime;
    private float _lastDamageHeroCacheTime;
    private float _lastBossLikeCacheTime;
    private const float CacheInterval = 0.5f;

    // FSM names (case-insensitive HashSet)
    private static readonly HashSet<string> DamageFsmNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "damages_hero", "damage hero", "damagehero", "damages hero"
    };
    
    // boss name patterns (for objects without HealthManager)
    private static readonly string[] BossNamePatterns =
    {
        "boss", "effigy", "guardian", "knight", "enemy"
    };

    // keywords
    private static readonly string[] AttackKeywords =
    {
        "hit", "slash", "attack", "damage", "hurt", "stab", "swing",
        "needle", "sword", "weapon", "blade", "strike", "combo",
        "projectile", "bullet", "shot", "beam", "thwip", "lunge",
        "dash", "charge", "thrust", "poke", "jab", "catcher"
    };

    private static readonly string[] ExcludeKeywords =
    {
        "camera", "lock", "region", "trigger", "detector", "respawn",
        "transition", "gate", "bounds", "wall", "enviro",
        "terrain", "tilemap", "ground", "roof", "particle", "clamber",
        "inspect", "npc", "dialogue", "scene", "appearance", "boss scene"
    };
    
    // environmental objects (excluded unless ShowEnvironmental is enabled)
    private static readonly string[] EnvironmentalKeywords =
    {
        "slashwind", "attack force", "grass", "coral", "wind"
    };
    
    // attack layers
    private const int AttackLayer1 = 11;
    private const int AttackLayer2 = 17;

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
        _didInitialColliderScan = false;
        _processedObjects.Clear();
        _loggedObjects.Clear();
        _playerObjects.Clear();
        _cachedHero = null;
        _heroSearched = false;
        _cachedHealthManagers = null;
        _cachedDamageHeroes = null;
        _cachedBossLikeObjects = null;
        _lastKeywordScanTime = 0;
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
        
        // scan HealthManager children
        var healthManagers = GetHealthManagers();
        for (int i = 0; i < healthManagers.Length; i++)
        {
            var hm = healthManagers[i];
            if (hm != null)
                ScanChildren(hm.transform, DebugDrawColliderRuntime.ColorType.Danger, !_didInitialColliderScan);
        }
        
        // scan boss-like objects by name pattern (for those without HealthManager)
        ScanBossLikeObjects();

        var damageHeroes = GetDamageHeroes();
        for (int i = 0; i < damageHeroes.Length; i++)
        {
            var dh = damageHeroes[i];
            if (dh != null)
                TryAddDebugCollider(dh.gameObject, DebugDrawColliderRuntime.ColorType.Danger);
        }

        var damageEnemies = FindObjectsByType<DamageEnemies>(FindObjectsSortMode.None);
        for (int i = 0; i < damageEnemies.Length; i++)
        {
            var de = damageEnemies[i];
            if (de != null)
                TryAddDebugCollider(de.gameObject, DebugDrawColliderRuntime.ColorType.Enemy);
        }
        
        // scan ReceivedDamageProxy objects (boss weak points/hurtboxes)
        var damageProxies = FindObjectsByType<ReceivedDamageProxy>(FindObjectsSortMode.None);
        for (int i = 0; i < damageProxies.Length; i++)
        {
            var dp = damageProxies[i];
            if (dp != null)
                ScanChildren(dp.transform, DebugDrawColliderRuntime.ColorType.Danger);
        }

        ScanFsmDamageObjects();

        if (!_didInitialColliderScan)
        {
            _didInitialColliderScan = true;
            ScanAllColliders();
        }

        if (Configs.HighlightPlayer && _cachedHero != null)
            ScanChildren(_cachedHero.transform, DebugDrawColliderRuntime.ColorType.Enemy);
    }
    
    private void ScanBossLikeObjects()
    {
        var bossLike = GetBossLikeObjects();
        for (int i = 0; i < bossLike.Length; i++)
        {
            var tr = bossLike[i];
            if (tr != null)
                ScanChildren(tr, DebugDrawColliderRuntime.ColorType.Danger);
        }
    }
    
    private Transform[] GetBossLikeObjects()
    {
        float time = Time.unscaledTime;
        if (_cachedBossLikeObjects == null || time - _lastBossLikeCacheTime >= CacheInterval)
        {
            _lastBossLikeCacheTime = time;
            
            var allTransforms = FindObjectsByType<Transform>(FindObjectsSortMode.None);
            var result = new List<Transform>();
            
            for (int i = 0; i < allTransforms.Length; i++)
            {
                var tr = allTransforms[i];
                if (tr == null) continue;
                if (tr.TryGetComponent<HealthManager>(out _)) continue;
                
                string name = tr.name;
                foreach (var pattern in BossNamePatterns)
                {
                    if (name.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        result.Add(tr);
                        break;
                    }
                }
            }
            
            _cachedBossLikeObjects = result.ToArray();
        }
        return _cachedBossLikeObjects;
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

    private void QuickScan()
    {
        var damageHeroes = GetDamageHeroes();
        for (int i = 0; i < damageHeroes.Length; i++)
        {
            var dh = damageHeroes[i];
            if (dh != null)
                TryAddDebugCollider(dh.gameObject, DebugDrawColliderRuntime.ColorType.Danger);
        }
        
        var healthManagers = GetHealthManagers();
        for (int i = 0; i < healthManagers.Length; i++)
        {
            var hm = healthManagers[i];
            if (hm != null)
                ScanChildren(hm.transform, DebugDrawColliderRuntime.ColorType.Danger);
        }
        
        // scan boss-like objects (without HealthManager)
        var bossLike = GetBossLikeObjects();
        for (int i = 0; i < bossLike.Length; i++)
        {
            var tr = bossLike[i];
            if (tr != null)
                ScanChildren(tr, DebugDrawColliderRuntime.ColorType.Danger);
        }
        
        // scan ReceivedDamageProxy objects (boss weak points)
        var damageProxies = FindObjectsByType<ReceivedDamageProxy>(FindObjectsSortMode.None);
        for (int i = 0; i < damageProxies.Length; i++)
        {
            var dp = damageProxies[i];
            if (dp != null)
                ScanChildren(dp.transform, DebugDrawColliderRuntime.ColorType.Danger);
        }
        
        // scan for new colliders with attack keywords (throttled)
        float time = Time.unscaledTime;
        if (time - _lastKeywordScanTime >= KeywordScanInterval)
        {
            _lastKeywordScanTime = time;
            ScanNewAttackColliders();
        }
    }
    
    private void ScanNewAttackColliders()
    {
        var allColliders = FindObjectsByType<Collider2D>(FindObjectsSortMode.None);
        for (int i = 0; i < allColliders.Length; i++)
        {
            var col = allColliders[i];
            if (col == null) continue;
            
            var go = col.gameObject;
            int instanceId = go.GetInstanceID();
            
            if (_processedObjects.Contains(instanceId)) continue;
            if (_playerObjects.Contains(instanceId)) continue;
            
            string goName = go.name;
            bool isAttack = false;
            
            foreach (var keyword in AttackKeywords)
            {
                if (goName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    isAttack = true;
                    break;
                }
            }
            
            if (isAttack)
                TryAddDebugCollider(go, DebugDrawColliderRuntime.ColorType.Danger);
        }
    }

    private void ScanFsmDamageObjects()
    {
        var allFsms = FindObjectsByType<PlayMakerFSM>(FindObjectsSortMode.None);
        for (int i = 0; i < allFsms.Length; i++)
        {
            var fsm = allFsms[i];
            if (fsm == null) continue;

            int instanceId = fsm.gameObject.GetInstanceID();
            if (_processedObjects.Contains(instanceId)) continue;

            string? fsmName = fsm.FsmName;
            if (string.IsNullOrEmpty(fsmName) || !DamageFsmNames.Contains(fsmName)) continue;

            if (IsExcluded(fsm.gameObject.name)) continue;

            TryAddDebugCollider(fsm.gameObject, DebugDrawColliderRuntime.ColorType.Danger);
            ScanChildren(fsm.transform, DebugDrawColliderRuntime.ColorType.Danger);
        }
    }

    private void ScanAllColliders()
    {
        var allColliders = FindObjectsByType<Collider2D>(FindObjectsSortMode.None);
        for (int i = 0; i < allColliders.Length; i++)
        {
            var col = allColliders[i];
            if (col == null) continue;

            var go = col.gameObject;
            int instanceId = go.GetInstanceID();

            // cheapest checks first
            if (_processedObjects.Contains(instanceId)) continue;
            if (_playerObjects.Contains(instanceId)) continue;
            
            // layer check before string ops
            int layer = go.layer;
            bool isAttackLayer = layer == AttackLayer1 || layer == AttackLayer2;

            string goName = go.name;

            // detection zones
            if (ContainsAnyIgnoreCase(goName, "range", "alert", "sense", "detect"))
            {
                TryAddDebugCollider(go, DebugDrawColliderRuntime.ColorType.Danger);
                continue;
            }

            if (IsExcluded(goName)) continue;
            
            string? parentName = go.transform.parent?.name;
            if (parentName != null && IsExcluded(parentName)) continue;
            
            if (go.TryGetComponent<CameraLockArea>(out _)) continue;
            if (go.TryGetComponent<DamageEnemies>(out _)) continue;

            bool isLikelyAttack = isAttackLayer;

            if (!isLikelyAttack)
            {
                foreach (var keyword in AttackKeywords)
                {
                    if (goName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        (parentName != null && parentName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        isLikelyAttack = true;
                        break;
                    }
                }
            }

            if (isLikelyAttack)
                TryAddDebugCollider(go, DebugDrawColliderRuntime.ColorType.Danger);

            if (Configs.DebugLogging && !_loggedObjects.Contains(instanceId))
            {
                _loggedObjects.Add(instanceId);
                LogColliderInfo(go, col);
            }
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

    private void ScanChildren(Transform parent, DebugDrawColliderRuntime.ColorType type, bool logBossChildren = false)
    {
        foreach (Transform child in parent)
        {
            if (logBossChildren && Configs.DebugLogging && child.TryGetComponent<Collider2D>(out var col))
                Utils.Logger.Info($"[Scanner] Boss child: {parent.name}/{child.name} | Has collider: {col.GetType().Name} | Enabled: {col.enabled}");
            
            TryAddDebugCollider(child.gameObject, type, skipCache: true);
            if (child.childCount > 0)
                ScanChildren(child, type, logBossChildren);
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

    private void LogColliderInfo(GameObject go, Collider2D col)
    {
        string fullPath = GetFullPath(go);

        if (fullPath.IndexOf("Terrain", StringComparison.OrdinalIgnoreCase) >= 0 || 
            fullPath.IndexOf("Tilemap", StringComparison.OrdinalIgnoreCase) >= 0 ||
            fullPath.IndexOf("Ground", StringComparison.OrdinalIgnoreCase) >= 0 || 
            fullPath.IndexOf("Chunk", StringComparison.OrdinalIgnoreCase) >= 0)
            return;

        var components = go.GetComponents<Component>();
        var names = new List<string>(components.Length);
        foreach (var c in components)
        {
            if (c != null)
                names.Add(c.GetType().Name);
        }

        Utils.Logger.Info($"[Scanner] Collider: {fullPath} | Layer: {LayerMask.LayerToName(go.layer)} ({go.layer}) | " +
                         $"Type: {col.GetType().Name} | Components: {string.Join(", ", names)}");
    }

    private static string GetFullPath(GameObject go)
    {
        string path = go.name;
        Transform parent = go.transform.parent;
        int depth = 0;
        while (parent != null && depth < 4)
        {
            path = parent.name + "/" + path;
            parent = parent.parent;
            depth++;
        }
        return path;
    }

    public static void ClearCache()
    {
        if (_instance != null)
        {
            _instance._processedObjects.Clear();
            _instance._playerObjects.Clear();
            _instance._needsRescan = true;
            _instance._didInitialColliderScan = false;
            _instance._cachedHealthManagers = null;
            _instance._cachedDamageHeroes = null;
            _instance._cachedBossLikeObjects = null;
        }
    }

    private void OnDestroy() => _instance = null;
}
