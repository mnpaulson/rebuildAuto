using System;
using Assets.Scripts;
using Assets.Scripts.PlayerControl;
using Assets.Scripts.UI;
using UnityEngine;

namespace RebuildBotPlugin.Controllers
{
    /// <summary>
    /// Manages the low-overhead "Headless-Lite" execution mode.
    /// Caps target framerate, disables scene geometry/sprite rendering via culling masks,
    /// hides uGUI canvas, and mutes audio to drastically reduce CPU and GPU usage when multi-botting.
    /// </summary>
    public class LowSpecController
    {
        private bool isCurrentlyLowSpec = false;
        private int originalTargetFrameRate = -1;
        private int originalVsyncCount = 1;
        private int originalCullingMask = -999;
        private CameraClearFlags originalClearFlags = CameraClearFlags.Skybox;
        private Color originalBgColor = Color.black;
        private float lastFpsSampleTime = 0f;
        private int frameCount = 0;
        public float CurrentCalculatedFps { get; private set; } = 0f;

        public bool IsActive => isCurrentlyLowSpec;

        public void ApplyState(bool enableLowSpec)
        {
            if (enableLowSpec == isCurrentlyLowSpec) return;

            isCurrentlyLowSpec = enableLowSpec;
            var config = BotConfigManager.Current;

            if (enableLowSpec)
            {
                originalTargetFrameRate = Application.targetFrameRate;
                originalVsyncCount = QualitySettings.vSyncCount;

                // Disable VSync so Application.targetFrameRate takes priority
                QualitySettings.vSyncCount = 0;
                int targetFps = config.TargetFrameRate > 0 ? config.TargetFrameRate : 10;
                Application.targetFrameRate = targetFps;

                if (config.MuteAudioInLowSpec)
                {
                    AudioListener.pause = true;
                }

                if (config.DisableRenderingInLowSpec)
                {
                    SetRenderingStripped(true);
                }

                BotEngine.Instance?.LogEvent($"[Low-Spec Mode] ENABLED: Framerate capped to {targetFps} FPS, Rendering {(config.DisableRenderingInLowSpec ? "Disabled" : "Active")}, Audio {(config.MuteAudioInLowSpec ? "Muted" : "Active")}.");
            }
            else
            {
                // Restore original game settings
                QualitySettings.vSyncCount = originalVsyncCount >= 0 ? originalVsyncCount : 1;
                Application.targetFrameRate = originalTargetFrameRate;
                AudioListener.pause = false;
                SetRenderingStripped(false);

                BotEngine.Instance?.LogEvent("[Low-Spec Mode] DISABLED: Normal rendering, framerate, and audio restored.");
            }
        }

        public void Toggle()
        {
            var config = BotConfigManager.Current;
            config.LowSpecMode = !config.LowSpecMode;
            BotConfigManager.SaveConfig();
            ApplyState(config.LowSpecMode);
        }

        public void Update(float now)
        {
            var config = BotConfigManager.Current;
            if (config.LowSpecMode != isCurrentlyLowSpec)
            {
                ApplyState(config.LowSpecMode);
            }

            // Ensure rendering state stays applied if map/camera changed
            if (isCurrentlyLowSpec && config.DisableRenderingInLowSpec)
            {
                var cam = CameraFollower.Instance?.Camera ?? Camera.main;
                if (cam != null && cam.cullingMask != 0)
                {
                    if (originalCullingMask == -999) originalCullingMask = cam.cullingMask;
                    cam.cullingMask = 0;
                }
            }

            // FPS calculation for telemetry & overlay monitor
            frameCount++;
            if (now - lastFpsSampleTime >= 1.0f)
            {
                CurrentCalculatedFps = frameCount / (now - lastFpsSampleTime);
                frameCount = 0;
                lastFpsSampleTime = now;
            }
        }

        private void SetRenderingStripped(bool stripped)
        {
            try
            {
                var cam = CameraFollower.Instance?.Camera ?? Camera.main;
                if (cam != null)
                {
                    if (stripped)
                    {
                        if (originalCullingMask == -999)
                        {
                            originalCullingMask = cam.cullingMask;
                            originalClearFlags = cam.clearFlags;
                            originalBgColor = cam.backgroundColor;
                        }
                        cam.cullingMask = 0; // Cull all layers -> 0 meshes/sprites drawn
                        cam.clearFlags = CameraClearFlags.SolidColor;
                        cam.backgroundColor = Color.black;
                    }
                    else
                    {
                        if (originalCullingMask != -999)
                        {
                            cam.cullingMask = originalCullingMask;
                            cam.clearFlags = originalClearFlags;
                            cam.backgroundColor = originalBgColor;
                        }
                        else
                        {
                            cam.cullingMask = -1; // Everything
                        }
                    }
                }

                // Manage uGUI Canvas visibility
                if (UiManager.Instance != null)
                {
                    UiManager.Instance.SetEnabled(!stripped);
                }
            }
            catch (Exception ex)
            {
                BotEngine.Instance?.LogEvent($"[Low-Spec Mode] Note on rendering toggle: {ex.Message}");
            }
        }
    }
}
