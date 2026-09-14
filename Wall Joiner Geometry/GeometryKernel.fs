namespace ProWallTools.Geometry

open System
open System.Collections.Generic

module private Internal =
    let inline sqr value = value * value

    let distanceSquared x1 y1 x2 y2 =
        sqr (x2 - x1) + sqr (y2 - y1)

    let distance x1 y1 x2 y2 =
        sqrt (distanceSquared x1 y1 x2 y2)

    let requireTolerance name value allowZero =
        if Double.IsNaN value || Double.IsInfinity value || (if allowZero then value < 0.0 else value <= 0.0) then
            invalidArg name "Tolerance must be finite and within the supported range."

    let cleanVertices (vertices: VertexDto array) closed tolerance =
        requireTolerance "tolerance" tolerance false
        if isNull vertices || vertices.Length = 0 then
            [||]
        else
            let toleranceSquared = tolerance * tolerance
            let cleaned = ResizeArray<VertexDto>()

            for vertex in vertices do
                if cleaned.Count = 0 then
                    cleaned.Add vertex
                else
                    let previous = cleaned[cleaned.Count - 1]
                    if distanceSquared previous.X previous.Y vertex.X vertex.Y <= toleranceSquared then
                        // Keep the last vertex in a duplicate run. Its bulge/width describe
                        // the next non-zero segment, matching the intent of Clean_poly.
                        cleaned[cleaned.Count - 1] <- vertex
                    else
                        cleaned.Add vertex

            if closed && cleaned.Count > 1 then
                let first = cleaned[0]
                let last = cleaned[cleaned.Count - 1]
                if distanceSquared first.X first.Y last.X last.Y <= toleranceSquared then
                    // For a closed polyline the final duplicate only represents a zero-length
                    // closing edge. Keep the first vertex so its outgoing bulge/width survive.
                    cleaned.RemoveAt(cleaned.Count - 1)

            cleaned.ToArray()

    type private UnionFind(size: int) =
        let parents = Array.init size id
        let ranks = Array.zeroCreate<byte> size

        member _.Find(value: int) =
            let rec root current =
                if parents[current] = current then current
                else
                    parents[current] <- root parents[current]
                    parents[current]
            root value

        member this.Union(first: int, second: int) =
            let firstRoot = this.Find first
            let secondRoot = this.Find second
            if firstRoot <> secondRoot then
                if ranks[firstRoot] < ranks[secondRoot] then
                    parents[firstRoot] <- secondRoot
                elif ranks[firstRoot] > ranks[secondRoot] then
                    parents[secondRoot] <- firstRoot
                else
                    parents[secondRoot] <- firstRoot
                    ranks[firstRoot] <- ranks[firstRoot] + 1uy

    type private EndpointTopology =
        {
            Roots: int array
            StartRoots: int array
            EndRoots: int array
            IncidentSegments: Dictionary<int, ResizeArray<int>>
            Degrees: Dictionary<int, int>
        }

    let private endpointCoordinates (segments: SegmentDto array) =
        Array.init (segments.Length * 2) (fun index ->
            let segment = segments[index / 2]
            if index % 2 = 0 then segment.StartX, segment.StartY
            else segment.EndX, segment.EndY)

    let private buildEndpointTopology (segments: SegmentDto array) tolerance =
        requireTolerance "tolerance" tolerance false
        let points = endpointCoordinates segments
        let unionFind = UnionFind(points.Length)
        let bucketSize = tolerance
        let buckets = Dictionary<struct (int64 * int64), ResizeArray<int>>()
        let toleranceSquared = tolerance * tolerance

        let bucketKey x y =
            struct (int64 (floor (x / bucketSize)), int64 (floor (y / bucketSize)))

        for endpointIndex = 0 to points.Length - 1 do
            let x, y = points[endpointIndex]
            let struct (bx, by) = bucketKey x y

            for dx in -1L .. 1L do
                for dy in -1L .. 1L do
                    let key = struct (bx + dx, by + dy)
                    match buckets.TryGetValue key with
                    | true, candidates ->
                        for candidateIndex in candidates do
                            let cx, cy = points[candidateIndex]
                            if distanceSquared x y cx cy <= toleranceSquared then
                                unionFind.Union(endpointIndex, candidateIndex)
                    | _ -> ()

            let ownKey = struct (bx, by)
            match buckets.TryGetValue ownKey with
            | true, bucket -> bucket.Add endpointIndex
            | _ ->
                let bucket = ResizeArray<int>()
                bucket.Add endpointIndex
                buckets.Add(ownKey, bucket)

        let roots = Array.init points.Length unionFind.Find
        let startRoots = Array.init segments.Length (fun i -> roots[i * 2])
        let endRoots = Array.init segments.Length (fun i -> roots[i * 2 + 1])
        let incident = Dictionary<int, ResizeArray<int>>()
        let degrees = Dictionary<int, int>()

        let addIncident root segmentIndex =
            match incident.TryGetValue root with
            | true, entries -> entries.Add segmentIndex
            | _ ->
                let entries = ResizeArray<int>()
                entries.Add segmentIndex
                incident.Add(root, entries)

        let addDegree root =
            match degrees.TryGetValue root with
            | true, count -> degrees[root] <- count + 1
            | _ -> degrees.Add(root, 1)

        for segmentIndex = 0 to segments.Length - 1 do
            let startRoot = startRoots[segmentIndex]
            let endRoot = endRoots[segmentIndex]
            addIncident startRoot segmentIndex
            addDegree startRoot
            addIncident endRoot segmentIndex
            addDegree endRoot

        {
            Roots = roots
            StartRoots = startRoots
            EndRoots = endRoots
            IncidentSegments = incident
            Degrees = degrees
        }

    let private reverseSegment (segment: SegmentDto) =
        {
            StartX = segment.EndX
            StartY = segment.EndY
            EndX = segment.StartX
            EndY = segment.StartY
            Bulge = -segment.Bulge
            SourceIndex = segment.SourceIndex
        }

    let private directedSegment (topology: EndpointTopology) (segments: SegmentDto array) index fromRoot =
        if topology.StartRoots[index] = fromRoot then
            segments[index], topology.EndRoots[index]
        else
            reverseSegment segments[index], topology.StartRoots[index]

    let private signedArea (segments: SegmentDto array) =
        segments
        |> Array.sumBy (fun segment -> segment.StartX * segment.EndY - segment.EndX * segment.StartY)
        |> fun twiceArea -> twiceArea * 0.5

    let stitchClosedLoops (segments: SegmentDto array) tolerance =
        requireTolerance "tolerance" tolerance false
        if isNull segments || segments.Length = 0 then
            [||]
        else
            let topology = buildEndpointTopology segments tolerance
            let componentVisited = Array.zeroCreate<bool> segments.Length
            let results = ResizeArray<LoopDto>()

            for seedIndex = 0 to segments.Length - 1 do
                if not componentVisited[seedIndex] then
                    let component = ResizeArray<int>()
                    let queue = Queue<int>()
                    queue.Enqueue seedIndex
                    componentVisited[seedIndex] <- true

                    while queue.Count > 0 do
                        let current = queue.Dequeue()
                        component.Add current
                        let roots = [| topology.StartRoots[current]; topology.EndRoots[current] |]
                        for root in roots do
                            match topology.IncidentSegments.TryGetValue root with
                            | true, incident ->
                                for adjacent in incident do
                                    if not componentVisited[adjacent] then
                                        componentVisited[adjacent] <- true
                                        queue.Enqueue adjacent
                            | _ -> ()

                    let componentSet = HashSet<int>(component)
                    let nodes = HashSet<int>()
                    for index in component do
                        nodes.Add topology.StartRoots[index] |> ignore
                        nodes.Add topology.EndRoots[index] |> ignore

                    let isSimpleClosedTopology =
                        nodes
                        |> Seq.forall (fun node ->
                            match topology.IncidentSegments.TryGetValue node with
                            | true, incident ->
                                incident |> Seq.filter componentSet.Contains |> Seq.length = 2
                            | _ -> false)

                    if isSimpleClosedTopology && component.Count >= 2 then
                        let ordered = ResizeArray<SegmentDto>()
                        let used = HashSet<int>()
                        let firstIndex = component[0]
                        let startRoot = topology.StartRoots[firstIndex]
                        let mutable currentRoot = startRoot
                        let mutable currentIndex = firstIndex
                        let mutable valid = true
                        let mutable completed = false

                        while valid && not completed && ordered.Count <= component.Count do
                            let directed, nextRoot = directedSegment topology segments currentIndex currentRoot
                            ordered.Add directed
                            used.Add currentIndex |> ignore
                            currentRoot <- nextRoot

                            if currentRoot = startRoot then
                                completed <- used.Count = component.Count
                                if not completed then valid <- false
                            else
                                match topology.IncidentSegments.TryGetValue currentRoot with
                                | true, incident ->
                                    let candidates =
                                        incident
                                        |> Seq.filter (fun candidate -> componentSet.Contains candidate && not (used.Contains candidate))
                                        |> Seq.toArray
                                    if candidates.Length = 1 then
                                        currentIndex <- candidates[0]
                                    else
                                        valid <- false
                                | _ -> valid <- false

                        if valid && completed && ordered.Count = component.Count then
                            let orderedArray = ordered.ToArray()
                            results.Add
                                {
                                    Segments = orderedArray
                                    SignedArea = signedArea orderedArray
                                }

            results.ToArray()

    let private orientation ax ay bx by cx cy =
        (bx - ax) * (cy - ay) - (by - ay) * (cx - ax)

    let private pointOnSegment px py ax ay bx by tolerance =
        let cross = abs (orientation ax ay bx by px py)
        if cross > tolerance then false
        else
            px >= min ax bx - tolerance && px <= max ax bx + tolerance &&
            py >= min ay by - tolerance && py <= max ay by + tolerance

    let private segmentsIntersect aStartX aStartY aEndX aEndY bStartX bStartY bEndX bEndY tolerance =
        let o1 = orientation aStartX aStartY aEndX aEndY bStartX bStartY
        let o2 = orientation aStartX aStartY aEndX aEndY bEndX bEndY
        let o3 = orientation bStartX bStartY bEndX bEndY aStartX aStartY
        let o4 = orientation bStartX bStartY bEndX bEndY aEndX aEndY
        let opposite first second = (first > tolerance && second < -tolerance) || (first < -tolerance && second > tolerance)

        if opposite o1 o2 && opposite o3 o4 then true
        elif abs o1 <= tolerance && pointOnSegment bStartX bStartY aStartX aStartY aEndX aEndY tolerance then true
        elif abs o2 <= tolerance && pointOnSegment bEndX bEndY aStartX aStartY aEndX aEndY tolerance then true
        elif abs o3 <= tolerance && pointOnSegment aStartX aStartY bStartX bStartY bEndX bEndY tolerance then true
        elif abs o4 <= tolerance && pointOnSegment aEndX aEndY bStartX bStartY bEndX bEndY tolerance then true
        else false

    let private samePoint x1 y1 x2 y2 tolerance =
        distanceSquared x1 y1 x2 y2 <= tolerance * tolerance

    let private bridgeCrossesSegment fromX fromY toX toY (segment: SegmentDto) tolerance =
        let touchesAllowedEndpoint =
            samePoint fromX fromY segment.StartX segment.StartY tolerance ||
            samePoint fromX fromY segment.EndX segment.EndY tolerance ||
            samePoint toX toY segment.StartX segment.StartY tolerance ||
            samePoint toX toY segment.EndX segment.EndY tolerance

        segmentsIntersect fromX fromY toX toY segment.StartX segment.StartY segment.EndX segment.EndY tolerance &&
        not touchesAllowedEndpoint

    let findBridges (segments: SegmentDto array) gapTolerance vertexTolerance =
        requireTolerance "gapTolerance" gapTolerance true
        requireTolerance "vertexTolerance" vertexTolerance false
        if isNull segments || segments.Length < 2 || gapTolerance <= vertexTolerance then
            [||]
        else
            let topology = buildEndpointTopology segments vertexTolerance
            let points = endpointCoordinates segments
            let dangling =
                [|
                    for endpointIndex = 0 to points.Length - 1 do
                        let root = topology.Roots[endpointIndex]
                        match topology.Degrees.TryGetValue root with
                        | true, 1 -> yield endpointIndex
                        | _ -> ()
                |]

            let candidates = ResizeArray<struct (float * int * int)>()
            for firstIndex = 0 to dangling.Length - 1 do
                for secondIndex = firstIndex + 1 to dangling.Length - 1 do
                    let firstEndpoint = dangling[firstIndex]
                    let secondEndpoint = dangling[secondIndex]
                    if firstEndpoint / 2 <> secondEndpoint / 2 then
                        let x1, y1 = points[firstEndpoint]
                        let x2, y2 = points[secondEndpoint]
                        let candidateDistance = distance x1 y1 x2 y2
                        if candidateDistance > vertexTolerance && candidateDistance <= gapTolerance then
                            let crossesExisting =
                                segments
                                |> Array.exists (fun segment -> bridgeCrossesSegment x1 y1 x2 y2 segment vertexTolerance)
                            if not crossesExisting then
                                candidates.Add(struct (candidateDistance, firstEndpoint, secondEndpoint))

            let orderedCandidates = candidates |> Seq.sortBy (fun struct (d, _, _) -> d)
            let usedEndpoints = HashSet<int>()
            let accepted = ResizeArray<BridgeDto>()

            for struct (candidateDistance, firstEndpoint, secondEndpoint) in orderedCandidates do
                if not (usedEndpoints.Contains firstEndpoint) && not (usedEndpoints.Contains secondEndpoint) then
                    let x1, y1 = points[firstEndpoint]
                    let x2, y2 = points[secondEndpoint]
                    let crossesAccepted =
                        accepted
                        |> Seq.exists (fun bridge ->
                            segmentsIntersect x1 y1 x2 y2 bridge.FromX bridge.FromY bridge.ToX bridge.ToY vertexTolerance &&
                            not (
                                samePoint x1 y1 bridge.FromX bridge.FromY vertexTolerance ||
                                samePoint x1 y1 bridge.ToX bridge.ToY vertexTolerance ||
                                samePoint x2 y2 bridge.FromX bridge.FromY vertexTolerance ||
                                samePoint x2 y2 bridge.ToX bridge.ToY vertexTolerance))

                    if not crossesAccepted then
                        usedEndpoints.Add firstEndpoint |> ignore
                        usedEndpoints.Add secondEndpoint |> ignore
                        accepted.Add
                            {
                                FromX = x1
                                FromY = y1
                                ToX = x2
                                ToY = y2
                                Distance = candidateDistance
                                FirstSourceIndex = segments[firstEndpoint / 2].SourceIndex
                                SecondSourceIndex = segments[secondEndpoint / 2].SourceIndex
                            }

            accepted.ToArray()

    let tryFindSnapVector (selectedPoints: PointDto array) (externalPoints: PointDto array) searchRadius vertexTolerance consensusTolerance =
        requireTolerance "searchRadius" searchRadius true
        requireTolerance "vertexTolerance" vertexTolerance false
        requireTolerance "consensusTolerance" consensusTolerance false

        if isNull selectedPoints || isNull externalPoints || selectedPoints.Length = 0 || externalPoints.Length = 0 || searchRadius <= 0.0 then
            {
                Found = false
                X = 0.0
                Y = 0.0
                SupportCount = 0
                Distance = 0.0
            }
        else
            let nearestVectors = ResizeArray<struct (float * float * float)>()

            for selected in selectedPoints do
                let mutable bestDistance = Double.PositiveInfinity
                let mutable bestX = 0.0
                let mutable bestY = 0.0
                for externalPoint in externalPoints do
                    let dx = externalPoint.X - selected.X
                    let dy = externalPoint.Y - selected.Y
                    let currentDistance = sqrt (dx * dx + dy * dy)
                    if currentDistance > vertexTolerance && currentDistance <= searchRadius && currentDistance < bestDistance then
                        bestDistance <- currentDistance
                        bestX <- dx
                        bestY <- dy

                if not (Double.IsPositiveInfinity bestDistance) then
                    nearestVectors.Add(struct (bestX, bestY, bestDistance))

            if nearestVectors.Count = 0 then
                {
                    Found = false
                    X = 0.0
                    Y = 0.0
                    SupportCount = 0
                    Distance = 0.0
                }
            else
                let assigned = Array.zeroCreate<bool> nearestVectors.Count
                let clusters = ResizeArray<struct (int * float * float * float)>()
                let toleranceSquared = consensusTolerance * consensusTolerance

                for i = 0 to nearestVectors.Count - 1 do
                    if not assigned[i] then
                        let struct (seedX, seedY, _) = nearestVectors[i]
                        let mutable count = 0
                        let mutable sumX = 0.0
                        let mutable sumY = 0.0
                        let mutable sumDistance = 0.0

                        for j = i to nearestVectors.Count - 1 do
                            if not assigned[j] then
                                let struct (candidateX, candidateY, candidateDistance) = nearestVectors[j]
                                if distanceSquared seedX seedY candidateX candidateY <= toleranceSquared then
                                    assigned[j] <- true
                                    count <- count + 1
                                    sumX <- sumX + candidateX
                                    sumY <- sumY + candidateY
                                    sumDistance <- sumDistance + candidateDistance

                        clusters.Add(struct (count, sumX / float count, sumY / float count, sumDistance / float count))

                let struct (supportCount, shiftX, shiftY, averageDistance) =
                    clusters
                    |> Seq.sortBy (fun struct (count, _, _, averageDistance) -> (-count, averageDistance))
                    |> Seq.head

                let minimumSupport = if selectedPoints.Length >= 2 then 2 else 1
                {
                    Found = supportCount >= minimumSupport
                    X = if supportCount >= minimumSupport then shiftX else 0.0
                    Y = if supportCount >= minimumSupport then shiftY else 0.0
                    SupportCount = supportCount
                    Distance = averageDistance
                }

[<AbstractClass; Sealed>]
type GeometryKernel private () =
    static member CleanVertices(vertices: VertexDto array, closed: bool, tolerance: float) =
        Internal.cleanVertices vertices closed tolerance

    static member StitchClosedLoops(segments: SegmentDto array, tolerance: float) =
        Internal.stitchClosedLoops segments tolerance

    static member FindBridges(segments: SegmentDto array, gapTolerance: float, vertexTolerance: float) =
        Internal.findBridges segments gapTolerance vertexTolerance

    static member TryFindSnapVector(
        selectedPoints: PointDto array,
        externalPoints: PointDto array,
        searchRadius: float,
        vertexTolerance: float,
        consensusTolerance: float) =
        Internal.tryFindSnapVector selectedPoints externalPoints searchRadius vertexTolerance consensusTolerance
