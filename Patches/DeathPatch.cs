using EFT;
using EFT.HealthSystem;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Linq;
using System.Reflection;
using EFT.Communications;
using ExtraLives.Features;
using ExtraLives.Helpers;
using UnityEngine;

namespace ExtraLives.Patches
{
    internal class DeathPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(ActiveHealthController), nameof(ActiveHealthController.Kill));
        }

        // Resolved once - Kill fires for every bot death, so the reflection
        // lookup must not repeat per call.
        private static readonly FieldInfo PlayerField =
            AccessTools.Field(typeof(ActiveHealthController), "Player");

        [PatchPrefix]
        static bool Prefix(ActiveHealthController __instance, EDamageType damageType)
        {
            try
            {
                if (PlayerField == null) return true;

                Player player = PlayerField.GetValue(__instance) as Player;
                if (player == null) return true;

                // Only our own death matters - every bot death lands here too.
                if (!player.IsYourPlayer || player.IsAI) return true;

                // Gave up?
                if (Plugin.GaveUp) return true;

                string playerId = player.ProfileId;

                // Already downed - a second damage source hitting Kill the same
                // frame would re-run HideDownedWeapon mid-animation and corrupt
                // the hands controller. Block the kill, skip re-entering critical.
                // Checked BEFORE the Fika test: once Extra Lives owns this death,
                // nothing else may finish the player off underneath us.
                if (RevivalFeatures.IsPlayerInCriticalState(playerId))
                    return false;

                // Fika wins whenever it actually intercepts this death (it downs the
                // player so a teammate can revive them). We yield rather than race it,
                // because HarmonyX runs every prefix unconditionally - returning false
                // here would NOT stop Fika's prefix from also firing. When Fika will
                // not down us (solo, teammates all dead, maxRevives used up, or a
                // headshot/grenade Fika treats as instant kill) we keep our own flow.
                if (FikaReviveDetector.WillFikaDownPlayer(player))
                {
                    Plugin.LogSource.LogInfo("Fika is handling this death (downed state); Extra Lives is yielding.");
                    return true;
                }

                var hc = player.ActiveHealthController;
                var headHealth = hc.GetBodyPartHealth(EBodyPart.Head, false);

                if (Settings.REQUIRE_HEAD_HEALTH.Value && headHealth.Current <= 0)
                {
                    if (!Plugin.shownDeathNotification)
                    {
                        NotificationManager.DisplayMessageNotification(
                            $"你死了！头部生命值为零，无法复活。",
                            ENotificationDurationType.Long,
                            ENotificationIconType.Default,
                            Color.red);
                        Plugin.shownDeathNotification = true;
                    }
                    return true;
                }

                // Check if player is buffed
                // REQUIRE_BUFF_TYPE stores a Chinese display name (e.g. "肾上腺素") so the F12
                // native dropdown shows Chinese. Convert it back to the English buff ID that
                // ActiveBuffsNames() returns before comparing.
                string requiredBuffId = Settings.GetBuffIdFromDisplayName(Settings.REQUIRE_BUFF_TYPE.Value);
                if (requiredBuffId != "None" && !__instance.ActiveBuffsNames().Contains(requiredBuffId))
                {
                    // The required buff is not active so die
                    if (!Plugin.shownDeathNotification)
                    {
                        NotificationManager.DisplayMessageNotification(
                            $"你死了！你倒地前没有使用{Settings.REQUIRE_BUFF_TYPE.Value}，无法进行复活。",
                            ENotificationDurationType.Long,
                            ENotificationIconType.Default,
                            Color.red);
                        Plugin.shownDeathNotification = true;
                    }

                    Plugin.LogSource.LogInfo($"Player {playerId} ({string.Join(",", __instance.ActiveBuffsNames())}) was not buffed with {Settings.REQUIRE_BUFF_TYPE.Value} and has died");
                    return true;
                }

                // Check if player has stim?
                if (Settings.REQUIRE_STIM.Value != "None")
                    if (!RevivalFeatures.hasReviveItem(player))
                    {
                        // The required buff is not active so die
                        if (!Plugin.shownDeathNotification)
                        {
                            NotificationManager.DisplayMessageNotification(
                                $"你死了！你没有携带{Settings.REQUIRE_STIM.Value}，无法进行复活。",
                                ENotificationDurationType.Long,
                                ENotificationIconType.Default,
                                Color.red);
                            Plugin.shownDeathNotification = true;
                        }

                        Plugin.LogSource.LogInfo($"Player {playerId} did not have {Settings.REQUIRE_STIM.Value} stim and has died");
                        return true;
                    }

                // Check if player has remaining lives
                if (Plugin.CurrentLives > 0 || Settings.TESTING.Value)
                {
                    Plugin.LogSource.LogInfo("DEATH PREVENTED: Setting player to critical state instead of death");

                    // Remember what downed us - GiveUp replays it so the profile's
                    // LethalDamage shows the real cause of death.
                    RevivalFeatures.RecordLethalDamage(playerId, damageType);

                    // Set the player in critical state for the revival system
                    RevivalFeatures.SetPlayerCriticalState(player, true);

                    Plugin.CurrentLives--;

                    Plugin.LogSource.LogInfo($"DEATH PREVENTED: Extra lives left {Plugin.CurrentLives}");

                    // Block the kill completely
                    return false;
                }
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"Error in Death prevention patch: {ex.Message}");
            }

            return true;
        }
    }
}
