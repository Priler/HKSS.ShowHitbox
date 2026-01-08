using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Configuration;
using BepInExUtils.Attributes;
using BepInExUtils.Interfaces;
using HarmonyLib;
using UnityEngine;
using HKSS.ShowHitbox;
using HKSS.ShowHitbox.Behaviour;

namespace HKSS.ShowHitbox;

[BepInUtils("io.github.ykysnk.HKSS.ShowHitbox", "Show Hitbox", Version)]
[BepInDependency("io.github.ykysnk.BepinExUtils", "1.0.0")]
[BepInProcess(Utils.GameName)]

// Main options
[ConfigBind<KeyCode>("ToggleKey", SectionOptions, KeyCode.F11, "The toggle key to toggle the hitbox display.")]
[ConfigBind<bool>("ShowHitbox", SectionOptions, false, "Show the hitbox.")]
[ConfigBind<bool>("MoreInfos", SectionOptions, true, "Show more info.")]

// Time control options
[ConfigBind<bool>("EnablePause", SectionTimeControl, true, "Enable pause functionality")]
[ConfigBind<KeyCode>("PauseKey", SectionTimeControl, KeyCode.Pause, "The key to pause/resume the game")]
[ConfigBind<bool>("EnableSlowMo", SectionTimeControl, true, "Enable slow motion functionality")]
[ConfigBind<KeyCode>("SlowMoKey", SectionTimeControl, KeyCode.ScrollLock, "The key to toggle slow motion")]
[ConfigBind<float>("SlowMoFactor", SectionTimeControl, 0.25f, "Slow motion speed factor (0.1 = very slow, 0.5 = half speed)")]

// Fill options
[ConfigBind<bool>("FillHitboxes", SectionFill, true, "Draw filled hitboxes background")]
[ConfigBind<float>("FillAlpha", SectionFill, 0.15f, "Hitbox fill opacity (0.0 - 1.0)")]
[ConfigBind<bool>("ShowDisabledColliders", SectionFill, true, "Show colliders even when disabled (dimmed)")]
[ConfigBind<float>("DisabledAlpha", SectionFill, 0.05f, "Opacity for disabled colliders (0.0 - 1.0)")]

// Fill filters - which hitbox types to fill
[ConfigBind<bool>("FillDanger", SectionFillFilters, true, "Fill Danger hitboxes (spikes, hazards)")]
[ConfigBind<bool>("FillEnemy", SectionFillFilters, true, "Fill Enemy hitboxes (things with health)")]
[ConfigBind<bool>("FillPlayerAttack", SectionFillFilters, true, "Fill Player attack hitboxes (nail slashes, spells)")]
[ConfigBind<bool>("FillWater", SectionFillFilters, true, "Fill Water regions")]
[ConfigBind<bool>("FillTerrain", SectionFillFilters, false, "Fill Terrain colliders (can be complex)")]
[ConfigBind<bool>("FillTilemap", SectionFillFilters, false, "Fill Tilemap colliders (can be complex)")]
[ConfigBind<bool>("FillRegion", SectionFillFilters, false, "Fill Region triggers")]
[ConfigBind<bool>("FillRoof", SectionFillFilters, false, "Fill Roof colliders")]
[ConfigBind<bool>("FillTransitionPoint", SectionFillFilters, false, "Fill Transition points")]
[ConfigBind<bool>("FillSandRegion", SectionFillFilters, false, "Fill Sand regions")]
[ConfigBind<bool>("FillShardRegion", SectionFillFilters, false, "Fill Shard regions")]
[ConfigBind<bool>("FillCameraLock", SectionFillFilters, false, "Fill Camera lock zones")]

// Outline options
[ConfigBind<bool>("OutlineHitboxes", SectionOutline, true, "Draw hitbox outlines")]

// Outline filters - which hitbox types to outline
[ConfigBind<bool>("OutlineDanger", SectionOutlineFilters, true, "Outline Danger hitboxes (spikes, hazards)")]
[ConfigBind<bool>("OutlineEnemy", SectionOutlineFilters, true, "Outline Enemy hitboxes (things with health)")]
[ConfigBind<bool>("OutlinePlayerAttack", SectionOutlineFilters, true, "Outline Player attack hitboxes (nail slashes, spells)")]
[ConfigBind<bool>("OutlineWater", SectionOutlineFilters, true, "Outline Water regions")]
[ConfigBind<bool>("OutlineTerrain", SectionOutlineFilters, true, "Outline Terrain colliders")]
[ConfigBind<bool>("OutlineTilemap", SectionOutlineFilters, true, "Outline Tilemap colliders")]
[ConfigBind<bool>("OutlineRegion", SectionOutlineFilters, true, "Outline Region triggers")]
[ConfigBind<bool>("OutlineRoof", SectionOutlineFilters, true, "Outline Roof colliders")]
[ConfigBind<bool>("OutlineTransitionPoint", SectionOutlineFilters, true, "Outline Transition points")]
[ConfigBind<bool>("OutlineSandRegion", SectionOutlineFilters, true, "Outline Sand regions")]
[ConfigBind<bool>("OutlineShardRegion", SectionOutlineFilters, true, "Outline Shard regions")]
[ConfigBind<bool>("OutlineCameraLock", SectionOutlineFilters, true, "Outline Camera lock zones")]

// Label options
[ConfigBind<int>("LabelFontSize", SectionLabels, 14, "Font size for hitbox labels (8-32)")]
[ConfigBind<bool>("LabelOutline", SectionLabels, true, "Draw outline around label text for better visibility")]
[ConfigBind<bool>("HidePlayerLabels", SectionLabels, true, "Hide labels containing 'Hero_Hornet' (player hitboxes)")]
[ConfigBind<bool>("HighlightPlayer", SectionLabels, false, "Highlight player hitbox in red like enemies")]

// Label filters - which hitbox types show labels
[ConfigBind<bool>("LabelDanger", SectionLabelFilters, true, "Show labels for Danger hitboxes")]
[ConfigBind<bool>("LabelEnemy", SectionLabelFilters, true, "Show labels for Enemy hitboxes")]
[ConfigBind<bool>("LabelPlayerAttack", SectionLabelFilters, false, "Show labels for Player attack hitboxes")]
[ConfigBind<bool>("LabelWater", SectionLabelFilters, false, "Show labels for Water regions")]
[ConfigBind<bool>("LabelTerrain", SectionLabelFilters, false, "Show labels for Terrain colliders")]
[ConfigBind<bool>("LabelTilemap", SectionLabelFilters, false, "Show labels for Tilemap colliders")]
[ConfigBind<bool>("LabelRegion", SectionLabelFilters, false, "Show labels for Region triggers")]
[ConfigBind<bool>("LabelRoof", SectionLabelFilters, false, "Show labels for Roof colliders")]
[ConfigBind<bool>("LabelTransitionPoint", SectionLabelFilters, false, "Show labels for Transition points")]
[ConfigBind<bool>("LabelSandRegion", SectionLabelFilters, false, "Show labels for Sand regions")]
[ConfigBind<bool>("LabelShardRegion", SectionLabelFilters, false, "Show labels for Shard regions")]
[ConfigBind<bool>("LabelCameraLock", SectionLabelFilters, false, "Show labels for Camera lock zones")]

public partial class Main
{
    private const string SectionOptions = "Options";
    private const string SectionTimeControl = "Time Control";
    private const string SectionFill = "Fill Options";
    private const string SectionFillFilters = "Fill Filters";
    private const string SectionOutline = "Outline Options";
    private const string SectionOutlineFilters = "Outline Filters";
    private const string SectionLabels = "Label Options";
    private const string SectionLabelFilters = "Label Filters";
    private const string Version = "0.2.3";

    private static bool _isPaused = false;
    private static bool _isSlowMo = false;
    private static float _normalTimeScale = 1f;

    private void Update()
    {
        if (UnityInput.Current.GetKeyDown(Configs.ToggleKey))
        {
            Configs.ShowHitbox = !Configs.ShowHitbox;
        }

        if (Configs.EnablePause && UnityInput.Current.GetKeyDown(Configs.PauseKey))
        {
            TogglePause();
        }

        if (Configs.EnableSlowMo && UnityInput.Current.GetKeyDown(Configs.SlowMoKey))
        {
            ToggleSlowMo();
        }

        EnforceTimeScale();
    }

    private void LateUpdate()
    {
        EnforceTimeScale();
    }

    private static void EnforceTimeScale()
    {
        float target = _isPaused ? 0f : _isSlowMo ? Mathf.Clamp(Configs.SlowMoFactor, 0.01f, 1f) : _normalTimeScale;

        if (Time.timeScale != target)
            Time.timeScale = target;
    }

    private static void TogglePause()
    {
        _isSlowMo = false;
        _isPaused = !_isPaused;
        Time.timeScale = _isPaused ? 0f : _normalTimeScale;
        Utils.Logger.Info(_isPaused ? "Game paused" : "Game resumed");
    }

    private static void ToggleSlowMo()
    {
        _isPaused = false;
        _isSlowMo = !_isSlowMo;
        Time.timeScale = _isSlowMo ? Mathf.Clamp(Configs.SlowMoFactor, 0.01f, 1f) : _normalTimeScale;
        Utils.Logger.Info(_isSlowMo ? $"Slow motion enabled ({Time.timeScale}x)" : "Slow motion disabled");
    }

    public void Init()
    {
        Configs.OnShowHitboxValueChanged += OnToggleHitbox;
        DebugDrawColliderRuntime.IsShowing = Configs.ShowHitbox;

        // Initialize the collider scanner
        ColliderScanner.Initialize();
    }

    private static void OnToggleHitbox(bool oldValue, bool newValue)
    {
        Utils.Logger.Info($"Debug hitbox is now turn {(newValue ? "on" : "off")}!");
        DebugDrawColliderRuntime.IsShowing = newValue;

        // Clear scanner cache when toggling
        if (newValue)
        {
            ColliderScanner.ClearCache();
        }
    }

    private void OnDestroy()
    {
        RestoreTimeScale();
    }

    private void OnApplicationQuit()
    {
        RestoreTimeScale();
    }

    private static void RestoreTimeScale()
    {
        Time.timeScale = _normalTimeScale;
        _isPaused = _isSlowMo = false;
    }
}