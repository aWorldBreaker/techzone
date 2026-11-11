# TechZone — Website bán đồ công nghệ

Đồ án: website thương mại điện tử B2C, 1 shop, 2 vai trò Khách / Admin.
**ASP.NET Core 8 + EF Core + SQLite (demo) + Razor MVC + ASP.NET Identity.**

> Trạng thái: Sprint 1–5 xong + vòng P0 Security + Sign-off + P1 follow-up
> (2026-11-06) + **Sprint 6 XONG toàn bộ (Module 1 Global Layout/Header/
> Footer + Module 3 Listing + Module 4 Details + Module 5 Cart/Checkout +
> Module 6 Account/Admin + Module 7 Admin/3D/micro) — 7 lỗi mở #10–16 đều
> XONG + follow-up M4-3, M4-3a/b**. Nhánh `security/signoff-20261106`,
> HEAD `980e996` (commit Sprint 6, đã push lên remote `main` thành
> `7930fac4` 2026-11-06 ~23:56, parent `c44df1e5`, trừ file
> `.github/workflows/ci.yml` trong manifest — App thiếu quyền Workflows;
> ci.yml SỐNG SÓT sau snapshot (SHA `5fe1d838…`) nên user KHÔNG cần up lại
> tay. **CI thật run #2 PASS 2026-11-06 ~23:57** (run #2, id 42257072894,
> 1m33s, job build-and-test + cả 7 bước xanh). Thay đổi Sprint 6 đã commit
> + push:
> Sprint 6 Module 1 (hamburger menu mobile, dropdown tài khoản, cart badge,
> footer dính đáy, placeholder, WebGL dark, alert/button còn thiếu + skip
> link) + Module 3 (cast `(double)` sort giá, panel lọc mobile thu gọn +
> nút "Bộ lọc" có badge, placeholder filter dùng token, nút card 1 dòng ở
> mobile + 3 test `ProductsSortTests`) + Module 4 (rating tính từ review đã
> duyệt, form đánh giá chỉ khi login) + Module 5 (clamp SL về tồn kho 2 lớp
> + `cart-qty.js`) + Module 6 (`.main-content { width: 100% }` + 17 bảng
> bọc `.table-wrap`, trang 404 thân thiện giữ status 404) + Module 7
> (empty state chart doanh thu trống, `favicon.ico`) + follow-up M4-3
> (sao ở Listing/Home tính từ review đã duyệt, không in `DanhGiaTB` stale)
> + follow-up cuối M4-3a/b (bảng Compare tính sao từ review đã duyệt; hero
> tính động từ review đã duyệt của Xiaomi 15, 0 review thì bỏ câu đánh giá).
> Build 0/0, test **225/225 PASS** (runner trực tiếp, xem `TEST-REPORT.md`
> §15–20; E2E Playwright cuối **14/14 PASS**, visual regression 5 PASS /
> 11 FAIL đã giải thích từng diff). M4-3 đã XONG (follow-up 2026-11-06,
> runner 225/225 + E2E 14/14 sau fix — xem `TEST-REPORT.md` §19);
> M4-3a/b đã XONG (follow-up cuối 2026-11-06, runner 225/225 + E2E 14/14
> sau fix — xem `TEST-REPORT.md` §20). **Phase 1 — Hoàn thiện Module 3
> XONG 2026-11-07, chưa commit** (sort giá hiệu dụng batch, bỏ cast
> `(double)`; tie-breaker `ThenBy(Id)`; pagination `aria-current`/clamp/
> windowing; badge chỉ đếm filter; nút Đóng; axe-core 8 lượt 0 critical;
> viewport 320/360/1024 lần đầu; Compare 390 hết tràn — runner **237/237**,
> E2E 14 cũ xanh + 10 test Phase 1 mới PASS, xem `TEST-REPORT.md` §24).
> **Phase 2 — Tách Admin XONG 2026-11-07, chưa commit** (`_AdminLayout` +
> `admin.css` + `admin.js` + `_ViewStart` Admin; 14 view Admin + Returns
> AdminIndex sang layout mới; sidebar/topbar/breadcrumb; dời CSS Admin;
> Admin không tải WebGL/`site.css`; Returns tràn 422px @390 đã sửa → 0px;
> axe Dashboard/Products 0 violation — runner **237/237**, E2E full 44
> passed / 4 skipped / 0 failed, xem `TEST-REPORT.md` §25).
> **Phase 3 — Authentication UX XONG 2026-11-07, chưa commit** (`_AuthLayout` +
> `auth.css` + `auth.js`; show/hide password, Caps Lock, chống submit lặp,
> Register không lộ email; Caps Lock sửa `closest('.form-group')`, link
> gạch chân `auth.css:67` — axe 0 violation; runner **243/243**, E2E auth
> 16/16, hồi quy full 68 passed / 0 failed / 4 skipped, tràn 0px mọi
> viewport 320–1440, xem `TEST-REPORT.md` §26 + §26.11).
> **Phase 4 — Product & Cart XONG 2026-11-07, chưa commit** (gallery
> `thumb-btn` + variant `data-gia`/`data-gia-goc` + nút +/− giỏ + Remove
> TempData + điểm trong giỏ + `ViewBag.ComboMon` + banner checkout cũ→mới
> qua `TraVeXacNhanLaiGia`/`TongCongTuAnh`; 4 fix sau QA: text guest về
> điểm, GiaGoc = Gia + GiaThem, `dinhDangTien` en-US, tràn giỏ 390; runner
> **255/255**, E2E Phase 4 **18/18**, axe Details/Cart 12 lượt 0 critical,
> hồi quy full 98 passed / 0 failed / 4 skipped (hiệu dụng — 3 fail do
> `/tmp` đầy, chạy lại 3/3 PASS), tràn 0px cả 24 tổ hợp, không-JS PASS,
> xem `TEST-REPORT.md` §27).
> **Phase 5 — Landing XONG 2026-11-07, commit local `c2b926e` + push GitHub `60dfab9b` (181 paths, ci.yml + favicon còn nguyên, 4/4 blob khớp) + space sync XONG** (hero + voucher đọc từ DB, tồn kho trên card theo ngưỡng TonKho ≤ 10 có sẵn, cam kết giữ/gỡ/làm mềm theo căn cứ code, "Trả góp 0% (giả lập)" mọi chỗ, polish: pill hero, gradient + viền, chip chữ cái, hover zoom 1.06, countdown tabular, voucher viền đứt; runner **259/259**, E2E Phase 5 **18/18**, axe Home 8 lượt 0 critical (0 violation mới), hồi quy full 98 passed / 0 failed / 4 skipped, tràn 0px cả 72 tổ hợp, before/after 17 ảnh/bên; xem `TEST-REPORT.md` §28). **Sprint 6 Continuation xong cả 5 phase, đã commit + push + sync space (TEST-REPORT §29). CI run #3 cho `60dfab9b` PASS 259/259.**
> Chi tiết
> Sprint 6: `docs/UI-AUDIT.md`, `docs/UI-ROADMAP.md`.
> Space TechZone (artifact `techzone`): đã đồng bộ 11 thay đổi Module 1+3
> ngày 2026-11-06 (build thành công, builder probe VERIFIED 11/11; xem
> `TEST-REPORT.md` §14) và Module 4–7 + M4-3 + M4-3a/b ngày 2026-11-07
> (build thành công, builder probe VERIFIED 11/11 + mục đo Compare 390
> 0px; xem `TEST-REPORT.md` §22), và Sprint 6 Continuation Phase 1–5
> ngày 2026-11-07 (build thành công; 6 mục VERIFIED/VERIFIED phần lớn,
> 3 VERIFIED một phần, 2 NOT_RUN trực tiếp — kiểm chứng code/bundle thay
> thế; audit cuối pass, 0 console error, tràn mobile 0px; dữ liệu durable
> kiểm chứng độc lập qua action: 13 SP / 4 voucher / 1 banner / 3 flash
> slot / 0 review / 0 đơn; xem `TEST-REPORT.md` §29).

## Chạy

```bash
cd TechZone
dotnet restore
dotnet run
```

Mở URL http in ra ở terminal (mặc định `http://localhost:5000`).
Lần chạy đầu tiên tự tạo file `techzone.db` (SQLite), chạy migrations rồi seed
dữ liệu mẫu. Muốn seed lại từ đầu: xóa `techzone.db` rồi chạy lại
(DB dev này chỉ là dữ liệu demo, không phải dữ liệu thật).

Yêu cầu .NET 8 SDK (đã xác minh 8.0.427). Trong sandbox, `dotnet` nằm ở
`~/.dotnet` (không có trên PATH): `export PATH="$HOME/.dotnet:$PATH"`.

## Tài khoản demo (seed sẵn — CHỈ DEMO)

| Vai trò | Email |
|---------|-------|
| Admin | admin@techzone.vn |
| Khách | khach@techzone.vn |

**Mật khẩu demo KHÔNG nằm trong code hay bất kỳ file nào được Git theo
dõi** — chỉ cấp qua biến môi trường (ASP.NET Core tự map vào cấu hình):

```bash
# Windows PowerShell (chạy local/demo):
$env:DemoAccounts__Enabled="true"
$env:DemoAccounts__AdminPassword="MatKhauAdminManhCuaBan"
$env:DemoAccounts__CustomerPassword="MatKhauKhachManhCuaBan"
dotnet run
```

Quy tắc seed (`Data/SeedData.cs`, SEC-01): tài khoản demo chỉ được tạo khi
**đủ 3 điều kiện** — `DemoAccounts:Enabled=true` một cách chủ động
(**mặc định TẮT ở mọi môi trường**, kể cả Development), có đủ email +
mật khẩu cho từng tài khoản, và mật khẩu lấy từ biến môi trường (hoặc
User Secrets / `appsettings.Development.json` — file này đã gitignore).
Thiếu mật khẩu → bỏ qua tài khoản đó + log warning. Production mặc định
không seed demo; chỉ seed khi bật chủ động có chủ ý + có mật khẩu qua env
(lúc đó app log cảnh báo LOUD).

**Lưu ý:** tắt seed KHÔNG vô hiệu tài khoản demo đã seed từ trước — nếu DB
cũ còn tài khoản demo trong khi demo đang tắt (hoặc đang chạy Production),
app log cảnh báo LOUD khi khởi động. Xóa/vô hiệu thủ công: đăng nhập Admin
→ Quản lý khách hàng, hoặc đổi mật khẩu mạnh + khóa tài khoản. Trang đăng
nhập chỉ hiện gợi ý chung về chế độ demo khi demo đang bật, không hiện
mật khẩu.

## Luồng demo (theo SCOPE.md)

### 1. Khách mua hàng + voucher + COD
1. Trang chủ → tìm "Xiaomi 15" → xem chi tiết (Snapdragon 8 Elite, RAM 12GB...).
2. Chọn phiên bản → Thêm vào giỏ.
3. Giỏ hàng → nhập mã `TECHZONE500` → Áp dụng (giảm 500.000đ, đơn tối thiểu 5.000.000đ).
4. Đặt hàng → nhập tên / SĐT / địa chỉ → chọn COD → Đặt hàng.
5. Xem đơn ở "Đơn của tôi". Khách vãng lai (không đăng nhập) xem đơn bằng
   **mã tra cứu** hiển thị sau khi đặt hàng (`/Orders/Details/{id}?token=...`).

### 2. Flash Sale
Trang chủ → mục Flash Sale (đếm ngược + khung giờ 0h/12h/20h).
Mua Xiaomi 15 5G giá sale 12.990.000đ (gốc 15.990.000đ) hoặc Tai nghe
Xiaomi Buds 5 giá 1.090.000đ. Giá hiển thị ở trang chi tiết dùng chung
`PriceCalculationService` với giỏ/checkout nên không lệch.

### 3. Admin
1. Đăng nhập `admin@techzone.vn` → Dashboard: doanh thu, đơn chờ xác nhận,
   top bán chạy, tồn kho thấp, doanh thu 7 ngày.
2. Quản lý sản phẩm → Thêm sản phẩm mới (giá/tồn kho được validate phía
   server: chặn giá âm, khuyến mãi 0 hoặc ≥ giá gốc, tồn âm).
3. Quản lý đơn hàng → đổi trạng thái theo bảng hợp lệ (Đã hủy/Đã giao là cuối).
4. Quản lý voucher / combo / Flash Sale + khung giờ / banner / kho / khách hàng.

## Tính năng đã có (Sprint 1–5 + vòng P0)

- Catalog, tìm kiếm/lọc, chi tiết SP, biến thể, so sánh, wishlist, đánh giá
  có ảnh + Q&A, session cart, đã xem gần đây.
- Checkout demo 1 transaction: giá tính lại từ DB, trừ kho/voucher có điều
  kiện, idempotency key (POST trùng không tạo đơn mới), hoàn tác đầy đủ khi
  hủy/đổi trả (tồn/diểm/voucher/lượt sale/hoàn tiền — chỉ hoàn tiền khi đã
  thu thật, COD chưa trả thì không tạo hoàn tiền giả).
- Voucher theo hãng/danh mục/hạng/freeship; combo theo DB; Flash Sale chuẩn
  + theo khung giờ; loyalty Đồng/Bạc/Vàng/**Bạch kim ≥ 50tr** (1 điểm = 1.000đ).
- VAT giả lập 8%/10% theo cấu hình + snapshot từng dòng; hóa đơn DN yêu cầu
  đủ tên/MST/địa chỉ, ghi rõ "demo".
- Đổi trả / bảo hành online (chỉ đơn đã giao), tra cứu bảo hành theo mã đơn
  (+ mã tra cứu với đơn vãng lai), timeline theo dõi đơn, hủy/đổi địa chỉ.
- Trả góp 0% giả lập (6/12 tháng), thanh toán ví giả lập (MoMo/ZaloPay/VNPay).
- 3D viewer (`model-viewer`) cho Xiaomi 15 / TUF F15 / WH-CH520; 4 file GLB
  trong `wwwroot/models/` (gamepad chưa map vì seed chưa có SP tay cầm).
- Dark mode (toggle header, lưu localStorage, mặc định theo hệ thống).

## Thiết kế

Minimalism & Swiss Style (UI UX Pro Max). **Palette đã duyệt:**
xanh điện `#2563EB`, cam san hô `#FF6B35` (giá/badge sale), teal `#0EA5A4`,
nền trắng, chữ `#0F172A`, font **Be Vietnam Pro** (1 stack duy nhất),
bo góc tối đa 8px. Token chuẩn duy nhất: `wwwroot/css/design-tokens.css`
(bản sao ở `design-system/techzone/`).

## Cơ sở dữ liệu — 35 bảng, 5 migrations

`InitialCreate` → `AddSprint2Financial` → `AddSprint2Rollback` →
`AddSprint3Combo` → `AddGuestToken` (mã tra cứu đơn vãng lai) →
`GuestTokenHardening` (SEC-04: hash + hạn + rate limit, xem dưới).
Migrate tự chạy khi khởi động (`MigrateAsync`). DB cũ thời `EnsureCreated`
(21 bảng) chuyển đổi bằng `Data/LegacyDbConverter.cs` — xem
`DATABASE-MIGRATION.md`, luôn làm trên **bản sao** + backup trước.

## Dữ liệu seed — thật, không bịa

13 sản phẩm, 8 danh mục, 9 hãng, giá/thông số theo giá bán lẻ thực tế
tháng 11/2026. 12/13 ảnh thật đã audit (`IMAGE-AUDIT-SPRINT3.md`); ảnh
Xiaomi Buds 5 (`earbuds.jpg`) chưa xác minh được nguồn. Ảnh brand chỉ
dùng demo/phi thương mại; SSD 980 PRO là CC-BY-SA-4.0 (ghi công
D-Kuru/Wikimedia Commons).

## Bảo mật — đã làm (vòng P0 + siết thêm vòng Sign-off, 2026-11-06)

- Đơn khách vãng lai chỉ xem được bằng **mã tra cứu** không đoán được
  (`Order.GuestToken`); đơn của tài khoản chỉ chủ đơn/Admin xem được.
  Áp cho `Orders/Details`, `Orders/HoaDon`, `Returns/TraCuu`.
  **SEC-04 (2026-11-06) — mã tra cứu siết thêm:**
  mã sinh bằng `RandomNumberGenerator` (16 byte = 128 bit, hex 32 ký tự);
  DB chỉ lưu **SHA-256 hash** (hex 64 ký tự), không lưu plaintext —
  mã gốc chỉ hiện 1 lần duy nhất sau khi đặt hàng (TempData) và khi Admin
  cấp lại; verify hash rồi so sánh bằng `FixedTimeEquals`. Mã chỉ gửi
  bằng **form POST** (không còn `?token=` trong URL — tránh lộ vào
  history/log/Referer); GET không đủ quyền thì hiện form nhập mã chung
  (không lộ đơn có tồn tại hay thuộc loại nào). **Hạn dùng 30 ngày** kể từ
  ngày đặt (đủ tra cứu/bảo hành sau khi nhận hàng, không để mã rò rỉ sống
  mãi); quá hạn thì Admin cấp lại. **Rate limit lưu trong DB:** nhập sai
  5 lần liên tiếp → khóa tra cứu đơn đó 15 phút; nhập đúng reset đếm.
  **Không có quyền xem → trả 404** (không phải 403/302) để chống lộ tín
  hiệu "đơn tồn tại" — kể cả `Returns/Details` (SEC-05); message tra cứu
  thống nhất một câu chung. Admin cấp lại mã tra cứu
  (`POST CapLaiMaTraCuu`, chỉ đơn vãng lai — mã cũ vô hiệu ngay) **chỉ
  sau khi nhập họ tên + SĐT khách cung cấp khớp với đơn** (đơn vãng lai
  không có email riêng — đối chiếu tên + SĐT trong `DiaChiGiao`).
  Token cũ (plaintext, trước SEC-04) vẫn dùng được: verify chấp nhận cả
  hai dạng, lần khớp đầu tiên tự nâng cấp lên hash; migration
  `GuestTokenHardening` ân hạn 30 ngày cho token cũ, và
  `Services/GuestTokenBackfill.ChayAsync` hash hàng loạt khi cần (chạy
  theo lệnh — SQLite không có hàm SHA-256 trong SQL nên không hash được
  trực tiếp trong migration). Đơn cũ `GuestToken = NULL` vẫn chỉ Admin
  xem được — không tự động backfill.
- Tài khoản demo qua cấu hình `DemoAccounts` (xem trên): mặc định TẮT,
  mật khẩu chỉ cấp qua biến môi trường, không còn trong bất kỳ file nào
  được Git theo dõi. App log cảnh báo LOUD nếu DB còn tài khoản demo
  trong khi demo đang tắt / đang chạy Production.
- Đăng nhập sai 5 lần → khóa 5 phút (`lockoutOnFailure: true`); có test tự
  động bằng harness SignInManager/UserManager thật (`LockoutTests`). Login
  một thông báo chung, không phân biệt "không có user" vs "sai mật khẩu".
- Mọi action nhạy cảm đều `[Authorize]` + kiểm ownership hoặc kiểm tra mã
  tra cứu (đơn vãng lai); Admin có `[Authorize(Roles="Admin")]` ở class.

## Cảnh báo trước khi public

1. **VAT NEEDS_TAX_REVIEW** — cả 8 nhóm chưa đối chiếu mã HS theo
   NĐ 174/2025/NĐ-CP; thuế suất 8% là giả định, **không dùng khai thuế thật**.
2. Thanh toán / hóa đơn VAT đều là **giả lập** — không thu tiền thật, không
   phát hành hóa đơn điện tử hợp pháp.
3. Đăng ký chỉ tự xác nhận email (`EmailConfirmed = true`) khi KHÔNG phải
   Production (Development/demo) vì chưa có hệ thống gửi email thật — ở
   Production đăng ký để `EmailConfirmed = false`. Phải làm xác thực email
   thật trước khi public.
4. Đơn vãng lai tạo trước khi có `GuestToken` (`GuestToken = NULL`) thì chủ
   cũ không xem được nữa (chỉ Admin xem được) — cần backfill token nếu muốn
   giữ quyền xem đơn cũ.

## Kiểm thử

```bash
dotnet test TechZone.Tests/TechZone.Tests.csproj   # máy thường / CI
dotnet run --project TechZone.Tests.Runner/TechZone.Tests.Runner.csproj  # dự phòng
```

`dotnet test` bị chặn trong sandbox (vstest TCP channel hỏng do proxy) nên
dùng runner trực tiếp (gọi thẳng các method `[Fact]`, cùng code/assertion).
Hiện tại: **225/225 PASS** (Migration 5, Checkout 9, FlashSale 10, Rollback
10, Vat 11, VoucherLoyalty 12, Combo 8, Platinum 10, AdminInventory 25,
ConcurrencyQa 3, Security 8, DemoAccounts 15, GuestToken 14, Lockout 9,
GuestTokenHardening 13, CartCheckoutParity 11, CheckoutTampering 30
(21 Bước 4a + 6 chống race / POST trực tiếp + 3 Final Check: P1-1 ×1,
P1-2 ×2), Warranty 19 (17 có sẵn + 2 GET báo trùng BH/đổi trả, Final Check),
ProductsSort 3 (Sprint 6 Module 3: sort giá tăng/giảm + phân trang)).
Xem `TEST-REPORT.md` (thư mục docs ngoài). 15 luồng nghiệm thu chạy lại đủ
vòng Phase 3 follow-up (2026-11-06): **15/15 PASS** — F14 (3D) PASS có lưu ý
(viewer 72/76, 4 FAIL cùng check reset góc, thuộc tính `camera-orbit` luôn
đúng map, chỉ số đo theta lag trên headless).

## Chưa làm / còn lại

- VAT đối chiếu mã HS (việc pháp lý, ngoài code); phí ship không tính VAT.
- **FIN-02 (XONG 2026-11-06, đã kiểm chứng Phase 3):** tổng giỏ = tổng
  checkout với khách có hạng — cả hai đi qua `PriceCalculationService.
  TinhTongDonAsync()`; giỏ hiện dòng "Giảm hạng Vàng (2%)". Ví dụ live:
  2× Xiaomi 15 + TECHZONE500 = 25.980.000đ − 500.000đ − 519.600đ =
  24.960.400đ ở giỏ, checkout và đơn (11 test `CartCheckoutParityTests`).
  **Bước 4a (2026-11-06, đã commit local `41bef27` + đã push lên remote
  `main` thành `40c9bd90` (trừ file ci.yml, user up tay sau), nhánh
  `security/signoff-20261106`):**
  Checkout POST không còn tin `GiamGiaHang`/`GiamGiaDiem`/`FreeShip`/
  `GiamGiaCombo` từ controller — `CheckoutService` tính lại toàn bộ từ
  DB trong transaction (giảm hạng theo hạng hiện tại, điểm theo số dư
  thật + chặn ở phần còn lại, freeship hạng/voucher, phí ship); giá
  dòng snapshot từ giá tính lại. Đổi dữ liệu giữa GET và POST (hạng,
  voucher, Flash Sale, điểm) thì POST trả về lại trang Checkout với
  tổng mới + "Giá đã thay đổi, vui lòng xác nhận lại", submit lại mới
  đặt; hết hàng thì từ chối thẳng về giỏ. **Chống race (tiếp theo Bước
  4a, 2026-11-06):** cửa sổ còn lại là giữa lúc controller so với ảnh
  GET và lúc service chạy transaction — controller truyền tổng đã xác
  nhận (`KetQuaTongDon.TongCong`) vào service qua `CheckoutInput.
  TongXacNhan`; service so lại tổng cuối trong transaction trước khi
  tạo đơn, khác thì hủy toàn bộ, không tạo đơn, trả cờ
  `CheckoutResult.GiaDaThayDoi = true` để controller hiện lại trang
  Checkout với tổng mới. **Chính sách xác nhận giá (chốt):**
  POST trực tiếp không qua GET (không có snapshot) được chấp nhận —
  tổng service tính lại trong transaction chính là tổng xác nhận
  (submit = xác nhận); snapshot chỉ bảo vệ trường hợp giá đổi giữa
  GET và POST. 30 test `CheckoutTamperingTests` (21 Bước 4a + 6 mới
  chống race / POST trực tiếp + 3 Final Check: P1-1 ×1, P1-2 ×2),
  runner 225/225 PASS (xem §Kiểm thử — lúc Step 4 commit là 222/222).
  **Final Check (2026-11-06, đã commit local `41bef27` cùng Step 4):** P1-1 — khách vãng lai nhập
  điểm không còn kẹt vòng lặp "Giá đã thay đổi": controller kiểm tra
  `LoiDiem` trước `CoThayDoi` bất kể đăng nhập (trả form + lỗi "Cần đăng
  nhập để dùng điểm tích lũy") và form ẩn ô điểm khi chưa đăng nhập.
  P1-2 — hết quota Flash Sale xử lý như hết giờ (hướng A: quota chỉ
  giới hạn số suất GIÁ SALE, khách vẫn mua được giá gốc qua trang xác
  nhận lại; service chấp nhận khi bên gọi đã gửi giá gốc, vẫn từ chối
  "vừa hết suất" khi bên gọi còn giữ giá sale cũ; suất mất ngay trong
  transaction thì trả cờ `GiaDaThayDoi` để xác nhận lại giá gốc).
  P2 — GET form bảo hành báo trùng ngay khi đã có yêu cầu đang mở.
- Chống trùng yêu cầu bảo hành/đổi trả hiện là check-then-act (GET/POST
  kiểm tra rồi mới tạo) nên 2 POST song song vẫn có thể tạo 2 yêu cầu
  trùng — cần unique index DB (partial index theo trạng thái đang mở) +
  xử lý lỗi trùng ở POST nếu muốn siết hẳn (P2, còn mở sau Final Check).
- Thu hồi điểm Earn khi hủy có thể làm số dư điểm âm (đã ghi rõ "nợ điểm").
- Đơn Chờ xác nhận vẫn tính vào chi tiêu xét hạng; Gold 2% tính trên toàn
  bộ tạm tính (trước khi trừ combo); lệch làm tròn 1đ/1 điểm.
- Admin ProductForm vẫn cho sửa tay Tồn kho/Giá (bypass luồng nhập kho
  StockIn) — đã validate giá trị nhưng chưa khóa hẳn.
- CI: workflow `.github/workflows/ci.yml` đã tạo (build + `dotnet test` trên
  runner GitHub). **CI thật đã chạy 2 lần, đều PASS:** run #1 2026-11-06
  ~19:42 (`dotnet test` 222/222) trên commit `c44df1e5`; run #2 2026-11-06
  ~23:57 (id 42257072894, 1m33s, job build-and-test + cả 7 bước xanh) trên
  commit Sprint 6 `7930fac4` — ci.yml sống sót sau snapshot push nên không
  cần up lại tay; chưa gắn làm required check. Số test run #2 đã đọc trực
  tiếp từ log bước Test qua browser 2026-11-07 00:05: **Passed: 225,
  Failed: 0, Total: 225 — VERIFIED**. **Đã sửa 2026-11-06 (vòng Sign-off):** thêm bước Build
  Tests (repo không có .sln nên `dotnet build` ở root chỉ build app) +
  `permissions: contents: read`; đã mô phỏng trên bản sao sạch: DLL Tests
  tồn tại, `dotnet test --no-build` chỉ dừng ở lỗi vstest transport đã
  biết trong sandbox (trên runner GitHub thật sẽ chạy test bình thường).
- Voucher hết hạn dạng date-midnight (hết hiệu lực đầu ngày hết hạn).
