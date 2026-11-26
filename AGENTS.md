# AGENTS.md — Thiết kế lại giao diện TechZone (Trang chủ, Danh sách, Chi tiết sản phẩm, Giỏ hàng, Thanh toán)

Đặt file này ở **thư mục gốc repo** (cùng cấp `TechZone.csproj`). Codex đọc file này trước khi làm việc.
Nhiệm vụ: thiết kế lại giao diện 5 trang storefront (3 trang catalog + luồng giỏ hàng, thanh toán, xác nhận đơn) theo brief bên dưới. **Chỉ đổi giao diện, không đổi logic.**
Hướng cho giỏ và thanh toán: **cơ bản, ít bước, chạy chắc** (mục 5.5 và 5.6), không thêm chức năng cần sửa server.

---

## 0. Cách làm việc

1. Làm **theo thứ tự tác vụ T0 → T8** (mục 6). Xong mỗi tác vụ: build, chạy test, commit riêng (`git commit -m "ui: T1 tokens + price tag + product card"`).
2. Trước khi viết code cho mỗi trang, viết **kế hoạch ngắn** (màu/font đã chốt ở mục 3, wireframe ASCII, điểm khác với bản cũ), rồi tự rà soát: *phần nào trông giống bản mặc định cho mọi shop điện tử thì sửa lại và ghi rõ đã sửa gì*. Sau đó mới code.
3. Không hỏi lại những gì file này đã quyết. Nếu thật sự mâu thuẫn hoặc thiếu dữ liệu, chọn phương án an toàn nhất, ghi vào báo cáo cuối (mục 8) và đi tiếp.
4. Dự án dùng tiếng Việt. Mọi chữ hiển thị cho người dùng viết bằng tiếng Việt, **viết hoa đầu câu** (sentence case), động từ rõ ràng.

### 0.5 Prototype tham chiếu

Prototype nằm **trong repo** để bạn đọc được (không phụ thuộc link ngoài; link online chỉ để con người xem nhanh):
- `docs/prototype-c.html`: trang chủ hướng C (hero bản vẽ chú thích, 3 chip nhu cầu, hoạt ảnh #1 đến #5). Online: https://claude.ai/artifact/GYAL4HJ6RG6KQGvaMndowk
- `docs/prototype-cart.html`: bố cục giỏ hàng, thanh toán, xác nhận. **Màu và chữ là bản cũ**; chỉ lấy bố cục và thứ tự bước, áp màu, chữ, quy tắc theo mục 4. Online: https://claude.ai/artifact/AXrdFijJVVhHPVzGsXuh79
Prototype là **tham chiếu hình ảnh**, **không sao chép nguyên mã**: mọi bất biến ở mục 3 vẫn thắng. Hai file này chỉ để đọc, không sửa, không đưa vào build. Chỉ có ở prototype và **không được giả lập trong repo**: dữ liệu sản phẩm mẫu, bản vẽ minh họa 3 mẫu, giỏ hàng lưu trong bộ nhớ trang, mã đơn ngẫu nhiên, thông báo "Đã thêm vào giỏ" chạy ngay khi bấm, kiểm tra form hoàn toàn ở client. Repo dùng dữ liệu và luồng POST/redirect hiện có.

---

## 1. Dự án

- **TechZone**: website bán đồ công nghệ. ASP.NET Core 8, EF Core + SQLite, Razor MVC, ASP.NET Identity.
- Không có file `.sln`. Chạy: `dotnet restore && dotnet run` (lần đầu tự tạo `techzone.db` và seed).
- Tài khoản demo chỉ bật qua biến môi trường: `DemoAccounts__Enabled=true`, `DemoAccounts__AdminPassword=...`, `DemoAccounts__CustomerPassword=...`. **Không ghi mật khẩu vào file nào được Git theo dõi.**
- Dữ liệu seed: 8 danh mục (Điện thoại, Laptop, Tablet, Tai nghe, Đồng hồ, Phụ kiện, Gaming, Linh kiện PC), 9 hãng, 13 sản phẩm. Ảnh sản phẩm ở `wwwroot/images`, model 3D `.glb` ở `wwwroot/models`.

## 2. Phạm vi và những thứ CẤM động vào

**ĐƯỢC sửa:**
- `Views/Home/Index.cshtml`, `Views/Home/_Banners.cshtml`, `Views/Home/_FlashSaleSlots.cshtml`
- `Views/Products/Index.cshtml`, `Views/Products/Details.cshtml`
- `Views/Shared/_ProductCard.cshtml`, `Views/Shared/_Layout.cshtml` (chỉ phần head/font/header/footer cần cho brief; header dính khi cuộn), `Views/Shared/_StockStatus.cshtml`, `Views/Shared/_InstallmentBadge.cshtml`, `Views/Shared/_RecentlyViewed.cshtml`
- View Giỏ hàng, Thanh toán, Xác nhận đơn của storefront. **Chưa biết tên file**: tìm bằng `grep -rl "cart-item" Views` và `grep -rliE "checkout|thanh-?toan|dat-?hang" Views`, ghi danh sách tìm được vào báo cáo. Không đụng `Views/Admin/` và `Views/Returns/`.
- `wwwroot/css/*` (trừ `admin.css`), `wwwroot/js/*` (trừ `admin.js`, `model-viewer.min.js`), `wwwroot/fonts/*`

**CẤM sửa:** `Controllers/`, `Services/`, `Models/`, `Data/` (kể cả Migrations), `appsettings.json`, `TechZone.csproj`, `TechZone.Tests*/`, `Views/Admin/`, `Views/Shared/_AdminLayout.cshtml`, `Views/Returns/`, `wwwroot/css/admin.css`, `wwwroot/js/admin.js`.
Một số test Admin đọc trực tiếp markup, nên không đụng vào Admin. Các test của Home/Listing/Details **không** đọc file `.cshtml` (đã kiểm tra), nhưng vẫn phải chạy test sau mỗi tác vụ.

## 3. Bất biến phải giữ nguyên (đổi là vỡ chức năng)

**Razor / server:**
- Mọi `asp-controller`, `asp-action`, `asp-route-*`, `asp-items`, `asp-all-route-data`, `asp-fragment`, `asp-antiforgery`.
- `name` của mọi `input/select/form`: `NhuCau`, `GiaDen`, `GiaTu`, `CategoryId`, `BrandId`, `Chip`, `Ram`, `DungLuong`, `TuKhoa`, `SapXep`, `Trang`, `productId`, `soLuong`, `variantId`, `comboId`, `returnUrl`.
- Các `ViewBag`/model đang dùng: `ViewBag.Hero`, `NoiBat`, `BanChay`, `DanhMuc`, `FlashSale`, `FlashSaleKetThuc`, `GiaHieuDung`, `Banners`, `TongSanPham`, `TongHang`, `TongDanhMuc`, `HeroSoDanhGia`, `HeroDiemTB`, `ProductFilterViewModel` (`KetQua`, `TongSo`, `TongTrang`, `TrangVuotGioiHan`, `GiaHieuDung`, …), `ViewBag.TuongTu`, `Combo*`, `ComboMon`, `ComboId`, `Model3d`, `GiaTheoVariant`, `GiaHienThi`, `FlashSaleApDung`, `TenKhuyenMai`, `CauHoi`, `RecentlyViewed`.
- `ViewData["ProductCardPrices"]`, `["ProductCardNeed"]`, `["CompareReturnUrl"]`, `["FlashRemaining"]` của `_ProductCard`.
- Các partial đang dùng: `_ProductCard`, `_StockStatus`, `_InstallmentBadge`, `_Banners`, `_FlashSaleSlots`, `_RecentlyViewed`, `_ThemeIcon`. Không viết lại logic của chúng, chỉ đổi markup/CSS.
- Cách tính sao: **chỉ từ review đã duyệt** (`Model.Reviews`), không dùng `DanhGiaTB`. Không có review → "Chưa có đánh giá".

**Id/class mà JS đang bám (giữ nguyên tên và vai trò):**
`#need-finder`, `#filter-toggle`, `#filter-panel`, `#filter-close`, `#catalog-results`, `#flash-sale`, `#khuyen-mai`, `.flash-countdown[data-ket-thuc]` với các thẻ `<b>` bên trong (`#cd-gio`, `#cd-phut`, `#cd-giay`), `#product-image`, `#product-viewer`, `#viewer-error`, `#product-3d`, `#btn-3d-show`, `#btn-3d-hide`, `#btn-3d-reset`, `#btn-3d-rotate`, `#btn-3d-fullscreen`, `.thumb-btn[data-full]`, `#variantId` (và `data-gia`, `data-gia-goc`, `data-installment-6`, `data-installment-12` trên option), `.price-big`, `.price-old`, `.detail-buy [data-installment]`, `#detail-buy-submit`, `form.qty`, `.qty-btn`, `.cart-item input[name="soLuong"]`, `#menu-toggle`, `#theme-toggle`, `#user-menu-btn`.
Trước khi đổi cấu trúc DOM của một khối, mở file JS tương ứng trong `wwwroot/js` và xác nhận selector vẫn khớp.

**Chung:** `lang="vi"`, skip link, `aria-*` đang có, `<html data-theme="light|dark">` do script đầu `<head>` đặt (không đổi cơ chế `localStorage('techzone-theme')`), không dùng CDN hay font ngoài, giữ trạng thái không-JS dùng được (form vẫn gửi được).

---

## 4. Design brief

### 4.1 Ý tưởng chủ đạo: "bản vẽ có chú thích"

Khách mua đồ công nghệ cần **hiểu món đồ trước khi trả giá**. Ý tưởng: sản phẩm có **đường chú thích** kéo ra từng **vùng** (màn hình, chip, pin), và chọn nhu cầu thì thông tin được nhấn theo nhu cầu đó. Đây là chức năng, không phải trang trí.
**Phạm vi áp dụng, có chủ ý hẹp:**
- **Trang chi tiết sản phẩm (nơi chính):** chú thích đặt lên **ảnh sản phẩm thật**, vì đây là chỗ khách đọc thông số (mục 5.3, khối 9).
- **Hero trang chủ:** bản vẽ minh họa theo danh mục do chip nhu cầu điều khiển (mục 5.1, khối 1).
- **Mọi nơi khác (danh mục, Flash Sale, thẻ sản phẩm, danh sách, giỏ, thanh toán) giữ yên tĩnh, kiểu thương mại điện tử rõ ràng.** Lưới kẻ ô **chỉ** nằm trong khối `.blueprint` ở hero và đầu trang chi tiết, không phải nền toàn trang, để trang nhiều thông tin dễ đọc. Đừng cố "rải" phong cách bản vẽ khắp site.
**Thẻ giá vàng** vẫn là component chính cho giá (4.5), "treo" vào ảnh/bản vẽ ở hero và chi tiết.
**Để hero không lạc lõng so với phần còn lại:** mọi khối khác dùng cùng token màu, chữ, bo góc, thẻ giá; chúng chỉ khác ở chỗ không có lưới và không có chú thích.
Prototype tham chiếu: `docs/prototype-c.html` (mục 0.5).

### 4.2 Màu (đổi **giá trị**, giữ **tên** token trong `design-tokens.css`; thêm token mới nếu cần)

| Vai trò | Giá trị | Dùng cho |
|---|---|---|
| Nền trang | `#F3F5F4` | `--color-background` (một màu phẳng, **không** có lưới) |
| Mực | `#16212B` | chữ, tiêu đề (`--color-foreground`) |
| Ngọc | `#0F6B5C` | nút hành động, link, focus (`--color-primary`, `--color-focus`) |
| Vàng nhãn | `#FFD23F` | **chỉ** trong thẻ giá (`--color-tag`), chữ trên đó dùng Mực (`--color-tag-ink: #16212B`) |
| Đỏ giảm giá | `#B3261E` | **chỉ** chip "−19%" |
| Viền sương | `#C9D1D8` | `--color-border` |
| Sàn chụp | `#E6EBE9` | nền ô ảnh sản phẩm (`--color-stage`) |
| Lưới bản vẽ | vạch `#DDE3E0` mỗi 32px | `--color-grid`; **chỉ** là `background-image` của `.blueprint` (hero, đầu trang chi tiết), không đặt lên `body` |
| Đường chú thích | Ngọc `#0F6B5C` | nét và chấm chú thích trên bản vẽ (`--color-primary`) |

Dark (`html[data-theme="dark"]`): nền `#0F161C`, vạch kẻ `#1C2831`, thẻ `#17212B`, chữ `#E8EEF2`, Ngọc sáng `#4FD1B5` cho link/focus/chú thích, sàn chụp `#17212B`. Vàng nhãn và chữ Mực trên thẻ giá **giữ nguyên** ở cả hai chế độ.
Các token cũ (`--color-accent`, `--color-accent-bg`, `--color-teal`, `--color-success-*`, `--color-warning-*`…): `grep` xem đang dùng ở đâu, ánh xạ sang palette mới, bảo đảm tương phản **WCAG AA** (chữ thường ≥ 4.5:1) ở cả sáng và tối. Không dùng gradient làm trang trí. Không dùng `#000`/`#111`; chữ đen là Mực.

### 4.3 Chữ

- **Tiêu đề và nội dung/UI:** IBM Plex Sans (400, 600, 700). **Chú thích thông số, giá, đồng hồ đếm ngược:** IBM Plex Mono (500, 700). Chỉ hai họ này; cả hai có subset tiếng Việt.
- Khai báo `--font-sans` (nội dung, fallback `'Noto Sans Variable', system-ui, sans-serif`) và `--font-mono` (chú thích, giá); giữ `--font-display` trỏ về `--font-sans` để CSS cũ không vỡ.
- Font **tự host** trong `wwwroot/fonts` (woff2, subset `vietnamese` + `latin`, kèm file giấy phép OFL như `NotoSans-OFL.txt`). `font-display: swap`. Thêm `<link rel="preload">` cho 1–2 file chính trong `_Layout`.
- **Nếu không tải được font** (không có mạng): KHÔNG bịa file. Khai báo `@font-face` kèm TODO trong CSS, dùng fallback Noto Sans đang có, và ghi vào báo cáo cuối danh sách file font cần bổ sung và tên gói gợi ý (`@fontsource/ibm-plex-sans`, `@fontsource/ibm-plex-mono`; xác nhận có subset `vietnamese`). Nếu IBM Plex Mono thiếu ký tự tiếng Việt cần dùng, chỉ dùng nó cho chữ số và nhãn ngắn, còn lại dùng IBM Plex Sans.
- Giá: `font-variant-numeric: tabular-nums`. Độ rộng dòng nội dung < 80 ký tự. Thang chữ rõ ràng (ví dụ 14 / 16 / 20 / 28 / 40), tiêu đề `line-height` chặt, nội dung ~1.55.

### 4.4 Hình khối, bóng, chuyển động

- Bo góc **theo cấp bậc**: ô ảnh 14px, thẻ giá 6px (có khấc), nút 10px, chip dạng viên thuốc. Không dùng một radius cho mọi thứ.
- **Thẻ sản phẩm không viền, không bóng.** Bóng chỉ dùng cho thanh dính (sticky bar) và dropdown.
- Ảnh sản phẩm đặt trên "sàn chụp" `--color-stage`, thử `mix-blend-mode: multiply` để ảnh nền trắng hòa vào (kiểm tra trên cả 13 ảnh; tắt nếu ảnh nào bị xấu). Đặt `width/height` hoặc `aspect-ratio` để không nhảy layout. Dùng `loading="lazy"` trừ ảnh hero.
- **Hoạt ảnh là điểm nhấn của thiết kế, nhưng theo bộ quy tắc ở mục 4.4b.** Vẫn **bỏ**: hover-zoom 1.06, hover transition trên mọi thẻ, fade-slide-up cho mỗi section, hiệu ứng nhảy/nảy không gắn với việc gì. Thông báo ngắn sau khi thêm vào giỏ chỉ khi hệ thống đã có cơ chế thông báo như `TempData`. Không giả lập thông báo thành công trước khi server trả về.

### 4.4b Hoạt ảnh (đúng 5, không làm thêm)

Một hoạt ảnh chỉ tồn tại nếu giúp khách **hiểu** hoặc **nhận ra thay đổi**. Thứ không đạt tiêu chí đó thì không làm, kể cả khi thấy đẹp.

**Kỹ thuật chung**
- Mọi hoạt ảnh nằm trong `@media (prefers-reduced-motion: no-preference)`. **Trạng thái mặc định của CSS là trạng thái cuối** (đã vẽ xong, đã hiện); hoạt ảnh chỉ đi từ trạng thái đầu về trạng thái cuối. Không JS hoặc bật giảm chuyển động thì trang vẫn đầy đủ.
- Chỉ animate `transform`, `opacity`, `stroke-dashoffset`. **Cấm** animate `width`, `height`, `top`, `left`, `box-shadow`, `filter`.
- Độ dài: phản hồi thao tác 150 đến 300ms; vẽ nét 700 đến 900ms; chuỗi hero tối đa 2s. Easing `cubic-bezier(.4,0,.2,1)` cho vẽ/kéo, `cubic-bezier(.2,.8,.3,1.1)` cho vật rơi vào chỗ.
- Vẽ nét SVG: `pathLength="1"` trên từng `rect/path/circle/line`, `stroke-dasharray:1`, animate `stroke-dashoffset` từ `1` về `0`, so le theo `--i` (`animation-delay: calc(var(--i)*120ms)`). Hình tô màu tách khỏi nét.
- Hoạt ảnh kích hoạt khi cuộn tới dùng `IntersectionObserver` (một lần, rồi `unobserve`); thiếu API thì hiện luôn trạng thái cuối.

| # | Ở đâu | Chuyển động | Mục đích |
|---|---|---|---|
| 1 | Bản vẽ hero; chú thích trang chi tiết | **Vẽ nét**, rồi **đường chú thích kéo ra**, chấm/vùng hiện, chữ trượt 8px + fade | Dạy khách đọc thông số theo vị trí |
| 2 | Thẻ giá (hero, chi tiết) | **Rơi vào chỗ** sau #1: từ −18px, xoay −3°, nảy nhẹ về 0 (≈550ms), một lần | Nhấn giá đúng lúc khách đã hiểu món đồ |
| 3 | Chip nhu cầu | Hero: chọn chip thì bản vẽ **vẽ lại** theo sản phẩm hợp nhất. Chi tiết: thông số liên quan nhu cầu nổi lên, phần còn lại mờ (opacity, 200ms). Chip đổi màu 200ms | Cho thấy nhu cầu điều khiển thông tin |
| 4 | Thanh còn suất Flash Sale | Từ 0 đến mức thật bằng `transform: scaleX`, 900ms, khi cuộn tới | Cho thấy độ khan hàng |
| 5 | Đồng hồ Flash Sale | Chữ số nào đổi thì **lăn lên** (translateY 40% + fade, 300ms); giữ `data-ket-thuc` và 3 thẻ `<b>` | Cho thấy thời gian đang chạy |

**Không làm** (đã cân nhắc và bỏ): tự chuyển sản phẩm ở hero (kéo theo JS và nút tạm dừng mà không có lợi cho khách); vẽ lại icon khi rê chuột hoặc khi cuộn tới ở danh mục và thẻ sản phẩm; thẻ giá đung đưa khi hover; mọi hoạt ảnh ở giỏ hàng và thanh toán (số lượng, tổng tiền cập nhật tức thì); hoạt ảnh lặp vô hạn; hoạt ảnh che nội dung hoặc chặn thao tác (mọi nút phải bấm được ngay).

**Kiểm tra hoạt ảnh** (thêm vào T8): bật "Reduce motion" của hệ điều hành thì không còn chuyển động nào, trang đầy đủ; tab bàn phím không mất focus; Performance tab không có layout shift do hoạt ảnh (CLS = 0); thử CPU throttle 4x vẫn mượt.

### 4.5 Component bắt buộc: `.price-tag`

Thêm vào `product-cards.css` (hoặc `common.css`) và dùng ở card, hero, chi tiết sản phẩm. Phác thảo:

```css
.price-tag{
  position:relative; display:inline-flex; flex-wrap:wrap; align-items:baseline; gap:.15rem .6rem;
  padding:.35rem .8rem .35rem 1.15rem;
  background:var(--color-tag); color:var(--color-tag-ink);
  border-radius:6px;
  font-family:var(--font-display); font-variant-numeric:tabular-nums;
}
.price-tag::before{ /* khấc / lỗ treo */
  content:""; position:absolute; left:.45rem; top:50%; width:.42rem; height:.42rem; margin-top:-.21rem;
  border-radius:50%; background:var(--color-background);
}
.price-tag .price-amount{ font-weight:800; font-size:1.25rem; }
.price-tag s{ font-size:.85rem; opacity:.75; }
.chip-giam{ background:#B3261E; color:#fff; border-radius:999px; padding:.1rem .5rem; font-size:.8rem; font-weight:700; }
```
Ở trang chi tiết, giữ class `.price-big` và `.price-old` bên trong thẻ giá để `product-detail.js` cập nhật được giá khi đổi phiên bản.
Ở giỏ hàng, thanh toán và xác nhận đơn: thẻ giá vàng **chỉ dùng cho "Tổng cộng"**. Đơn giá và thành tiền từng dòng là chữ thường, để thẻ vàng giữ vai trò điểm nhấn duy nhất.

### 4.6 Những thứ phải TRÁNH (dấu hiệu "trang sinh tự động")

- Nền kem + serif tương phản cao + màu đất nung/cam đất; nền đen + một màu nhấn xanh/cam chói; kiểu báo với đường kẻ mảnh và cột dày đặc.
- Bộ "thẻ SaaS": nội dung chia thành thẻ giống hệt nhau, cùng radius, cùng bóng xám, nền gradient.
- Nhãn IN HOA giãn chữ phía trên mỗi tiêu đề (eyebrow); đánh số 01/02/03 khi nội dung không phải thứ tự. **Monospace chỉ được dùng** cho chú thích thông số trên bản vẽ, giá và đồng hồ đếm ngược; chip nhu cầu và nút dùng Sans; không dùng cho nhãn dữ liệu nhỏ khác hay đoạn văn.
- Mũi tên "→" nối vào cuối link/nút; chuỗi thông tin nối bằng dấu chấm giữa ("A · B · C"); nhãn dạng "TỪ — mảnh".
- Tô riêng một từ trong tiêu đề (đậm/nghiêng/đổi màu).
- Khối số liệu "số to + nhãn nhỏ + gradient".
- Chỉ **đánh số khi nội dung thật là thứ tự**: ở đây chỉ mục "Bán chạy" (xếp hạng) được đánh số.
- Cần tránh cả những chỗ bản hiện tại đang có: `grep` trong 3 view và `_ProductCard` các ký tự `→`, ` · `, `＋`, `♡`, `♥` và thay bằng bố cục/nút icon có `aria-label`.

### 4.7 Viết chữ (copy)

Chữ là nội dung thiết kế. Ngắn, rõ, theo góc nhìn người dùng, câu chủ động. Nút nói đúng việc sẽ xảy ra ("Thêm vào giỏ", không phải "Gửi"); một hành động giữ **cùng một tên** xuyên suốt luồng. Thông báo lỗi/rỗng nói rõ chuyện gì và làm gì tiếp, không xin lỗi, không mơ hồ. Không viết khẩu hiệu quảng cáo; mô tả cụ thể. Giữ nguyên các chữ "(giả lập)" cạnh trả góp 0%.

---

## 5. Cấu trúc các trang (5.1 đến 5.3: catalog, 5.4: thẻ sản phẩm, 5.5 và 5.6: giỏ hàng, thanh toán)

### 5.1 Trang chủ (`Views/Home/Index.cshtml`, `home.css`, `market-theme.css`)

```
[Header: logo | tìm kiếm ............ | So sánh  Yêu thích  Giỏ  Tài khoản  ☾]
┌─ .blueprint (lưới 32px, toàn bề ngang) ──────────────────────────┐
│ Xem rõ thông số trước khi chọn.                                   │
│ [Làm việc] [Chụp ảnh] [Nghe nhạc]  Ngân sách (…)(…)  [Xem sản phẩm phù hợp]
│                                                                   │
│  chú thích ─── ◯ ┌─────────────┐ ◯ ─── chú thích   (bản vẽ minh họa)│
│                  │   sản phẩm  │                                   │
│  chú thích ─── ◯ └─────────────┘ ◯ ─── chú thích                   │
│  Xiaomi 15 5G  [thẻ giá treo]                                      │
└───────────────────────────────────────────────────────────────────┘
Một câu cam kết (chính hãng, bảo hành 24 tháng, trả góp 0% (giả lập))
Danh mục: ◻ ◻ ◻ ◻ ◻ ◻ ◻ ◻  (cuộn ngang)
Flash Sale:  [đồng hồ]  [khung giờ chips]   [SP][SP][SP][SP]
Ưu đãi:      [banner][banner]
Nổi bật:     [   SP lớn   ][SP][SP]
             [   SP lớn   ][SP][SP]
Bán chạy:    1 SP   2 SP   3 SP   4 SP   5 SP
```

| # | Khối | Dữ liệu thật | Yêu cầu |
|---|---|---|---|
| 1 | Quầy tư vấn + hero | `ProductNeed.All`, `ViewBag.Hero`, `GiaHieuDung`, `HeroSoDanhGia/HeroDiemTB` | **Không chia 2 cột cứng**: tiêu đề, chip nhu cầu, ngân sách và nút xếp thành một dải phía trên; bản vẽ chiếm **toàn bề ngang** khối `.blueprint` bên dưới (mobile xếp dọc, chip lên trước). Chip nhu cầu **dựng từ `ProductNeed.All`, không viết cứng số lượng**. Giữ `form#need-finder`, `NhuCau` (radio, `required`), `GiaDen`. Ngân sách đổi từ `<select>` thành nhóm radio dạng chip gửi cùng `name="GiaDen"` (giá trị: rỗng, 2000000, 15000000, 25000000). **Bản vẽ hero** (4.4b #1 đến #3): minh họa SVG **theo danh mục** của sản phẩm hợp nhất với nhu cầu đang chọn, kèm chữ "Bản vẽ minh họa" vì mọi máy cùng danh mục dùng chung một mẫu; nơi khách xem đúng máy mình định mua là trang chi tiết (5.3, khối 9). Chú thích trỏ vào **vùng** (vòng tròn khoảng 28px), không cố chỉ đúng từng chi tiết. Thông số lấy từ dữ liệu sản phẩm hiện có; **chưa biết repo lưu thế nào**: ở T0 `grep -rniE "ProductSpecs|spec|thongso" Models Views` và ghi lại. Thiếu dữ liệu thì ẩn chú thích, chỉ còn bản vẽ + thẻ giá; **không bịa thông số, không đổi model**. **Không tự chuyển sản phẩm**; chỉ đổi khi khách chọn chip. Mỗi bản vẽ có `role="img"` và `aria-label` nêu tên + thông số; trên mobile chú thích thành danh sách chữ dưới bản vẽ. Thẻ giá `.price-tag` treo dưới bản vẽ. Nút hero ghi tên cụ thể ("Xem Xiaomi 15 5G"), bỏ "→". Câu đánh giá hero chỉ hiện khi có review đã duyệt (giữ logic hiện tại). **Gợi ý trực tiếp:** dưới nút gửi có vùng `aria-live="polite"` cập nhật khi đổi nhu cầu hoặc ngân sách, hiện "N sản phẩm phù hợp" và tối đa 3 dòng (ảnh nhỏ, tên, thẻ giá). Dữ liệu lấy từ các sản phẩm **đã có trên trang** (`NoiBat`, `BanChay`, `FlashSale`) qua thuộc tính `data-*` thêm vào markup; không thêm endpoint, không đổi controller. Nếu markup không đủ dữ liệu nhu cầu thì lọc theo giá và ghi vào báo cáo. Hết kết quả thì nói rõ và gợi ý nâng ngân sách. Không JS: form vẫn gửi bình thường. |
| 2 | Cam kết | chuỗi tĩnh | **Một dòng văn bản**, thay thanh `proof-strip`. Bỏ định dạng "số to + nhãn". Có thể dùng `ViewBag.TongSanPham/TongHang` trong câu văn nếu tự nhiên. |
| 3 | Danh mục | `ViewBag.DanhMuc` (mỗi mục có `Products`) | Hàng ngang cuộn (scroll-snap), mỗi ô: ảnh đại diện (sản phẩm bán chạy nhất của danh mục, như cách `need-finder` đang chọn) + tên. Link `asp-controller="Products" asp-action="Index" asp-route-CategoryId`. |
| 4 | Flash Sale | `FlashSale`, `FlashSaleKetThuc`, `_FlashSaleSlots` | Trái: tiêu đề + đồng hồ đếm ngược dạng một dòng; phải: lưới 4 sản phẩm dùng `_ProductCard` với `FlashRemaining`. Giữ `#flash-sale`, `.flash-countdown[data-ket-thuc]`, `data-countdown-ended`, và 3 thẻ `<b>` (`cd-gio/cd-phut/cd-giay`). Khung giờ là hàng chip. Trạng thái không có flash sale vẫn hiển thị `_FlashSaleSlots`. Dưới mỗi thẻ có **thanh còn suất** (`role="img"`, `aria-label="Còn N trên M suất"`) dựng từ `FlashRemaining` và tổng suất nếu dữ liệu hiện có cung cấp; không có tổng suất thì bỏ thanh, chỉ giữ dòng "Còn N suất Flash Sale". |
| 5 | Ưu đãi | `ViewBag.Banners` | Dải banner ngang; giữ `#khuyen-mai` và link tới `#flash-sale`. Chỉ render khi có banner. Mỗi banner là một khối màu Ngọc, tiêu đề + một dòng phụ + liên kết nói đúng việc ("Xem laptop trả góp", không "Xem thêm"). Banner trả góp giữ nhãn "(giả lập)". |
| 6 | Nổi bật | `ViewBag.NoiBat` | Sản phẩm đầu tiên là ô lớn (chiếm 2 cột × 2 hàng ≥1024px), phần còn lại ô thường. Số lượng thay đổi, nên xử lý đúng khi ít hơn 2 sản phẩm (về lưới thường). Link "Xem tất cả sản phẩm" không có "→". |
| 7 | Bán chạy | `ViewBag.BanChay` | Hàng ngang xếp hạng, **đánh số 1…n** (đây là thứ tự thật), hiển thị "Đã bán N". |

### 5.2 Danh sách sản phẩm (`Views/Products/Index.cshtml`, `catalog.css`, `filter-toggle.js`)

```
Trang chủ / Ngành hàng
Khám phá ngành hàng                       [Tất cả][Làm việc][Chụp ảnh][Nghe nhạc]
13 sản phẩm        [Bộ lọc (2)]   Sắp xếp [Mặc định ▾] [Áp dụng]
(chip đang lọc ×) (chip ×)
┌ Bộ lọc (dính) ─┐ ┌ lưới 4 cột ─────────────────────────────────┐
│ Danh mục       │ │ [SP] [SP] [SP] [SP]                         │
│ Hãng  ◯◯◯      │ │ [SP] [SP] [SP] [SP]                         │
│ Chip / RAM …   │ │         phân trang                          │
│ Giá: 2 ô + mức │ └─────────────────────────────────────────────┘
└────────────────┘
```

| # | Khối | Yêu cầu |
|---|---|---|
| 1 | Đầu trang | Breadcrumb, `h1` (`need?.Title ?? categoryTitle ?? "Khám phá ngành hàng"`), mô tả `need.Departments`. **Tab nhu cầu** là link `asp-action="Index" asp-route-NhuCau` (Tất cả bỏ `NhuCau`), tab đang chọn có `aria-current="page"`. Bỏ "→". |
| 2 | Thanh công cụ | Một hàng: số kết quả (`role="status"`), nút `#filter-toggle` (có badge số bộ lọc, chỉ đếm bộ lọc), `#SapXep` với `form="filter-panel"`, nút Áp dụng. |
| 3 | Chip đang lọc | Giữ logic `boLoc`; mỗi chip là link gỡ bộ lọc, `aria-label="Gỡ bộ lọc …"`. |
| 4 | Bộ lọc | `form#filter-panel` method GET. ≥1024px: cột trái dính. Mobile: ngăn kéo do `filter-toggle.js` điều khiển, nút `#filter-close`. **Hãng, Chip, RAM, Dung lượng** chuyển thành nhóm chip một lựa chọn (`<fieldset>` + radio, mục "Tất cả" có `value=""`), giữ đúng `name` (`BrandId`, `Chip`, `Ram`, `DungLuong`) và giá trị lấy từ cùng `SelectList` hiện tại. **Danh mục** giữ `<select id="CategoryId">`. Giá: giữ `GiaTu/GiaDen`, thêm các mức nhanh (dưới 2 triệu / 2–15 triệu / 15–25 triệu / trên 25 triệu) là nút nhỏ điền hai ô giá bằng JS (không-JS vẫn dùng được hai ô). Giữ `TuKhoa`, `NhuCau` ẩn. |
| 5 | Lưới | `Model.KetQua`, `cardData` y nguyên. 4 cột ≥1280px, 3 cột ≥1024px, 2 cột còn lại. Giữ `#catalog-results` (`tabindex="-1"`) và khối `catalog-compare`. |
| 6 | Thẻ sản phẩm | Xem 5.4. |
| 7 | Phân trang + rỗng | Giữ nguyên logic: nút Trước/Sau, windowing có dấu `…`, `aria-current`, clamp trang, 3 trạng thái (vượt trang, lọc không khớp, rỗng thật). Chỉ đổi giao diện. |

### 5.3 Chi tiết sản phẩm (`Views/Products/Details.cshtml`, `product-details.css`, `product-detail.js`)

```
Trang chủ / Danh mục / Tên sản phẩm
┌ gallery (dính) ─────────┐ ┌ khối mua ─────────────────────────────┐
│ [ảnh lớn / 3D]          │ │ Tên sản phẩm (h1)    ★ 4.5 (2)        │
│ [ảnh][ảnh][ảnh]  [Ảnh|3D]│ │ ┌ thẻ giá ──────────────┐ -19%       │
└─────────────────────────┘ │ │ 12.990.000đ  15.990.000đ│            │
                            │ └────────────────────────┘            │
                            │ Phiên bản: (256GB)(512GB)             │
                            │ Số lượng [- 1 +]  [Thêm vào giỏ]      │
                            │ Bảo hành 24 tháng | Miễn phí ship …   │
                            │ Trả góp 0% (giả lập)   So sánh  Yêu thích│
                            └───────────────────────────────────────┘
[Thông số] [Mua kèm] [Đánh giá] [Hỏi đáp] [Tương tự]   (thanh neo dính)
… các section …
(mobile) thanh dính đáy: giá + [Thêm vào giỏ]
```

| # | Khối | Yêu cầu |
|---|---|---|
| 1 | Gallery | Cột trái, `position: sticky` ≥1024px. Ảnh chính `#product-image` trên sàn chụp, dải ảnh nhỏ `.thumb-btn[data-full]`, chuyển **Ảnh / Xem 3D** bằng các id `#btn-3d-*`, `#product-viewer`, `#viewer-error`, `#product-3d`. Không đổi `product-3d.js`/`product-detail.js` trừ khi bắt buộc. Có `alt` cho ảnh. |
| 2 | Khối mua | Cột phải. `h1`, sao tính từ review đã duyệt (link neo tới #danh-gia), thẻ giá lớn (`.price-tag` chứa `.price-big`, `.price-old`), chip −% khi có giảm, tên khuyến mãi/flash nếu có. **Phiên bản**: giữ `<select id="variantId" name="variantId">` cùng các `data-*` trên `<option>`; thêm bộ chip do JS dựng từ select (chọn chip → đặt giá trị select → `dispatchEvent(new Event('change', {bubbles:true}))` để `product-detail.js` tiếp tục đổi giá). Không-JS vẫn dùng select. Số lượng giữ `name="soLuong"` (`min=1`, `max=TonKho`). Nút `#detail-buy-submit`. Ba cam kết (bảo hành, miễn phí ship, đổi trả) là danh sách, không nối bằng "·". `_InstallmentBadge` giữ nhãn "(giả lập)". So sánh / Yêu thích giữ form và logic `daYeuThich` (Add/Remove). |
| 3 | Thanh mua dính (mobile) | Cố định đáy, hiện giá hiệu dụng + nút "Thêm vào giỏ" gửi **cùng form** (dùng thuộc tính `form=`), che khi khối mua chính còn trong màn hình (IntersectionObserver, có fallback). Chừa `padding-bottom` và `env(safe-area-inset-bottom)`. |
| 4 | Thanh neo | Link neo `#thong-so`, `#mua-kem`, `#danh-gia`, `#hoi-dap`, `#tuong-tu`; dính dưới header; mục đang xem có `aria-current`. Là điều hướng, không phải accordion. |
| 5 | Mua kèm | Giữ toàn bộ logic `Combo*`, `ComboMon`, `comboId`. Bố cục: hàng sản phẩm nối bằng dấu cộng (dùng phần tử trang trí `aria-hidden`), tổng combo ở cuối. |
| 6 | Thông số | `ProductSpecs` thành bảng 2 cột (`<dl>` hoặc `<table>` có `<th scope="row">`), nhóm theo loại, bọc `.table-wrap` nếu là bảng. |
| 7 | Đánh giá | Trái: điểm trung bình + số đánh giá (từ review đã duyệt). Phải: danh sách. Form đánh giá chỉ hiện khi đăng nhập (giữ logic). Không hiển thị số sao khi 0 review. |
| 8 | Hỏi đáp, Tương tự, Đã xem | Giữ nội dung và form; tương tự dùng `_ProductCard`; đổi giao diện cho đồng bộ. |
| 9 | Chú thích thông số trên ảnh | **Nơi chính của ý tưởng "bản vẽ có chú thích".** Đặt ở đầu trang trong `.blueprint` (lưới 32px), quanh **ảnh sản phẩm thật** (không vẽ lại sản phẩm). Mỗi thông số trỏ tới một **vùng** (vòng tròn khoảng 28px) theo **loại thông số** của `ProductSpecs` (đã nhóm theo loại: màn hình, chip, pin…) ánh xạ vào vài vùng cố định theo danh mục; loại nào không ánh xạ được thì chỉ hiện trong danh sách chữ cạnh ảnh, **không đoán**. Nếu URL có `NhuCau`, thông số thuộc loại liên quan được nổi lên, còn lại mờ (4.4b #3); bảng nhu cầu → loại thông số là một khai báo nhỏ trong code, ghi vào báo cáo để người duyệt xác nhận. Ảnh là placeholder hoặc không có thông số thì ẩn chú thích. Mobile: danh sách chữ dưới ảnh, bấm một dòng thì vùng tương ứng sáng lên. **Không sửa `product-detail.js` hiện có**: thêm `product-annotations.js` riêng. Chú thích trong `<model-viewer>` chỉ thử khi model đủ tốt (repo hiện có ít model 3D và đều chung chung), **không bắt buộc**. |

### 5.4 Thẻ sản phẩm (`Views/Shared/_ProductCard.cshtml`, `product-cards.css`)

- Ô ảnh trên sàn chụp, tỉ lệ 4:3, link tới `Details`; chip −% (đỏ) ở góc nếu có giảm.
- Tên sản phẩm tối đa 2 dòng (`line-clamp`).
- **Thông số: mỗi thông số một dòng riêng** (tối đa 2), không nối bằng "·".
- Thẻ giá `.price-tag` (giá hiệu dụng + giá gạch nếu có).
- Tồn kho: giữ `<partial name="_StockStatus" />`; với flash sale giữ dòng "Còn N suất Flash Sale".
- Sao + số đánh giá (từ `Model.Reviews`); 0 đánh giá → "Chưa có đánh giá". "Đã bán N" là dòng phụ riêng, không nối bằng dấu chấm.
- Nút chính "Thêm vào giỏ" (disabled + "Hết hàng" khi `TonKho <= 0`). **So sánh** và **Yêu thích** là nút icon (SVG nội tuyến, không dùng ký tự `＋ ♡`) có `aria-label` đầy đủ chứa tên sản phẩm; giữ form, `returnUrl`, `asp-*`.
- Không viền, không bóng, không hover-zoom. Focus-visible rõ.

### 5.5 Giỏ hàng (view tìm ở mục 2)

Luồng: `[Giỏ hàng] ---> [Thanh toán, một trang] ---> [Xác nhận đơn]`. Hướng **cơ bản, ít bước, chạy chắc**: khách hiểu ngay mình trả bao nhiêu, cho cái gì, và không gặp lỗi bất ngờ.

```
Giỏ hàng (thanh bước: Giỏ hàng | Thanh toán | Xác nhận)
┌ danh sách ──────────────────────────┐ ┌ tóm tắt (dính ≥1024px) ─┐
│ [ảnh] Tên sản phẩm                   │ │ Tạm tính (n sản phẩm)   │
│       [- 1 +]  Xóa        12.990.000đ│ │ Phí vận chuyển          │
│       Chỉ còn 3 sản phẩm trong kho   │ │ Tổng cộng [thẻ giá]     │
│ [ảnh] …                              │ │ [Tiến hành thanh toán]  │
└──────────────────────────────────────┘ └─────────────────────────┘
```

| # | Khối | Yêu cầu |
|---|---|---|
| 1 | Dòng sản phẩm | Ảnh trên sàn chụp (đúng tỉ lệ, có `alt`), tên là link tới `Details`, bộ số lượng giữ `form.qty`, `.qty-btn`, `.cart-item input[name="soLuong"]` (`min=1`, `max=TonKho`), nút **Xóa** giữ form và action hiện có, thành tiền dòng là chữ thường. Nút đổi số lượng có `aria-label` chứa tên sản phẩm. |
| 2 | Tồn kho | Không cho vượt `TonKho`, khi chạm trần hiện "Chỉ còn N sản phẩm trong kho". Lỗi do server trả về hiển thị gần dòng bị lỗi, nói rõ sản phẩm nào và cần làm gì. |
| 3 | Tóm tắt | Cột phải, `position: sticky` ≥1024px. Tạm tính, phí vận chuyển và tổng lấy từ dữ liệu hiện có (không tự tính lại logic giá). Dòng "Mua thêm … để được miễn phí vận chuyển" chỉ hiện nếu hệ thống đã có ngưỡng này. **Tổng cộng** nằm trong `.price-tag`. Nút "Tiến hành thanh toán". |
| 4 | Giỏ trống | Một câu nói rõ trạng thái và nút "Tiếp tục mua sắm". |
| 5 | Mobile | Tóm tắt xuống dưới danh sách. Thanh dính đáy hiện tổng cộng + nút thanh toán, chừa `env(safe-area-inset-bottom)`. |

### 5.6 Thanh toán và Xác nhận đơn

- **Thanh toán trên một trang**, 2 cột ≥1024px: trái là thông tin nhận hàng và phương thức thanh toán, phải là tóm tắt như 5.5 (chỉ đọc). Phía trên có thanh bước 3 mục; đánh dấu bước hiện tại bằng `aria-current="step"` (đây là thứ tự thật nên được đánh dấu).
- **Trường nhập:** dùng đúng `name` và validation phía server hiện có (xác nhận bằng `grep`, **không đổi tên**). Mỗi trường có `label` hiển thị, `autocomplete` phù hợp, và thông báo lỗi gắn `aria-describedby`. Thông báo cụ thể ("Nhập số điện thoại gồm 10 chữ số, bắt đầu bằng 0."), không xin lỗi, không mơ hồ. Lỗi đầu tiên được focus. Không JS: form vẫn gửi và lỗi server vẫn hiện.
- **Phương thức thanh toán:** nhóm radio, giữ các lựa chọn và giá trị đang có. Mỗi lựa chọn có một dòng mô tả. Giữ nhãn "(giả lập)" nếu phương thức là giả lập.
- **Nút:** "Đặt hàng" (cùng một tên xuyên suốt luồng), kèm liên kết "Quay lại giỏ hàng". Không bấm đôi được khi đang gửi (nếu đã có cơ chế).
- **Xác nhận đơn:** "Đặt hàng thành công", mã đơn, danh sách sản phẩm và tổng cộng (thẻ giá), thông tin giao đến, phương thức thanh toán, nút "Tiếp tục mua sắm". Chỉ hiển thị dữ liệu đơn có thật; **không bịa** thời gian giao hay cuộc gọi xác nhận nếu hệ thống không làm.
- **Hoãn, không làm trong đợt này** (cần sửa `Controllers/`, `Services/`, `Models/` nên ngoài phạm vi): mã giảm giá, mua kèm trong giỏ, lưu nhiều địa chỉ, nhiều cổng thanh toán, theo dõi đơn nâng cao. Ghi vào báo cáo như đề xuất cho đợt sau.

---

## 6. Tác vụ (làm đúng thứ tự, commit riêng từng bước)

**T0 — Baseline.** Chạy lệnh ở mục 7 để xác nhận build và test xanh **trước khi sửa** (nếu môi trường không có .NET hoặc mạng, ghi rõ và đi tiếp, vẫn phải kiểm tra tĩnh bằng `grep`). `grep` các token trong `design-tokens.css` đang dùng ở đâu. Liệt kê CSS mà mỗi trang nạp (`_Layout` + `@section Styles`).
*Nghiệm thu:* có ghi chú baseline trong báo cáo.

**T1 — Token, font, thẻ giá, thẻ sản phẩm.** Mục 4 + 5.4. Cập nhật `design-tokens.css` (sáng + tối), thêm `@font-face`, `.price-tag`, `.chip-giam`, viết lại `_ProductCard.cshtml` và `product-cards.css`. Làm lại `_StockStatus` nếu cần giao diện.
*Nghiệm thu:* Home và Listing vẫn render; thẻ sản phẩm đúng 5.4; không còn `→`, ` · `, `＋`, `♡` trong `_ProductCard`.

**T2 — Trang chủ.** Mục 5.1.
*Nghiệm thu:* đủ 7 khối; `form#need-finder` gửi đúng `NhuCau`/`GiaDen`; đồng hồ đếm ngược vẫn chạy; không có `proof-strip` kiểu "số to + nhãn"; không có `→`.

**T3 — Danh sách sản phẩm.** Mục 5.2.
*Nghiệm thu:* lọc/sắp xếp/phân trang hoạt động như cũ (URL query không đổi tên tham số); ngăn kéo lọc mở/đóng ở mobile; chip nhóm hoạt động cả khi tắt JS.

**T4 — Chi tiết sản phẩm.** Mục 5.3, gồm khối 9 (chú thích thông số).
*Nghiệm thu:* đổi phiên bản vẫn đổi giá (`.price-big`, `.price-old`, trả góp); 3D bật/tắt được; thêm vào giỏ gửi đúng `productId`, `variantId`, `soLuong`; combo và đánh giá không đổi hành vi.

**T5 — Giỏ hàng.** Mục 5.5. Tìm và ghi lại các view liên quan (mục 2), mở JS bám vào `.cart-item`/`form.qty`/`.qty-btn` để xác nhận selector còn khớp.
*Nghiệm thu:* đổi số lượng, xóa, giỏ trống hoạt động như cũ; không vượt `TonKho`; thẻ giá vàng chỉ ở "Tổng cộng"; không còn `→`, ` · ` trong các view này.

**T6 — Thanh toán và Xác nhận đơn.** Mục 5.6.
*Nghiệm thu:* gửi đơn đúng như trước (cùng `name`, cùng action, cùng antiforgery); lỗi hiển thị cụ thể và focus đúng ô; trang xác nhận chỉ hiện dữ liệu có thật; không-JS vẫn đặt được hàng.

**T7 — Dark mode và đồng bộ.** Kiểm tra năm trang (thêm Giỏ hàng, Thanh toán) ở `data-theme="dark"`; `_Layout` header/footer hài hòa với palette mới (không đổi cấu trúc điều hướng).
*Nghiệm thu:* tương phản AA ở cả hai chế độ; không còn màu tím chàm cũ (`#4338CA`, `rgb(67 56 202 …)`) trong các CSS storefront.

**T8 — Kiểm tra cuối.** Mục 7 và 8, cộng danh sách "Kiểm tra hoạt ảnh" ở 4.4b (reduce motion, focus bàn phím, CLS = 0, CPU throttle 4x). Báo cáo ghi rõ hoạt ảnh nào (#1 đến #5) đã làm và chỗ nào chưa làm được.

---

## 7. Lệnh kiểm tra (giống CI `.github/workflows/ci.yml`)

```bash
dotnet restore
dotnet build --configuration Release --no-restore -warnaserror
dotnet build TechZone.Tests/TechZone.Tests.csproj --configuration Release -warnaserror
dotnet test  TechZone.Tests/TechZone.Tests.csproj --configuration Release --no-build
# nếu `dotnet test` bị chặn trong sandbox: dotnet run --project TechZone.Tests.Runner
```
`-warnaserror` nghĩa là **cảnh báo Razor cũng làm hỏng build**: không để lại biến không dùng, `using` thừa, hay null-dereference mới.

Kiểm tra tĩnh (chạy được cả khi không có .NET):
```bash
grep -rn "→" Views/Home/Index.cshtml Views/Products/Index.cshtml Views/Products/Details.cshtml Views/Shared/_ProductCard.cshtml
grep -rn " · " Views/Home/Index.cshtml Views/Products/Index.cshtml Views/Products/Details.cshtml Views/Shared/_ProductCard.cshtml
grep -rn "4338CA\|67 56 202" wwwroot/css | grep -v admin.css
grep -rn "https\?://" Views wwwroot/css wwwroot/js | grep -v "model-viewer.min.js"
```
Hai lệnh đầu phải không còn kết quả; chạy thêm đúng hai lệnh đó trên các view Giỏ hàng, Thanh toán, Xác nhận đã tìm ở mục 2. Lệnh thứ ba chỉ còn trong `admin.css` (nếu có). Lệnh thứ tư không được có tài nguyên ngoài.

Nếu có thể chạy app (`dotnet run`), kiểm tra bằng trình duyệt/Playwright ở 320, 390, 768, 1024, 1440 px, chế độ sáng và tối: không tràn ngang (`document.documentElement.scrollWidth <= innerWidth`), tab bằng bàn phím thấy focus, không lỗi console.

## 8. Báo cáo cuối (bắt buộc)

Trả lời ngắn gọn, đủ các mục:
1. Danh sách file đã sửa/thêm.
2. Với từng trang: khối nào đổi cấu trúc DOM và selector JS nào bị ảnh hưởng (đã xác nhận còn khớp).
3. Kết quả build và test (hoặc lý do không chạy được) và kết quả các lệnh `grep`.
4. Những phần trong brief mà bạn đã **thay đổi hoặc bỏ** và lý do.
5. Việc còn lại cho con người: danh sách chức năng giỏ/thanh toán đã hoãn (mục 5.6) và chỗ nào repo khác prototype (mục 0.5), file font cần tải (nếu chưa có), cần kiểm tra bằng mắt ảnh nào với `mix-blend-mode`, và bất kỳ giả định nào bạn đã đặt ra.
