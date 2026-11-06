using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TechZone.Models;

namespace TechZone.Data;

/// <summary>
/// Du lieu mau THAT: ten, gia, thong so lay theo gia ban le thuc te thang 11/2026
/// (gia co the lech chut tuy thoi diem, nhung khong co gia vo ly kieu laptop
/// 2.390.000d, khong lap san pham, khong bia thong so). Anh: 12/13 SP co anh that
/// da audit (Sprint 3, B-8 — xem IMAGE-AUDIT-SPRINT3.md); chi Buds 5 dung tam
/// earbuds.jpg chua xac minh duoc model. placeholder.svg chi con la fallback trong
/// code cho SP nao chua co anh (hien khong SP nao roi vao truong hop nay).
/// </summary>
public static class SeedData
{
    public static async Task InitializeAsync(IServiceProvider services)
    {
        var context = services.GetRequiredService<AppDbContext>();
        // Sprint 1 (P0-2): migrations thay cho EnsureCreated. EnsureCreated chi
        // tao DB khi file chua ton tai va khong them bang/cot moi; MigrateAsync
        // ap migration len ca DB moi lan DB cu. DB cu thoi EnsureCreated phai
        // chay quy trinh chuyen doi trong DATABASE-MIGRATION.md (baseline)
        // truoc khi chay app, neu khong MigrateAsync se loi "table already exists".
        await context.Database.MigrateAsync();

        // --- Tai khoan & vai tro ---
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        foreach (var role in new[] { "Admin", "Khach" })
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));

        // SEC-01 (vong Sign-off): tai khoan demo CHI seed khi du 3 dieu kien:
        //   1. DemoAccounts:Enabled=true MOT CACH CHU DONG (mac dinh false o
        //      moi moi truong — Program.cs dung cung default nay, khong lech);
        //   2. Co du email + MAT KHAU cho tung tai khoan. Mat khau KHONG nam
        //      trong bat ky file nao duoc Git theo doi: chi cap qua bien moi
        //      truong DemoAccounts__AdminPassword / DemoAccounts__CustomerPassword
        //      (ASP.NET Core tu map env var vao IConfiguration), User Secrets,
        //      hoac appsettings.Development.json (da gitignore). Thieu mat
        //      khau -> bo qua tai khoan do + warning (xem TaoTaiKhoanDemoAsync).
        //   3. Production: mac dinh TAT (do default Enabled=false). Production
        //      chi seed demo khi bat chu dong co chu y (Enabled=true + mat
        //      khau qua env) — luc do log canh bao LOUD ben duoi.
        // Tat seed KHONG vo hieu tai khoan demo da seed tu truoc: neu DB van
        // con tai khoan demo trong khi seed dang tat (hoac dang chay Production)
        // thi log canh bao LOUD + huong dan xu ly thu cong (khong tu doi/xoa
        // credential cua user dang ton tai mot cach lang le).
        var logger = services.GetService<ILoggerFactory>()?.CreateLogger("TechZone.Data.SeedData");
        var env = services.GetService<IHostEnvironment>();
        // Khong dang ky IHostEnvironment (vd test build ServiceCollection tay)
        // thi coi nhu khong phai Production.
        var laProduction = env?.IsProduction() ?? false;
        var demoSection = services.GetService<IConfiguration>()?.GetSection("DemoAccounts");
        var demoEnabled = demoSection?.GetValue<bool>("Enabled", false) ?? false;
        var adminEmail = demoSection?["AdminEmail"] ?? "admin@techzone.vn";
        var khachEmail = demoSection?["CustomerEmail"] ?? "khach@techzone.vn";
        ApplicationUser? admin = null;
        ApplicationUser? khach = null;
        if (!demoEnabled)
        {
            logger?.LogWarning("DemoAccounts dang TAT (mac dinh): bo qua tao tai khoan demo. Muon bat o local: DemoAccounts__Enabled=true + DemoAccounts__AdminPassword / DemoAccounts__CustomerPassword qua bien moi truong.");
        }
        else
        {
            if (laProduction)
                logger?.LogWarning("CANH BAO BAO MAT: DemoAccounts dang BAT trong Production — tai khoan demo se ton tai tren moi truong production. Tat ngay khi khong can demo nua (DemoAccounts__Enabled=false) va xoa/vohieu tai khoan demo cu.");
            admin = await TaoTaiKhoanDemoAsync(userManager, logger,
                adminEmail, demoSection?["AdminPassword"],
                "Quản trị TechZone", "0901234567", "Admin");
            khach = await TaoTaiKhoanDemoAsync(userManager, logger,
                khachEmail, demoSection?["CustomerPassword"],
                "Nguyễn Văn An", "0909123456", "Khach");
        }

        // Tai khoan demo CU van ton tai du seed da tat (hoac dang Production):
        // canh bao LOUD, khong tu xoa. Xu ly thu cong: Admin -> Quan ly khach
        // hang -> xoa/vohieu tai khoan, hoac doi mat khau manh + khoa tai khoan.
        if (!demoEnabled || laProduction)
        {
            var tonTai = new List<string>();
            if (await userManager.FindByEmailAsync(adminEmail) is not null) tonTai.Add(adminEmail);
            if (await userManager.FindByEmailAsync(khachEmail) is not null) tonTai.Add(khachEmail);
            if (tonTai.Count > 0)
                logger?.LogWarning("CANH BAO BAO MAT: tai khoan demo van ton tai trong DB ({Emails}) trong khi DemoAccounts dang tat{Production} — tat seed KHONG vo hieu tai khoan cu. Xoa/vohieu thu cong: Admin -> Quan ly khach hang, hoac doi mat khau manh + khoa tai khoan.", string.Join(", ", tonTai), laProduction ? " / dang chay Production" : "");
        }

        // --- Danh muc: 8 nhom do cong nghe ---
        // Seed tung nhom chi khi nhom do trong (P0-2): DB cu da co SP thi truoc
        // day return ngay, khien role/user + 11 bang moi khong bao gio duoc seed.
        Category[] categories =
        {
            new Category { Ten = "Điện thoại", Slug = "dien-thoai", MoTa = "Smartphone chính hãng" },
            new Category { Ten = "Laptop", Slug = "laptop", MoTa = "Laptop văn phòng và gaming" },
            new Category { Ten = "Tablet", Slug = "tablet", MoTa = "Máy tính bảng" },
            new Category { Ten = "Tai nghe", Slug = "tai-nghe", MoTa = "Tai nghe có dây và không dây" },
            new Category { Ten = "Đồng hồ", Slug = "dong-ho", MoTa = "Đồng hồ thông minh" },
            new Category { Ten = "Phụ kiện", Slug = "phu-kien", MoTa = "Sạc, cáp, ốp lưng, bao da" },
            new Category { Ten = "Gaming", Slug = "gaming", MoTa = "Chuột, bàn phím, tay cầm chơi game" },
            new Category { Ten = "Linh kiện PC", Slug = "linh-kien-pc", MoTa = "SSD, RAM, card đồ họa" },
        };
        Brand[] brands =
        {
            new Brand { Ten = "Xiaomi" },
            new Brand { Ten = "Apple" },
            new Brand { Ten = "Samsung" },
            new Brand { Ten = "OPPO" },
            new Brand { Ten = "ASUS" },
            new Brand { Ten = "Lenovo" },
            new Brand { Ten = "Sony" },
            new Brand { Ten = "Huawei" },
            new Brand { Ten = "Logitech" },
        };
        if (!await context.Categories.AnyAsync())
        {
            context.Categories.AddRange(categories);
            context.Brands.AddRange(brands);
            await context.SaveChangesAsync();
        }
        else
        {
            // DB cu: nap lai de helper Cat()/Br() tra dung Id that trong DB.
            categories = await context.Categories.ToArrayAsync();
            brands = await context.Brands.ToArrayAsync();
        }

        Category Cat(string ten) => categories.First(c => c.Ten == ten);
        Brand Br(string ten) => brands.First(b => b.Ten == ten);

        // --- San pham (13) ---
        // Sprint 3 — Admin (audit P1-8): TonKho seed = ton SAU KHI TRU don
        // mau (don1: Xiaomi x1, Buds x1; don2: TUF x1; don3: WH-CH520 x2)
        // nen khop voi StockIn "nhap lo dau" tru di so luong da ban:
        // Xiaomi 25-1=24, Buds 50-1=49, TUF 12-1=11, WH-CH520 40-2=38.
        // LuotBan giu nguyen tong lich su (da bao gom so luong don mau).
        var products = new List<Product>
        {
            new() { Ten = "Xiaomi 15 5G", MoTa = "Flagship Xiaomi: chip Snapdragon 8 Elite, camera Leica 50MP, sạc nhanh 90W.", Gia = 15_990_000, GiaKhuyenMai = 12_990_000, CategoryId = Cat("Điện thoại").Id, BrandId = Br("Xiaomi").Id, TonKho = 24, LuotBan = 128, DanhGiaTB = 4.8, LaNoiBat = true },
            new() { Ten = "iPhone 15 Pro", MoTa = "iPhone khung titan, chip Apple A17 Pro, camera chính 48MP, cổng USB-C.", Gia = 31_990_000, GiaKhuyenMai = 28_990_000, CategoryId = Cat("Điện thoại").Id, BrandId = Br("Apple").Id, TonKho = 18, LuotBan = 96, DanhGiaTB = 4.9, LaNoiBat = true },
            new() { Ten = "Laptop ASUS TUF Gaming F15", MoTa = "Laptop gaming bền chuẩn quân đội, RTX 3050, màn 144Hz.", Gia = 24_650_000, GiaKhuyenMai = 18_490_000, CategoryId = Cat("Laptop").Id, BrandId = Br("ASUS").Id, TonKho = 11, LuotBan = 54, DanhGiaTB = 4.7, LaNoiBat = true },
            new() { Ten = "Tai nghe Sony WH-CH520", MoTa = "Tai nghe on-ear nhẹ, pin 50 giờ, Bluetooth 5.2, mic đàm thoại rõ.", Gia = 1_150_000, GiaKhuyenMai = 890_000, CategoryId = Cat("Tai nghe").Id, BrandId = Br("Sony").Id, TonKho = 38, LuotBan = 210, DanhGiaTB = 4.6, LaNoiBat = true },
            new() { Ten = "Samsung Galaxy A55 5G", MoTa = "Tầm trung Samsung: khung kim loại, màn Super AMOLED 120Hz, chống nước IP67.", Gia = 9_990_000, GiaKhuyenMai = 7_490_000, CategoryId = Cat("Điện thoại").Id, BrandId = Br("Samsung").Id, TonKho = 30, LuotBan = 87, DanhGiaTB = 4.7 },
            new() { Ten = "OPPO Reno11 5G", MoTa = "Chụp chân dung đẹp, sạc siêu nhanh 67W, màn AMOLED cong 120Hz.", Gia = 10_990_000, GiaKhuyenMai = 9_990_000, CategoryId = Cat("Điện thoại").Id, BrandId = Br("OPPO").Id, TonKho = 22, LuotBan = 63, DanhGiaTB = 4.6 },
            new() { Ten = "Laptop Lenovo LOQ 15", MoTa = "Laptop gaming giá tốt, RTX 4050, tản nhiệt 2 quạt, màn 144Hz.", Gia = 25_990_000, GiaKhuyenMai = 22_990_000, CategoryId = Cat("Laptop").Id, BrandId = Br("Lenovo").Id, TonKho = 10, LuotBan = 41, DanhGiaTB = 4.7 },
            new() { Ten = "iPad Gen 10 10.9\" 64GB WiFi", MoTa = "iPad phổ thông: màn Liquid Retina 10.9 inch, chip A14, cổng USB-C.", Gia = 11_490_000, GiaKhuyenMai = 9_990_000, CategoryId = Cat("Tablet").Id, BrandId = Br("Apple").Id, TonKho = 15, LuotBan = 58, DanhGiaTB = 4.8 },
            new() { Ten = "Đồng hồ Xiaomi Watch S3", MoTa = "Smartwatch mặt tròn AMOLED 1.43 inch, GPS, pin 15 ngày.", Gia = 3_290_000, GiaKhuyenMai = 2_390_000, CategoryId = Cat("Đồng hồ").Id, BrandId = Br("Xiaomi").Id, TonKho = 35, LuotBan = 74, DanhGiaTB = 4.5 },
            new() { Ten = "Đồng hồ Huawei Watch GT 4", MoTa = "Smartwatch pin 14 ngày, theo dõi sức khỏe, GPS chính xác.", Gia = 5_500_000, GiaKhuyenMai = 4_290_000, CategoryId = Cat("Đồng hồ").Id, BrandId = Br("Huawei").Id, TonKho = 20, LuotBan = 47, DanhGiaTB = 4.6 },
            new() { Ten = "Tai nghe True Wireless Xiaomi Buds 5", MoTa = "Tai nghe không dây chống ồn ANC, Bluetooth 5.4, pin 24 giờ kèm hộp sạc.", Gia = 1_650_000, GiaKhuyenMai = 1_190_000, CategoryId = Cat("Tai nghe").Id, BrandId = Br("Xiaomi").Id, TonKho = 49, LuotBan = 132, DanhGiaTB = 4.5 },
            new() { Ten = "Chuột Gaming Logitech G502 HERO", MoTa = "Chuột gaming cảm biến HERO 25K DPI, 11 nút lập trình, tạ điều chỉnh.", Gia = 1_390_000, GiaKhuyenMai = 1_090_000, CategoryId = Cat("Gaming").Id, BrandId = Br("Logitech").Id, TonKho = 45, LuotBan = 88, DanhGiaTB = 4.8 },
            new() { Ten = "SSD Samsung 980 PRO 1TB NVMe", MoTa = "SSD NVMe PCIe 4.0, đọc 7000MB/s, cho PC và PS5.", Gia = 3_190_000, GiaKhuyenMai = 2_490_000, CategoryId = Cat("Linh kiện PC").Id, BrandId = Br("Samsung").Id, TonKho = 28, LuotBan = 66, DanhGiaTB = 4.9 },
        };
        if (!await context.Products.AnyAsync())
        {
            context.Products.AddRange(products);
            await context.SaveChangesAsync();
        }
        else
        {
            // DB cu: nap lai de helper P() tra dung Id that trong DB.
            products = await context.Products.ToListAsync();
        }

        Product P(string ten) => products.First(p => p.Ten == ten);

        // --- Thong so ky thuat (bang rieng ProductSpecs) ---
        if (!await context.ProductSpecs.AnyAsync())
            context.ProductSpecs.AddRange(
            new ProductSpecs { ProductId = P("Xiaomi 15 5G").Id, Chip = "Snapdragon 8 Elite", Ram = "12GB", Rom = "256GB", Pin = "5400mAh", ManHinh = "6.7\" AMOLED 120Hz", Camera = "50MP" },
            new ProductSpecs { ProductId = P("iPhone 15 Pro").Id, Chip = "Apple A17 Pro", Ram = "8GB", Rom = "256GB", Pin = "3274mAh", ManHinh = "6.1\" Super Retina XDR", Camera = "48MP" },
            new ProductSpecs { ProductId = P("Laptop ASUS TUF Gaming F15").Id, Chip = "Intel Core i5-12450H", Ram = "8GB", Rom = "512GB SSD", Pin = "48Wh", ManHinh = "15.6\" FHD 144Hz", CardDoHoa = "NVIDIA RTX 3050" },
            new ProductSpecs { ProductId = P("Tai nghe Sony WH-CH520").Id, Pin = "50 giờ", ThongSoKhac = "Bluetooth 5.2, chống ồn, mic đàm thoại" },
            new ProductSpecs { ProductId = P("Samsung Galaxy A55 5G").Id, Chip = "Exynos 1480", Ram = "8GB", Rom = "128GB", Pin = "5000mAh", ManHinh = "6.6\" Super AMOLED 120Hz", Camera = "50MP" },
            new ProductSpecs { ProductId = P("OPPO Reno11 5G").Id, Chip = "Dimensity 7050", Ram = "12GB", Rom = "256GB", Pin = "5000mAh", ManHinh = "6.7\" AMOLED 120Hz", Camera = "50MP" },
            new ProductSpecs { ProductId = P("Laptop Lenovo LOQ 15").Id, Chip = "Intel Core i5-12450HX", Ram = "16GB", Rom = "512GB SSD", Pin = "60Wh", ManHinh = "15.6\" FHD 144Hz", CardDoHoa = "NVIDIA RTX 4050" },
            new ProductSpecs { ProductId = P("iPad Gen 10 10.9\" 64GB WiFi").Id, Chip = "Apple A14 Bionic", Ram = "4GB", Rom = "64GB", Pin = "28.6Wh", ManHinh = "10.9\" Liquid Retina", Camera = "12MP" },
            new ProductSpecs { ProductId = P("Đồng hồ Xiaomi Watch S3").Id, Pin = "15 ngày", ManHinh = "1.43\" AMOLED", ThongSoKhac = "GPS, chống nước 5ATM" },
            new ProductSpecs { ProductId = P("Đồng hồ Huawei Watch GT 4").Id, Pin = "14 ngày", ManHinh = "1.43\" AMOLED", ThongSoKhac = "GPS, theo dõi nhịp tim, SpO2" },
            new ProductSpecs { ProductId = P("Tai nghe True Wireless Xiaomi Buds 5").Id, Pin = "24 giờ (kèm hộp sạc)", ThongSoKhac = "Bluetooth 5.4, chống ồn ANC" },
            new ProductSpecs { ProductId = P("Chuột Gaming Logitech G502 HERO").Id, ThongSoKhac = "Cảm biến HERO 25K DPI, 11 nút lập trình, dây 2.1m" },
            new ProductSpecs { ProductId = P("SSD Samsung 980 PRO 1TB NVMe").Id, ThongSoKhac = "NVMe PCIe 4.0, đọc 7000MB/s, ghi 5000MB/s" }
        );

        // --- Anh san pham (Sprint 3, B-8 — xem IMAGE-AUDIT-SPRINT3.md) ---
        // 12/13 SP co anh that da audit: xiaomi15.png (Xiaomi 15), iphone15pro.jpg
        // (iPhone 15 Pro), tuf-f15.jpg (TUF F15 — thay anh ROG sai model),
        // wh-ch520.jpg (WH-CH520), a55.jpg, reno11.jpg, loq15.jpg, ipad10.png,
        // watch-s3.png, watch-gt4.png, g502-hero.webp, ssd-980pro.jpg.
        // Chi Buds 5 van dung earbuds.jpg tam (anh tai nghe trang generic, chua
        // xac minh duoc dung model Buds 5, 642x350 — can anh chuan sau).
        // Nguon anh: anh san pham chinh thuc cua hang / packshot hang do nha ban
        // le lon dang (Samsung, OPPO, Xiaomi, Apple, Huawei, Lenovo, Logitech,
        // Sony, ASUS) + 1 anh CC-BY-SA-4.0 (SSD, Wikimedia Commons, tac gia
        // D-Kuru — can ghi attribution neu public). Tat ca chi dung cho do an
        // demo phi thuong mai; khong co giay phep mo nao cho anh hang — muon
        // public/thuong mai phai xin phep hang hoac thay bang anh tu chup.
        // Luu y seed: anh chi duoc seed khi bang ProductImages trong (guard
        // ben duoi) — DB cu da co anh se KHONG tu doi sang anh moi.
        // rog-laptop.png (thuc chat WebP, anh ROG Strix sai model) khong con
        // duoc tro toi — file van nam trong wwwroot/images/, coordinator xoa sau.
        var images = new List<ProductImage>();
        void Img(string tenSp, string url) =>
            images.Add(new ProductImage { ProductId = P(tenSp).Id, Url = url, LaAnhChinh = true, ThuTu = 0 });
        Img("Xiaomi 15 5G", "/images/xiaomi15.png");
        Img("iPhone 15 Pro", "/images/iphone15pro.jpg");
        Img("Laptop ASUS TUF Gaming F15", "/images/tuf-f15.jpg");
        Img("Tai nghe Sony WH-CH520", "/images/wh-ch520.jpg");
        Img("Samsung Galaxy A55 5G", "/images/a55.jpg");
        Img("OPPO Reno11 5G", "/images/reno11.jpg");
        Img("Laptop Lenovo LOQ 15", "/images/loq15.jpg");
        Img("iPad Gen 10 10.9\" 64GB WiFi", "/images/ipad10.png");
        Img("Đồng hồ Xiaomi Watch S3", "/images/watch-s3.png");
        Img("Đồng hồ Huawei Watch GT 4", "/images/watch-gt4.png");
        Img("Chuột Gaming Logitech G502 HERO", "/images/g502-hero.webp");
        Img("SSD Samsung 980 PRO 1TB NVMe", "/images/ssd-980pro.jpg");
        Img("Tai nghe True Wireless Xiaomi Buds 5", "/images/earbuds.jpg");
        foreach (var sp in products.Where(p => !images.Any(i => i.ProductId == p.Id)))
            images.Add(new ProductImage { ProductId = sp.Id, Url = "/images/placeholder.svg", LaAnhChinh = true, ThuTu = 0 });
        if (!await context.ProductImages.AnyAsync())
            context.ProductImages.AddRange(images);

        // --- Bien the: mau / dung luong ---
        // Sprint 3 — Admin (audit P1-7): tong TonKho cac bien the = TonKho
        // SP (Xiaomi 12+8+4=24, iPhone 8+6+4=18, TUF 6+5=11, WH-CH520
        // 23+15=38). Chi de so lieu hien thi khong lech — TonKho bien the
        // KHONG dung trong tinh toan ton kho (xem ProductVariant.cs).
        if (!await context.ProductVariants.AnyAsync())
            context.ProductVariants.AddRange(
            new ProductVariant { ProductId = P("Xiaomi 15 5G").Id, TenBienThe = "Đen / 256GB", GiaThem = 0, TonKho = 12 },
            new ProductVariant { ProductId = P("Xiaomi 15 5G").Id, TenBienThe = "Trắng / 256GB", GiaThem = 0, TonKho = 8 },
            new ProductVariant { ProductId = P("Xiaomi 15 5G").Id, TenBienThe = "Đen / 512GB", GiaThem = 3_000_000, TonKho = 4 },
            new ProductVariant { ProductId = P("iPhone 15 Pro").Id, TenBienThe = "Titan Tự nhiên / 256GB", GiaThem = 0, TonKho = 8 },
            new ProductVariant { ProductId = P("iPhone 15 Pro").Id, TenBienThe = "Titan Đen / 256GB", GiaThem = 0, TonKho = 6 },
            new ProductVariant { ProductId = P("iPhone 15 Pro").Id, TenBienThe = "Titan Tự nhiên / 512GB", GiaThem = 4_000_000, TonKho = 4 },
            new ProductVariant { ProductId = P("Laptop ASUS TUF Gaming F15").Id, TenBienThe = "8GB / 512GB", GiaThem = 0, TonKho = 6 },
            new ProductVariant { ProductId = P("Laptop ASUS TUF Gaming F15").Id, TenBienThe = "16GB / 512GB", GiaThem = 2_000_000, TonKho = 5 },
            new ProductVariant { ProductId = P("Tai nghe Sony WH-CH520").Id, TenBienThe = "Đen", GiaThem = 0, TonKho = 23 },
            new ProductVariant { ProductId = P("Tai nghe Sony WH-CH520").Id, TenBienThe = "Trắng", GiaThem = 0, TonKho = 15 }
        );

        // --- Voucher ---
        var vouchers = new[]
        {
            new Voucher { Ma = "TECHZONE500", LoaiGiam = "Tien", GiaTri = 500_000, DieuKien = 5_000_000, NgayHetHan = new DateTime(2026, 12, 31), SoLuong = 100, DaDung = 1 },
            new Voucher { Ma = "WELCOME100", LoaiGiam = "Tien", GiaTri = 100_000, DieuKien = 1_000_000, NgayHetHan = new DateTime(2026, 12, 31), SoLuong = 200, DaDung = 0 },
            // Voucher nang cao (nhom B): theo hang / mien phi ship / theo hang thanh vien.
            new Voucher { Ma = "XIAOMI10", LoaiGiam = "PhanTram", GiaTri = 10, DieuKien = 0, NgayHetHan = new DateTime(2026, 12, 31), SoLuong = 100, DaDung = 0, BrandId = Br("Xiaomi").Id },
            new Voucher { Ma = "FREESHIP", LoaiGiam = "Tien", GiaTri = 0, DieuKien = 500_000, NgayHetHan = new DateTime(2026, 12, 31), SoLuong = 100, DaDung = 0, FreeShip = true },
            new Voucher { Ma = "VÀNG5", LoaiGiam = "PhanTram", GiaTri = 5, DieuKien = 0, NgayHetHan = new DateTime(2026, 12, 31), SoLuong = 50, DaDung = 0, MinTier = "Vàng" },
        };
        if (!await context.Vouchers.AnyAsync())
            context.Vouchers.AddRange(vouchers);

        // --- Flash Sale: chay trong ngay seed (hom nay) ---
        var homNay = DateTime.Today;
        if (!await context.FlashSales.AnyAsync())
            context.FlashSales.AddRange(
            new FlashSale { ProductId = P("Xiaomi 15 5G").Id, GiaSale = 12_990_000, BatDau = homNay, KetThuc = homNay.AddDays(1).AddTicks(-1), GioiHanSL = 20, DaBan = 7 },
            new FlashSale { ProductId = P("Tai nghe True Wireless Xiaomi Buds 5").Id, GiaSale = 1_090_000, BatDau = homNay, KetThuc = homNay.AddDays(1).AddTicks(-1), GioiHanSL = 30, DaBan = 12 },
            new FlashSale { ProductId = P("Tai nghe Sony WH-CH520").Id, GiaSale = 850_000, BatDau = homNay, KetThuc = homNay.AddDays(1).AddTicks(-1), GioiHanSL = 25, DaBan = 9 }
        );

        // --- Combo mau (Sprint 3 — B-7) ---
        // Cap mon lay theo ban live artifact (COMBO_PARTNER): Xiaomi 15 + Buds 5,
        // TUF F15 + G502 HERO. Gia goc lay dung gia seed: Xiaomi 15 12.990.000d
        // (GiaKhuyenMai) + Buds 5 1.190.000d = 14.180.000d/bo; TUF 18.490.000d +
        // G502 1.090.000d = 19.580.000d/bo. % giam la quyet dinh kinh doanh moi
        // (artifact chi cong gia, khong giam them): 10% / 5%.
        // LUU Y: trong ngay seed, Buds 5 dang co Flash Sale 1.090.000d nen gia
        // thuc tra cua bo 1 luc checkout la 14.080.000d (khong phai 14.180.000d)
        // va giam 10% = 1.408.000d — combo % ap tren gia thuc tra, xem Combo.cs.
        if (!await context.Combos.AnyAsync())
        {
            var comboXiaomi = new Combo
            {
                Ten = "Combo Xiaomi 15 + Buds 5",
                MoTa = "Flagship Xiaomi 15 kèm tai nghe chống ồn Buds 5 — giảm 10% cả bộ.",
                PhanTramGiam = 10, BatDau = homNay, KetThuc = homNay.AddDays(30),
                IsActive = true, GioiHanSL = 20, DaBan = 0,
                Items = new List<ComboItem>
                {
                    new() { ProductId = P("Xiaomi 15 5G").Id, SoLuong = 1 },
                    new() { ProductId = P("Tai nghe True Wireless Xiaomi Buds 5").Id, SoLuong = 1 },
                },
            };
            var comboTuf = new Combo
            {
                Ten = "Combo TUF F15 + G502 HERO",
                MoTa = "Laptop gaming TUF F15 kèm chuột G502 HERO — giảm 5% cả bộ.",
                PhanTramGiam = 5, BatDau = homNay, KetThuc = homNay.AddDays(30),
                IsActive = true, GioiHanSL = 15, DaBan = 0,
                Items = new List<ComboItem>
                {
                    new() { ProductId = P("Laptop ASUS TUF Gaming F15").Id, SoLuong = 1 },
                    new() { ProductId = P("Chuột Gaming Logitech G502 HERO").Id, SoLuong = 1 },
                },
            };
            context.Combos.AddRange(comboXiaomi, comboTuf);
        }

        // SEC-01: DemoAccounts tat (hoac thieu 1 trong 2 tai khoan) thi khong
        // seed duoc don mau / diem / thong bao phu thuoc tai khoan demo.
        // Luu cac nhom da seed o tren (danh muc, SP, voucher, flash sale,
        // combo...) roi ket thuc.
        if (khach is null || admin is null)
        {
            await context.SaveChangesAsync();
            return;
        }

        // --- Danh gia mau ---
        if (!await context.Reviews.AnyAsync())
            context.Reviews.AddRange(
            new Review { ProductId = P("Xiaomi 15 5G").Id, UserId = khach.Id, Sao = 5, NoiDung = "Máy chạy mượt, pin trâu, chụp đêm đẹp.", NgayDanhGia = new DateTime(2026, 11, 3), IsApproved = true },
            new Review { ProductId = P("Xiaomi 15 5G").Id, UserId = khach.Id, Sao = 4, NoiDung = "Màn đẹp, loa hơi nhỏ so với kỳ vọng.", NgayDanhGia = new DateTime(2026, 11, 4), IsApproved = true },
            new Review { ProductId = P("iPhone 15 Pro").Id, UserId = khach.Id, Sao = 5, NoiDung = "Khung titan cầm nhẹ, camera quay video rất tốt.", NgayDanhGia = new DateTime(2026, 11, 2), IsApproved = true },
            new Review { ProductId = P("Laptop ASUS TUF Gaming F15").Id, UserId = khach.Id, Sao = 5, NoiDung = "Chơi game mượt, quạt hơi ồn khi chạy max setting.", NgayDanhGia = new DateTime(2026, 11, 1), IsApproved = true }
        );

        // --- Don hang mau (tong tien khop voi chi tiet) ---
        var don1 = new Order
        {
            UserId = khach.Id, TrangThai = OrderStatus.DaGiao,
            DiaChiGiao = "Nguyễn Văn An, 0909123456, 123 Đường Lê Lợi, Quận 1, TP.HCM",
            PhuongThucTT = "MoMo", NgayDat = new DateTime(2026, 11, 2),
            InstallmentMonths = 6, DaThanhToan = true,
            MaVoucher = "TECHZONE500", GiamGia = 500_000, PhiShip = 0,
            TongTien = 13_680_000, // (12.990.000 + 1.190.000) - 500.000
            ChiTiet = new List<OrderDetail>
            {
                new() { ProductId = P("Xiaomi 15 5G").Id, DonGia = 12_990_000, SoLuong = 1 },
                new() { ProductId = P("Tai nghe True Wireless Xiaomi Buds 5").Id, DonGia = 1_190_000, SoLuong = 1 },
            }
        };
        var don2 = new Order
        {
            UserId = khach.Id, TrangThai = OrderStatus.DangGiao,
            DiaChiGiao = "Nguyễn Văn An, 0909123456, 123 Đường Lê Lợi, Quận 1, TP.HCM",
            PhuongThucTT = "COD", NgayDat = new DateTime(2026, 11, 4),
            GiamGia = 0, PhiShip = 0, TongTien = 18_490_000,
            ChiTiet = new List<OrderDetail>
            {
                new() { ProductId = P("Laptop ASUS TUF Gaming F15").Id, DonGia = 18_490_000, SoLuong = 1 },
            }
        };
        var don3 = new Order
        {
            UserId = khach.Id, TrangThai = OrderStatus.ChoXacNhan,
            DiaChiGiao = "Nguyễn Văn An, 0909123456, 123 Đường Lê Lợi, Quận 1, TP.HCM",
            PhuongThucTT = "COD", NgayDat = new DateTime(2026, 11, 5),
            GiamGia = 0, PhiShip = 30_000, TongTien = 1_780_000, // 890.000 x 2
            ChiTiet = new List<OrderDetail>
            {
                new() { ProductId = P("Tai nghe Sony WH-CH520").Id, DonGia = 890_000, SoLuong = 2 },
            }
        };
        if (!await context.Orders.AnyAsync())
        {
            context.Orders.AddRange(don1, don2, don3);
            await context.SaveChangesAsync();
        }
        else
        {
            // DB cu: dung lai 3 don mau neu co (nhan dien theo NgayDat, moi don
            // 1 ngay khac nhau); don nao thieu thi tao moi de lich su/diem
            // seed phia duoi co OrderId that de tham chieu.
            var found1 = await context.Orders.FirstOrDefaultAsync(o => o.NgayDat == don1.NgayDat);
            var found2 = await context.Orders.FirstOrDefaultAsync(o => o.NgayDat == don2.NgayDat);
            var found3 = await context.Orders.FirstOrDefaultAsync(o => o.NgayDat == don3.NgayDat);
            var missing = new List<Order>();
            if (found1 is null) missing.Add(don1); else don1 = found1;
            if (found2 is null) missing.Add(don2); else don2 = found2;
            if (found3 is null) missing.Add(don3); else don3 = found3;
            if (missing.Count > 0)
            {
                context.Orders.AddRange(missing);
                await context.SaveChangesAsync();
            }
        }

        // --- Flash Sale theo khung gio (nhom B): 3 khung/ngay ---
        if (!await context.FlashSaleSlots.AnyAsync())
        {
            var slot0 = new FlashSaleSlot { GioBatDau = 0, GioKetThuc = 12 };
            var slot12 = new FlashSaleSlot { GioBatDau = 12, GioKetThuc = 20 };
            var slot20 = new FlashSaleSlot { GioBatDau = 20, GioKetThuc = 24 };
            context.FlashSaleSlots.AddRange(slot0, slot12, slot20);
            await context.SaveChangesAsync();
            context.FlashSaleSlotItems.AddRange(
                new FlashSaleSlotItem { SlotId = slot0.Id, ProductId = P("Xiaomi 15 5G").Id, GiaSale = 12_990_000 },
                new FlashSaleSlotItem { SlotId = slot0.Id, ProductId = P("Tai nghe True Wireless Xiaomi Buds 5").Id, GiaSale = 1_090_000 },
                new FlashSaleSlotItem { SlotId = slot12.Id, ProductId = P("Laptop ASUS TUF Gaming F15").Id, GiaSale = 18_490_000 },
                new FlashSaleSlotItem { SlotId = slot12.Id, ProductId = P("Tai nghe Sony WH-CH520").Id, GiaSale = 850_000 },
                new FlashSaleSlotItem { SlotId = slot20.Id, ProductId = P("iPhone 15 Pro").Id, GiaSale = 28_990_000 },
                new FlashSaleSlotItem { SlotId = slot20.Id, ProductId = P("Đồng hồ Xiaomi Watch S3").Id, GiaSale = 2_390_000 }
            );
        }

        // --- Banner trang chu (nhom D) ---
        if (!await context.Banners.AnyAsync())
            context.Banners.AddRange(
            new Banner { TieuDe = "Flash Sale 12h — Giảm tới 30%", AnhUrl = "/images/xiaomi15.png", LinkUrl = "/Products", ThuTu = 1, IsActive = true },
            new Banner { TieuDe = "Trả góp 0% — Xiaomi 15 chỉ 2.165.000đ/tháng", AnhUrl = "/images/iphone15pro.jpg", LinkUrl = "/Products/Details/1", ThuTu = 2, IsActive = true }
        );

        // --- Lich su trang thai don mau (nhom C): timeline theo doi don ---
        if (!await context.OrderStatusHistories.AnyAsync())
            context.OrderStatusHistories.AddRange(
            new OrderStatusHistory { OrderId = don1.Id, TrangThai = OrderStatus.ChoXacNhan, NgayCapNhat = new DateTime(2026, 11, 2, 10, 0, 0) },
            new OrderStatusHistory { OrderId = don1.Id, TrangThai = OrderStatus.DaXacNhan, NgayCapNhat = new DateTime(2026, 11, 2, 14, 0, 0) },
            new OrderStatusHistory { OrderId = don1.Id, TrangThai = OrderStatus.DangDongGoi, NgayCapNhat = new DateTime(2026, 11, 3, 9, 0, 0) },
            new OrderStatusHistory { OrderId = don1.Id, TrangThai = OrderStatus.DangGiao, NgayCapNhat = new DateTime(2026, 11, 3, 15, 0, 0) },
            new OrderStatusHistory { OrderId = don1.Id, TrangThai = OrderStatus.DaGiao, NgayCapNhat = new DateTime(2026, 11, 4, 11, 0, 0) },
            new OrderStatusHistory { OrderId = don2.Id, TrangThai = OrderStatus.ChoXacNhan, NgayCapNhat = new DateTime(2026, 11, 4, 9, 30, 0) },
            new OrderStatusHistory { OrderId = don2.Id, TrangThai = OrderStatus.DaXacNhan, NgayCapNhat = new DateTime(2026, 11, 4, 11, 0, 0) },
            new OrderStatusHistory { OrderId = don2.Id, TrangThai = OrderStatus.DangDongGoi, NgayCapNhat = new DateTime(2026, 11, 4, 16, 0, 0) },
            new OrderStatusHistory { OrderId = don2.Id, TrangThai = OrderStatus.DangGiao, NgayCapNhat = new DateTime(2026, 11, 5, 8, 0, 0) },
            new OrderStatusHistory { OrderId = don3.Id, TrangThai = OrderStatus.ChoXacNhan, NgayCapNhat = new DateTime(2026, 11, 5, 8, 15, 0) }
        );

        // --- Diem tich luy cua khach demo (nhom B): 1% gia tri don ---
        // don1 13.680.000 -> 136 diem; don2 18.490.000 -> 184; don3 1.780.000 -> 17.
        // Tong chi tieu 33.950.000d -> hang Vang (>20tr) ngay tu dau de demo duoc.
        if (!await context.PointTransactions.AnyAsync())
            context.PointTransactions.AddRange(
            new PointTransaction { UserId = khach.Id, Diem = 136, Loai = "Earn", OrderId = don1.Id, NgayTao = new DateTime(2026, 11, 2) },
            new PointTransaction { UserId = khach.Id, Diem = 184, Loai = "Earn", OrderId = don2.Id, NgayTao = new DateTime(2026, 11, 4) },
            new PointTransaction { UserId = khach.Id, Diem = 17, Loai = "Earn", OrderId = don3.Id, NgayTao = new DateTime(2026, 11, 5) }
        );

        // --- Thong bao mau (nhom B/D) ---
        if (!await context.Notifications.AnyAsync())
            context.Notifications.AddRange(
            new Notification { UserId = khach.Id, TieuDe = "Đơn #3 đang chờ xác nhận", NoiDung = "Đơn hàng #3 của bạn đang chờ TechZone xác nhận.", DaDoc = false, NgayTao = new DateTime(2026, 11, 5, 8, 15, 0) },
            new Notification { UserId = khach.Id, TieuDe = "Flash Sale 12h sắp mở", NoiDung = "Khung sale 12h–20h có ASUS TUF Gaming F15 giảm còn 18.490.000đ.", DaDoc = true, NgayTao = new DateTime(2026, 11, 5, 7, 0, 0) }
        );

        // --- Hoi - dap mau (nhom A/D) ---
        if (!await context.ProductQuestions.AnyAsync())
            context.ProductQuestions.AddRange(
            new ProductQuestion { ProductId = P("Xiaomi 15 5G").Id, UserId = khach.Id, CauHoi = "Máy có hỗ trợ sạc không dây không?", TraLoi = "Có, Xiaomi 15 hỗ trợ sạc không dây 50W.", NguoiTraLoiId = admin.Id, NgayHoi = new DateTime(2026, 11, 3), NgayTraLoi = new DateTime(2026, 11, 3) },
            new ProductQuestion { ProductId = P("Laptop ASUS TUF Gaming F15").Id, UserId = khach.Id, CauHoi = "Laptop có nâng cấp RAM được không?", NgayHoi = new DateTime(2026, 11, 4) }
        );

        // --- Yeu cau doi tra mau (nhom C) ---
        if (!await context.ReturnRequests.AnyAsync())
            context.ReturnRequests.Add(
            new ReturnRequest { OrderId = don1.Id, ProductId = P("Tai nghe True Wireless Xiaomi Buds 5").Id, UserId = khach.Id, LyDo = "Muốn đổi sang màu đen (đang dùng màu trắng).", TrangThai = "Chờ duyệt", NgayTao = new DateTime(2026, 11, 5) }
        );

        // --- Lich su nhap kho mau (nhom D) ---
        // So luong nhap = ton ban dau truoc don mau: Xiaomi nhap 25 - don1
        // x1 = TonKho seed 24; TUF 12-1=11; Buds 50-1=49 (xem muc San pham).
        if (!await context.StockIns.AnyAsync())
            context.StockIns.AddRange(
            new StockIn { ProductId = P("Xiaomi 15 5G").Id, SoLuong = 25, GhiChu = "Nhập lô đầu", NgayNhap = new DateTime(2026, 11, 1) },
            new StockIn { ProductId = P("Laptop ASUS TUF Gaming F15").Id, SoLuong = 12, GhiChu = "Nhập lô đầu", NgayNhap = new DateTime(2026, 11, 1) },
            new StockIn { ProductId = P("Tai nghe True Wireless Xiaomi Buds 5").Id, SoLuong = 50, GhiChu = "Nhập lô đầu", NgayNhap = new DateTime(2026, 11, 2) }
        );

        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Tao 1 tai khoan demo tu cau hinh (SEC-01). Email/password rong thi bo
    /// qua tai khoan do + warning. User da ton tai thi giu nguyen (khong doi
    /// mat khau) — giong hanh vi cu cua CreateUserAsync.
    /// </summary>
    private static async Task<ApplicationUser?> TaoTaiKhoanDemoAsync(
        UserManager<ApplicationUser> userManager, ILogger? logger,
        string? email, string? password, string hoTen, string sdt, string role)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            logger?.LogWarning("DemoAccounts: thieu email/mat khau cho role {Role}, bo qua tai khoan demo nay.", role);
            return null;
        }
        return await CreateUserAsync(userManager, email.Trim(), password, hoTen, sdt, role);
    }

    private static async Task<ApplicationUser> CreateUserAsync(
        UserManager<ApplicationUser> userManager,
        string email, string password, string hoTen, string sdt, string role)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is not null) return user;
        user = new ApplicationUser
        {
            UserName = email, Email = email, HoTen = hoTen,
            SoDienThoai = sdt, EmailConfirmed = true
        };
        var result = await userManager.CreateAsync(user, password);
        if (!result.Succeeded)
            throw new InvalidOperationException("Khong tao duoc tai khoan " + email + ": " +
                string.Join("; ", result.Errors.Select(e => e.Description)));
        await userManager.AddToRoleAsync(user, role);
        return user;
    }
}
