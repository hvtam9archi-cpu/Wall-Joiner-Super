using System;
using System.Runtime.CompilerServices;
using ProWallTools;

namespace ProWallTools.Tests
{
    internal static class SettingsSafetyChecks
    {
        [ModuleInitializer]
        internal static void VerifyNonStrictModePreservesOriginals()
        {
            var settings = new WallSettings
            {
                StrictMode = false,
                KeepOriginals = false
            };

            if (!settings.KeepOriginals)
            {
                throw new InvalidOperationException(
                    "Non-strict mode must preserve source entities to avoid partial-processing data loss.");
            }
        }
    }
}
