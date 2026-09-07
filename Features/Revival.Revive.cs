using Diz.LanguageExtensions;
using EFT;
using EFT.Ballistics;
using EFT.Communications;
using EFT.HealthSystem;
using EFT.InventoryLogic;
using ExtraLives.Helpers;
using System;
using System.Linq;
using UnityEngine;
using System.Reflection;

namespace ExtraLives.Features
{
    // Manual revive, give up, healing, and the revive-item lookup/consume logic.
    internal partial class RevivalFeatures
    {
        public static bool TryPerformManualRevival(Player player)
        {
            if (player == null) return false;

            string playerId = player.ProfileId;

            // Clear critical BEFORE standing so the per-frame downed tick can't
            // re-slam prone this same frame after we stand the player up.
            SetPlayerCriticalState(player, false);
            _lastRevivalTimesByPlayer[playerId] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            if (Settings.REQUIRE_STIM.Value != "None")
                ConsumeReviveItem(player);

            HealPlayer(player);

            StartInvulnerability(player);

            CancelDownedMovementBlock(player);

            StandPlayerUp(player);

            player.Say(EPhraseTrigger.OnMutter, false, 2f, ETagStatus.Combat, 100, true);

            // Show successful revival notification
            NotificationManager.DisplayMessageNotification(
                $"无敌状态开始，持续{Settings.REVIVAL_DURATION.Value}秒！剩余{Plugin.CurrentLives}次复活次数",
                ENotificationDurationType.Long,
                ENotificationIconType.Default,
                Color.green);

            Plugin.LogSource.LogInfo($"Manual revival performed for player {playerId}");
            return true;
        }

        public static bool GiveUp(Player player)
        {
            if (player == null) return false;

            string playerId = player.ProfileId;

            // Set alive first before applying effects
            SetPlayerCriticalState(player, false);
            _lastRevivalTimesByPlayer[playerId] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            Plugin.GaveUp = true;
            player.ActiveHealthController.IsAlive = true;
            RestoreDamageCoeff(player);

            // Kill player by applying fatal damage to head
            if (player.ActiveHealthController != null)
            {
                player
                .ActiveHealthController
                .ApplyDamage(EBodyPart.Head, player.ActiveHealthController.GetBodyPartHealth(EBodyPart.Head).Maximum + 100f, new DamageInfo { DamageType = GetLastLethalDamageType(playerId) });
            }

            // Show successful revival notification
            NotificationManager.DisplayMessageNotification(
                $"你放弃了复活机会！",
                ENotificationDurationType.Long,
                ENotificationIconType.Default,
                Color.red);

            Plugin.LogSource.LogInfo($"Player gave up {playerId}");
            return true;
        }

        public static void HealPlayer(Player player)
        {
            try
            {
                ActiveHealthController healthController = player.ActiveHealthController;
                if (healthController == null)
                {
                    Plugin.LogSource.LogError("Could not get ActiveHealthController");
                    return;
                }

                float healingRatio = Settings.RESTORE_DESTROYED_BODY_PARTS_HEALING.Value / 100f;

                // Resolve the (version-sensitive) internal sync methods/fields once instead of per body part.
                var syncDestroyedMethod = typeof(ActiveHealthController).GetMethod("NetworkSyncDestroyedBodyPart", BindingFlags.Instance | BindingFlags.NonPublic);
                var syncHealthMethod = typeof(ActiveHealthController).GetMethod("NetworkSyncBodyHealth", BindingFlags.Instance | BindingFlags.NonPublic);
                var restoredEventField = typeof(ActiveHealthController).GetField("BodyPartRestoredEvent", BindingFlags.Instance | BindingFlags.NonPublic);
                var currentAndMaxProp = typeof(HealthValue).GetProperty("CurrentAndMaximum");

                foreach (EBodyPart bodyPart in Enum.GetValues(typeof(EBodyPart)))
                {
                    if (bodyPart == EBodyPart.Common)
                        continue;

                    if (!healthController._bodyState.TryGetValue(bodyPart, out var bodyPartState))
                        continue;

                    // Strip the selected bad effects from this body part FIRST - controlled by the
                    // "Clear Debuffs" switch, independent of the "Restore destroyed body parts" switch.
                    if (Settings.CLEAR_DEBUFFS.Value)
                        RemoveSelectedDebuffs(healthController, bodyPart);

                    if (Settings.RESTORE_DESTROYED_BODY_PARTS.Value)
                    {
                        float maxHealth = bodyPartState.Health.Maximum;
                        float targetHealth = Mathf.Max(1f, Mathf.Ceil(maxHealth * healingRatio));
                        bool wasDestroyed = bodyPartState.IsDestroyed;

                        // Lift the destroyed flag before restoring (head/chest cannot go through the
                        // official RestoreBodyPart, so we handle every part the same way here).
                        if (wasDestroyed)
                        {
                            bodyPartState.IsDestroyed = false;
                        }

                        // Restore up to the configured ratio: blackened parts go 0 -> ratio,
                        // intact parts below ratio get lifted, intact parts at/above ratio keep their value.
                        float targetCurrent = Mathf.Max(bodyPartState.Health.Current, targetHealth);
                        bodyPartState.Health = new HealthValue(targetCurrent, maxHealth, 0f);

                        // Network sync so the server + local UI/cache reflect the change.
                        if (wasDestroyed)
                            syncDestroyedMethod?.Invoke(healthController, new object[] { bodyPart, EDamageType.Medicine });
                        syncHealthMethod?.Invoke(healthController, new object[] { bodyPart });

                        // Fire the restored event for EVERY healed part (head/chest/limbs/abdomen) so the
                        // movement/weapon/animation systems recompute and clear lingering states (cannot-run,
                        // weapon sway) that a plain RestoreBodyPart leaves behind for damaged legs/arms.
                        if (restoredEventField != null && currentAndMaxProp != null)
                        {
                            var restoredEvent = restoredEventField.GetValue(healthController) as Delegate;
                            if (restoredEvent != null)
                            {
                                object valueStruct = currentAndMaxProp.GetValue(bodyPartState.Health);
                                restoredEvent.DynamicInvoke(bodyPart, valueStruct);
                            }
                        }
                    }
                }

                // ======= Restore thirst (hydration) and hunger (energy) =======
                // Uses max(current, max*ratio) so values already above the configured ratio are
                // neither lowered nor altered. ChangeHydration/ChangeEnergy are relative (delta),
                // and since the delta is >= 0 here we never reduce an already-high value.
                if (Settings.RESTORE_HYDRATION_ENERGY.Value)
                {
                    float hydraRatio = Settings.RESTORE_HYDRATION_ENERGY_HEALING.Value / 100f;

                    float hydraTarget = Mathf.Max(healthController.Hydration.Current, healthController.Hydration.Maximum * hydraRatio);
                    float hydraDelta = hydraTarget - healthController.Hydration.Current;
                    if (hydraDelta > 0f)
                        healthController.ChangeHydration(hydraDelta);

                    float energyTarget = Mathf.Max(healthController.Energy.Current, healthController.Energy.Maximum * hydraRatio);
                    float energyDelta = energyTarget - healthController.Energy.Current;
                    if (energyDelta > 0f)
                        healthController.ChangeEnergy(energyDelta);
                }
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"Error applying revival effects: {ex.Message}");
            }
        }

        // The bad effects to strip on revive (per user selection). Matched by runtime type name so
        // we never have to reference the game's internal effect classes directly across assemblies.
        // PainKiller / Misfire / Encumbered / OverEncumbered are intentionally NOT included.
        private static readonly string[] DebuffTypeNamesToRemove = new[]
        {
            "Bleeding", "LightBleeding", "HeavyBleeding",
            "Fracture",
            "Contusion",
            "Stun",
            "Tremor",
            "Wound",
            "Intoxication", "LethalIntoxication",
            "Dehydration",
            "Exhaustion",
            "Frostbite",
            "Pain",
            "MusclePain", "MildMusclePain", "SevereMusclePain",
            "ZombieInfection",
        };

        private static void RemoveSelectedDebuffs(ActiveHealthController hc, EBodyPart bodyPart)
        {
            try
            {
                var effects = hc.FindActiveEffects<ActiveHealthController.Effect>(bodyPart);
                if (effects == null) return;

                foreach (var effect in effects.ToList())
                {
                    if (effect == null) continue;
                    if (DebuffTypeNamesToRemove.Contains(effect.GetType().Name, StringComparer.Ordinal))
                    {
                        effect.ForceRemove();
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"Error removing debuffs: {ex.Message}");
            }
        }

        // Stims ordered cheapest -> most expensive. Used when REQUIRE_STIM is "Any".
        private static readonly string[] StimsCheapestFirst = new[]
        {
            "肾上腺素", "曲马多", "P22", "AHF1-M", "xTG-12", "PNB", "M.U.L.E",
            "Zagustin", "Obdolbos", "Obdolbos2", "米屈肼", "人造血", "eTG-c",
            "L1", "2A2-(b-TG)", "SJ1 TGLabs", "SJ6 TGLabs", "SJ12", "3-(b-TG)", "Propital",
        };

        // Returns the template id of the revive item the player should use, or "" if none.
        // For "Any", picks the cheapest stim currently in inventory.
        private static string ResolveReviveItemId(Player player)
        {
            if (Settings.REQUIRE_STIM.Value == "Any")
            {
                var inRaidItems = player.Inventory.GetPlayerItems(EFT.InventoryLogic.EPlayerItems.Equipment);
                foreach (var stimName in StimsCheapestFirst)
                {
                    var id = FindItemId(stimName);
                    if (id == "") continue;
                    if (inRaidItems.Any(item => item.TemplateId == id))
                        return id;
                }
                return "";
            }

            return FindItemId(Settings.REQUIRE_STIM.Value);
        }

        public static bool hasReviveItem(Player player)
        {
            var reviveItemId = ResolveReviveItemId(player);

            if (reviveItemId == "") return false;

            var inRaidItems = player.Inventory.GetPlayerItems(EFT.InventoryLogic.EPlayerItems.Equipment);

            return inRaidItems.Any(item => item.TemplateId == reviveItemId);
        }

        private static void ConsumeReviveItem(Player player)
        {
            try
            {
                var reviveItemId = ResolveReviveItemId(player);

                if (reviveItemId == "") return;

                var inRaidItems = player.Inventory.GetPlayerItems(EFT.InventoryLogic.EPlayerItems.Equipment);
                var reviveItem = inRaidItems.FirstOrDefault(item => item.TemplateId == reviveItemId);

                if (reviveItem != null)
                {
                    Plugin.LogSource.LogInfo($"Consuming revive item: {FindItemName(reviveItemId)} ({reviveItemId})");

                    OperationResult<DiscardResult> result = ItemManipulator.Discard(reviveItem, player.InventoryController, false);
                    if (result.Failed)
                    {
                        Plugin.LogSource.LogError($"Error consuming item: {result.Error}");
                    }
                    else
                    {
                        Plugin.LogSource.LogInfo($"You have {CountReviveItemsInRaid(player, reviveItemId)} revive items left");
                    }
                }


            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"Error consuming item: {ex.Message}");
            }
        }

        public static int CountReviveItemsInRaid(Player player, string reviveItemId)
        {
            // Initialize a counter for items matching the reviveItemId
            int count = 0;

            // Retrieve all items in raid from the player's inventory
            var inRaidItems = player.Inventory.GetPlayerItems(EFT.InventoryLogic.EPlayerItems.Equipment);

            // Iterate over the items to count matching items
            foreach (var item in inRaidItems)
            {
                if (item.TemplateId == reviveItemId)
                {
                    count++;
                }
            }

            return count;
        }

        public static string FindItemId(string stimName)
        {
            return stimName switch
            {
                "肾上腺素" => "5c10c8fd86f7743d7d706df3",
                "Propital" => "5c0e530286f7747fa1419862",
                "SJ1 TGLabs" => "5c0e531286f7747fa54205c2",
                "SJ6 TGLabs" => "5c0e531d86f7747fa23f4d42",
                "Zagustin" => "5c0e533786f7747fa23f4d47",
                "eTG-c" => "5c0e534186f7747fa1419867",
                "2A2-(b-TG)" => "66507eabf5ddb0818b085b68",
                "3-(b-TG)" => "5ed515c8d380ab312177c0fa",
                "AHF1-M" => "5ed515f6915ec335206e4152",
                "xTG-12" => "5fca138c2a7b221b2852a5c6",
                "L1" => "5ed515e03a40a50460332579",
                "M.U.L.E" => "5ed51652f6c34d2cc26336a1",
                "米屈肼" => "5ed5160a87bb8443d10680b5",
                "Obdolbos" => "5ed5166ad380ab312177c100",
                "Obdolbos2" => "637b60c3b7afa97bfc3d7001",
                "P22" => "5ed515ece452db0eb56fc028",
                "PNB" => "637b6179104668754b72f8f5",
                "人造血" => "637b6251104668754b72f8f9",
                "SJ12" => "637b612fb7afa97bfc3d7005",
                "曲马多" => "637b620db7afa97bfc3d7009",
                _ => "",
            };

        }

        // Reverse of FindItemId: template id -> friendly stim name, or the id if unknown.
        public static string FindItemName(string templateId)
        {
            foreach (var stimName in StimsCheapestFirst)
            {
                if (FindItemId(stimName) == templateId)
                    return stimName;
            }
            return templateId;
        }
    }
}
