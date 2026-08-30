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
}
