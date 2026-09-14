using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Json;
using ProWallTools;
using ProWallTools.Geometry;

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

            VerifyCleanPolyline();
            VerifyClosedLoopStitching();
            VerifyAmbiguousTopologyRejected();
            VerifySafeBridgeSelection();
            VerifyConsensusSnap();

            if (Failures.Count == 0)
            {
                Console.WriteLine("Wall Joiner core and F# geometry checks passed.");
                return 0;
            }

            foreach (string failure in Failures)
            {
                Console.Error.WriteLine("FAILED: " + failure);
            }
            return 1;
        }

        private static void VerifyCleanPolyline()
        {
            var source = new[]
            {
                Vertex(0, 0, 0.1, 1, 2),
                Vertex(0, 0, 0.25, 3, 4),
                Vertex(10, 0, 0, 5, 6),
                Vertex(10, 10, 0, 7, 8),
                Vertex(0, 0, 0.9, 9, 10)
            };

            VertexDto[] cleaned = GeometryKernel.CleanVertices(source, true, 1e-9);
            Verify("Clean poly removes duplicate run and closing duplicate", cleaned.Length == 3);
            Verify("Clean poly keeps outgoing bulge from last duplicate", cleaned.Length > 0 && Nearly(cleaned[0].Bulge, 0.25));
            Verify("Clean poly keeps start width", cleaned.Length > 0 && Nearly(cleaned[0].StartWidth, 3));
            Verify("Clean poly keeps end width", cleaned.Length > 0 && Nearly(cleaned[0].EndWidth, 4));

            VertexDto[] cleanedTwice = GeometryKernel.CleanVertices(cleaned, true, 1e-9);
            Verify("Clean poly is idempotent", SameVertices(cleaned, cleanedTwice));
        }

        private static void VerifyClosedLoopStitching()
        {
            var segments = new[]
            {
                Segment(0, 0, 10, 0, 0, 0),
                Segment(10, 10, 10, 0, 0.5, 1),
                Segment(0, 10, 10, 10, 0, 2),
                Segment(0, 0, 0, 10, 0, 3)
            };

            LoopDto[] loops = GeometryKernel.StitchClosedLoops(segments, 1e-9);
            Verify("Topology stitches one closed loop", loops.Length == 1);
            Verify("Topology loop contains all segments", loops.Length == 1 && loops[0].Segments.Length == 4);

            if (loops.Length == 1)
            {
                bool reversedArcFound = false;
                foreach (SegmentDto segment in loops[0].Segments)
                {
                    if (segment.SourceIndex == 1 && Nearly(segment.Bulge, -0.5))
                    {
                        reversedArcFound = true;
                    }
                }
                Verify("Reversing an arc segment flips bulge", reversedArcFound);
            }
        }

        private static void VerifyAmbiguousTopologyRejected()
        {
            var segments = new[]
            {
                Segment(0, 0, 10, 0, 0, 0),
                Segment(10, 0, 20, 0, 0, 1),
                Segment(10, 0, 10, 10, 0, 2)
            };

            LoopDto[] loops = GeometryKernel.StitchClosedLoops(segments, 1e-9);
            Verify("T-junction is not silently converted to a loop", loops.Length == 0);
        }

        private static void VerifySafeBridgeSelection()
        {
            var segments = new[]
            {
                Segment(0, 0, 10, 0, 0, 0),
                Segment(10, 5, 0, 5, 0, 1)
            };

            BridgeDto[] bridges = GeometryKernel.FindBridges(segments, 6, 1e-6);
            Verify("Safe bridge solver closes both dangling sides", bridges.Length == 2);
            if (bridges.Length == 2)
            {
                Verify("Safe bridge length is local", Nearly(bridges[0].Distance, 5) && Nearly(bridges[1].Distance, 5));
            }
        }

        private static void VerifyConsensusSnap()
        {
            var selected = new[]
            {
                Point(0, 0),
                Point(10, 0),
                Point(0, 10)
            };
            var external = new[]
            {
                Point(2, 0),
                Point(12, 0),
                Point(2, 10),
                Point(100, 100)
            };

            SnapVectorDto snap = GeometryKernel.TryFindSnapVector(
                selected,
                external,
                3,
                1e-6,
                0.05);
            Verify("Consensus snap found", snap.Found);
            Verify("Consensus snap uses all agreeing vertices", snap.SupportCount == 3);
            Verify("Consensus snap X", snap.X, 2);
            Verify("Consensus snap Y", snap.Y, 0);

            var oneMatch = new[] { Point(2, 0) };
            SnapVectorDto rejected = GeometryKernel.TryFindSnapVector(
                selected,
                oneMatch,
                3,
                1e-6,
                0.05);
            Verify("Single accidental match is rejected for multi-vertex selection", !rejected.Found);
        }

        private static VertexDto Vertex(
            double x,
            double y,
            double bulge,
            double startWidth,
            double endWidth)
        {
            return new VertexDto
            {
                X = x,
                Y = y,
                Bulge = bulge,
                StartWidth = startWidth,
                EndWidth = endWidth
            };
        }

        private static SegmentDto Segment(
            double startX,
            double startY,
            double endX,
            double endY,
            double bulge,
            int sourceIndex)
        {
            return new SegmentDto
            {
                StartX = startX,
                StartY = startY,
                EndX = endX,
                EndY = endY,
                Bulge = bulge,
                SourceIndex = sourceIndex
            };
        }

        private static PointDto Point(double x, double y)
        {
            return new PointDto { X = x, Y = y };
        }

        private static bool SameVertices(VertexDto[] first, VertexDto[] second)
        {
            if (first == null || second == null || first.Length != second.Length) return false;
            for (int i = 0; i < first.Length; i++)
            {
                if (!Nearly(first[i].X, second[i].X) ||
                    !Nearly(first[i].Y, second[i].Y) ||
                    !Nearly(first[i].Bulge, second[i].Bulge) ||
                    !Nearly(first[i].StartWidth, second[i].StartWidth) ||
                    !Nearly(first[i].EndWidth, second[i].EndWidth))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool Nearly(double first, double second)
        {
            return Math.Abs(first - second) <= 1e-9;
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
