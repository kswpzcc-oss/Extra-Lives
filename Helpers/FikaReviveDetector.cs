using EFT;
using EFT.HealthSystem;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace ExtraLives.Helpers
{
    /// <summary>
    /// 在不引用 Fika.Core.dll 的前提下(软依赖)判断 Fika 是否会接管某一次死亡。
    ///
    /// Fika 的 Fika.Core.Main.Patches.Revival.ClientHealthController_Kill_Patch 也 patch 了
    /// ActiveHealthController.Kill,条件是:
    ///     Player.IsYourPlayer &amp;&amp; ReviveEnabled &amp;&amp; CanBeDowned &amp;&amp; !CheckIfDamageShouldInstantKill()
    /// 命中时它把 IsAlive 置 false 并进入倒地/流血流程;未命中则放行原版 Kill(玩家真的死)。
    ///
    /// 为什么必须"精确复刻"而不是只看开关:
    ///   HarmonyX 与 Harmony 2 的前缀语义不同 —— HarmonyX 下所有 prefix 必定执行,返回 false 只是把
    ///   __runOriginal 置 false 从而跳过原方法体,并不能阻止 Fika 的 prefix 运行。所以 ExtraLives
    ///   无法"抢先",只能判断 Fika 会不会动手:会 → 让位;不会 → 按自己的规则处理。
    ///
    /// 另外 CanBeDowned 已经把"队里至少还有 2 个活着的真人、没流血死、没超 maxRevives"都封装好了,
    /// 所以单人战局 / 队友全死 / Fika 次数用尽时这里会返回 false,ExtraLives 照常生效。
    /// </summary>
    internal static class FikaReviveDetector
    {
        private const string FikaHealthControllerTypeName = "Fika.Core.Main.ClientClasses.ClientHealthController";

        // 每个 ActiveHealthController 子类型只解析一次反射信息。
        // Kill 对地图上每个 bot 的死亡都会触发,解析不能放在调用路径里重复做。
        private static readonly Dictionary<Type, Probe> Probes = new Dictionary<Type, Probe>();

        private sealed class Probe
        {
            // 该类型看起来是 Fika 的 ClientHealthController。
            public bool IsFika;

            // 三个成员全部解析成功。false 表示 Fika 在但版本对不上。
            public bool Resolved;

            public PropertyInfo ReviveEnabled;
            public PropertyInfo CanBeDowned;
            public MethodInfo InstantKill;
        }

        private static Probe GetProbe(Type type)
        {
            if (Probes.TryGetValue(type, out Probe cached))
                return cached;

            var probe = new Probe();

            // 类型名对不上时再用 ReviveEnabled 属性兜底,以防 Fika 改了类名但保留了成员。
            probe.IsFika = type.FullName == FikaHealthControllerTypeName
                           || type.GetProperty("ReviveEnabled", BindingFlags.Instance | BindingFlags.Public) != null;

            if (probe.IsFika)
            {
                const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public;

                probe.ReviveEnabled = type.GetProperty("ReviveEnabled", Flags);
                probe.CanBeDowned = type.GetProperty("CanBeDowned", Flags);
                probe.InstantKill = type.GetMethod("CheckIfDamageShouldInstantKill", Flags, null, Type.EmptyTypes, null);

                probe.Resolved = probe.ReviveEnabled != null
                                 && probe.CanBeDowned != null
                                 && probe.InstantKill != null
                                 && probe.ReviveEnabled.PropertyType == typeof(bool)
                                 && probe.CanBeDowned.PropertyType == typeof(bool)
                                 && probe.InstantKill.ReturnType == typeof(bool);

                if (!probe.Resolved)
                {
                    Plugin.LogSource.LogError(
                        $"Fika health controller '{type.FullName}' was found but its revive members could not be resolved " +
                        "(ReviveEnabled / CanBeDowned / CheckIfDamageShouldInstantKill). " +
                        "This Fika version is probably unsupported; Extra Lives will keep yielding to Fika.");
                }
            }

            Probes[type] = probe;
            return probe;
        }

        /// <summary>
        /// true  = Fika 会拦截这次死亡(把玩家击倒进入流血流程),ExtraLives 必须让位。
        /// false = Fika 不接管,ExtraLives 可以按自己的规则处理这次死亡。
        /// </summary>
        public static bool WillFikaDownPlayer(Player player)
        {
            if (player == null)
                return false;

            try
            {
                ActiveHealthController healthController = player.ActiveHealthController;
                if (healthController == null)
                    return false;

                Probe probe = GetProbe(healthController.GetType());
                if (!probe.IsFika)
                    return false;

                // Fika 在,但成员解析不出来:无法判断,按"会接管"处理,避免两套系统同时动手。
                if (!probe.Resolved)
                    return true;

                if (!(bool)probe.ReviveEnabled.GetValue(healthController))
                    return false;

                if (!(bool)probe.CanBeDowned.GetValue(healthController))
                    return false;

                // Fika 判定爆头/手雷为即死不进倒地时,这次死亡交还给 ExtraLives。
                return !(bool)probe.InstantKill.Invoke(healthController, null);
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogWarning($"Could not check Fika revive status: {ex.Message}");
                return false;
            }
        }
    }
}
