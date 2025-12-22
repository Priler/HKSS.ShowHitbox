using UnityEngine;
using System.Collections.Generic;

namespace HKSS.ShowHitbox.Behaviour;

public class ColliderScanner : MonoBehaviour
{
    private static ColliderScanner? _instance;
    private float _scanInterval = 0.5f;
    private float _lastScanTime = 0f;

    private HashSet<int> _processedObjects = new HashSet<int>();

    public static void Initialize()
    {
        if (_instance != null) return;

        var go = new GameObject("HKSS_ColliderScanner");
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<ColliderScanner>();
    }

    private void Update()
    {
        if (!DebugDrawColliderRuntime.IsShowing) return;

        if (Time.unscaledTime - _lastScanTime < _scanInterval) return;
        _lastScanTime = Time.unscaledTime;

        ScanForMissingHitboxes();
    }

    private void ScanForMissingHitboxes()
    {
        // Find all HealthManager components (enemies/damageable objects)
        var healthManagers = FindObjectsOfType<HealthManager>();
        foreach (var hm in healthManagers)
        {
            TryAddDebugCollider(hm.gameObject, DebugDrawColliderRuntime.ColorType.Enemy);
        }

        // Find all DamageHero components (things that damage the player)
        var damageHeroes = FindObjectsOfType<DamageHero>();
        foreach (var dh in damageHeroes)
        {
            if (dh.damageDealt > 0)
            {
                TryAddDebugCollider(dh.gameObject, DebugDrawColliderRuntime.ColorType.Danger);
            }
        }

        // Find all DamageEnemies components (player attacks)
        var damageEnemies = FindObjectsOfType<DamageEnemies>();
        foreach (var de in damageEnemies)
        {
            TryAddDebugCollider(de.gameObject, DebugDrawColliderRuntime.ColorType.Enemy);
        }
    }

    private void TryAddDebugCollider(GameObject go, DebugDrawColliderRuntime.ColorType type)
    {
        int instanceId = go.GetInstanceID();

        // Skip if already processed
        if (_processedObjects.Contains(instanceId)) return;

        // Skip if already has DebugDrawColliderRuntime
        if (go.GetComponent<DebugDrawColliderRuntime>() != null)
        {
            _processedObjects.Add(instanceId);
            return;
        }

        // Check if the object has any 2D colliders
        bool hasCollider = go.GetComponent<Collider2D>() != null;

        if (!hasCollider) return;

        // Add DebugDrawColliderRuntime using the game's method
        DebugDrawColliderRuntime.AddOrUpdate(go, type, true);

        _processedObjects.Add(instanceId);
    }

    // Clear processed objects when scene changes or hitbox display is toggled
    public static void ClearCache()
    {
        if (_instance != null)
        {
            _instance._processedObjects.Clear();
        }
    }

    private void OnDestroy()
    {
        _instance = null;
    }
}