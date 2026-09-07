using BepInEx.Configuration;
using System.Collections.Generic;
using UnityEngine;

namespace ExtraLives.Helpers
{
    internal class Settings
    {
        public static ConfigEntry<float> REVIVAL_DURATION;
        public static ConfigEntry<KeyCode> REVIVAL_KEY;
        public static ConfigEntry<KeyCode> GIVE_UP_KEY;
        public static ConfigEntry<bool> RESTORE_DESTROYED_BODY_PARTS;
        public static ConfigEntry<bool> TESTING;
        public static ConfigEntry<bool> REQUIRE_HEAD_HEALTH;
        public static ConfigEntry<int> PLAYER_LIVES;
        public static ConfigEntry<int> RESTORE_DESTROYED_BODY_PARTS_HEALING;
        public static ConfigEntry<string> REQUIRE_BUFF_TYPE;
        public static ConfigEntry<string> REQUIRE_STIM;
        public static ConfigEntry<bool> CLEAR_DEBUFFS;
        public static ConfigEntry<bool> RESTORE_HYDRATION_ENERGY;
        public static ConfigEntry<int> RESTORE_HYDRATION_ENERGY_HEALING;

        public static void Init(ConfigFile config)
        {
            PLAYER_LIVES = config.Bind(
                "General",
                "Extra Lives",
                1,
               "How many revives per raid."
            );
            REVIVAL_DURATION = config.Bind(
                "General",
                "Invulnerability Duration (s)",
                10f,
               "How long you are invulnerable for after revive."
            );
            REVIVAL_KEY = config.Bind(
                "General",
                "Revival Key",
                KeyCode.F5
            );
            GIVE_UP_KEY = config.Bind(
                 "General",
                 "Give Up Key",
                 KeyCode.F9
             );


            REQUIRE_BUFF_TYPE = config.Bind(
                "Revive Conditions",
                "Require Active Buff",
                "无",
                new ConfigDescription(
                    "Select required buff to be active for revive.",
                   new AcceptableValueList<string>("无", "肾上腺素", "Propital",
                    "SJ1 TGLabs", "SJ6 TGLabs", "Zagustin", "eTG-c",
                    "2A2-(b-TG)", "3-(b-TG)", "AHF1-M", "xTG-12",
                    "L1", "M.U.L.E", "米屈肼", "Obdolbos", "Obdolbos2",
                    "P22", "PNB", "人造血", "SJ12", "曲马多")
                )
            );

            REQUIRE_STIM = config.Bind(
                "Revive Conditions",
                "Require Stim",
                "None",
                new ConfigDescription(
                    "Select stim that will be used for revive. 'Any' uses the cheapest stim you carry first.",
                    new AcceptableValueList<string>("None", "Any", "肾上腺素", "Propital",
                    "SJ1 TGLabs", "SJ6 TGLabs", "Zagustin", "eTG-c", "2A2-(b-TG)", "3-(b-TG)",
                    "AHF1-M", "xTG-12", "L1", "M.U.L.E", "米屈肼", "Obdolbos", "Obdolbos2",
                    "P22", "PNB", "人造血", "SJ12", "曲马多")
                )
            );

            REQUIRE_HEAD_HEALTH = config.Bind(
                "Revive Conditions",
                "Require Head Health > 0",
                false,
                "if your head health is 0, revives will no longer work."
            );


            RESTORE_DESTROYED_BODY_PARTS = config.Bind(
                "On Revive",
                "Restore destroyed body parts",
                true,
                new ConfigDescription(
                    "Blackened body parts are restored%",
                    null,
                    new ConfigurationManagerAttributes { Order = 50 }
                )
            );

            RESTORE_DESTROYED_BODY_PARTS_HEALING = config.Bind(
                "On Revive",
                "Restore destroyed body parts healing",
                25,
                 new ConfigDescription(
                    "Healing amount %",
                    new AcceptableValueRange<int>(1, 100),
                    new ConfigurationManagerAttributes { Order = 40 }
                )
            );

            CLEAR_DEBUFFS = config.Bind(
                "On Revive",
                "Clear Debuffs",
                true,
                new ConfigDescription(
                    "Remove bleeding, fracture, intoxication, dehydration and other negative effects on revive.",
                    null,
                    new ConfigurationManagerAttributes { Order = 60 }
                )
            );

            RESTORE_HYDRATION_ENERGY = config.Bind(
                "On Revive",
                "Restore hydration and energy",
                true,
                new ConfigDescription(
                    "Restore thirst (hydration) and hunger (energy) up to the configured percentage on revive.",
                    null,
                    new ConfigurationManagerAttributes { Order = 30 }
                )
            );

            RESTORE_HYDRATION_ENERGY_HEALING = config.Bind(
                "On Revive",
                "Restore hydration and energy healing",
                50,
                new ConfigDescription(
                    "Restore amount % (applied to both hydration and energy)",
                    new AcceptableValueRange<int>(1, 100),
                    new ConfigurationManagerAttributes { Order = 20 }
                )
            );

            TESTING = config.Bind(
                "Development",
                "Test Mode",
                false,
                new ConfigDescription("", null, new ConfigurationManagerAttributes { IsAdvanced = true })
            );
        }

        // Converts the stored Chinese buff name (what the F12 dropdown shows, e.g. "肾上腺素")
        // back to the English enum ID that ActiveBuffsNames() returns (e.g. "BuffsAdrenaline"),
        // so the death-check in DeathPatch can compare against the active-buff names.
        public static string GetBuffIdFromDisplayName(string displayName)
        {
            if (string.IsNullOrEmpty(displayName))
                return displayName;
            return DisplayNameToBuffId.TryGetValue(displayName, out var id) ? id : displayName;
        }

        private static readonly Dictionary<string, string> DisplayNameToBuffId = new Dictionary<string, string>
        {
            { "无", "None" },
            { "肾上腺素", "BuffsAdrenaline" },
            { "Propital", "BuffsPropital" },
            { "SJ1 TGLabs", "BuffsSJ1TGLabs" },
            { "SJ6 TGLabs", "BuffsSJ6TGLabs" },
            { "Zagustin", "BuffsZagustin" },
            { "eTG-c", "BuffseTGchange" },
            { "2A2-(b-TG)", "Buffs_2A2bTG" },
            { "3-(b-TG)", "Buffs_3bTG" },
            { "AHF1-M", "Buffs_AHF1M" },
            { "xTG-12", "Buffs_Antidote" },
            { "L1", "Buffs_L1" },
            { "M.U.L.E", "Buffs_MULE" },
            { "米屈肼", "Buffs_Meldonin" },
            { "Obdolbos", "Buffs_Obdolbos" },
            { "Obdolbos2", "Buffs_Obdolbos2" },
            { "P22", "Buffs_P22" },
            { "PNB", "Buffs_PNB" },
            { "人造血", "Buffs_Perfotoran" },
            { "SJ12", "Buffs_SJ12_TGLabs" },
            { "曲马多", "Buffs_Trimadol" },
        };
    }
}
