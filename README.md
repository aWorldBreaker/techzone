# TechZone — Website thương mại điện tử đồ công nghệ (đồ án demo)

TechZone hiện dùng **ASP.NET Core 8 MVC + Razor Views + EF Core + SQLite + ASP.NET Identity**. Đây là đồ án/demo B2C tại Việt Nam, **không** tích hợp thu tiền thật hoặc phát hành hóa đơn điện tử hợp pháp. Không dùng prompt React/NestJS/PostgreSQL đời đầu để viết lại hệ thống.

## Nguồn tài liệu hiện hành

- [`AGENTS.md`](AGENTS.md): quy tắc vận hành cho Muse/Codex trong repository.
- [`docs/PLAN-STATUS.md`](docs/PLAN-STATUS.md): những gì có code, những gì phải kiểm thử, thứ tự ưu tiên.
- [`docs/IMPLEMENTATION-GUIDE.md`](docs/IMPLEMENTATION-GUIDE.md): quy trình đọc source, triển khai và xác minh an toàn.
- [`TEST-REPORT.md`](TEST-REPORT.md): mẫu ghi kết quả 15 luồng nghiệm thu; mặc định **NOT_RUN** cho đến khi có bằng chứng.
- `MASTER.md`, `SCOPE.md`, `DATABASE-MIGRATION.md` (nếu có trong workspace nội bộ): đọc khi tồn tại, đối chiếu với mã nguồn và quyết định mới nhất; không giả định chúng đã được commit.

## Chạy local

Yêu cầu .NET 8 SDK. Từ thư mục chứa `TechZone.csproj`:

```bash
dotnet --info
dotnet restore TechZone.csproj
dotnet build TechZone.csproj
dotnet run --project TechZone.csproj
```

Dùng URL HTTP/HTTPS **do terminal in ra** thay vì mặc định một port cố định. SQLite dùng đường dẫn `Data Source=techzone.db` trong `appsettings.json`. Khi khởi động, `SeedData.InitializeAsync` gọi `MigrateAsync` và seed dữ liệu demo. **Không chạy trực tiếp trên DB cũ hoặc DB có dữ liệu quan trọng trước khi backup và kiểm tra đường chuyển đổi legacy.**

### DB legacy / rollback

Mã nguồn có `Data/LegacyDbConverter.cs` để xử lý database cũ được tạo bởi `EnsureCreated`; không giả định rằng việc chạy `dotnet run` sẽ tự chuyển đổi an toàn mọi DB legacy. Hãy thử migration trên **bản sao**, xác thực dữ liệu/khóa ngoại, kiểm tra restore backup trước khi dùng DB thật. Xem `docs/IMPLEMENTATION-GUIDE.md`.

### Kiểm thử

```bash
dotnet test TechZone.Tests/TechZone.Tests.csproj
# Chỉ dùng runner dự phòng khi có lỗi môi trường testhost được xác minh:
dotnet run --project TechZone.Tests.Runner/TechZone.Tests.Runner.csproj
```

**Trạng thái:** Repository có unit/integration tests và runner dự phòng. Tài liệu này không tuyên bố các lệnh trên đã PASS ở commit hiện tại. Kết quả phải được ghi vào `TEST-REPORT.md`.

## Chức năng đã có mã nguồn

- Catalog, tìm kiếm/lọc, chi tiết sản phẩm, biến thể, so sánh, wishlist, đánh giá/Q&A, session cart.
- Checkout demo và đơn hàng; `CheckoutService` có giao dịch DB, cập nhật tồn kho có điều kiện, idempotency key.
- Admin: sản phẩm, giá, tồn kho, đơn hàng, voucher, combo, flash sale slots, banner, khách hàng, thông báo, duyệt đánh giá.
- Loyalty Đồng/Bạc/Vàng/Bạch kim; voucher, điểm, đổi trả/hoàn đơn, VAT *demo*.
- 3D viewer cho sản phẩm có mapping; có xoay/zoom/ẩn-hiện/reset và fallback ảnh.

**Có code không đồng nghĩa đã được chạy và nghiệm thu.** Đọc ma trận ở `docs/PLAN-STATUS.md`.

## Tài nguyên tại bản snapshot GitHub 2026-11-05

Commit `a3eb8d2d42d02e03e6ebc3a848f368cd33e61c31` chứa **13 file ảnh sản phẩm** (cộng `placeholder.svg`) và **4 GLB** tại `wwwroot/models/`: smartphone, laptop, headphones, gamepad. Một số asset là minh họa/generic, không phải sản phẩm 3D chính thức của hãng; gamepad chưa được map vào sản phẩm bán trong seed. Cần kiểm thử HTTP 200, render 3D và giấy phép ảnh trước khi public. Không lặp lại báo cáo cũ rằng ảnh/GLB còn thiếu ở GitHub HEAD này.

## Thiết kế đã duyệt

Hiện dùng **giao diện sáng, navy trầm**, chữ `#0F172A`, điểm nhấn `#1E3A5F`, CSS ở `wwwroot/css/`. Đây là quyết định mới hơn prompt ban đầu về giao diện tối/cyan. Tránh viết lại UI hoặc đổi framework nếu chưa được yêu cầu.

## Cảnh báo bảo mật và nghiệp vụ trước khi triển khai công khai

1. `Data/SeedData.cs` tạo **tài khoản demo với thông tin đăng nhập tĩnh**. Không dùng mặc định này trên môi trường public/production. Không đưa mật khẩu vào tài liệu hay log.
2. `OrdersController.Details()`/`HoaDon()` và tra cứu bảo hành có thể truy cập đơn **guest** theo ID. Cần thêm cơ chế xác thực quyền riêng tư cho guest trước public.
3. `ProductForm` thiếu server-side validation đầy đủ đối với giá và tồn kho; `AccountController.Register` gán email xác nhận ngay; cần đánh giá theo môi trường chạy.
4. Giá Flash Sale trên trang chi tiết và trong checkout có thể lệch vì chưa dùng cùng một luồng tính; cần thử các mốc giờ và quota.
5. VAT 8%/10% trong `appsettings.json` đang được đánh dấu `DaXacMinh=false`. **Không dùng để khai thuế thật** nếu chưa được chuyên gia thuế xác minh từng nhóm hàng.
6. Không có bằng chứng kiểm thử runtime/CI cho commit trong tài liệu này.

## Phương thức thanh toán

Các phương thức COD/chuyển khoản/ví trong demo được **giả lập**. UI/biên nhận phải tiếp tục ghi rõ đây không phải thanh toán thật.

## Tiêu chí hoàn thành

Một hạng mục chỉ được báo `PASS` sau khi chạy thành công trong môi trường kiểm tra. Sử dụng `TEST-REPORT.md` để ghi 15 luồng, log, hình ảnh, hash commit và các vấn đề còn lại. Không xóa các ghi chú `NOT_RUN` chỉ vì đã bổ sung code.