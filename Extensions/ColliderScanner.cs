using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using HutongGames.PlayMaker;

namespace HKSS.ShowHitbox.Behaviour;

// [ Hitbox Scanner ]--
// Scans for damage objects that might not call AddOrUpdate:
// 1. DamageHero components (normal enemy attacks)
// 2. PlayMaker FSM "damages_hero" (some boss attacks use this instead)

public class ColliderScanner : MonoBehaviour
{
    private static ColliderScanner? _instance;
    
    // scan frequently to catch short-lived attacks
    private float _scanInterval = 0.1f;
    private float _lastScanTime = 0f;
    
    // full rescan less often
    private float _fullScanInterval = 2f;
    private float _lastFullScanTime = 0f;

    private HashSet<int> _processedObjects = new HashSet<int>();
    private bool _needsRescan = true;

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
        _processedObjects.Clear();
    }

    private void Update()
    {
        if (!DebugDrawColliderRuntime.IsShowing) return;

        float time = Time.unscaledTime;

        // full scan periodically or when requested
        if (_needsRescan || time - _lastFullScanTime >= _fullScanInterval)
        {
            _lastFullScanTime = time;
            _lastScanTime = time;
            _needsRescan = false;
            FullScan();
        }
        // quick scan for FSM-based attacks more frequently
        else if (time - _lastScanTime >= _scanInterval)
        {
            _lastScanTime = time;
            ScanForFsmDamageObjects();
        }
    }

    private void FullScan()
    {
        // scan for HealthManager objects
        var healthManagers = FindObjectsOfType<HealthManager>();
        foreach (var hm in healthManagers)
        {
            if (hm != null)
                TryAddDebugCollider(hm.gameObject, DebugDrawColliderRuntime.ColorType.Enemy);
        }

        // scan for DamageHero objects
        var damageHeroes = FindObjectsOfType<DamageHero>();
        foreach (var dh in damageHeroes)
        {
            if (dh != null && dh.damageDealt > 0)
                TryAddDebugCollider(dh.gameObject, DebugDrawColliderRuntime.ColorType.Danger);
        }

        // scan for DamageEnemies objects
        var damageEnemies = FindObjectsOfType<DamageEnemies>();
        foreach (var de in damageEnemies)
        {
            if (de != null)
                TryAddDebugCollider(de.gameObject, DebugDrawColliderRuntime.ColorType.Enemy);
        }

        // scan for FSM-based damage objects
        ScanForFsmDamageObjects();

        // scan player if highlight enabled
        if (Configs.HighlightPlayer)
        {
            var heroController = FindObjectOfType<HeroController>();
            if (heroController != null)
                ScanGameObjectAndChildren(heroController.gameObject, DebugDrawColliderRuntime.ColorType.Enemy);
        }
    }

    private void ScanForFsmDamageObjects()
    {
        // find all PlayMakerFSM components and check for "damages_hero" FSM
        var allFsms = FindObjectsOfType<PlayMakerFSM>();
        foreach (var fsm in allFsms)
        {
            if (fsm == null || !fsm.isActiveAndEnabled) continue;
            
            // check if this FSM is named "damages_hero"
            if (fsm.FsmName == "damages_hero")
            {
                TryAddDebugCollider(fsm.gameObject, DebugDrawColliderRuntime.ColorType.Danger);
            }
        }
    }

    private void ScanGameObjectAndChildren(GameObject root, DebugDrawColliderRuntime.ColorType type)
    {
        TryAddDebugCollider(root, type);

        foreach (Transform child in root.transform)
        {
            ScanGameObjectAndChildren(child.gameObject, type);
        }
    }

    private void TryAddDebugCollider(GameObject go, DebugDrawColliderRuntime.ColorType type)
    {
        int instanceId = go.GetInstanceID();

        if (_processedObjects.Contains(instanceId)) return;

        if (go.GetComponent<DebugDrawColliderRuntime>() != null)
        {
            _processedObjects.Add(instanceId);
            return;
        }

        bool hasCollider = go.GetComponent<Collider2D>() != null;

        if (!hasCollider) return;

        DebugDrawColliderRuntime.AddOrUpdate(go, type, true);

        _processedObjects.Add(instanceId);
    }

    public static void ClearCache()
    {
        if (_instance != null)
        {
            _instance._processedObjects.Clear();
            _instance._needsRescan = true;
        }
    }

    private void OnDestroy()
    {
        _instance = null;
    }
}
