using System;
using Assets.Scripts;
using Assets.Scripts.Network;
using HarmonyLib;
using RebuildSharedData.Enum;
using RebuildSharedData.Networking;
using UnityEngine;

namespace RebuildBotPlugin
{
    [HarmonyPatch(typeof(CameraFollower), "ScreenCastV2")]
    public static class ScreenCastV2Patch
    {
        public static void Prefix(ref bool isOverUi)
        {
            if (BotGuiOverlay.IsMouseOverOverlay)
            {
                isOverUi = true;
            }
        }
    }

    [HarmonyPatch(typeof(NetworkManager), nameof(NetworkManager.MovePlayer))]
    public static class MovePlayerPatch
    {
        public static bool Prefix()
        {
            if (BotGuiOverlay.IsMouseOverOverlay)
            {
                return false; // Suppress movement packet when mouse is over bot GUI
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(NetworkManager), nameof(NetworkManager.PrepareAttackMotionSettings))]
    public static class PrepareAttackMotionPatch
    {
        public static void Postfix(ServerControllable src, ServerControllable target)
        {
            if (src != null && target != null && NetworkManager.Instance != null && target.Id == NetworkManager.Instance.PlayerId)
            {
                BotEngine.Instance?.Targeting.RegisterAttacker(src.Id);
            }

            if (src != null && target != null)
            {
                BotEngine.Instance?.Party.OnAttackMotion(src, target);
                BotEngine.Instance?.Skills.OnAttackMotion(src, target);
            }
        }
    }

    [HarmonyPatch(typeof(CameraFollower), nameof(CameraFollower.UpdatePlayerExp))]
    public static class UpdatePlayerExpPatch
    {
        public static void Postfix(int exp, int maxExp)
        {
            BotEngine.Instance?.ExpTracker.UpdateBaseExp(exp, maxExp);
        }
    }

    [HarmonyPatch(typeof(CameraFollower), nameof(CameraFollower.UpdatePlayerJobExp))]
    public static class UpdatePlayerJobExpPatch
    {
        public static void Postfix(int exp, int maxExp)
        {
            BotEngine.Instance?.ExpTracker.UpdateJobExp(exp, maxExp);
        }
    }

    [HarmonyPatch(typeof(ServerControllable), nameof(ServerControllable.StartCastBar))]
    public static class StartCastBarPatch
    {
        public static void Postfix(ServerControllable __instance, CharacterSkill skill, float duration)
        {
            if (__instance != null)
            {
                __instance.IsCasting = true;
                if (NetworkManager.Instance != null && __instance.Id == NetworkManager.Instance.PlayerId)
                {
                    Controllers.SkillController.ActiveCastEndTime = Time.timeSinceLevelLoad + Mathf.Max(0.1f, duration);
                }
            }
        }
    }

    [HarmonyPatch(typeof(ServerControllable), nameof(ServerControllable.StopCasting))]
    public static class StopCastingPatch
    {
        public static void Postfix(ServerControllable __instance)
        {
            if (__instance != null)
            {
                __instance.IsCasting = false;
                if (NetworkManager.Instance != null && __instance.Id == NetworkManager.Instance.PlayerId)
                {
                    Controllers.SkillController.ActiveCastEndTime = 0f;
                }
            }
        }
    }

    [HarmonyPatch(typeof(Assets.Scripts.UI.Hud.ToastNotificationArea), nameof(Assets.Scripts.UI.Hud.ToastNotificationArea.AddPartyInvite))]
    public static class AddPartyInvitePatch
    {
        public static void Postfix(int partyId, string leaderName, string partyName)
        {
            BotEngine.Instance?.Party.OnPartyInviteReceived(partyId, leaderName, partyName);
        }
    }

    [HarmonyPatch(typeof(Assets.Scripts.UI.Hud.MinimapController), nameof(Assets.Scripts.UI.Hud.MinimapController.SetEntityPosition))]
    public static class MinimapSetEntityPositionPatch
    {
        public static void Postfix(int entityId, CharacterDisplayType type, Vector2Int pos)
        {
            Services.BossTrackingService.Instance.OnSetEntityPosition(entityId, type, pos);
        }
    }

    [HarmonyPatch(typeof(Assets.Scripts.UI.Hud.MinimapController), nameof(Assets.Scripts.UI.Hud.MinimapController.RemoveEntity))]
    public static class MinimapRemoveEntityPatch
    {
        public static void Postfix(int entityId)
        {
            Services.BossTrackingService.Instance.OnRemoveEntity(entityId);
        }
    }

    [HarmonyPatch(typeof(Assets.Scripts.UI.Hud.MinimapController), nameof(Assets.Scripts.UI.Hud.MinimapController.RemoveAllEntities))]
    public static class MinimapRemoveAllEntitiesPatch
    {
        public static void Postfix()
        {
            Services.BossTrackingService.Instance.Clear();
        }
    }
}

