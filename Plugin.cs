using BepInEx;
using BepInEx.Logging;
using ExtraLives.Patches;
using ExtraLives.Helpers;
using ExtraLives.Features;

namespace ExtraLives
{
    [BepInPlugin("com.abcde.extralives", "Extra Lives", "4.1.0")]
    public class Plugin : BaseUnityPlugin
    {
        public static ManualLogSource LogSource;
        public static int CurrentLives;
        public static bool shownDeathNotification = false;
        public static bool shownFikaReviveNotification = false;
        public static bool GaveUp = false;

        private void Awake()
        {
          
            // save the Logger to variable so we can use it elsewhere in the project
            LogSource = Logger;
            LogSource.LogInfo("Extra Lives plugin loaded!");
            Settings.Init(Config);

            // Enable patches
            new DeathPatch().Enable();
            new RevivalFeatures().Enable();
            new RaidStartPatch().Enable();
        }
        
    }
}
