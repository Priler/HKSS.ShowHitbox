using UnityEngine;
using System.Collections.Generic;
using Color = UnityEngine.Color;

namespace HKSS.ShowHitbox.Behaviour;

public class MoreInfosController : MonoBehaviour
{
    private const float VerticalOffset = 20f;

    internal DebugDrawColliderRuntime? DebugDrawColliderRuntime;

    private GUIStyle? _labelStyle;
    private GUIStyle? _outlineStyle;

    // static tracking of label positions per frame to avoid overlap
    private static readonly Dictionary<int, LabelInfo> _labelsThisFrame = new();
    private static int _lastFrameCount = -1;

    // cached label info for this instance
    private Vector2 _cachedPosition;
    private Vector2 _cachedSize;
    private string? _cachedText;
    private bool _hasCalculatedThisFrame;
    private int _lastCalculatedFrame = -1;

    private struct LabelInfo
    {
        public Vector2 Position;
        public Vector2 Size;
    }


    private void OnGUI()
    {
        // only process during Repaint to avoid duplicate calculations
        if (Event.current.type != EventType.Repaint) return;

        if (!Configs.ShowHitbox || !Configs.MoreInfos) return;
        if (!DebugDrawColliderRuntime) return;

        if (!ShouldShowLabel())
            return;

        var cam = GameCameras.instance.mainCamera;
        if (!cam) return;

        var objScreenPos = cam.WorldToScreenPoint(transform.position);

        if (objScreenPos.z < 0) return;

        // clear label tracking on new frame
        if (Time.frameCount != _lastFrameCount)
        {
            _labelsThisFrame.Clear();
            _lastFrameCount = Time.frameCount;
        }

        InitStyles();

        int instanceId = gameObject.GetInstanceID();

        // calculate position only once per frame
        if (_lastCalculatedFrame != Time.frameCount)
        {
            _cachedText = gameObject.FullName();
            _lastCalculatedFrame = Time.frameCount;
            _hasCalculatedThisFrame = false;
        }

        if (string.IsNullOrEmpty(_cachedText))
            return;

        if (Configs.HidePlayerLabels && IsPlayerLabel(_cachedText!))
            return;

        // calculate or use cached position
        if (!_hasCalculatedThisFrame)
        {
            _labelStyle!.fontSize = Configs.LabelFontSize;
            var content = new GUIContent(_cachedText);
            _cachedSize = _labelStyle.CalcSize(content);

            float baseX = objScreenPos.x - _cachedSize.x / 2;
            float baseY = Screen.height - objScreenPos.y - _cachedSize.y / 2;

            _cachedPosition = ResolveOverlap(new Vector2(baseX, baseY), _cachedSize, instanceId);

            // register position for other labels to check against
            _labelsThisFrame[instanceId] = new LabelInfo
            {
                Position = _cachedPosition,
                Size = _cachedSize
            };

            _hasCalculatedThisFrame = true;
        }

        var labelRect = new Rect(
            _cachedPosition.x,
            _cachedPosition.y,
            _cachedSize.x + 10,
            _cachedSize.y + 4
        );

        Color labelColor = HitboxColors.GetHitboxColor(gameObject, DebugDrawColliderRuntime.type, 1f);

        if (Configs.LabelOutline)
        {
            _outlineStyle!.fontSize = Configs.LabelFontSize;
            GUI.color = Color.black;

            for (int x = -1; x <= 1; x++)
            {
                for (int y = -1; y <= 1; y++)
                {
                    if (x == 0 && y == 0) continue;
                    var outlineRect = new Rect(labelRect.x + x, labelRect.y + y, labelRect.width, labelRect.height);
                    GUI.Label(outlineRect, _cachedText, _outlineStyle);
                }
            }
        }

        GUI.color = labelColor;
        GUI.Label(labelRect, _cachedText, _labelStyle);
    }


    private bool IsPlayerLabel(string labelText)
    {
        return labelText.Contains("Hero_Hornet");
    }


    private Vector2 ResolveOverlap(Vector2 basePosition, Vector2 size, int instanceId)
    {
        Vector2 position = basePosition;
        int maxIterations = 10;
        int iteration = 0;

        while (iteration < maxIterations)
        {
            bool hasOverlap = false;

            foreach (var kvp in _labelsThisFrame)
            {
                if (kvp.Key == instanceId)
                    continue;

                var label = kvp.Value;
                if (IsOverlapping(position, size, label.Position, label.Size))
                {
                    position.y += VerticalOffset + label.Size.y;
                    hasOverlap = true;
                    break;
                }
            }

            if (!hasOverlap)
                break;

            iteration++;
        }

        return position;
    }


    private bool IsOverlapping(Vector2 pos1, Vector2 size1, Vector2 pos2, Vector2 size2)
    {
        float left1 = pos1.x;
        float right1 = pos1.x + size1.x;
        float top1 = pos1.y;
        float bottom1 = pos1.y + size1.y;

        float left2 = pos2.x;
        float right2 = pos2.x + size2.x;
        float top2 = pos2.y;
        float bottom2 = pos2.y + size2.y;

        float padding = 5f;

        return !(right1 + padding < left2 ||
                 left1 - padding > right2 ||
                 bottom1 + padding < top2 ||
                 top1 - padding > bottom2);
    }


    private void InitStyles()
    {
        if (_labelStyle == null)
        {
            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                wordWrap = false
            };
        }

        if (_outlineStyle == null)
        {
            _outlineStyle = new GUIStyle(GUI.skin.label)
            {
                wordWrap = false
            };
        }
    }


    private bool ShouldShowLabel()
    {
        if (!DebugDrawColliderRuntime) return false;

        // check for special categories first
        var damageEnemies = gameObject.GetComponent<DamageEnemies>();
        if (damageEnemies != null)
            return Configs.LabelPlayerAttack;

        var healthManager = gameObject.GetComponent<HealthManager>();
        if (healthManager != null)
            return Configs.LabelEnemy;

        // fallback to ColorType
        return DebugDrawColliderRuntime.type switch
        {
            DebugDrawColliderRuntime.ColorType.Danger => Configs.LabelDanger,
            DebugDrawColliderRuntime.ColorType.Enemy => Configs.LabelEnemy,
            DebugDrawColliderRuntime.ColorType.Water => Configs.LabelWater,
            DebugDrawColliderRuntime.ColorType.TerrainCollider => Configs.LabelTerrain,
            DebugDrawColliderRuntime.ColorType.Tilemap => Configs.LabelTilemap,
            DebugDrawColliderRuntime.ColorType.Region => Configs.LabelRegion,
            DebugDrawColliderRuntime.ColorType.Roof => Configs.LabelRoof,
            DebugDrawColliderRuntime.ColorType.TransitionPoint => Configs.LabelTransitionPoint,
            DebugDrawColliderRuntime.ColorType.SandRegion => Configs.LabelSandRegion,
            DebugDrawColliderRuntime.ColorType.ShardRegion => Configs.LabelShardRegion,
            DebugDrawColliderRuntime.ColorType.CameraLock => Configs.LabelCameraLock,
            _ => false
        };
    }
}