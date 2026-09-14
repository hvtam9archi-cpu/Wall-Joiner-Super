# Wall Joiner Super

Plugin AutoCAD x64 hỗ trợ nối đường bao tường (`WJ`), tạo đường hoàn thiện (`FW`) và
làm sạch lưới tọa độ (`BW`). Phiên bản 1.1 ưu tiên an toàn dữ liệu bằng transaction,
Strict Mode và một geometry kernel F# thuần để tách topology khỏi vòng đời AutoCAD DBObject.

Luồng `WJ`/`FW`/`BW` không có preview hay bước Apply/Cancel. Sau khi selection hợp lệ,
plugin snapshot dữ liệu, tính kết quả trong bộ nhớ rồi ghi theo một transaction ngắn.
`WJ` mặc định xóa đối tượng nguồn an toàn (`Keep originals = false`).

## Lệnh

- `WJ`: gom các curve gần nhau, làm sạch vertex, tạo boundary kín và đưa về Wall Layer.
- `FW`: dùng cùng pipeline boundary rồi tạo offset trong, ngoài hoặc cả hai phía.
- `BW`: làm tròn LINE/LWPOLYLINE theo bước lưới; ưu tiên snap theo consensus của các
  vertex thật gần geometry xung quanh thay vì snap theo góc bounding box.
- `WJ_UI`: mở bảng cấu hình.

## Geometry pipeline

Phần tương tác AutoCAD vẫn viết bằng C#, còn topology thuần được tách sang project F#
`Wall Joiner Geometry` (`netstandard2.0`). Cách chia này giữ `Transaction`, `ObjectId`,
`Region`, layer và database write ở C#, trong khi clean/stitch/bridge/snap có thể kiểm thử
mà không cần host AutoCAD.

Pipeline WJ/FW hiện tại:

```text
AutoCAD selection
    -> snapshot + normalize XY
    -> clean duplicate vertices
    -> sweep clustering + geometry proximity check
    -> AutoCAD Region / Boolean Union
    -> F# endpoint topology + loop reconstruction
    -> nếu cần: safe bridge chỉ giữa dangling endpoints
    -> retry Region
    -> topology fallback an toàn
    -> short write transaction
```

Logic clean vertex được tham khảo từ `Clean_poly` của gile: loại vertex chồng nhau nhưng
bảo toàn bulge/start width/end width gắn với vertex. Logic dựng lại loop được tham khảo từ
`FUSION / MergePlines` của Gilles Chanteau: ưu tiên Region/Boolean của AutoCAD, explode ra
Line/Arc rồi nối theo endpoint continuity, đảo dấu bulge khi segment bị đảo chiều. Bản hiện
tại không copy Lisp trực tiếp; các ý tưởng trên được viết lại thành geometry kernel có test.

## Cơ chế an toàn

- `Strict Mode` mặc định bật: nếu một đầu vào/cụm không xử lý được thì transaction
  không commit.
- `Keep originals` mặc định tắt cho `WJ` khi `Strict Mode` bật.
- Khi `Strict Mode` tắt, plugin luôn giữ originals dù cấu hình cũ từng lưu `Keep originals = false`;
  điều này ngăn partial-processing xóa source của cụm thành công trong khi cụm khác thất bại.
- Khi được phép xóa originals, block chỉ bị xóa nếu toàn bộ nội dung đã được trích xuất
  thành curve hỗ trợ. Block chứa text hoặc entity khác luôn được giữ lại và có cảnh báo.
- Region được thử trên geometry đã clean trước. Bridge chỉ được xét sau khi Region không
  cho kết quả và chỉ nối dangling endpoints trong `Gap Tolerance`.
- Bridge giao cắt geometry hiện hữu hoặc bridge đã chọn bị loại bỏ.
- F# topology từ chối T/X junction mơ hồ thay vì tự tạo loop tùy ý.
- BW cần ít nhất hai vertex đồng thuận khi selection có nhiều vertex trước khi snap sang
  geometry lân cận; nếu không đủ consensus thì quay về grid snap.
- Entity trên layer khóa được giữ nguyên.
- Sau lệnh, command line báo số đối tượng chọn, xử lý, tạo mới, xóa và bỏ qua.

## Cấu hình

Cấu hình được lưu tại:

```text
%APPDATA%\WallJoinerSuper\settings.json
```

Các giá trị khoảng cách đều dùng **đơn vị bản vẽ AutoCAD**, không phải pixel.

| Cấu hình | Ý nghĩa |
|---|---|
| Gap Tolerance | Khoảng cách tối đa để gom cụm/nối khe |
| Vertex Tolerance | Dung sai so sánh đỉnh và kiểm tra topology |
| Finish Offset | Chiều dày offset cho `FW` |
| Finish Direction | `Outside`, `Inside` hoặc `Both` |
| Snap Radius | Phạm vi tìm geometry lân cận cho `BW` |
| Grid Step | Bước làm tròn tọa độ cho `BW` |
| Wall/Finish Layer | Layer kết quả; tự tạo nếu chưa tồn tại |

## Build

Yêu cầu:

- Windows x64;
- .NET SDK có khả năng build `net48` và F# `netstandard2.0`;
- package `AutoCAD.NET` 23.1.0 đã restore.

Build Release:

```powershell
dotnet build "Wall Joiner Super/Wall Joiner Super.csproj" -c Release -p:Platform=x64
```

Build project C# sẽ build project F# tham chiếu và bundle các runtime dependency bắt buộc:

```text
WallJoinerSuper.dll
WallJoiner.Geometry.dll
FSharp.Core.dll
```

Bundle được tạo tại:

```text
Wall Joiner Super/bin/Release/WallJoinerSuper.bundle
```

Build không tự động ghi vào thư mục plugin của người dùng. Muốn build và triển khai:

```powershell
dotnet build "Wall Joiner Super/Wall Joiner Super.csproj" -c Release -p:Platform=x64 -p:DeployPlugin=true
```

Đóng AutoCAD trước khi deploy để DLL trong bundle đích không bị khóa. Target deploy chỉ
làm sạch đúng `%APPDATA%\Autodesk\ApplicationPlugins\WallJoinerSuper.bundle`, không tác
động bundle của plugin khác.

Autoloader manifest khai báo API series tối thiểu `R23.1`. Nếu cần hỗ trợ series
AutoCAD khác, phải build và kiểm thử với bộ managed DLL tương ứng.

## Kiểm thử

Chạy các kiểm tra logic thuần, không yêu cầu AutoCAD:

```powershell
dotnet run --project "Wall Joiner Super.Tests/Wall Joiner Super.Tests.csproj" -c Release -p:Platform=x64
```

Bộ kiểm tra hiện bao phủ:

- numeric/settings và JSON round-trip;
- non-strict mode bắt buộc giữ source entities;
- clean duplicate vertex, giữ bulge và width, tính idempotent;
- stitch loop khi segment bị đảo chiều và đảo dấu bulge;
- từ chối T-junction mơ hồ;
- safe bridge giữa dangling endpoints;
- consensus snap và từ chối single accidental match.

GitHub Actions `.github/workflows/build.yml` chạy trên Windows và bắt buộc ba bước cùng pass:
F# kernel build, pure geometry checks và AutoCAD plugin/bundle build.

Trước khi phát hành vẫn nên chạy bộ DWG hồi quy trong AutoCAD với các trường hợp:

- rectangle kín, loop hở trong/ngoài dung sai;
- góc L/T/X, tường chồng nhau, đảo chiều polyline;
- arc/bulge, loop hai cung và block lồng nhau;
- spline/ellipse/circle và old-style POLYLINE;
- block chứa đồng thời curve và text;
- layer khóa, UCS xoay, tọa độ âm/lớn;
- selection từ 1.000 đến 10.000 curve.

## Cấu trúc

- `Commands.cs`: entry point và điều phối prompt/UI.
- `WallInteraction.cs`: selection filter và chuẩn hóa kết quả OK/Cancel/Error.
- `WallJoinWorkflow.cs`: workflow duy nhất cho WJ/FW/BW; snapshot, Strict Mode và ghi atomic.
- `GeometryPipeline.cs`: clean -> Region -> F# topology -> safe bridge -> fallback.
- `GeometryKernelAdapter.cs`: chuyển AutoCAD Curve/Polyline sang DTO F# và ngược lại.
- `Wall Joiner Geometry/GeometryTypes.fs`: DTO immutable-friendly cho interop C#/F#.
- `Wall Joiner Geometry/GeometryKernel.fs`: clean, topology graph, bridge solver và snap consensus.
- `GeometryProcessor.cs`: extraction, clustering, offset và các helper AutoCAD hiện hữu.
- `WallDatabaseWriter.cs`: append batch entity in-memory vào Current Space.
- `WallSettings*.cs`: mô hình, kiểm tra, đọc/ghi cấu hình.
- `LayerService.cs`: tạo layer và kiểm tra layer khóa.
- `WallJoinWindow.*`: giao diện cấu hình; constructor không truy cập AutoCAD document/database.
- `RibbonSetup.cs`: Ribbon idempotent theo ID, icon Pack URI và vòng đời event.

Cách tách entity tạo trong bộ nhớ khỏi bước append Database, cùng lớp interaction riêng,
được tham khảo từ hướng `NoDraw` / `Draw` / `Interaction` của AutoCADCodePack. Project
không thêm dependency tới AutoCADCodePack.

## Giới hạn hiện tại

- `BW` chỉ nhận `LINE` và `LWPOLYLINE`; old-style `POLYLINE` không bị thay đổi bởi BW.
- Hình học WJ/FW được chuẩn hóa về mặt phẳng XY; quy trình 3D không nằm trong phạm vi.
- Curve không phải Line/Arc/LWPOLYLINE được sample thành segment trong topology fallback;
  Region của AutoCAD vẫn là đường xử lý ưu tiên để giữ độ chính xác hình học cao nhất.
- Automated tests bao phủ geometry kernel thuần; thao tác `Region`, `Offset`, transaction và
  database ownership vẫn cần DWG regression trong AutoCAD thật trước khi phát hành rộng.
