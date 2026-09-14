namespace ProWallTools.Geometry

[<CLIMutable>]
type PointDto =
    {
        X: float
        Y: float
    }

[<CLIMutable>]
type VertexDto =
    {
        X: float
        Y: float
        Bulge: float
        StartWidth: float
        EndWidth: float
    }

[<CLIMutable>]
type SegmentDto =
    {
        StartX: float
        StartY: float
        EndX: float
        EndY: float
        Bulge: float
        SourceIndex: int
    }

[<CLIMutable>]
type BridgeDto =
    {
        FromX: float
        FromY: float
        ToX: float
        ToY: float
        Distance: float
        FirstSourceIndex: int
        SecondSourceIndex: int
    }

[<CLIMutable>]
type LoopDto =
    {
        Segments: SegmentDto array
        SignedArea: float
    }

[<CLIMutable>]
type SnapVectorDto =
    {
        Found: bool
        X: float
        Y: float
        SupportCount: int
        Distance: float
    }
