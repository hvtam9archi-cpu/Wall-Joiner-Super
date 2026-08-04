using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Json;
using ProWallTools;

namespace ProWallTools.Tests
{
    internal static class Program
    {
        private static readonly List<string> Failures = new List<string>();

        private static int Main()
        {
            Verify("Round positive", NumericGeometry.RoundToStep(7.5, 5), 10);
            Verify("Round negative", NumericGeometry.RoundToStep(-7.5, 5), -10);
            Verify("Round below midpoint", NumericGeometry.RoundToStep(7.49, 5), 5);
            Verify("Round decimal step", NumericGeometry.RoundToStep(1.24, 0.5), 1);
            Verify("Nearly equal true", NumericGeometry.NearlyEqual(1, 1.0001, 0.001));
            Verify("Nearly equal false", !NumericGeometry.NearlyEqual(1, 1.01, 0.001));

            var defaults = new WallSettings();
            Verify("Default settings valid", defaults.Validate().Count == 0);
            Verify("Default removes safe originals", !defaults.KeepOriginals);
            Verify("Default strict mode enabled", defaults.StrictMode);

            WallSettings clone = defaults.Clone();
            clone.WallLayer = "CHANGED";
            Verify("Clone is independent", defaults.WallLayer != clone.WallLayer);
            Verify("Settings JSON round-trip", RoundTrip(defaults).FinishOffsetMode == FinishOffsetMode.Outside);

            var invalid = new WallSettings
            {
                GapTolerance = double.NaN,
                VertexTolerance = 0,
                FinishOffset = -1,
                SnapRadius = double.PositiveInfinity,
                SnapStep = 0,
                WallLayer = " ",
                FinishLayer = null,
                FinishOffsetMode = (FinishOffsetMode)99
            };
            Verify("Invalid settings rejected", invalid.Validate().Count == 8);
            VerifyThrows<ArgumentOutOfRangeException>(
                "Zero grid step rejected",
                () => NumericGeometry.RoundToStep(10, 0));

            if (Failures.Count == 0)
            {
                Console.WriteLine("Wall Joiner core checks passed.");
                return 0;
            }

            foreach (string failure in Failures)
            {
                Console.Error.WriteLine("FAILED: " + failure);
            }
            return 1;
        }

        private static void Verify(string name, bool condition)
        {
            if (!condition) Failures.Add(name);
        }

        private static void Verify(string name, double actual, double expected)
        {
            if (!NumericGeometry.NearlyEqual(actual, expected, 1e-9))
            {
                Failures.Add($"{name}; expected {expected}, actual {actual}");
            }
        }

        private static void VerifyThrows<TException>(string name, Action action)
            where TException : Exception
        {
            try
            {
                action();
                Failures.Add(name + "; no exception was thrown");
            }
            catch (TException)
            {
                // Expected.
            }
        }

        private static WallSettings RoundTrip(WallSettings settings)
        {
            var serializer = new DataContractJsonSerializer(typeof(WallSettings));
            using (var stream = new MemoryStream())
            {
                serializer.WriteObject(stream, settings);
                stream.Position = 0;
                return (WallSettings)serializer.ReadObject(stream);
            }
        }
    }
}
