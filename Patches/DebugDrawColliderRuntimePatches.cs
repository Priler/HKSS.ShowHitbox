using HarmonyLib;
using HKSS.ShowHitbox.Behaviour;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using static DebugDrawColliderRuntime;

namespace HKSS.ShowHitbox.Patches;

[HarmonyPatch(typeof(DebugDrawColliderRuntime))]
internal class DebugDrawColliderRuntimePatches
{
    private const int GL_LINES = 1;
    private const int GL_TRIANGLES = 4;

    // Config flags
    private static bool FillEnabled => Configs.FillHitboxes;
    private static bool OutlineEnabled => Configs.OutlineHitboxes;
    private static float Alpha => Configs.FillAlpha;
    
    // current type being rendered (for disabled collider checks)
    private static ColorType _currentRenderType = ColorType.None;
    private static bool _currentIsPlayerAttack = false;

    // Private fields from DebugDrawColliderRuntime
    private static readonly FieldInfo TypeField =
        typeof(DebugDrawColliderRuntime).GetField(
            "type", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo MaterialField =
        typeof(DebugDrawColliderRuntime).GetField(
            "material", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo IsInitializedField =
        typeof(DebugDrawColliderRuntime).GetField(
            "isInitialized", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo DamageHeroField =
        typeof(DebugDrawColliderRuntime).GetField(
            "damageHero", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo DamageEnemiesField =
        typeof(DebugDrawColliderRuntime).GetField(
            "damageEnemies", BindingFlags.Instance | BindingFlags.NonPublic);

    #region AddOrUpdate patch - force registration of all hitboxes

    [HarmonyPatch("AddOrUpdate")]
    [HarmonyPrefix]
    private static bool AddOrUpdatePrefix(GameObject gameObject, ColorType type, ref bool forceAdd)
    {
        // always force add so hitboxes are registered even when display is off
        // this ensures boss attacks that spawn briefly are captured
        forceAdd = true;
        return true;
    }

    #endregion

    #region Init patch - attach MoreInfosController

    [HarmonyPatch("Init")]
    [HarmonyPrefix]
    private static void InitPrefix(DebugDrawColliderRuntime __instance)
    {
        bool isInitialized = false;
        if (IsInitializedField != null)
            isInitialized = (bool)IsInitializedField.GetValue(__instance);

        if (isInitialized)
            return;

        var moreInfosController = __instance.GetComponent<MoreInfosController>()
                               ?? __instance.gameObject.AddComponent<MoreInfosController>();

        if (moreInfosController != null)
            moreInfosController.DebugDrawColliderRuntime = __instance;
    }

    #endregion

    #region OnPostRenderCamera patch - replace original rendering entirely

    [HarmonyPatch("OnPostRenderCamera")]
    [HarmonyPrefix]
    private static bool OnPostRenderCameraPrefix(
        DebugDrawColliderRuntime __instance,
        CameraRenderHooks.CameraSource source)
    {
        if (!DebugDrawColliderRuntime.IsShowing)
            return false;

        if (source != CameraRenderHooks.CameraSource.MainCamera)
            return false;

        ColorType type = GetType(__instance);
        GameObject go = __instance.gameObject;

        // skip enemy body colliders (objects with HealthManager)
        if (Configs.HideEnemyBody)
        {
            var healthManager = go.GetComponent<HealthManager>();
            if (healthManager != null)
                return false;
        }

        // check if this is a detection zone
        bool isDetectionZone = IsDetectionZone(go.name);
        
        // skip detection zones if configured to hide them
        if (Configs.HideEnemyZones && isDetectionZone)
            return false;

        // detection zones use their own color and always render (if not hidden)
        bool shouldFill, shouldOutline;
        if (isDetectionZone)
        {
            shouldFill = FillEnabled;
            shouldOutline = OutlineEnabled;
        }
        else
        {
            shouldFill = FillEnabled && ShouldFill(type, go);
            shouldOutline = OutlineEnabled && ShouldOutline(type, go);
        }

        if (!shouldFill && !shouldOutline)
            return false;

        var mat = MaterialField?.GetValue(__instance) as Material;
        if (mat == null)
            return false;

        var tr = __instance.transform;

        // use detection zone color if applicable
        Color hitboxColor = isDetectionZone 
            ? HitboxColors.DetectionZoneColor
            : HitboxColors.GetHitboxColor(go, type, 1f);
        Color fillColor = hitboxColor;
        fillColor.a = Alpha;

        // store type for disabled collider checks
        _currentRenderType = type;
        _currentIsPlayerAttack = go.GetComponent<DamageEnemies>() != null;

        GL.PushMatrix();
        mat.SetPass(0);

        if (shouldFill)
        {
            FillBoxes(__instance, tr, fillColor);
            FillPolygons(__instance, tr, fillColor);
            FillCircles(__instance, tr, fillColor);
            FillCapsules(__instance, tr, fillColor);
        }

        if (shouldOutline)
        {
            DrawBoxOutlines(__instance, tr, hitboxColor);
            DrawPolygonOutlines(__instance, tr, hitboxColor);
            DrawCircleOutlines(__instance, tr, hitboxColor);
            DrawEdgeOutlines(__instance, tr, hitboxColor);
            DrawCapsuleOutlines(__instance, tr, hitboxColor);
        }

        GL.PopMatrix();

        return false;
    }

    #endregion

    #region Filtering

    private static bool ShouldFill(ColorType type, GameObject go)
    {
        var damageEnemies = go.GetComponent<DamageEnemies>();
        if (damageEnemies != null)
            return Configs.FillPlayerAttack;

        var healthManager = go.GetComponent<HealthManager>();
        if (healthManager != null)
            return Configs.FillEnemy;

        var damageHero = go.GetComponent<DamageHero>();
        if (damageHero != null)
            return Configs.FillDanger;

        return type switch
        {
            ColorType.Danger => Configs.FillDanger,
            ColorType.Enemy => Configs.FillEnemy,
            ColorType.Water => Configs.FillWater,
            ColorType.TerrainCollider => Configs.FillTerrain,
            ColorType.Tilemap => Configs.FillTilemap,
            ColorType.Region => Configs.FillRegion,
            ColorType.Roof => Configs.FillRoof,
            ColorType.TransitionPoint => Configs.FillTransitionPoint,
            ColorType.SandRegion => Configs.FillSandRegion,
            ColorType.ShardRegion => Configs.FillShardRegion,
            ColorType.CameraLock => Configs.FillCameraLock,
            _ => false
        };
    }

    private static bool ShouldOutline(ColorType type, GameObject go)
    {
        var damageEnemies = go.GetComponent<DamageEnemies>();
        if (damageEnemies != null)
            return Configs.OutlinePlayerAttack;

        var healthManager = go.GetComponent<HealthManager>();
        if (healthManager != null)
        {
            if (Configs.FillDanger && Configs.FillEnemy)
                return Configs.OutlineDanger;
            return Configs.OutlineEnemy;
        }

        var damageHero = go.GetComponent<DamageHero>();
        if (damageHero != null)
            return Configs.OutlineDanger;

        return type switch
        {
            ColorType.Danger => Configs.OutlineDanger,
            ColorType.Enemy => Configs.OutlineEnemy,
            ColorType.Water => Configs.OutlineWater,
            ColorType.TerrainCollider => Configs.OutlineTerrain,
            ColorType.Tilemap => Configs.OutlineTilemap,
            ColorType.Region => Configs.OutlineRegion,
            ColorType.Roof => Configs.OutlineRoof,
            ColorType.TransitionPoint => Configs.OutlineTransitionPoint,
            ColorType.SandRegion => Configs.OutlineSandRegion,
            ColorType.ShardRegion => Configs.OutlineShardRegion,
            ColorType.CameraLock => Configs.OutlineCameraLock,
            _ => false
        };
    }

    #endregion

    #region Capsule Drawing Methods

    private static void FillCapsules(DebugDrawColliderRuntime inst, Transform tr, Color fillColor)
    {
        var capsules = inst.GetComponents<CapsuleCollider2D>();
        if (capsules == null || capsules.Length == 0)
            return;

        foreach (var capsule in capsules)
        {
            Color drawColor = GetColliderColor(fillColor, capsule.enabled);
            if (drawColor.a <= 0) continue;

            Vector2 size = capsule.size;
            Vector2 offset = capsule.offset;
            CapsuleDirection2D direction = capsule.direction;

            float radius, height;
            if (direction == CapsuleDirection2D.Vertical)
            {
                radius = size.x / 2f;
                height = Mathf.Max(0, size.y - size.x);
            }
            else
            {
                radius = size.y / 2f;
                height = Mathf.Max(0, size.x - size.y);
            }

            GL.PushMatrix();
            GL.MultMatrix(tr.localToWorldMatrix);

            int segments = 16;

            // Draw center rectangle if height > 0
            if (height > 0)
            {
                GL.Begin(GL_TRIANGLES);
                GL.Color(drawColor);

                Vector3 halfHeight = direction == CapsuleDirection2D.Vertical
                    ? new Vector3(0, height / 2f, 0)
                    : new Vector3(height / 2f, 0, 0);

                Vector3 perpendicular = direction == CapsuleDirection2D.Vertical
                    ? new Vector3(radius, 0, 0)
                    : new Vector3(0, radius, 0);

                Vector3 center = offset.ToVector3(0f);

                Vector3 v0 = center - halfHeight - perpendicular;
                Vector3 v1 = center - halfHeight + perpendicular;
                Vector3 v2 = center + halfHeight + perpendicular;
                Vector3 v3 = center + halfHeight - perpendicular;

                GL.Vertex(v0);
                GL.Vertex(v1);
                GL.Vertex(v2);

                GL.Vertex(v2);
                GL.Vertex(v3);
                GL.Vertex(v0);

                GL.End();
            }

            // Draw end caps (semicircles)
            Vector3 cap1Center, cap2Center;
            if (direction == CapsuleDirection2D.Vertical)
            {
                cap1Center = offset.ToVector3(0f) + new Vector3(0, height / 2f, 0);
                cap2Center = offset.ToVector3(0f) - new Vector3(0, height / 2f, 0);
            }
            else
            {
                cap1Center = offset.ToVector3(0f) + new Vector3(height / 2f, 0, 0);
                cap2Center = offset.ToVector3(0f) - new Vector3(height / 2f, 0, 0);
            }

            // Cap 1 (top/right semicircle)
            GL.Begin(GL_TRIANGLES);
            GL.Color(drawColor);

            float startAngle1 = direction == CapsuleDirection2D.Vertical ? 0 : -Mathf.PI / 2f;
            for (int i = 0; i < segments; i++)
            {
                float t0 = startAngle1 + (float)i / segments * Mathf.PI;
                float t1 = startAngle1 + (float)(i + 1) / segments * Mathf.PI;

                Vector3 pt0 = cap1Center + new Vector3(Mathf.Cos(t0) * radius, Mathf.Sin(t0) * radius, 0f);
                Vector3 pt1 = cap1Center + new Vector3(Mathf.Cos(t1) * radius, Mathf.Sin(t1) * radius, 0f);

                GL.Vertex(cap1Center);
                GL.Vertex(pt0);
                GL.Vertex(pt1);
            }

            GL.End();

            // Cap 2 (bottom/left semicircle)
            GL.Begin(GL_TRIANGLES);
            GL.Color(drawColor);

            float startAngle2 = direction == CapsuleDirection2D.Vertical ? Mathf.PI : Mathf.PI / 2f;
            for (int i = 0; i < segments; i++)
            {
                float t0 = startAngle2 + (float)i / segments * Mathf.PI;
                float t1 = startAngle2 + (float)(i + 1) / segments * Mathf.PI;

                Vector3 pt0 = cap2Center + new Vector3(Mathf.Cos(t0) * radius, Mathf.Sin(t0) * radius, 0f);
                Vector3 pt1 = cap2Center + new Vector3(Mathf.Cos(t1) * radius, Mathf.Sin(t1) * radius, 0f);

                GL.Vertex(cap2Center);
                GL.Vertex(pt0);
                GL.Vertex(pt1);
            }

            GL.End();
            GL.PopMatrix();
        }
    }

    private static void DrawCapsuleOutlines(DebugDrawColliderRuntime inst, Transform tr, Color color)
    {
        var capsules = inst.GetComponents<CapsuleCollider2D>();
        if (capsules == null || capsules.Length == 0)
            return;

        foreach (var capsule in capsules)
        {
            Color drawColor = GetColliderColor(color, capsule.enabled);
            if (drawColor.a <= 0) continue;

            Vector2 size = capsule.size;
            Vector2 offset = capsule.offset;
            CapsuleDirection2D direction = capsule.direction;

            float radius, height;
            if (direction == CapsuleDirection2D.Vertical)
            {
                radius = size.x / 2f;
                height = Mathf.Max(0, size.y - size.x);
            }
            else
            {
                radius = size.y / 2f;
                height = Mathf.Max(0, size.x - size.y);
            }

            GL.PushMatrix();
            GL.MultMatrix(tr.localToWorldMatrix);
            GL.Begin(GL_LINES);
            GL.Color(drawColor);

            int segments = 16;

            Vector3 cap1Center, cap2Center;
            if (direction == CapsuleDirection2D.Vertical)
            {
                cap1Center = offset.ToVector3(0f) + new Vector3(0, height / 2f, 0);
                cap2Center = offset.ToVector3(0f) - new Vector3(0, height / 2f, 0);
            }
            else
            {
                cap1Center = offset.ToVector3(0f) + new Vector3(height / 2f, 0, 0);
                cap2Center = offset.ToVector3(0f) - new Vector3(height / 2f, 0, 0);
            }

            // Draw straight lines connecting the caps
            if (height > 0)
            {
                Vector3 perpendicular = direction == CapsuleDirection2D.Vertical
                    ? new Vector3(radius, 0, 0)
                    : new Vector3(0, radius, 0);

                GL.Vertex(cap1Center + perpendicular);
                GL.Vertex(cap2Center + perpendicular);

                GL.Vertex(cap1Center - perpendicular);
                GL.Vertex(cap2Center - perpendicular);
            }

            // Cap 1 arc (top/right)
            float startAngle1 = direction == CapsuleDirection2D.Vertical ? 0 : -Mathf.PI / 2f;
            for (int i = 0; i < segments; i++)
            {
                float t0 = startAngle1 + (float)i / segments * Mathf.PI;
                float t1 = startAngle1 + (float)(i + 1) / segments * Mathf.PI;

                Vector3 pt0 = cap1Center + new Vector3(Mathf.Cos(t0) * radius, Mathf.Sin(t0) * radius, 0f);
                Vector3 pt1 = cap1Center + new Vector3(Mathf.Cos(t1) * radius, Mathf.Sin(t1) * radius, 0f);

                GL.Vertex(pt0);
                GL.Vertex(pt1);
            }

            // Cap 2 arc (bottom/left)
            float startAngle2 = direction == CapsuleDirection2D.Vertical ? Mathf.PI : Mathf.PI / 2f;
            for (int i = 0; i < segments; i++)
            {
                float t0 = startAngle2 + (float)i / segments * Mathf.PI;
                float t1 = startAngle2 + (float)(i + 1) / segments * Mathf.PI;

                Vector3 pt0 = cap2Center + new Vector3(Mathf.Cos(t0) * radius, Mathf.Sin(t0) * radius, 0f);
                Vector3 pt1 = cap2Center + new Vector3(Mathf.Cos(t1) * radius, Mathf.Sin(t1) * radius, 0f);

                GL.Vertex(pt0);
                GL.Vertex(pt1);
            }

            GL.End();
            GL.PopMatrix();
        }
    }

    #endregion

    #region Outline Drawing Methods

    private static Color GetColliderColor(Color baseColor, bool isEnabled)
    {
        if (isEnabled) return baseColor;
        
        // check if we should show this disabled collider
        bool showDisabled = Configs.ShowDisabledColliders;
        
        // check specific attack options
        if (!showDisabled)
        {
            if (_currentIsPlayerAttack && Configs.ShowDisabledAttacksPlayer)
                showDisabled = true;
            else if (!_currentIsPlayerAttack && _currentRenderType == ColorType.Danger && Configs.ShowDisabledAttacksEnemy)
                showDisabled = true;
        }
        
        if (!showDisabled) return Color.clear;
        
        // dimmed color for disabled colliders
        Color dimmed = baseColor;
        dimmed.a = Configs.DisabledAlpha;
        return dimmed;
    }

    private static void DrawBoxOutlines(DebugDrawColliderRuntime inst, Transform tr, Color color)
    {
        var boxes = inst.GetComponents<BoxCollider2D>();
        if (boxes == null || boxes.Length == 0)
            return;

        foreach (var box in boxes)
        {
            Color drawColor = GetColliderColor(color, box.enabled);
            if (drawColor.a <= 0) continue;

            Vector2 size = box.size;
            Vector2 offset = box.offset;
            Vector2 half = size * 0.5f;

            Vector2 p0 = offset - half;
            Vector2 p2 = offset + half;
            Vector2 p1 = new(p0.x, p2.y);
            Vector2 p3 = new(p2.x, p0.y);

            Vector3 v0 = tr.TransformPoint(p0);
            Vector3 v1 = tr.TransformPoint(p1);
            Vector3 v2 = tr.TransformPoint(p2);
            Vector3 v3 = tr.TransformPoint(p3);

            GL.Begin(GL_LINES);
            GL.Color(drawColor);

            GL.Vertex(v0); GL.Vertex(v1);
            GL.Vertex(v1); GL.Vertex(v2);
            GL.Vertex(v2); GL.Vertex(v3);
            GL.Vertex(v3); GL.Vertex(v0);

            GL.End();
        }
    }

    private static void DrawPolygonOutlines(DebugDrawColliderRuntime inst, Transform tr, Color color)
    {
        var polys = inst.GetComponents<PolygonCollider2D>();
        if (polys == null || polys.Length == 0)
            return;

        foreach (var poly in polys)
        {
            Color drawColor = GetColliderColor(color, poly.enabled);
            if (drawColor.a <= 0) continue;

            for (int pathIndex = 0; pathIndex < poly.pathCount; pathIndex++)
            {
                var pts = poly.GetPath(pathIndex);
                if (pts == null || pts.Length < 2) continue;

                GL.Begin(GL_LINES);
                GL.Color(drawColor);

                for (int i = 0; i < pts.Length; i++)
                {
                    Vector3 current = tr.TransformPoint(pts[i] + poly.offset);
                    Vector3 next = tr.TransformPoint(pts[(i + 1) % pts.Length] + poly.offset);

                    GL.Vertex(current);
                    GL.Vertex(next);
                }

                GL.End();
            }
        }
    }

    private static void DrawCircleOutlines(DebugDrawColliderRuntime inst, Transform tr, Color color)
    {
        var circles = inst.GetComponents<CircleCollider2D>();
        if (circles == null || circles.Length == 0)
            return;

        foreach (var circle in circles)
        {
            Color drawColor = GetColliderColor(color, circle.enabled);
            if (drawColor.a <= 0) continue;

            Vector3 lossyScale = tr.lossyScale;
            int points = Mathf.RoundToInt(
                Mathf.Log(circle.radius * Mathf.Max(lossyScale.x, lossyScale.y) * 100f) * 10f
            );
            if (points < 8) points = 8;

            GL.PushMatrix();
            GL.MultMatrix(tr.localToWorldMatrix);
            GL.Begin(GL_LINES);
            GL.Color(drawColor);

            Vector3 center = circle.offset.ToVector3(0f);

            for (int i = 0; i < points; i++)
            {
                float t0 = (float)i / points * Mathf.PI * 2f;
                float t1 = (float)((i + 1) % points) / points * Mathf.PI * 2f;

                Vector3 pt0 = center + new Vector3(
                    Mathf.Cos(t0) * circle.radius,
                    Mathf.Sin(t0) * circle.radius,
                    0f);

                Vector3 pt1 = center + new Vector3(
                    Mathf.Cos(t1) * circle.radius,
                    Mathf.Sin(t1) * circle.radius,
                    0f);

                GL.Vertex(pt0);
                GL.Vertex(pt1);
            }

            GL.End();
            GL.PopMatrix();
        }
    }

    private static void DrawEdgeOutlines(DebugDrawColliderRuntime inst, Transform tr, Color color)
    {
        var edges = inst.GetComponents<EdgeCollider2D>();
        if (edges == null || edges.Length == 0)
            return;

        foreach (var edge in edges)
        {
            Color drawColor = GetColliderColor(color, edge.enabled);
            if (drawColor.a <= 0) continue;

            var pts = edge.points;
            if (pts == null || pts.Length < 2) continue;

            GL.Begin(GL_LINES);
            GL.Color(drawColor);

            for (int i = 0; i < pts.Length - 1; i++)
            {
                Vector3 current = tr.TransformPoint(pts[i] + edge.offset);
                Vector3 next = tr.TransformPoint(pts[i + 1] + edge.offset);

                GL.Vertex(current);
                GL.Vertex(next);
            }

            GL.End();
        }
    }

    #endregion

    #region Fill Methods

    private static void FillBoxes(DebugDrawColliderRuntime inst, Transform tr, Color fillColor)
    {
        var boxes = inst.GetComponents<BoxCollider2D>();
        if (boxes == null || boxes.Length == 0)
            return;

        foreach (var box in boxes)
        {
            Color drawColor = GetColliderColor(fillColor, box.enabled);
            if (drawColor.a <= 0) continue;

            Vector2 size = box.size;
            Vector2 offset = box.offset;
            Vector2 half = size * 0.5f;

            Vector2 p0 = offset - half;
            Vector2 p2 = offset + half;
            Vector2 p1 = new(p0.x, p2.y);
            Vector2 p3 = new(p2.x, p0.y);

            Vector3 v0 = tr.TransformPoint(p0);
            Vector3 v1 = tr.TransformPoint(p1);
            Vector3 v2 = tr.TransformPoint(p2);
            Vector3 v3 = tr.TransformPoint(p3);

            GL.Begin(GL_TRIANGLES);
            GL.Color(drawColor);

            GL.Vertex(v0);
            GL.Vertex(v1);
            GL.Vertex(v2);

            GL.Vertex(v2);
            GL.Vertex(v3);
            GL.Vertex(v0);

            GL.End();
        }
    }

    private static void FillPolygons(DebugDrawColliderRuntime inst, Transform tr, Color fillColor)
    {
        var polys = inst.GetComponents<PolygonCollider2D>();
        if (polys == null || polys.Length == 0)
            return;

        foreach (var poly in polys)
        {
            Color drawColor = GetColliderColor(fillColor, poly.enabled);
            if (drawColor.a <= 0) continue;

            for (int pathIndex = 0; pathIndex < poly.pathCount; pathIndex++)
            {
                var pts = poly.GetPath(pathIndex);
                if (pts == null || pts.Length < 3) continue;

                var triangles = TriangulatePolygon(pts);
                if (triangles == null || triangles.Count == 0) continue;

                GL.Begin(GL_TRIANGLES);
                GL.Color(drawColor);

                for (int i = 0; i < triangles.Count; i += 3)
                {
                    Vector3 v0 = tr.TransformPoint(pts[triangles[i]] + poly.offset);
                    Vector3 v1 = tr.TransformPoint(pts[triangles[i + 1]] + poly.offset);
                    Vector3 v2 = tr.TransformPoint(pts[triangles[i + 2]] + poly.offset);

                    GL.Vertex(v0);
                    GL.Vertex(v1);
                    GL.Vertex(v2);
                }

                GL.End();
            }
        }
    }

    private static void FillCircles(DebugDrawColliderRuntime inst, Transform tr, Color fillColor)
    {
        var circles = inst.GetComponents<CircleCollider2D>();
        if (circles == null || circles.Length == 0)
            return;

        foreach (var circle in circles)
        {
            Color drawColor = GetColliderColor(fillColor, circle.enabled);
            if (drawColor.a <= 0) continue;

            Vector3 lossyScale = tr.lossyScale;
            int points = Mathf.RoundToInt(
                Mathf.Log(circle.radius * Mathf.Max(lossyScale.x, lossyScale.y) * 100f) * 10f
            );
            if (points < 8) points = 8;

            GL.PushMatrix();
            GL.MultMatrix(tr.localToWorldMatrix);

            GL.Begin(GL_TRIANGLES);
            GL.Color(drawColor);

            Vector3 center = circle.offset.ToVector3(0f);

            for (int i = 0; i < points; i++)
            {
                float t0 = (float)i / points * Mathf.PI * 2f;
                float t1 = (float)((i + 1) % points) / points * Mathf.PI * 2f;

                Vector3 pt0 = center + new Vector3(
                    Mathf.Cos(t0) * circle.radius,
                    Mathf.Sin(t0) * circle.radius,
                    0f);

                Vector3 pt1 = center + new Vector3(
                    Mathf.Cos(t1) * circle.radius,
                    Mathf.Sin(t1) * circle.radius,
                    0f);

                GL.Vertex(center);
                GL.Vertex(pt0);
                GL.Vertex(pt1);
            }

            GL.End();
            GL.PopMatrix();
        }
    }

    #endregion

    #region Triangulation

    private static List<int> TriangulatePolygon(Vector2[] points)
    {
        var result = new List<int>();
        int n = points.Length;

        if (n < 3) return result;

        if (n == 3)
        {
            result.Add(0);
            result.Add(1);
            result.Add(2);
            return result;
        }

        var V = new List<int>(n);

        if (GetSignedArea(points) > 0)
        {
            for (int i = 0; i < n; i++)
                V.Add(i);
        }
        else
        {
            for (int i = n - 1; i >= 0; i--)
                V.Add(i);
        }

        int nv = n;
        int count = 2 * nv;

        for (int v = nv - 1; nv > 2;)
        {
            if (count-- <= 0)
                return FallbackTriangulate(points);

            int u = v;
            if (u >= nv) u = 0;

            v = u + 1;
            if (v >= nv) v = 0;

            int w = v + 1;
            if (w >= nv) w = 0;

            if (IsEar(points, V, u, v, w, nv))
            {
                result.Add(V[u]);
                result.Add(V[v]);
                result.Add(V[w]);

                V.RemoveAt(v);
                nv--;

                count = 2 * nv;
                v = u;
            }
        }

        return result;
    }

    private static float GetSignedArea(Vector2[] points)
    {
        float area = 0f;
        int n = points.Length;

        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            area += points[i].x * points[j].y;
            area -= points[j].x * points[i].y;
        }

        return area * 0.5f;
    }

    private static bool IsEar(Vector2[] points, List<int> V, int u, int v, int w, int n)
    {
        Vector2 A = points[V[u]];
        Vector2 B = points[V[v]];
        Vector2 C = points[V[w]];

        float cross = (B.x - A.x) * (C.y - A.y) - (B.y - A.y) * (C.x - A.x);

        if (cross <= 0.0001f)
            return false;

        for (int p = 0; p < n; p++)
        {
            if (p == u || p == v || p == w)
                continue;

            if (IsPointInTriangle(points[V[p]], A, B, C))
                return false;
        }

        return true;
    }

    private static bool IsPointInTriangle(Vector2 P, Vector2 A, Vector2 B, Vector2 C)
    {
        float areaABC = CrossProduct2D(B - A, C - A);

        if (Mathf.Abs(areaABC) < 0.0001f)
            return false;

        float areaPBC = CrossProduct2D(B - P, C - P);
        float areaPCA = CrossProduct2D(C - P, A - P);

        float s = areaPBC / areaABC;
        float t = areaPCA / areaABC;
        float uu = 1f - s - t;

        return s >= -0.0001f && t >= -0.0001f && uu >= -0.0001f;
    }

    private static float CrossProduct2D(Vector2 a, Vector2 b)
    {
        return a.x * b.y - a.y * b.x;
    }

    private static List<int> FallbackTriangulate(Vector2[] points)
    {
        var result = new List<int>();
        int n = points.Length;

        if (n < 3) return result;

        for (int i = 1; i < n - 1; i++)
        {
            result.Add(0);
            result.Add(i);
            result.Add(i + 1);
        }

        return result;
    }

    #endregion

    #region Helpers

    private static ColorType GetType(DebugDrawColliderRuntime inst)
    {
        if (TypeField == null)
            return ColorType.None;

        return (ColorType)TypeField.GetValue(inst);
    }

    private static bool IsDetectionZone(string name)
    {
        string nameLower = name.ToLowerInvariant();
        return nameLower.Contains("range") || 
               nameLower.Contains("alert") || 
               nameLower.Contains("sense") || 
               nameLower.Contains("detect");
    }

    #endregion
}