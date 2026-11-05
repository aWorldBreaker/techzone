using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TechZone.Models;

namespace TechZone.Data;

/// <summary>
/// Du lieu mau THAT: ten, gia, thong so lay theo gia ban le thuc te thang 11/2026
/// (gia co the lech chut tuy thoi diem, nhung khong co gia vo ly kieu laptop
/// 2.390.000d, khong lap san pham, khong bia thong so). Anh: chi 4 san pham co
/// anh that cua hang trong wwwroot/images; cac san pham khac dung placeholder.svg
/// cho den khi co anh hang — xem README muc "Anh san pham".
/// </summary>
public static class SeedData
{
    public static async Task InitializeAsync(IServiceProvider services)
    {
        var context = services.GetRequiredService<AppDbContext>();
        await context.Database.EnsureCreatedAsync();
        if (await context.Products.AnyAsync()) return;

        // --- Tai khoan & vai tro ---
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        foreach (var role in new[] { "Admin", "Khach" })
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));

        var admin = await CreateUserAsync(userManager, "admin@techzone.vn", "Admin@123", "Quản trị TechZone", "0901234567", "Admin");
        var khach = await CreateUserAsync(userManager, "khach@techzone.vn", "Khach@123", "Nguyễn Văn An", "0909123456", "Khach");

        // --- Danh muc: 8 nhom do cong nghe ---
        var categories = new[]
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
        context.Categories.AddRange(categories);

        var brands = new[]
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
        context.Brands.AddRange(brands);
        await context.SaveChangesAsync();

        Category Cat(string ten) => categories.First(c => c.Ten == ten);
        Brand Br(string ten) => brands.First(b => b.Ten == ten);

        // --- San pham (13) ---
        var products = new List<Product>
        {
            new() { Ten = "Xiaomi 15 5G", MoTa = "Flagship Xiaomi: chip Snapdragon 8 Gen 3, camera Leica 50MP, sạc nhanh 90W.", Gia = 15_990_000, GiaKhuyenMai = 12_990_000, CategoryId = Cat("Điện thoại").Id, BrandId = Br("Xiaomi").Id, TonKho = 25, LuotBan = 128, DanhGiaTB = 4.8, LaNoiBat = true },
            new() { Ten = "iPhone 15 Pro", MoTa = "iPhone khung titan, chip Apple A17 Pro, camera chính 48MP, cổng USB-C.", Gia = 31_990_000, GiaKhuyenMai = 28_990_000, CategoryId = Cat("Điện thoại").Id, BrandId = Br("Apple").Id, TonKho = 18, LuotBan = 96, DanhGiaTB = 4.9, LaNoiBat = true },
            new() { Ten = "Laptop ASUS TUF Gaming F15", MoTa = "Laptop gaming bền chuẩn quân đội, RTX 3050, màn 144Hz.", Gia = 24_650_000, GiaKhuyenMai = 18_490_000, CategoryId = Cat("Laptop").Id, BrandId = Br("ASUS").Id, TonKho = 12, LuotBan = 54, DanhGiaTB = 4.7, LaNoiBat = true },
            new() { Ten = "Tai nghe Sony WH-CH520", MoTa = "Tai nghe on-ear nhẹ, pin 50 giờ, Bluetooth 5.2, mic đàm thoại rõ.", Gia = 1_150_000, GiaKhuyenMai = 890_000, CategoryId = Cat("Tai nghe").Id, BrandId = Br("Sony").Id, TonKho = 40, LuotBan = 210, DanhGiaTB = 4.6, LaNoiBat = true },
            new() { Ten = "Samsung Galaxy A55 5G", MoTa = "Tầm trung Samsung: khung kim loại, màn Super AMOLED 120Hz, chống nước IP67.", Gia = 9_990_000, GiaKhuyenMai = 7_490_000, CategoryId = Cat("Điện thoại").Id, BrandId = Br("Samsung").Id, TonKho = 30, LuotBan = 87, DanhGiaTB = 4.7 },
            new() { Ten = "OPPO Reno11 5G", MoTa = "Chụp chân dung đẹp, sạc siêu nhanh 67W, màn AMOLED cong 120Hz.", Gia = 10_990_000, GiaKhuyenMai = 9_990_000, CategoryId = Cat("Điện thoại").Id, BrandId = Br("OPPO").Id, TonKho = 22, LuotBan = 63, DanhGiaTB = 4.6 },
            new() { Ten = "Laptop Lenovo LOQ 15", MoTa = "Laptop gaming giá tốt, RTX 4050, tản nhiệt 2 quạt, màn 144Hz.", Gia = 25_990_000, GiaKhuyenMai = 22_990_000, CategoryId = Cat("Laptop").Id, BrandId = Br("Lenovo").Id, TonKho = 10, LuotBan = 41, DanhGiaTB = 4.7 },
            new() { Ten = "iPad Gen 10 10.9\" 64GB WiFi", MoTa = "iPad phổ thông: màn Liquid Retina 10.9 inch, chip A14, cổng USB-C.", Gia = 11_490_000, GiaKhuyenMai = 9_990_000, CategoryId = Cat("Tablet").Id, BrandId = Br("Apple").Id, TonKho = 15, LuotBan = 58, DanhGiaTB = 4.8 },
            new() { Ten = "Đồng hồ Xiaomi Watch S3", MoTa = "Smartwatch mặt tròn AMOLED 1.43 inch, GPS, pin 15 ngày.", Gia = 3_290_000, GiaKhuyenMai = 2_390_000, CategoryId = Cat("Đồng hồ").Id, BrandId = Br("Xiaomi").Id, TonKho = 35, LuotBan = 74, DanhGiaTB = 4.5 },
            new() { Ten = "Đồng hồ Huawei Watch GT 4", MoTa = "Smartwatch pin 14 ngày, theo dõi sức khỏe, GPS chính xác.", Gia = 5_500_000, GiaKhuyenMai = 4_290_000, CategoryId = Cat("Đồng hồ").Id, BrandId = Br("Huawei").Id, TonKho = 20, LuotBan = 47, DanhGiaTB = 4.6 },
            new() { Ten = "Tai nghe True Wireless Xiaomi Buds 5", MoTa = "Tai nghe không dây chống ồn ANC, Bluetooth 5.4, pin 24 giờ kèm hộp sạc.", Gia = 1_650_000, GiaKhuyenMai = 1_190_000, CategoryId = Cat("Tai nghe").Id, BrandId = Br("Xiaomi").Id, TonKho = 50, LuotBan = 132, DanhGiaTB = 4.5 },
            new() { Ten = "Chuột Gaming Logitech G502 HERO", MoTa = "Chuột gaming cảm biến HERO 25K DPI, 11 nút lập trình, tạ điều chỉnh.", Gia = 1_390_000, GiaKhuyenMai = 1_090_000, CategoryId = Cat("Gaming").Id, BrandId = Br("Logitech").Id, TonKho = 45, LuotBan = 88, DanhGiaTB = 4.8 },
            new() { Ten = "SSD Samsung 980 PRO 1TB NVMe", MoTa = "SSD NVMe PCIe 4.0, đọc 7000MB/s, cho PC và PS5.", Gia = 3_190_000, GiaKhuyenMai = 2_490_000, CategoryId = Cat("Linh kiện PC").Id, BrandId = Br("Samsung").Id, TonKho = 28, LuotBan = 66, DanhGiaTB = 4.9 },
        };
        context.Products.AddRange(products);
        await context.SaveChangesAsync();

        Product P(string ten) => products.First(p => p.Ten == ten);

        // --- Thong so ky thuat (bang rieng ProductSpecs) ---
        context.ProductSpecs.AddRange(
            new ProductSpecs { ProductId = P("Xiaomi 15 5G").Id, Chip = "Snapdragon 8 Gen 3", Ram = "12GB", Rom = "256GB", Pin = "5000mAh", ManHinh = "6.7\" AMOLED 120Hz", Camera = "50MP" },
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

        // --- Anh san pham: chi 4 anh that, con lai placeholder ---
        // xiaomi15.png: anh that Xiaomi 15. iphone15pro.jpg: anh that iPhone 15 Pro.
        // rog-laptop.png: anh laptop gaming ASUS (dung tam cho TUF F15, cung hang ASUS).
        // earbuds.jpg: anh that tai nghe true wireless (dung cho Buds 5).
        // WH-CH520 la tai nghe on-ear, khong dung anh earbuds cho no — dung placeholder.
        var images = new List<ProductImage>();
        void Img(string tenSp, string url) =>
            images.Add(new ProductImage { ProductId = P(tenSp).Id, Url = url, LaAnhChinh = true, ThuTu = 0 });
        Img("Xiaomi 15 5G", "/images/xiaomi15.png");
        Img("iPhone 15 Pro", "/images/iphone15pro.jpg");
        Img("Laptop ASUS TUF Gaming F15", "/images/rog-laptop.png");
        Img("Tai nghe True Wireless Xiaomi Buds 5", "/images/earbuds.jpg");
        foreach (var sp in products.Where(p => !images.Any(i => i.ProductId == p.Id)))
            images.Add(new ProductImage { ProductId = sp.Id, Url = "/images/placeholder.svg", LaAnhChinh = true, ThuTu = 0 });
        context.ProductImages.AddRange(images);

        // --- Bien the: mau / dung luong ---
        context.ProductVariants.AddRange(
            new ProductVariant { ProductId = P("Xiaomi 15 5G").Id, TenBienThe = "Đen / 256GB", GiaThem = 0, TonKho = 15 },
            new ProductVariant { ProductId = P("Xiaomi 15 5G").Id, TenBienThe = "Trắng / 256GB", GiaThem = 0, TonKho = 10 },
            new ProductVariant { ProductId = P("Xiaomi 15 5G").Id, TenBienThe = "Đen / 512GB", GiaThem = 3_000_000, TonKho = 8 },
            new ProductVariant { ProductId = P("iPhone 15 Pro").Id, TenBienThe = "Titan Tự nhiên / 256GB", GiaThem = 0, TonKho = 10 },
            new ProductVariant { ProductId = P("iPhone 15 Pro").Id, TenBienThe = "Titan Đen / 256GB", GiaThem = 0, TonKho = 8 },
            new ProductVariant { ProductId = P("iPhone 15 Pro").Id, TenBienThe = "Titan Tự nhiên / 512GB", GiaThem = 4_000_000, TonKho = 5 },
            new ProductVariant { ProductId = P("Laptop ASUS TUF Gaming F15").Id, TenBienThe = "8GB / 512GB", GiaThem = 0, TonKho = 7 },
            new ProductVariant { ProductId = P("Laptop ASUS TUF Gaming F15").Id, TenBienThe = "16GB / 512GB", GiaThem = 2_000_000, TonKho = 5 },
            new ProductVariant { ProductId = P("Tai nghe Sony WH-CH520").Id, TenBienThe = "Đen", GiaThem = 0, TonKho = 25 },
            new ProductVariant { ProductId = P("Tai nghe Sony WH-CH520").Id, TenBienThe = "Trắng", GiaThem = 0, TonKho = 15 }
        );

        // --- Voucher ---
        var vouchers = new[]
        {
            new Voucher { Ma = "TECHZONE500", LoaiGiam = "Tien", GiaTri = 500_000, DieuKien = 5_000_000, NgayHetHan = new DateTime(2026, 12, 31), SoLuong = 100, DaDung = 1 },
            new Voucher { Ma = "WELCOME100", LoaiGiam = "Tien", GiaTri = 100_000, DieuKien = 1_000_000, NgayHetHan = new DateTime(2026, 12, 31), SoLuong = 200, DaDung = 0 },
        };
        context.Vouchers.AddRange(vouchers);

        // --- Flash Sale: chay trong ngay seed (hom nay) ---
        var homNay = DateTime.Today;
        context.FlashSales.AddRange(
            new FlashSale { ProductId = P("Xiaomi 15 5G").Id, GiaSale = 12_990_000, BatDau = homNay, KetThuc = homNay.AddDays(1).AddTicks(-1), GioiHanSL = 20, DaBan = 7 },
            new FlashSale { ProductId = P("Tai nghe True Wireless Xiaomi Buds 5").Id, GiaSale = 1_090_000, BatDau = homNay, KetThuc = homNay.AddDays(1).AddTicks(-1), GioiHanSL = 30, DaBan = 12 },
            new FlashSale { ProductId = P("Tai nghe Sony WH-CH520").Id, GiaSale = 850_000, BatDau = homNay, KetThuc = homNay.AddDays(1).AddTicks(-1), GioiHanSL = 25, DaBan = 9 }
        );

        // --- Danh gia mau ---
        context.Reviews.AddRange(
            new Review { ProductId = P("Xiaomi 15 5G").Id, UserId = khach.Id, Sao = 5, NoiDung = "Máy chạy mượt, pin trâu, chụp đêm đẹp.", NgayDanhGia = new DateTime(2026, 11, 3) },
            new Review { ProductId = P("Xiaomi 15 5G").Id, UserId = khach.Id, Sao = 4, NoiDung = "Màn đẹp, loa hơi nhỏ so với kỳ vọng.", NgayDanhGia = new DateTime(2026, 11, 4) },
            new Review { ProductId = P("iPhone 15 Pro").Id, UserId = khach.Id, Sao = 5, NoiDung = "Khung titan cầm nhẹ, camera quay video rất tốt.", NgayDanhGia = new DateTime(2026, 11, 2) },
            new Review { ProductId = P("Laptop ASUS TUF Gaming F15").Id, UserId = khach.Id, Sao = 5, NoiDung = "Chơi game mượt, quạt hơi ồn khi chạy max setting.", NgayDanhGia = new DateTime(2026, 11, 1) }
        );

        // --- Don hang mau (tong tien khop voi chi tiet) ---
        var don1 = new Order
        {
            UserId = khach.Id, TrangThai = OrderStatus.DaGiao,
            DiaChiGiao = "Nguyễn Văn An, 0909123456, 123 Đường Lê Lợi, Quận 1, TP.HCM",
            PhuongThucTT = "COD", NgayDat = new DateTime(2026, 11, 2),
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
        context.Orders.AddRange(don1, don2, don3);

        await context.SaveChangesAsync();
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
