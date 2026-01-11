using UnityEngine;
using System.Collections.Generic;
using System;

namespace HKSS.ShowHitbox.Behaviour;

// [ Projectile Scanner ]--
// scans for visual objects that might be projectiles without standard colliders

public class ProjectileScanner : MonoBehaviour
{
    private static ProjectileScanner? _instance;
    
    private bool _scanRequested;
    private float _lastContinuousScanTime;
    private const float ContinuousScanInterval = 0.1f;
    
    // track what we've already logged to avoid spam
    private readonly HashSet<int> _loggedVisualObjects = new();
    private readonly HashSet<int> _loggedNewObjects = new();
    
    // projectile-like keywords
    private static readonly string[] ProjectileKeywords =
    {
        "projectile", "bullet", "shot", "beam", "crescent", "moon", "arc",
        "slash", "wave", "orb", "sphere", "bolt", "missile", "attack",
        "throw", "spit", "fire", "blast", "burst", "needle", "thorn",
        "spike", "shard", "fragment", "slice", "cut", "swing",
        "sickle", "scythe", "claw", "fang", "tornado", "vortex", "wind", "spin"
    };
    
    // boss-related keywords
    private static readonly string[] BossKeywords =
    {
        "boss", "cloverstag", "stag", "effigy", "guardian", "enemy", "trobbio", "torment"
    };
    
    public static void Initialize()
    {
        if (_instance != null) return;
        var go = new GameObject("HKSS_ProjectileScanner");
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<ProjectileScanner>();
    }
    
    public static void TriggerScan()
    {
        if (_instance == null) Initialize();
        if (_instance != null)
        {
            _instance._scanRequested = true;
            _instance._loggedVisualObjects.Clear();
            Utils.Logger.Info("=== PROJECTILE SCAN TRIGGERED ===");
        }
    }
    
    private void Update()
    {
        if (_scanRequested)
        {
            _scanRequested = false;
            FullVisualScan();
        }
        
        // continuous scanning when ProjectileDebug is enabled
        if (Configs.ProjectileDebug && Time.unscaledTime - _lastContinuousScanTime >= ContinuousScanInterval)
        {
            _lastContinuousScanTime = Time.unscaledTime;
            ScanForNewObjects();
        }
    }
    
    private void FullVisualScan()
    {
        Utils.Logger.Info("=== F10 SCAN START ===");
        
        // first, scan ALL attack layer objects (most important for hitboxes)
        Utils.Logger.Info("--- Scanning ALL Attack Layer Objects (11 & 17) ---");
        var allColliders = FindObjectsByType<Collider2D>(FindObjectsSortMode.None);
        int attackLayerCount = 0;
        foreach (var col in allColliders)
        {
            if (col == null || !col.gameObject.activeInHierarchy) continue;
            int layer = col.gameObject.layer;
            if (layer == 11 || layer == 17)
            {
                LogColliderDetails(col);
                attackLayerCount++;
            }
        }
        Utils.Logger.Info($"Found {attackLayerCount} attack layer colliders");
        
        Utils.Logger.Info("--- Scanning SpriteRenderers ---");
        var sprites = FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None);
        int spriteCount = 0;
        foreach (var sr in sprites)
        {
            if (sr == null || !sr.enabled || !sr.gameObject.activeInHierarchy) continue;
            if (IsInteresting(sr.gameObject))
            {
                LogVisualObject(sr.gameObject, "SpriteRenderer", sr.sprite?.name ?? "null");
                spriteCount++;
            }
        }
        Utils.Logger.Info($"Found {spriteCount} interesting sprites");
        
        Utils.Logger.Info("--- Scanning MeshRenderers ---");
        var meshes = FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None);
        int meshCount = 0;
        foreach (var mr in meshes)
        {
            if (mr == null || !mr.enabled || !mr.gameObject.activeInHierarchy) continue;
            if (IsInteresting(mr.gameObject))
            {
                var mf = mr.GetComponent<MeshFilter>();
                LogVisualObject(mr.gameObject, "MeshRenderer", mf?.sharedMesh?.name ?? "null");
                meshCount++;
            }
        }
        Utils.Logger.Info($"Found {meshCount} interesting meshes");
        
        Utils.Logger.Info("--- Scanning LineRenderers ---");
        var lines = FindObjectsByType<LineRenderer>(FindObjectsSortMode.None);
        foreach (var lr in lines)
        {
            if (lr == null || !lr.enabled || !lr.gameObject.activeInHierarchy) continue;
            LogVisualObject(lr.gameObject, "LineRenderer", $"positions={lr.positionCount}");
        }
        Utils.Logger.Info($"Found {lines.Length} line renderers");
        
        Utils.Logger.Info("--- Scanning TrailRenderers ---");
        var trails = FindObjectsByType<TrailRenderer>(FindObjectsSortMode.None);
        foreach (var tr in trails)
        {
            if (tr == null || !tr.enabled || !tr.gameObject.activeInHierarchy) continue;
            LogVisualObject(tr.gameObject, "TrailRenderer", $"time={tr.time}");
        }
        Utils.Logger.Info($"Found {trails.Length} trail renderers");
        
        Utils.Logger.Info("--- Scanning OTHER Collider2D (triggers, interesting names) ---");
        var colliders = FindObjectsByType<Collider2D>(FindObjectsSortMode.None);
        int colliderCount = 0;
        foreach (var col in colliders)
        {
            if (col == null || !col.gameObject.activeInHierarchy) continue;
            int layer = col.gameObject.layer;
            if (layer == 11 || layer == 17) continue; // already logged above
            if (IsInteresting(col.gameObject) || col.isTrigger)
            {
                LogColliderDetails(col);
                colliderCount++;
            }
        }
        Utils.Logger.Info($"Found {colliderCount} other interesting colliders");
        
        Utils.Logger.Info("=== SCAN COMPLETE ===");
    }
    
    private void ScanForNewObjects()
    {
        // look for newly spawned objects that might be projectiles
        var sprites = FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None);
        foreach (var sr in sprites)
        {
            if (sr == null || !sr.enabled || !sr.gameObject.activeInHierarchy) continue;
            
            int id = sr.gameObject.GetInstanceID();
            if (_loggedNewObjects.Contains(id)) continue;
            _loggedNewObjects.Add(id);
            
            if (IsLikelyProjectile(sr.gameObject))
            {
                Utils.Logger.Info($"[NEW] Possible projectile: {GetFullPath(sr.gameObject)}");
                LogVisualObject(sr.gameObject, "SpriteRenderer", sr.sprite?.name ?? "null");
            }
        }
    }
    
    private bool IsInteresting(GameObject go)
    {
        string nameLower = go.name.ToLowerInvariant();
        string? parentName = go.transform.parent?.name.ToLowerInvariant();
        
        // check projectile keywords
        foreach (var kw in ProjectileKeywords)
        {
            if (nameLower.Contains(kw)) return true;
            if (parentName != null && parentName.Contains(kw)) return true;
        }
        
        // check boss keywords
        foreach (var kw in BossKeywords)
        {
            if (nameLower.Contains(kw)) return true;
            if (parentName != null && parentName.Contains(kw)) return true;
        }
        
        return false;
    }
    
    private bool IsLikelyProjectile(GameObject go)
    {
        string nameLower = go.name.ToLowerInvariant();
        
        // more aggressive check for projectiles
        foreach (var kw in ProjectileKeywords)
        {
            if (nameLower.Contains(kw)) return true;
        }
        
        // check if parent is boss-related
        Transform? parent = go.transform.parent;
        while (parent != null)
        {
            string parentLower = parent.name.ToLowerInvariant();
            foreach (var kw in BossKeywords)
            {
                if (parentLower.Contains(kw))
                {
                    // this object is under a boss, check if it could be an attack
                    if (go.TryGetComponent<Collider2D>(out _)) return true;
                    if (go.layer == 11 || go.layer == 17) return true; // attack layers
                }
            }
            parent = parent.parent;
        }
        
        return false;
    }
    
    private void LogVisualObject(GameObject go, string visualType, string extra)
    {
        int id = go.GetInstanceID();
        if (_loggedVisualObjects.Contains(id)) return;
        _loggedVisualObjects.Add(id);
        
        string path = GetFullPath(go);
        string layer = LayerMask.LayerToName(go.layer);
        
        // get all components
        var components = go.GetComponents<Component>();
        var compNames = new List<string>();
        bool hasCollider = false;
        bool hasDamageHero = false;
        bool hasDamageEnemies = false;
        
        foreach (var c in components)
        {
            if (c == null) continue;
            string typeName = c.GetType().Name;
            compNames.Add(typeName);
            
            if (c is Collider2D) hasCollider = true;
            if (typeName == "DamageHero") hasDamageHero = true;
            if (typeName == "DamageEnemies") hasDamageEnemies = true;
        }
        
        string flags = "";
        if (hasCollider) flags += " [HAS COLLIDER]";
        if (hasDamageHero) flags += " [DAMAGES HERO]";
        if (hasDamageEnemies) flags += " [DAMAGES ENEMIES]";
        
        Utils.Logger.Info($"[{visualType}] {path} | Layer: {layer} ({go.layer}) | {extra}{flags}");
        Utils.Logger.Info($"  Components: {string.Join(", ", compNames)}");
    }
    
    private void LogColliderDetails(Collider2D col)
    {
        var go = col.gameObject;
        string path = GetFullPath(go);
        string layer = LayerMask.LayerToName(go.layer);
        
        string colliderType = col.GetType().Name;
        string triggerStr = col.isTrigger ? "TRIGGER" : "SOLID";
        string enabledStr = col.enabled ? "enabled" : "DISABLED";
        
        // get bounds info
        var bounds = col.bounds;
        string boundsStr = $"size=({bounds.size.x:F2}, {bounds.size.y:F2})";
        
        // check for damage components
        bool hasDamageHero = go.TryGetComponent<DamageHero>(out _);
        bool hasDamageEnemies = go.TryGetComponent<DamageEnemies>(out _);
        bool hasDebugDraw = go.TryGetComponent<DebugDrawColliderRuntime>(out _);
        
        string flags = "";
        if (hasDamageHero) flags += " [DAMAGES HERO]";
        if (hasDamageEnemies) flags += " [DAMAGES ENEMIES]";
        if (hasDebugDraw) flags += " [HAS DEBUGDRAW]";
        else flags += " [NO DEBUGDRAW]";
        
        Utils.Logger.Info($"[Collider] {path} | {colliderType} | {triggerStr} | {enabledStr} | Layer: {layer} ({go.layer}) | {boundsStr}{flags}");
    }
    
    private static string GetFullPath(GameObject go)
    {
        string path = go.name;
        Transform? parent = go.transform.parent;
        int depth = 0;
        while (parent != null && depth < 5)
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
            _instance._loggedVisualObjects.Clear();
            _instance._loggedNewObjects.Clear();
        }
    }
    
    private void OnDestroy() => _instance = null;
}
