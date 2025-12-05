using HarmonyLib;
using HKSS.ShowHitbox.Behaviour;
using System.Reflection;
using UnityEngine;
using static DebugDrawColliderRuntime;

namespace HKSS.ShowHitbox.Patches;

[HarmonyPatch(typeof(DebugDrawColliderRuntime))]
internal class DebugDrawColliderRuntimePatches
{
    private const int GL_LINES = 1;
    private const int GL_TRIANGLES = 4;

    // config flags
    private static bool FillEnabled => Configs.FillHitboxes;
    private static bool OutlineEnabled => Configs.OutlineHitboxes;
    private static float Alpha => Configs.FillAlpha;

    // private fields from DebugDrawColliderRuntime in-game class
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

        moreInfosController?.DebugDrawColliderRuntime = __instance;
    }

    #endregion

    #region OnPostRenderCamera patch - replace original rendering entirely

    [HarmonyPatch("OnPostRenderCamera")]
    [HarmonyPrefix]
    private static bool OnPostRenderCameraPrefix(
        DebugDrawColliderRuntime __instance,
        CameraRenderHooks.CameraSource source)
    {
        // skip if turned off or not in the main camera
        if (!DebugDrawColliderRuntime.IsShowing)
            return false;

        if (source != CameraRenderHooks.CameraSource.MainCamera)
            return false;

        // get components for type detection
        var damageEnemies = DamageEnemiesField?.GetValue(__instance) as DamageEnemies;
        var damageHero = DamageHeroField?.GetValue(__instance) as DamageHero;

        // original method's visibility check
        if (damageEnemies == null && damageHero != null && damageHero.damageDealt <= 0)
            return false;

        ColorType type = GetType(__instance);
        GameObject go = __instance.gameObject;

        // check if we should render anything for this hitbox
        bool shouldFill = FillEnabled && ShouldFill(type, go);
        bool shouldOutline = OutlineEnabled && ShouldOutline(type, go);

        // skip if neither fill nor outline should be rendered
        if (!shouldFill && !shouldOutline)
            return false;

        var mat = MaterialField?.GetValue(__instance) as Material;
        if (mat == null)
            return false;

        var tr = __instance.transform;

        // get color via shared utility
        Color hitboxColor = HitboxColors.GetHitboxColor(go, type, 1f);
        Color fillColor = hitboxColor;
        fillColor.a = Alpha;

        GL.PushMatrix();
        mat.SetPass(0);

        // draw fill first (if enabled for this type)
        if (shouldFill)
        {
            FillBoxes(__instance, tr, fillColor);
            FillPolygons(__instance, tr, fillColor);
            FillCircles(__instance, tr, fillColor);
        }

        // draw outlines on top (if enabled for this type)
        if (shouldOutline)
        {
            DrawBoxOutlines(__instance, tr, hitboxColor);
            DrawPolygonOutlines(__instance, tr, hitboxColor);
            DrawCircleOutlines(__instance, tr, hitboxColor);
            DrawEdgeOutlines(__instance, tr, hitboxColor);
        }

        GL.PopMatrix();

        return false;
    }

    #endregion

    #region Filtering

    private static bool ShouldFill(ColorType type, GameObject go)
    {
        // check for special categories first
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
        // check for special categories first
        var damageEnemies = go.GetComponent<DamageEnemies>();
        if (damageEnemies != null)
            return Configs.OutlinePlayerAttack;

        var healthManager = go.GetComponent<HealthManager>();
        if (healthManager != null)
            return Configs.OutlineEnemy;

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

    #region Outline Drawing Methods

    private static void DrawBoxOutlines(DebugDrawColliderRuntime inst, Transform tr, Color color)
    {
        var boxes = inst.GetComponents<BoxCollider2D>();
        if (boxes == null || boxes.Length == 0)
            return;

        foreach (var box in boxes)
        {
            if (!box.enabled) continue;

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
            GL.Color(color);

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
            if (!poly.enabled) continue;

            for (int pathIndex = 0; pathIndex < poly.pathCount; pathIndex++)
            {
                var pts = poly.GetPath(pathIndex);
                if (pts == null || pts.Length < 2) continue;

                GL.Begin(GL_LINES);
                GL.Color(color);

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
            if (!circle.enabled) continue;

            Vector3 lossyScale = tr.lossyScale;
            int points = Mathf.RoundToInt(
                Mathf.Log(circle.radius * Mathf.Max(lossyScale.x, lossyScale.y) * 100f) * 10f
            );
            if (points < 8) points = 8;

            GL.Begin(GL_LINES);
            GL.PushMatrix();
            GL.MultMatrix(tr.localToWorldMatrix);
            GL.Color(color);

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

            GL.PopMatrix();
            GL.End();
        }
    }

    private static void DrawEdgeOutlines(DebugDrawColliderRuntime inst, Transform tr, Color color)
    {
        var edges = inst.GetComponents<EdgeCollider2D>();
        if (edges == null || edges.Length == 0)
            return;

        foreach (var edge in edges)
        {
            if (!edge.enabled) continue;

            var pts = edge.points;
            if (pts == null || pts.Length < 2) continue;

            GL.Begin(GL_LINES);
            GL.Color(color);

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
            if (!box.enabled) continue;

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
            GL.Color(fillColor);

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
            if (!poly.enabled) continue;

            for (int pathIndex = 0; pathIndex < poly.pathCount; pathIndex++)
            {
                var pts = poly.GetPath(pathIndex);
                if (pts == null || pts.Length < 3) continue;

                var triangles = TriangulatePolygon(pts);
                if (triangles == null || triangles.Count == 0) continue;

                GL.Begin(GL_TRIANGLES);
                GL.Color(fillColor);

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
            if (!circle.enabled) continue;

            Vector3 lossyScale = tr.lossyScale;
            int points = Mathf.RoundToInt(
                Mathf.Log(circle.radius * Mathf.Max(lossyScale.x, lossyScale.y) * 100f) * 10f
            );
            if (points < 8) points = 8;

            GL.PushMatrix();
            GL.MultMatrix(tr.localToWorldMatrix);

            GL.Begin(GL_TRIANGLES);
            GL.Color(fillColor);

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

    #region Triangulation (bravo six going dark xD)

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
            {
                return FallbackTriangulate(points);
            }

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

    #endregion
}