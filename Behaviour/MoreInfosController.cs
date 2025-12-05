using UnityEngine;
using Color = UnityEngine.Color;

namespace HKSS.ShowHitbox.Behaviour;

public class MoreInfosController : MonoBehaviour
{
    private const float VerticalOffset = 20f;

    internal DebugDrawColliderRuntime? DebugDrawColliderRuntime;

    private GUIStyle? _labelStyle;
    private GUIStyle? _outlineStyle;

    // static tracking of label positions per frame to avoid overlap
    // @todo: doesn't seem to work whatsoever
    private static readonly List<LabelInfo> _labelsThisFrame = [];
    private static int _lastFrameCount = -1;


    private struct LabelInfo
    {
        public Vector2 Position;
        public Vector2 Size;
        public int InstanceId;
    }


    private void OnGUI()
    {
        if (!Configs.ShowHitbox || !Configs.MoreInfos) return;
        if (!DebugDrawColliderRuntime) return;

        // check if this type should display labels
        if (!ShouldShowLabel())
            return;

        var cam = GameCameras.instance.mainCamera;
        if (!cam) return;

        var objScreenPos = cam.WorldToScreenPoint(transform.position);

        // don't render if behind camera
        if (objScreenPos.z < 0) return;

        // clear label tracking on new frame
        if (Time.frameCount != _lastFrameCount)
        {
            _labelsThisFrame.Clear();
            _lastFrameCount = Time.frameCount;
        }

        // initialize styles (if needed
        InitStyles();

        var labelText = gameObject.FullName();

        // skip if label text is null or empty
        if (string.IsNullOrEmpty(labelText))
            return;

        // hide player labels if option is enabled and label matches
        if (Configs.HidePlayerLabels && IsPlayerLabel(labelText!))
            return;

        // calculate text size for centering
        _labelStyle!.fontSize = Configs.LabelFontSize;
        var content = new GUIContent(labelText);
        var textSize = _labelStyle.CalcSize(content);

        // base position (centered on object)
        float baseX = objScreenPos.x - textSize.x / 2;
        float baseY = Screen.height - objScreenPos.y - textSize.y / 2;

        // check for overlaps and offset if needed (to avoid label stacking on top of each other)
        Vector2 finalPosition = ResolveOverlap(
            new Vector2(baseX, baseY),
            textSize,
            gameObject.GetInstanceID()
        );

        var labelRect = new Rect(
            finalPosition.x,
            finalPosition.y,
            textSize.x + 10,
            textSize.y + 4
        );

        // register this label position
        _labelsThisFrame.Add(new LabelInfo
        {
            Position = finalPosition,
            Size = textSize,
            InstanceId = gameObject.GetInstanceID()
        });

        // get color using shared utility (same as fill color, but full alpha)
        Color labelColor = HitboxColors.GetHitboxColor(gameObject, DebugDrawColliderRuntime.type, 1f);

        // draw outline for better visibility
        if (Configs.LabelOutline)
        {
            _outlineStyle!.fontSize = Configs.LabelFontSize;
            GUI.color = Color.black;

            // draw outline in 8 directions
            for (int x = -1; x <= 1; x++)
            {
                for (int y = -1; y <= 1; y++)
                {
                    if (x == 0 && y == 0) continue;
                    var outlineRect = new Rect(labelRect.x + x, labelRect.y + y, labelRect.width, labelRect.height);
                    GUI.Label(outlineRect, labelText, _outlineStyle);
                }
            }
        }

        // draw main label
        GUI.color = labelColor;
        GUI.Label(labelRect, labelText, _labelStyle);
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

            foreach (var label in _labelsThisFrame)
            {
                if (label.InstanceId == instanceId)
                    continue;

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