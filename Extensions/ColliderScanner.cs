using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using HutongGames.PlayMaker;

namespace HKSS.ShowHitbox.Behaviour;

// [ Hitbox Scanner ]--
// Scans for damage objects that might not call AddOrUpdate

public class ColliderScanner : MonoBehaviour
{
    private static ColliderScanner? _instance;

    // timing - reduced frequency for better performance
    private const float QuickScanInterval = 0.15f;
    private const float FullScanInterval = 3f;
    
    private float _lastQuickScanTime;
    private float _lastFullScanTime;
    private bool _needsRescan = true;
    private bool _didInitialColliderScan;

    // caches
    private readonly HashSet<int> _processedObjects = new();
    private readonly HashSet<int> _loggedObjects = new();
    
    // cached references (cleared on scene load)
    private HeroController? _cachedHero;
    private bool _heroSearched;

    // FSM names that indicate damage behavior (pre-lowercased)
    private static readonly HashSet<string> DamageFsmNames = new()
    {
        "damages_hero",
        "damage hero",
        "damagehero",
        "damages hero"
    };

    // keywords suggesting attack colliders
    private static readonly string[] AttackKeywords =
    {
        "hit", "slash", "attack", "damage", "hurt", "stab", "swing",
        "needle", "sword", "weapon", "blade", "strike", "combo",
        "projectile", "bullet", "shot", "beam"
    };

    // keywords for non-attack objects
    private static readonly string[] ExcludeKeywords =
    {
        "camera", "lock", "region", "trigger", "detector", "respawn",
        "transition", "gate", "bounds", "wall", "enviro",
        "terrain", "tilemap", "ground", "roof", "particle", "clamber",
        "inspect", "npc", "dialogue", "scene", "appearance", "boss scene"
    };

    public static void Initialize()
    {
        if (_instance != null) return;

        var go = new GameObject("HKSS_ColliderScanner");
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<ColliderScanner>();
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        _needsRescan = true;
        _didInitialColliderScan = false;
        _processedObjects.Clear();
        _loggedObjects.Clear();
        _cachedHero = null;
        _heroSearched = false;
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

    private void FullScan()
    {
        // scan HealthManager objects - only their children for attack hitboxes
        // the boss body itself is handled by the game's DebugDrawColliderRuntime
        var healthManagers = FindObjectsByType<HealthManager>(FindObjectsSortMode.None);
        foreach (var hm in healthManagers)
        {
            if (hm == null) continue;
            // only scan children for attack colliders, don't add the boss body itself
            ScanChildren(hm.transform, DebugDrawColliderRuntime.ColorType.Danger);
        }

        // scan DamageHero objects
        var damageHeroes = FindObjectsByType<DamageHero>(FindObjectsSortMode.None);
        foreach (var dh in damageHeroes)
        {
            if (dh != null)
                TryAddDebugCollider(dh.gameObject, DebugDrawColliderRuntime.ColorType.Danger);
        }

        // scan DamageEnemies objects
        var damageEnemies = FindObjectsByType<DamageEnemies>(FindObjectsSortMode.None);
        foreach (var de in damageEnemies)
        {
            if (de != null)
                TryAddDebugCollider(de.gameObject, DebugDrawColliderRuntime.ColorType.Enemy);
        }

        // scan FSM-based damage objects
        ScanFsmDamageObjects();

        // full collider scan only once per scene (expensive)
        if (!_didInitialColliderScan)
        {
            _didInitialColliderScan = true;
            ScanAllColliders();
        }

        // scan player if highlight enabled
        if (Configs.HighlightPlayer)
        {
            if (!_heroSearched)
            {
                _cachedHero = FindAnyObjectByType<HeroController>();
                _heroSearched = true;
            }
            if (_cachedHero != null)
                ScanChildren(_cachedHero.transform, DebugDrawColliderRuntime.ColorType.Enemy);
        }
    }

    private void QuickScan()
    {
        // only scan for new DamageHero components (most common for attacks)
        var damageHeroes = FindObjectsByType<DamageHero>(FindObjectsSortMode.None);
        foreach (var dh in damageHeroes)
        {
            if (dh != null)
                TryAddDebugCollider(dh.gameObject, DebugDrawColliderRuntime.ColorType.Danger);
        }
    }

    private void ScanFsmDamageObjects()
    {
        var allFsms = FindObjectsByType<PlayMakerFSM>(FindObjectsSortMode.None);
        foreach (var fsm in allFsms)
        {
            if (fsm == null) continue;

            int instanceId = fsm.gameObject.GetInstanceID();
            if (_processedObjects.Contains(instanceId)) continue;

            string fsmName = fsm.FsmName?.ToLowerInvariant() ?? "";
            if (!DamageFsmNames.Contains(fsmName)) continue;

            string goName = fsm.gameObject.name.ToLowerInvariant();
            if (IsExcluded(goName)) continue;

            TryAddDebugCollider(fsm.gameObject, DebugDrawColliderRuntime.ColorType.Danger);
            ScanChildren(fsm.transform, DebugDrawColliderRuntime.ColorType.Danger);
        }
    }

    private void ScanAllColliders()
    {
        var allColliders = FindObjectsByType<Collider2D>(FindObjectsSortMode.None);
        foreach (var col in allColliders)
        {
            if (col == null) continue;

            var go = col.gameObject;
            int instanceId = go.GetInstanceID();

            if (_processedObjects.Contains(instanceId)) continue;

            string nameLower = go.name.ToLowerInvariant();
            string parentName = go.transform.parent?.name?.ToLowerInvariant() ?? "";

            // check if it's a detection zone (bypass other filters)
            bool isDetectionZone = nameLower.Contains("range") || 
                                   nameLower.Contains("alert") || 
                                   nameLower.Contains("sense") ||
                                   nameLower.Contains("detect");
            
            if (isDetectionZone)
            {
                TryAddDebugCollider(go, DebugDrawColliderRuntime.ColorType.Danger);
                continue;
            }

            // check exclusions
            if (IsExcluded(nameLower) || IsExcluded(parentName)) continue;
            if (go.GetComponent<CameraLockArea>() != null) continue;

            bool isLikelyAttack = false;

            // check attack keywords
            foreach (var keyword in AttackKeywords)
            {
                if (nameLower.Contains(keyword) || parentName.Contains(keyword))
                {
                    isLikelyAttack = true;
                    break;
                }
            }

            // check attack layers (11, 17)
            int layer = go.layer;
            if (layer == 11 || layer == 17)
                isLikelyAttack = true;

            if (isLikelyAttack)
                TryAddDebugCollider(go, DebugDrawColliderRuntime.ColorType.Danger);

            // debug logging
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
            if (name.Contains(exclude))
                return true;
        }
        return false;
    }

    private void ScanChildren(Transform parent, DebugDrawColliderRuntime.ColorType type)
    {
        foreach (Transform child in parent)
        {
            TryAddDebugCollider(child.gameObject, type);
            if (child.childCount > 0)
                ScanChildren(child, type);
        }
    }

    private void TryAddDebugCollider(GameObject go, DebugDrawColliderRuntime.ColorType type)
    {
        int instanceId = go.GetInstanceID();
        if (_processedObjects.Contains(instanceId)) return;

        string nameLower = go.name.ToLowerInvariant();
        
        // detection zones bypass normal exclusion logic
        bool isDetectionZone = nameLower.Contains("range") || 
                               nameLower.Contains("alert") || 
                               nameLower.Contains("sense") ||
                               nameLower.Contains("detect");
        
        if (!isDetectionZone && IsExcluded(nameLower))
        {
            _processedObjects.Add(instanceId);
            return;
        }

        // skip enemy bodies (they have their own rendering via game's system)
        if (go.GetComponent<HealthManager>() != null)
        {
            _processedObjects.Add(instanceId);
            return;
        }

        if (go.GetComponent<CameraLockArea>() != null)
        {
            _processedObjects.Add(instanceId);
            return;
        }
        
        // skip if already has the component (game already registered it)
        if (go.GetComponent<DebugDrawColliderRuntime>() != null)
        {
            _processedObjects.Add(instanceId);
            return;
        }

        if (go.GetComponent<Collider2D>() == null) return;

        DebugDrawColliderRuntime.AddOrUpdate(go, type, true);
        _processedObjects.Add(instanceId);
    }

    private void LogColliderInfo(GameObject go, Collider2D col)
    {
        string fullPath = GetFullPath(go);

        if (fullPath.Contains("Terrain") || fullPath.Contains("Tilemap") ||
            fullPath.Contains("Ground") || fullPath.Contains("Chunk"))
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
            _instance._needsRescan = true;
            _instance._didInitialColliderScan = false;
        }
    }

    private void OnDestroy()
    {
        _instance = null;
    }
}
