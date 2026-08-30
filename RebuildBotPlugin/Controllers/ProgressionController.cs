using System;
using Assets.Scripts.Network;
using Assets.Scripts.PlayerControl;
using RebuildSharedData.Enum;
using RebuildSharedData.Enum.EntityStats;
using UnityEngine;

namespace RebuildBotPlugin.Controllers
{
    public class ProgressionController
    {
        private float lastEvaluationTime = 0f;
        private float lastSkillActionTime = 0f;
        private const float EvaluationCadence = 0.8f;
        private const float SkillActionCadence = 0.6f;

        public string ActiveStatGoalText { get; private set; } = "None";
        public string ActiveSkillGoalText { get; private set; } = "None";

        public void Reset()
        {
            lastEvaluationTime = 0f;
            lastSkillActionTime = 0f;
            ActiveStatGoalText = "None";
            ActiveSkillGoalText = "None";
        }

        public void ProcessProgression(NetworkManager netManager, ServerControllable player, float now)
        {
            if (netManager == null || player == null || !player.IsCharacterAlive || player.Hp <= 0)
                return;

            if (now - lastEvaluationTime < EvaluationCadence)
                return;

            lastEvaluationTime = now;
            var playerState = PlayerState.Instance;
            if (playerState == null) return;

            if (BotConfigManager.Current.AutoStatAllocation)
            {
                ProcessStatAllocation(playerState, netManager);
            }
            else
            {
                ActiveStatGoalText = "Disabled";
            }

            if (BotConfigManager.Current.AutoSkillAllocation)
            {
                ProcessSkillAllocation(playerState, netManager, now);
            }
            else
            {
                ActiveSkillGoalText = "Disabled";
            }
        }

        private void ProcessStatAllocation(PlayerState playerState, NetworkManager netManager)
        {
            int unspentPoints = playerState.GetData(PlayerStat.StatPoints);
            var plan = BotConfigManager.Current.StatBuildPlan;

            if (plan == null || plan.Count == 0)
            {
                ActiveStatGoalText = unspentPoints > 0 ? $"{unspentPoints} pts (No plan)" : "Completed";
                return;
            }

            bool foundActiveGoal = false;

            for (int i = 0; i < plan.Count; i++)
            {
                var goal = plan[i];
                if (goal == null || goal.Target <= 0) continue;
                if (!goal.TryGetStatIndex(out int statIndex, out PlayerStat playerStat)) continue;

                int currentStat = playerState.GetData(playerStat);
                int targetCap = Math.Min(goal.Target, 99);

                if (currentStat >= targetCap)
                {
                    // Goal already satisfied at or above target
                    continue;
                }

                foundActiveGoal = true;

                if (unspentPoints <= 0)
                {
                    int nextCost = 2 + (currentStat - 1) / 10;
                    ActiveStatGoalText = $"{goal.Stat.ToUpperInvariant()} -> {goal.Target} (Cur: {currentStat}, Need: {nextCost} pts)";
                    break;
                }

                // Calculate bulk affordable increments toward targetCap
                int tempStat = currentStat;
                int pointsRemaining = unspentPoints;
                int pointsToAdd = 0;

                while (tempStat < targetCap)
                {
                    int cost = 2 + (tempStat - 1) / 10;
                    if (pointsRemaining < cost)
                        break;

                    pointsRemaining -= cost;
                    tempStat++;
                    pointsToAdd++;
                }

                if (pointsToAdd > 0)
                {
                    int[] adjustments = new int[6];
                    adjustments[statIndex] = pointsToAdd;

                    netManager.SendApplyStatPoints(adjustments);
                    BotEngine.Instance?.LogEvent($"[Progression] Auto-Allocated +{pointsToAdd} {goal.Stat.ToUpperInvariant()} ({currentStat} -> {currentStat + pointsToAdd} / Target: {goal.Target}, Remaining Stat Points: {pointsRemaining}).");

                    ActiveStatGoalText = $"{goal.Stat.ToUpperInvariant()} -> {goal.Target} (Cur: {currentStat + pointsToAdd})";
                }
                else
                {
                    int nextCost = 2 + (currentStat - 1) / 10;
                    ActiveStatGoalText = $"{goal.Stat.ToUpperInvariant()} -> {goal.Target} (Cur: {currentStat}, Need: {nextCost} pts, Have: {unspentPoints})";
                }

                // Strict sequential order: do not advance to subsequent goals until this one is satisfied
                break;
            }

            if (!foundActiveGoal)
            {
                ActiveStatGoalText = unspentPoints > 0 ? $"{unspentPoints} pts (Build Finished)" : "Build Finished";
            }
        }

        private void ProcessSkillAllocation(PlayerState playerState, NetworkManager netManager, float now)
        {
            int unspentSkills = playerState.SkillPoints;
            var plan = BotConfigManager.Current.SkillBuildPlan;

            if (plan == null || plan.Count == 0)
            {
                ActiveSkillGoalText = unspentSkills > 0 ? $"{unspentSkills} pts (No plan)" : "Completed";
                return;
            }

            bool foundActiveGoal = false;

            for (int i = 0; i < plan.Count; i++)
            {
                var goal = plan[i];
                if (goal == null || goal.Target <= 0) continue;
                if (!goal.TryGetSkill(out CharacterSkill skillEnum) || skillEnum == CharacterSkill.None) continue;

                int currentLvl = 0;
                if (playerState.KnownSkills != null && playerState.KnownSkills.TryGetValue(skillEnum, out int lvl))
                {
                    currentLvl = lvl;
                }

                if (currentLvl >= goal.Target)
                {
                    // Goal satisfied
                    continue;
                }

                foundActiveGoal = true;

                if (unspentSkills <= 0)
                {
                    ActiveSkillGoalText = $"{goal.Skill} -> {goal.Target} (Cur: {currentLvl}/{goal.Target})";
                    break;
                }

                // Single-step application with cadence to allow server synchronization
                if (now - lastSkillActionTime >= SkillActionCadence)
                {
                    lastSkillActionTime = now;
                    netManager.SendApplySkillPoint(skillEnum);
                    BotEngine.Instance?.LogEvent($"[Progression] Auto-Allocated Skill Point to '{goal.Skill}' (Level: {currentLvl} -> {currentLvl + 1} / Target: {goal.Target}, Remaining Skill Points: {unspentSkills - 1}).");
                }

                ActiveSkillGoalText = $"{goal.Skill} -> {goal.Target} (Cur: {currentLvl}/{goal.Target})";

                // Strict sequential order (Option A): wait until this skill reaches its target
                break;
            }

            if (!foundActiveGoal)
            {
                ActiveSkillGoalText = unspentSkills > 0 ? $"{unspentSkills} pts (Build Finished)" : "Build Finished";
            }
        }
    }
}
