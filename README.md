# Wall Joiner Super

Plugin AutoCAD x64 hỗ trợ nối đường bao tường (`WJ`), tạo đường hoàn thiện (`FW`) và
làm sạch lưới tọa độ (`BW`). Phiên bản 1.1 ưu tiên an toàn dữ liệu bằng transaction
và Strict Mode, đồng thời thực thi ngay sau khi người dùng chọn đối tượng.

Luồng `WJ`/`FW`/`BW` không có preview hay bước Apply/Cancel. Sau khi selection hợp lệ,
plugin tính kết quả trong bộ nhớ rồi ghi theo một transaction ngắn. `WJ` mặc định xóa
đối tượng nguồn an toàn (`Keep originals = false`).

## Lệnh

- `WJ`: gom các curve gần nhau, tạo boundary kín và đưa về Wall Layer.
- `FW`: tạo offset trong, ngoài hoặc cả hai phía từ boundary tường.
- `BW`: làm tròn LINE/LWPOLYLINE theo bước lưới; giữ nguyên open/closed, bulge,
  width và các thuộc tính được clone từ entity nguồn.
- `WJ_UI`: mở bảng cấu hình.

## Cơ chế an toàn

- `Strict Mode` mặc định bật: nếu một đầu vào/cụm không xử lý được thì transaction
  không commit.
- `Keep originals` mặc định tắt cho `WJ`.
- Khi tắt `Keep originals`, block chỉ bị xóa nếu toàn bộ nội dung đã được trích xuất
  thành curve hỗ trợ. Block chứa text hoặc entity khác luôn được giữ lại và có cảnh báo.
- Chuỗi curve hở chỉ được đóng khi khe cuối nằm trong dung sai; plugin không tạo
  cạnh đóng dài ngoài ý muốn.
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
| Vertex Tolerance | Dung sai so sánh đỉnh và kiểm tra loop kín |
| Finish Offset | Chiều dày offset cho `FW` |
| Finish Direction | `Outside`, `Inside` hoặc `Both` |
| Snap Radius | Phạm vi tìm hình học lân cận cho `BW` |
| Grid Step | Bước làm tròn tọa độ cho `BW` |
| Wall/Finish Layer | Layer kết quả; tự tạo nếu chưa tồn tại |

## Build

Yêu cầu:

- Windows x64;
- .NET SDK có khả năng build `net48`;
- package `AutoCAD.NET` 23.1.0 đã restore.

Build Release:

```powershell
dotnet build "Wall Joiner Super/Wall Joiner Super.csproj" -c Release -p:Platform=x64
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
dotnet run --project "Wall Joiner Super.Tests/Wall Joiner Super.Tests.csproj" -c Release
```

Trước khi phát hành nên chạy thêm bộ DWG hồi quy trong AutoCAD với các trường hợp:

- rectangle kín, loop hở trong/ngoài dung sai;
- góc L/T, tường chồng nhau, đảo chiều polyline;
- arc/bulge và block lồng nhau;
- block chứa đồng thời curve và text;
- layer khóa, UCS xoay, tọa độ âm/lớn;
- selection từ 1.000 đến 10.000 curve.

## Cấu trúc

- `Commands.cs`: entry point, lấy document và điều phối prompt/UI.
- `WallInteraction.cs`: selection filter và chuẩn hóa kết quả OK/Cancel/Error.
- `WallJoinLogic.cs`: snapshot đọc, tính toán ngoài transaction và điều phối ghi atomic.
- `WallDatabaseWriter.cs`: append batch entity đã tạo trong bộ nhớ vào Current Space.
- `GeometryProcessor.cs`: trích xuất, sweep clustering, bridge, Region, offset và BW.
- `WallSettings*.cs`: mô hình, kiểm tra, đọc/ghi cấu hình.
- `LayerService.cs`: tạo layer và kiểm tra layer khóa.
- `WallJoinWindow.*`: giao diện cấu hình; constructor không truy cập AutoCAD document/database.
- `RibbonSetup.cs`: Ribbon idempotent theo ID, icon Pack URI và vòng đời event.

Cách tách entity tạo trong bộ nhớ khỏi bước append Database, cùng lớp interaction riêng,
được tham khảo từ hướng `NoDraw` / `Draw` / `Interaction` của AutoCADCodePack. Project
không thêm dependency tới AutoCADCodePack.

## Giới hạn hiện tại

- `BW` chỉ nhận `LINE` và `LWPOLYLINE`; old-style `POLYLINE` không bị thay đổi.
- Hình học WJ/FW được chuẩn hóa về mặt phẳng XY; quy trình 3D không nằm trong phạm vi.
- Curve không phải Line/Arc/Polyline trong block được nội suy thành polyline khi cần
  fallback; nên kiểm tra kết quả với spline/ellipse trên bản DWG thử nghiệm.
- Các kiểm tra tự động hiện chỉ bao phủ logic thuần. Geometry dựa trên AutoCAD cần
  được xác nhận bằng DWG hồi quy trong host thực.
