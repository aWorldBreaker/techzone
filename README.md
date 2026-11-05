# TechZone — Website bán đồ công nghệ

Đồ án: website thương mại điện tử B2C, 1 shop, 2 vai trò Khách / Admin.
ASP.NET Core 8 + EF Core + SQLite (demo) + Razor MVC + Identity.

## Chạy

```bash
cd TechZone
dotnet restore
dotnet run
```

Mở `https://localhost:5001` (hoặc cổng http in ra ở terminal).
Lần chạy đầu tiên tự tạo file `techzone.db` (SQLite) và seed dữ liệu mẫu.
Muốn seed lại từ đầu: xóa `techzone.db` rồi chạy lại.

> Chưa có `dotnet` trên máy thì cài .NET 8 SDK rồi chạy lệnh trên.
> Code trong repo này chưa từng được build trên máy viết code (không có dotnet),
> nên lần build đầu có thể lộ lỗi nhỏ — xem mục "Kiểm chứng" bên dưới.

## Tài khoản demo (seed sẵn)

| Vai trò | Email | Mật khẩu |
|---------|-------|----------|
| Admin | admin@techzone.vn | Admin@123 |
| Khách | khach@techzone.vn | Khach@123 |

## Luồng demo (theo SCOPE.md)

### 1. Khách mua hàng + voucher + COD
1. Trang chủ → tìm "Xiaomi 15" → xem chi tiết (thông số Snapdragon 8 Gen 3, RAM 12GB...).
2. Chọn phiên bản Đen / 256GB → Thêm vào giỏ.
3. Giỏ hàng → nhập mã `TECHZONE500` → Áp dụng (giảm 500.000đ, đơn tối thiểu 5.000.000đ).
4. Đặt hàng → nhập tên / SĐT / địa chỉ → chọn COD → Đặt hàng.
5. Xem đơn ở "Đơn của tôi": trạng thái Chờ xác nhận. Đơn #3 mẫu đã có sẵn để xem.

### 2. Flash Sale
1. Trang chủ → mục Flash Sale (đồng hồ đếm ngược đến 23:59 hôm nay).
2. Mua Xiaomi 15 5G giá sale 12.990.000đ (gốc 15.990.000đ) hoặc Tai nghe Xiaomi Buds 5 giá 1.090.000đ.
3. Hết giới hạn số lượng là hết deal (cột `GioiHanSL` / `DaBan` trong bảng FlashSales).

### 3. Admin
1. Đăng nhập `admin@techzone.vn` → vào Dashboard: doanh thu tháng, đơn chờ xác nhận, top bán chạy, sản phẩm sắp hết hàng, doanh thu 7 ngày.
2. Quản lý sản phẩm → Thêm sản phẩm mới (nhập đúng giá + thông số thật) → khách thấy ngay ở trang danh mục.
3. Quản lý đơn hàng → mở đơn #3 (Chờ xác nhận) → đổi sang Đang giao → Đã giao.
4. Khách vào chi tiết sản phẩm vừa giao → gửi đánh giá sao → điểm trung bình của sản phẩm cập nhật.
5. Quản lý voucher → thêm mã mới (vd: `GIAM200` giảm 200.000đ).

## Thiết kế hệ thống

### Cơ sở dữ liệu — 15 bảng
14 bảng theo SCOPE.md (Users qua Identity `AspNetUsers`, Addresses, Categories,
Brands, Products, ProductImages, ProductVariants, Carts, CartItems, Orders,
OrderDetails, Vouchers, FlashSales, Reviews) **+ 1 bảng thêm: ProductSpecs**.

**Lựa chọn về ProductSpecs:** tách thành bảng riêng, quan hệ 1-1 với Products,
thay vì nhét chip/RAM/ROM/Pin/Màn hình/Camera vào bảng Products. Lý do: thông số
khác nhau theo loại sản phẩm — đồng hồ không có chip/RAM, chuột không có pin,
SSD không có màn hình. Các trường trong ProductSpecs đều nullable: chỉ điền
trường nào sản phẩm thật sự có, không bịa thông số cho sản phẩm không có.
Bộ lọc danh mục (hãng / chip / RAM / dung lượng / giá) đọc trực tiếp từ bảng này.

### Giỏ hàng
Bản demo dùng **giỏ hàng session** (`Services/CartService`): dùng được cho cả khách
vãng lai chưa đăng nhập, giá/tồn kho đọc lại từ DB mỗi lần hiển thị nên luôn mới.
Bảng Carts + CartItems đã map sẵn trong DbContext để giai đoạn sau đồng bộ giỏ
session vào DB khi khách đăng nhập — không phải sửa schema.

### Thanh toán & vận chuyển (giả lập)
- COD hoặc chuyển khoản: đều là giả lập, không thu tiền thật.
- Phí ship: miễn phí đơn từ 2.000.000đ sau giảm, không thì 30.000đ.

### Design system
Minimalism & Swiss Style (UI UX Pro Max), nhưng **palette trong MASTER.md đã bị
ghi đè theo ý đã duyệt**: nền trắng, chữ xám đậm `#0F172A`, chỉ 1 màu nhấn
xanh navy trầm `#1E3A5F`. Không dùng xanh lá `#059669` / cam `#EA580C` của skill.
Font Rubik (tiêu đề) + Nunito Sans (nội dung), bo góc tối đa 8px, khoảng cách rộng.
File: `wwwroot/css/site.css`.

## Dữ liệu seed — thật, không bịa

13 sản phẩm, 8 danh mục (Điện thoại, Laptop, Tablet, Tai nghe, Đồng hồ,
Phụ kiện, Gaming, Linh kiện PC), 9 hãng. Giá và thông số theo giá bán lẻ thực tế
tháng 11/2026, ví dụ:
- Xiaomi 15 5G: 12.990.000đ (gốc 15.990.000đ), Snapdragon 8 Gen 3, RAM 12GB,
  ROM 256GB, Pin 5000mAh, 6.7" AMOLED, Camera 50MP.
- iPhone 15 Pro: 28.990.000đ (gốc 31.990.000đ), A17 Pro, 8GB/256GB.
- Laptop ASUS TUF Gaming F15: 18.490.000đ (gốc 24.650.000đ),
  i5-12450H/8GB/512GB/RTX 3050.
- Tai nghe Sony WH-CH520: 890.000đ (gốc 1.150.000đ), Bluetooth 5.2, Pin 50h.
- Voucher `TECHZONE500` giảm 500.000đ (đơn ≥ 5.000.000đ, hết hạn 31/12/2026).
- 3 đơn mẫu có tổng tiền khớp với chi tiết đơn (đã tính tay trong `Data/SeedData.cs`).

## Ảnh sản phẩm — ảnh thật, không ảnh AI

`wwwroot/images/` chỉ có ảnh chụp thật lấy từ image-search:
- `xiaomi15.png` — ảnh thật Xiaomi 15 → dùng cho Xiaomi 15 5G.
- `iphone15pro.jpg` — ảnh thật iPhone 15 Pro → dùng cho iPhone 15 Pro.
- `rog-laptop.png` — ảnh laptop gaming ASUS (ROG Strix) → dùng tạm cho
  Laptop ASUS TUF Gaming F15 (cùng hãng ASUS, cùng dòng gaming).
- `earbuds.jpg` — ảnh thật tai nghe true wireless → dùng cho Tai nghe
  Xiaomi Buds 5 (đúng loại true wireless).

### Chỗ còn thiếu ảnh thật
- **9/13 sản phẩm dùng `placeholder.svg`** (Samsung A55, OPPO Reno11, Lenovo LOQ 15,
  iPad Gen 10, 2 đồng hồ, chuột Logitech, SSD 980 PRO, Sony WH-CH520).
  Lý do: chưa có ảnh hãng cho các sản phẩm này trong máy. Đặc biệt lưu ý
  **Sony WH-CH520 là tai nghe on-ear, không dùng ảnh `earbuds.jpg`** (tai nghe
  nhét tai) cho nó — dùng sai loại còn tệ hơn dùng placeholder.
- Muốn đủ ảnh: tải ảnh chính thức từ trang hãng / nhà phân phối cho từng sản phẩm,
  bỏ vào `wwwroot/images/`, thêm dòng `ProductImage` trong `SeedData.cs`
  (hoặc thêm form upload ảnh ở Admin — hiện Admin chưa có upload ảnh, chỉ quản lý
  tên/giá/tồn kho/thông số; xem "Chưa làm" bên dưới).

## Chưa làm (nằm ngoài scope sương sương)
- Upload ảnh sản phẩm ở trang Admin (hiện chỉ gán ảnh qua seed).
- Đăng nhập Google / Facebook, thanh toán thật (VNPay/MoMo), vận chuyển thật (GHN/GHTK).
- Wishlist, chat với shop, gợi ý AI, app mobile.
- Phân trang admin chưa có (danh sách sản phẩm/đơn hiện toàn bộ).
- Chuyển sang SQL Server: đổi connection string trong `appsettings.json` và
  `UseSqlite` → `UseSqlServer` trong `Program.cs`, thêm migration
  (`dotnet ef migrations add Init` rồi `dotnet ef database update`).

## Kiểm chứng (chưa chạy build)
- Máy viết code **không có dotnet** nên chưa chạy `dotnet build` / `dotnet run`.
  Trước khi demo, chạy `dotnet build` một lần trên máy có .NET 8 SDK để bắt lỗi
  biên dịch (nếu có) rồi mới `dotnet run`.
- Đã kiểm tra bằng mắt: mọi giá seed đều có trong `SeedData.cs`, tổng tiền 3 đơn
  mẫu đã tính khớp, ảnh gán đúng sản phẩm (trừ 2 trường hợp đã ghi rõ ở trên).
