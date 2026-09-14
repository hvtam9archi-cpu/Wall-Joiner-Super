namespace ProWallTools.Geometry

open System
open System.Collections.Generic

module private LoopSimplifierInternal =
    let inline sqr value = value * value

    let distanceSquared x1 y1 x2 y2 =
        sqr (x2 - x1) + sqr (y2 - y1)

    let canMergeStraight (first: SegmentDto) (second: SegmentDto) tolerance =
        if abs first.Bulge > 1e-12 || abs second.Bulge > 1e-12 then
            false
        elif distanceSquared first.EndX first.EndY second.StartX second.StartY > tolerance * tolerance then
            false
        else
            let ax = first.EndX - first.StartX
            let ay = first.EndY - first.StartY
            let bx = second.EndX - second.StartX
            let by = second.EndY - second.StartY
            let firstLength = sqrt (ax * ax + ay * ay)
            let secondLength = sqrt (bx * bx + by * by)
            if firstLength <= tolerance || secondLength <= tolerance then
                false
            else
                let cross = abs (ax * by - ay * bx)
                let scaledTolerance = tolerance * max firstLength secondLength
                let dot = ax * bx + ay * by
                cross <= scaledTolerance && dot > 0.0

    let mergeStraight (first: SegmentDto) (second: SegmentDto) =
        {
            StartX = first.StartX
            StartY = first.StartY
            EndX = second.EndX
            EndY = second.EndY
            Bulge = 0.0
            SourceIndex = first.SourceIndex
        }

    let simplifyLoopSegments (segments: SegmentDto array) tolerance =
        if Double.IsNaN tolerance || Double.IsInfinity tolerance || tolerance <= 0.0 then
            invalidArg "tolerance" "Tolerance must be finite and greater than zero."

        if isNull segments || segments.Length <= 2 then
            if isNull segments then [||] else Array.copy segments
        else
            let working = ResizeArray<SegmentDto>(segments)
            let mutable changed = true

            while changed && working.Count > 2 do
                changed <- false
                let mutable index = 0

                while not changed && index < working.Count do
                    let nextIndex = (index + 1) % working.Count
                    let first = working[index]
                    let second = working[nextIndex]

                    if canMergeStraight first second tolerance then
                        let merged = mergeStraight first second
                        if nextIndex = 0 then
                            working[index] <- merged
                            working.RemoveAt(0)
                        else
                            working[index] <- merged
                            working.RemoveAt(nextIndex)
                        changed <- true
                    else
                        index <- index + 1

            working.ToArray()

[<AbstractClass; Sealed>]
type GeometrySimplifier private () =
    static member SimplifyLoopSegments(segments: SegmentDto array, tolerance: float) =
        LoopSimplifierInternal.simplifyLoopSegments segments tolerance
