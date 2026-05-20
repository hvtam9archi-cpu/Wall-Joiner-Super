# AutoCAD Wall Joiner Super

Plugin AutoCAD tối ưu hóa vẽ tường, tự động gộp vùng tường (Wall Join), tạo lớp vữa trát hoàn thiện (Finishing Wall) và nắn thẳng lưới tường (Beautify Walls) dựa trên nền tảng .NET API và giao diện WPF Modern Dark Theme cao cấp.

---

## 🛠️ Tính năng chính

1. **Gộp tường nhanh (Lệnh `WJ` - Wall Join):**
   - Tự động quét và trích xuất các đường bao từ Line, Polyline, LwPolyline, hoặc Block Reference.
   - Nhóm các đối tượng gần nhau dựa trên khoảng cách thiết lập (Gap Tolerance).
   - Tiến hành gộp vùng (Boolean Union Region) để tạo thành một đường bao Polyline khép kín duy nhất đại diện cho lõi tường.
   - Gán đối tượng kết quả về đúng lớp tường được thiết lập (mặc định là `ABC_A_Nettuong`) và tự động xóa các đối tượng gốc.

2. **Tạo lớp vữa trát hoàn thiện (Lệnh `FW` - Finishing Wall):**
   - Tương tự như `WJ` nhưng thay vì giữ nguyên đường bao, hệ thống tự động sinh thêm một đường bao cách tường một khoảng thiết lập (mặc định là `15.0px`).
   - Gán đối tượng kết quả về lớp hoàn thiện (mặc định là `ABC_A_Netmanh`) và bảo toàn các nét vẽ gốc bên trong.

3. **Nắn thẳng & triệt tiêu thập phân (Lệnh `BW` - Beautify Walls):**
   - Hỗ trợ chọn nhanh danh sách tường bị lệch trục.
   - Tự động bắt điểm từ tính (Magnet Snap) vào hệ lưới ảo của bản vẽ hiện hành.
   - Triệt tiêu hoàn toàn các số thập phân lẻ của tọa độ đỉnh để đưa về các số chẵn (bội của 5 hoặc 10), giúp bản vẽ sạch và chuẩn kích thước hình học.

4. **Giao diện cấu hình Premium Dark Theme (Lệnh `WJ_UI`):**
   - Bảng thiết lập WPF không viền, hỗ trợ bo góc 10px cao cấp.
   - Tự động nạp danh sách Layer thực tế từ bản vẽ đang mở vào ComboBox để người dùng lựa chọn trực quan.
   - Cho phép chỉnh sửa nhanh các thông số dung sai hình học: Gap Tolerance, Vertex Tolerance, và các Layer đích.

5. **Tích hợp Ribbon UI thông minh:**
   - Tạo tab **TH Tools** và panel **Wall Joiner** chứa các icon trực quan cho cả 4 tính năng.
   - Lắng nghe biến hệ thống `WSCURRENT` của AutoCAD để tự động render lại giao diện khi người dùng chuyển đổi Workspace vẽ.
   - Đăng ký sự kiện `Application.Idle` giúp plugin khởi động mượt mà, tránh lỗi NullReference.

---

## 📂 Cấu trúc mã nguồn (Decoupled Core Architecture)

Mã nguồn được phân tách rõ ràng theo chuẩn thiết kế sạch để dễ bảo trì và hạn chế tối đa nguy cơ rò rỉ bộ nhớ (RAM Leak) hoặc Crash AutoCAD:

- [Wall Joiner Super.csproj](file:///c:/Users/TamHoang/source/repos/hvtam9archi-cpu/Wall-Joiner-Super/Wall%20Joiner%20Super/Wall%20Joiner%20Super.csproj): Tệp cấu hình dự án chuẩn SDK-style, tự động khôi phục NuGet và thiết lập sao chép tệp bundle sau khi build.
- [PackageContents.xml](file:///c:/Users/TamHoang/source/repos/hvtam9archi-cpu/Wall-Joiner-Super/Wall%20Joiner%20Super/PackageContents.xml): File cấu hình Autoloader của Autodesk, tự động nạp DLL khi AutoCAD khởi chạy.
- [Commands.cs](file:///c:/Users/TamHoang/source/repos/hvtam9archi-cpu/Wall-Joiner-Super/Wall%20Joiner%20Super/Commands.cs): Chỉ chứa định nghĩa các CommandMethod (`BW`, `WJ`, `FW`, `WJ_UI`) làm entry point, hoàn toàn không chứa logic nghiệp vụ.
- [WallJoinLogic.cs](file:///c:/Users/TamHoang/source/repos/hvtam9archi-cpu/Wall-Joiner-Super/Wall%20Joiner%20Super/WallJoinLogic.cs): Chứa logic nghiệp vụ liên quan đến AutoCAD Database Transactions, khóa tài liệu (`LockDocument`) và dọn dẹp các đối tượng trung gian.
- [GeometryProcessor.cs](file:///c:/Users/TamHoang/source/repos/hvtam9archi-cpu/Wall-Joiner-Super/Wall%20Joiner%20Super/GeometryProcessor.cs): Chứa các hàm xử lý tính toán hình học thuần túy (clustering, bridging, region boolean operations, snapped beautifying).
- [LayerService.cs](file:///c:/Users/TamHoang/source/repos/hvtam9archi-cpu/Wall-Joiner-Super/Wall%20Joiner%20Super/LayerService.cs): Kiểm tra và tự động khởi tạo các lớp layer đích nếu chưa tồn tại trong bản vẽ.
- [WallConstants.cs](file:///c:/Users/TamHoang/source/repos/hvtam9archi-cpu/Wall-Joiner-Super/Wall%20Joiner%20Super/WallConstants.cs): Lưu trữ các cấu hình mặc định (dung sai, màu sắc, nét vẽ, layer đích) dạng static để cho phép cập nhật trực tiếp tại runtime.
- [WallJoinWindow.xaml](file:///c:/Users/TamHoang/source/repos/hvtam9archi-cpu/Wall-Joiner-Super/Wall%20Joiner%20Super/WallJoinWindow.xaml) & [WallJoinWindow.xaml.cs](file:///c:/Users/TamHoang/source/repos/hvtam9archi-cpu/Wall-Joiner-Super/Wall%20Joiner%20Super/WallJoinWindow.xaml.cs): Giao diện WPF thiết lập cấu hình.
- [RibbonSetup.cs](file:///c:/Users/TamHoang/source/repos/hvtam9archi-cpu/Wall-Joiner-Super/Wall%20Joiner%20Super/RibbonSetup.cs): Quản lý tích hợp UI trên Ribbon Bar của AutoCAD.
- [WallMarkers.cs](file:///c:/Users/TamHoang/source/repos/hvtam9archi-cpu/Wall-Joiner-Super/Wall%20Joiner%20Super/WallMarkers.cs): Quản lý các hình vẽ tạm thời (Transient Graphics) phục vụ vẽ phác thảo kiểm tra.

---

## 🚀 Hướng dẫn biên dịch & Cài đặt

### Yêu cầu hệ thống:
- .NET SDK (phiên bản hỗ trợ target framework `net48`)
- AutoCAD 2018 trở lên (hoặc bất kỳ phiên bản nào hỗ trợ bộ API AutoCAD.NET 23.1.0)

### Các bước thực hiện:
1. Mở terminal tại thư mục gốc của dự án.
2. Thực hiện lệnh biên dịch:
   ```bash
   dotnet build "Wall Joiner Super/Wall Joiner Super.csproj" -c Debug
   ```
3. Sau khi build thành công, tiến trình tự động (`CopyToPlugins`) sẽ đóng gói toàn bộ thư mục `.bundle` và đẩy trực tiếp vào:
   `%AppData%\Roaming\Autodesk\ApplicationPlugins\WallJoinerSuper.bundle`
4. Khởi động AutoCAD, plugin sẽ tự động được tải (nhờ cơ chế Autoloader). Bạn sẽ thấy xuất hiện Tab **TH Tools** trên thanh Ribbon của AutoCAD.
