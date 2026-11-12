using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TechZone.Controllers;
using TechZone.Data;
using TechZone.Models;
using TechZone.Services;

namespace TechZone.Tests;

/// <summary>
/// Test Dot 2 Admin-enhance ("Dung nhanh hon", 2026-11-12):
/// - (a) B5: chi so Dashboard moi (DoanhThuHomNay, GiaTriDonTrungBinh,
///   TiLeHuy, DoiTraChoDuyet, SanPhamHetHang, GiaTriTonKho theo GIA BAN,
///   DoanhThuThangTruoc, TiLeDoiThang) tinh dung; don huy khong tinh vao
///   doanh thu; thang truoc = 0 thi TiLeDoiThang = null.
/// - (b) B1: tim kiem + phan trang + sort server o Products / Orders /
///   Vouchers / KhachHang (tim theo ten/ma/SDT; KhachHang loc in-memory
///   tren ket qua da co, khong doi cach tinh tong chi tieu).
/// - (c) B6: BatTatSlot chi doi co IsActive (gia sale cua item khong doi),
///   goi 2 lan thi ve trang thai cu.
/// - (d) Marker file Dot 2: B1/B2/B5/B6/A4/A6/B7 co mat o dung file.
/// DB test: file tam + MigrateAsync; KHONG dung techzone.db that.
/// </summary>
[Collection("DbTests")]
public class AdminWave2Tests
{
    // ============ (a) B5: chi so moi ============

    [Fact]
    public async Task Dashboard_ChiSoMoiB5_TinhDung()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var thangTruocNgay = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddDays(-15);
            int idSp1, idSp2;
            using (var scope = services.CreateScope())
            {
                var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await ctx.Database.MigrateAsync();
                var cat = new Category { Ten = "Test Cat", Slug = "test-cat" };
                var brand = new Brand { Ten = "Test Brand" };
                ctx.Categories.Add(cat); ctx.Brands.Add(brand);
                await ctx.SaveChangesAsync();
                // Gia tri ton kho theo GIA BAN: P1 het hang (0 x 1.000.000),
                // P2 3 x gia KM 1.500.000, P3 10 x 500.000 -> 9.500.000.
                var p1 = new Product { Ten = "W2 P1", Gia = 1_000_000, CategoryId = cat.Id, BrandId = brand.Id, TonKho = 0, NgayTao = DateTime.Today };
                var p2 = new Product { Ten = "W2 P2", Gia = 2_000_000, GiaKhuyenMai = 1_500_000, CategoryId = cat.Id, BrandId = brand.Id, TonKho = 3, NgayTao = DateTime.Today };
                var p3 = new Product { Ten = "W2 P3", Gia = 500_000, CategoryId = cat.Id, BrandId = brand.Id, TonKho = 10, NgayTao = DateTime.Today };
                ctx.Products.AddRange(p1, p2, p3);
                await ctx.SaveChangesAsync();
                idSp1 = p1.Id; idSp2 = p2.Id;
                ctx.Orders.AddRange(
                    new Order { TrangThai = OrderStatus.DaGiao, NgayDat = DateTime.Today.AddHours(9), TongTien = 1_000_000, DiaChiGiao = "A" },
                    new Order { TrangThai = OrderStatus.ChoXacNhan, NgayDat = DateTime.Today.AddHours(10), TongTien = 200_000, DiaChiGiao = "B" },
                    new Order { TrangThai = OrderStatus.DaGiao, NgayDat = DateTime.Today.AddDays(-9), TongTien = 3_000_000, DiaChiGiao = "C" },
                    new Order { TrangThai = OrderStatus.DaGiao, NgayDat = thangTruocNgay, TongTien = 2_000_000, DiaChiGiao = "D" },
                    new Order { TrangThai = OrderStatus.DaHuy, NgayDat = thangTruocNgay, TongTien = 9_000_000, DiaChiGiao = "E" },
                    new Order { TrangThai = OrderStatus.DaHuy, NgayDat = DateTime.Today.AddDays(-2), TongTien = 500_000, DiaChiGiao = "F" });
                await ctx.SaveChangesAsync();
                var don1 = await ctx.Orders.FirstAsync(o => o.DiaChiGiao == "A");
                ctx.ReturnRequests.AddRange(
                    new ReturnRequest { OrderId = don1.Id, ProductId = idSp1, TrangThai = "Chờ duyệt", LyDo = "Loi", NgayTao = DateTime.Today },
                    new ReturnRequest { OrderId = don1.Id, ProductId = idSp2, TrangThai = "Chờ duyệt", LyDo = "Doi mau", NgayTao = DateTime.Today },
                    new ReturnRequest { OrderId = don1.Id, ProductId = idSp2, TrangThai = "Đã duyệt", LyDo = "Hong", NgayTao = DateTime.Today });
                await ctx.SaveChangesAsync();
            }

            DashboardViewModel vm;
            using (var scope = services.CreateScope())
            {
                var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var orders = scope.ServiceProvider.GetRequiredService<OrderService>();
                var admin = new AdminController(ctx, null!, orders);
                var view = Assert.IsType<ViewResult>(await admin.Dashboard());
                vm = Assert.IsType<DashboardViewModel>(view.Model);
            }

            // Hom nay = 1.000.000 + 200.000 (don huy 500.000 khong tinh).
            Assert.Equal(1_200_000, vm.DoanhThuHomNay);
            // Thang nay = 1.000.000 + 200.000 + 3.000.000 = 4.200.000 / 3 don.
            Assert.Equal(4_200_000, vm.DoanhThuThang);
            Assert.Equal(1_400_000, vm.GiaTriDonTrungBinh);
            // Huy 2 / tong 6 don.
            Assert.Equal(6, vm.TongDon);
            Assert.Equal(2, vm.DonDaHuy);
            Assert.Equal(2m / 6m, vm.TiLeHuy);
            Assert.Equal(2, vm.DoiTraChoDuyet);
            Assert.Equal(1, vm.SanPhamHetHang);
            Assert.Equal(9_500_000, vm.GiaTriTonKho);
            // Thang truoc = 2.000.000 (don huy 9.000.000 khong tinh);
            // delta = (4.200.000 - 2.000.000) / 2.000.000 = 1.1.
            Assert.Equal(2_000_000, vm.DoanhThuThangTruoc);
            Assert.NotNull(vm.TiLeDoiThang);
            Assert.Equal(1.1m, vm.TiLeDoiThang!.Value);
        }
        finally
        {
            DeleteDb(db);
        }
    }

    [Fact]
    public async Task Dashboard_ThangTruocBang0_TiLeDoiThangNull_GiaTriDonBang0()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            using (var scope = services.CreateScope())
            {
                var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await ctx.Database.MigrateAsync();
                ctx.Orders.Add(new Order
                {
                    TrangThai = OrderStatus.DaGiao,
                    NgayDat = DateTime.Today,
                    TongTien = 700_000,
                    DiaChiGiao = "A",
                });
                await ctx.SaveChangesAsync();
            }

            DashboardViewModel vm;
            using (var scope = services.CreateScope())
            {
                var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var orders = scope.ServiceProvider.GetRequiredService<OrderService>();
                var admin = new AdminController(ctx, null!, orders);
                var view = Assert.IsType<ViewResult>(await admin.Dashboard());
                vm = Assert.IsType<DashboardViewModel>(view.Model);
            }

            Assert.Equal(700_000, vm.DoanhThuHomNay);
            Assert.Equal(700_000, vm.GiaTriDonTrungBinh);
            Assert.Equal(0, vm.DoanhThuThangTruoc);
            Assert.Null(vm.TiLeDoiThang);
            Assert.Equal(0, vm.TiLeHuy);
            Assert.Equal(0, vm.DoiTraChoDuyet);
            Assert.Equal(0, vm.SanPhamHetHang);
            Assert.Equal(0, vm.GiaTriTonKho);
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ============ (b) B1: tim kiem + phan trang + sort ============

    [Fact]
    public async Task Products_TimKiem_PhanTrang_SortGia_TimTheoMa()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            int idMucTieu;
            using (var scope = services.CreateScope())
            {
                var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await ctx.Database.MigrateAsync();
                var cat = new Category { Ten = "Test Cat", Slug = "test-cat" };
                var brand = new Brand { Ten = "Test Brand" };
                ctx.Categories.Add(cat); ctx.Brands.Add(brand);
                await ctx.SaveChangesAsync();
                var list = new List<Product>();
                for (var i = 1; i <= 12; i++)
                    list.Add(new Product
                    {
                        Ten = $"W2-Phone-{i:00}",
                        Gia = i * 100_000,
                        CategoryId = cat.Id,
                        BrandId = brand.Id,
                        TonKho = i,
                        LuotBan = i,
                        NgayTao = DateTime.Today.AddDays(-i),
                    });
                ctx.Products.AddRange(list);
                await ctx.SaveChangesAsync();
                idMucTieu = list[4].Id; // W2-Phone-05
            }

            using (var scope = services.CreateScope())
            {
                var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var orders = scope.ServiceProvider.GetRequiredService<OrderService>();
                var admin = new AdminController(ctx, null!, orders);

                // Tim "W2-Phone-0" khop 01..09 (9 SP) — phan trang 10/trang
                // thi 1 trang; tim "W2-Phone" khop ca 12 -> 2 trang.
                var kqTim = Assert.IsType<ViewResult>(await admin.Products("W2-Phone", null, 1));
                Assert.Equal(12, (int)kqTim.ViewData["TongMuc"]!);
                Assert.Equal(2, (int)kqTim.ViewData["TongTrang"]!);
                Assert.Equal(10, Assert.IsType<List<Product>>(kqTim.Model).Count);
                var kqTrang2 = Assert.IsType<ViewResult>(await admin.Products("W2-Phone", null, 2));
                Assert.Equal(2, Assert.IsType<List<Product>>(kqTrang2.Model).Count);

                // Sort gia tang: muc dau tien re nhat (100.000).
                var kqSort = Assert.IsType<ViewResult>(await admin.Products("W2-Phone", "gia-asc", 1));
                var dsSort = Assert.IsType<List<Product>>(kqSort.Model);
                Assert.Equal(100_000, dsSort[0].GiaBan);
                Assert.Equal(900_000, dsSort[8].GiaBan);
                Assert.Equal(1_000_000, dsSort[9].GiaBan);

                // Tim theo ma SP (= Id): tra ve dung SP do.
                var kqMa = Assert.IsType<ViewResult>(await admin.Products(idMucTieu.ToString(), null, 1));
                var dsMa = Assert.IsType<List<Product>>(kqMa.Model);
                Assert.Contains(dsMa, p => p.Id == idMucTieu && p.Ten == "W2-Phone-05");

                // Tim khong co ket qua.
                var kqRong = Assert.IsType<ViewResult>(await admin.Products("W2-KHONG-CO", null, 1));
                Assert.Equal(0, (int)kqRong.ViewData["TongMuc"]!);
                Assert.Empty(Assert.IsType<List<Product>>(kqRong.Model));
            }
        }
        finally
        {
            DeleteDb(db);
        }
    }

    [Fact]
    public async Task Orders_TimTheoSdt_LocTrangThai_SortTongTien()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            using (var scope = services.CreateScope())
            {
                var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await ctx.Database.MigrateAsync();
                ctx.Orders.AddRange(
                    new Order { TrangThai = OrderStatus.ChoXacNhan, NgayDat = DateTime.Today, TongTien = 100_000, DiaChiGiao = "Nguyễn Test, 0901111222, Hà Nội" },
                    new Order { TrangThai = OrderStatus.DaXacNhan, NgayDat = DateTime.Today, TongTien = 900_000, DiaChiGiao = "Trần Test, 0901333444, TP.HCM" },
                    new Order { TrangThai = OrderStatus.DangDongGoi, NgayDat = DateTime.Today, TongTien = 500_000, DiaChiGiao = "Lê Test, 0901555666, Đà Nẵng" });
                await ctx.SaveChangesAsync();
            }

            using (var scope = services.CreateScope())
            {
                var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var orders = scope.ServiceProvider.GetRequiredService<OrderService>();
                var admin = new AdminController(ctx, null!, orders);

                var kqSdt = Assert.IsType<ViewResult>(await admin.Orders(null, "0901333444", null, 1));
                var dsSdt = Assert.IsType<List<Order>>(kqSdt.Model);
                Assert.Single(dsSdt);
                Assert.Equal(900_000, dsSdt[0].TongTien);

                // Loc dung 1 trong 6 trang thai (DaXacNhan — Dot 1 chip thieu).
                var kqTt = Assert.IsType<ViewResult>(await admin.Orders(OrderStatus.DaXacNhan, null, null, 1));
                Assert.Single(Assert.IsType<List<Order>>(kqTt.Model));

                // Sort tong tien giam: 900k, 500k, 100k.
                var kqSort = Assert.IsType<ViewResult>(await admin.Orders(null, null, "tong-desc", 1));
                var dsSort = Assert.IsType<List<Order>>(kqSort.Model);
                Assert.Equal(900_000, dsSort[0].TongTien);
                Assert.Equal(100_000, dsSort[2].TongTien);

                // Tim theo ma don: ket qua phai chua dung don do (tim so
                // con khop ca DiaChiGiao chua so do — tim substring, dung
                // thiet ke — nen khong assert Single o day).
                var idMucTieu = dsSort[1].Id;
                var kqMa = Assert.IsType<ViewResult>(await admin.Orders(null, idMucTieu.ToString(), null, 1));
                Assert.Contains(Assert.IsType<List<Order>>(kqMa.Model), o => o.Id == idMucTieu);
            }
        }
        finally
        {
            DeleteDb(db);
        }
    }

    [Fact]
    public async Task Vouchers_TimTheoMa_PhanTrang()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            using (var scope = services.CreateScope())
            {
                var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await ctx.Database.MigrateAsync();
                for (var i = 1; i <= 12; i++)
                    ctx.Vouchers.Add(new Voucher
                    {
                        Ma = $"W2-AAA{i:00}",
                        LoaiGiam = "Tien",
                        GiaTri = 10_000,
                        DieuKien = 0,
                        NgayHetHan = DateTime.Today.AddDays(30),
                        SoLuong = 10,
                    });
                await ctx.SaveChangesAsync();
            }

            using (var scope = services.CreateScope())
            {
                var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var orders = scope.ServiceProvider.GetRequiredService<OrderService>();
                var admin = new AdminController(ctx, null!, orders);

                // Tim chu thuong van khop (controller viet hoa tuKhoa).
                var kq = Assert.IsType<ViewResult>(await admin.Vouchers("w2-aaa", 1));
                Assert.Equal(12, (int)kq.ViewData["TongMuc"]!);
                Assert.Equal(2, (int)kq.ViewData["TongTrang"]!);
                Assert.Equal(10, Assert.IsType<List<Voucher>>(kq.Model).Count);
                var kq2 = Assert.IsType<ViewResult>(await admin.Vouchers("w2-aaa", 2));
                Assert.Equal(2, Assert.IsType<List<Voucher>>(kq2.Model).Count);
                var kq1 = Assert.IsType<ViewResult>(await admin.Vouchers("W2-AAA01", 1));
                Assert.Single(Assert.IsType<List<Voucher>>(kq1.Model));
            }
        }
        finally
        {
            DeleteDb(db);
        }
    }

    [Fact]
    public async Task KhachHang_TimTheoSdt_PhanTrang_KhongDoiTongChiTieu()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            using (var scope = services.CreateScope())
            {
                var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await ctx.Database.MigrateAsync();
                var u1 = new ApplicationUser { Id = Guid.NewGuid().ToString("N"), HoTen = "Nguyễn Văn Test", Email = "test1@x.vn", SoDienThoai = "0901111222", NgayTao = DateTime.Today, UserName = "test1@x.vn" };
                var u2 = new ApplicationUser { Id = Guid.NewGuid().ToString("N"), HoTen = "Trần Thị Test", Email = "test2@x.vn", SoDienThoai = "0901333444", NgayTao = DateTime.Today, UserName = "test2@x.vn" };
                ctx.Users.AddRange(u1, u2);
                // Don khong huy cua u1 = 1.000.000 (tong chi tieu giu nguyen
                // cach tinh cu: tru hoan, kep >= 0 — o day khong co hoan).
                ctx.Orders.AddRange(
                    new Order { UserId = u1.Id, TrangThai = OrderStatus.DaGiao, NgayDat = DateTime.Today, TongTien = 1_000_000, DiaChiGiao = "A" },
                    new Order { UserId = u1.Id, TrangThai = OrderStatus.DaHuy, NgayDat = DateTime.Today, TongTien = 5_000_000, DiaChiGiao = "A" },
                    new Order { UserId = u2.Id, TrangThai = OrderStatus.DaGiao, NgayDat = DateTime.Today, TongTien = 300_000, DiaChiGiao = "B" });
                await ctx.SaveChangesAsync();
            }

            using (var scope = services.CreateScope())
            {
                var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var orders = scope.ServiceProvider.GetRequiredService<OrderService>();
                var admin = new AdminController(ctx, null!, orders);

                var kq = Assert.IsType<ViewResult>(await admin.KhachHang("0901111222", 1));
                Assert.Equal("Customers", kq.ViewName);
                var ds = Assert.IsType<List<(ApplicationUser User, decimal TongChiTieu, string Hang, int Diem)>>(kq.Model);
                Assert.Single(ds);
                // Don huy 5.000.000 khong tinh — tong chi tieu van 1.000.000.
                Assert.Equal(1_000_000, ds[0].TongChiTieu);

                var kqHet = Assert.IsType<ViewResult>(await admin.KhachHang(null, 1));
                Assert.Equal(2, (int)kqHet.ViewData["TongMuc"]!);
                Assert.Equal(2, Assert.IsType<List<(ApplicationUser, decimal, string, int)>>(kqHet.Model).Count);
            }
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ============ (c) B6: BatTatSlot ============

    [Fact]
    public async Task BatTatSlot_ChiDoiCoIsActive_GiaSaleKhongDoi()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            int slotId, itemId;
            using (var scope = services.CreateScope())
            {
                var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await ctx.Database.MigrateAsync();
                var cat = new Category { Ten = "Test Cat", Slug = "test-cat" };
                var brand = new Brand { Ten = "Test Brand" };
                ctx.Categories.Add(cat); ctx.Brands.Add(brand);
                await ctx.SaveChangesAsync();
                var sp = new Product { Ten = "W2 SP", Gia = 1_000_000, CategoryId = cat.Id, BrandId = brand.Id, TonKho = 5, NgayTao = DateTime.Today };
                ctx.Products.Add(sp);
                await ctx.SaveChangesAsync();
                var slot = new FlashSaleSlot { GioBatDau = 8, GioKetThuc = 10, IsActive = true };
                ctx.FlashSaleSlots.Add(slot);
                await ctx.SaveChangesAsync();
                var item = new FlashSaleSlotItem { SlotId = slot.Id, ProductId = sp.Id, GiaSale = 800_000 };
                ctx.FlashSaleSlotItems.Add(item);
                await ctx.SaveChangesAsync();
                slotId = slot.Id; itemId = item.Id;
            }

            using (var scope = services.CreateScope())
            {
                var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var orders = scope.ServiceProvider.GetRequiredService<OrderService>();
                var admin = new AdminController(ctx, null!, orders);
                admin.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
                admin.TempData = new TempDataDictionary(admin.HttpContext, new FakeTempDataProvider());

                var kq1 = Assert.IsType<RedirectToActionResult>(await admin.BatTatSlot(slotId));
                Assert.Equal("FlashSaleSlots", kq1.ActionName);
                Assert.False((await ctx.FlashSaleSlots.FindAsync(slotId))!.IsActive);
                Assert.Equal(800_000, (await ctx.FlashSaleSlotItems.FindAsync(itemId))!.GiaSale);

                var kq2 = Assert.IsType<RedirectToActionResult>(await admin.BatTatSlot(slotId));
                Assert.Equal("FlashSaleSlots", kq2.ActionName);
                Assert.True((await ctx.FlashSaleSlots.FindAsync(slotId))!.IsActive);
                Assert.Equal(800_000, (await ctx.FlashSaleSlotItems.FindAsync(itemId))!.GiaSale);
            }
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ============ (d) Marker file Dot 2 ============

    [Fact]
    public void Wave2_MarkerFile_DuVaDung()
    {
        var root = ProjectRoot();
        string Doc(params string[] parts) => File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));

        // B5: field moi o ViewModel + hien o ca 2 trang Dashboard.
        var vm = Doc("Models", "ViewModels.cs");
        foreach (var f in new[] { "DoanhThuHomNay", "GiaTriDonTrungBinh", "TiLeHuy", "DoiTraChoDuyet", "SanPhamHetHang", "GiaTriTonKho", "DoanhThuThangTruoc", "TiLeDoiThang" })
            Assert.Contains(f, vm);
        var dash = Doc("Views", "Admin", "Dashboard.cshtml");
        Assert.Contains("Doanh thu hôm nay", dash);
        Assert.Contains("Giá trị đơn trung bình", dash);
        Assert.Contains("Tỷ lệ hủy", dash);
        Assert.Contains("Đổi trả chờ duyệt", dash);
        Assert.Contains("Sản phẩm hết hàng", dash);
        Assert.Contains("Giá trị tồn kho (theo giá bán)", dash);
        Assert.Contains("so với tháng trước", dash);
        var tq = Doc("Views", "Admin", "TongQuan.cshtml");
        Assert.Contains("Doanh thu hôm nay", tq);
        Assert.Contains("Giá trị tồn kho (theo giá bán)", tq);
        Assert.Contains("so với tháng trước", tq);

        // B1: param tuKhoa/sort + form tim kiem o 4 view.
        var ctrl = Doc("Controllers", "AdminController.cs");
        Assert.Contains("string? tuKhoa", ctrl);
        Assert.Contains("string? sort", ctrl);
        foreach (var f in new[] { "Products", "Orders", "Vouchers", "Customers" })
            Assert.Contains("admin-search", Doc("Views", "Admin", f + ".cshtml"));
        Assert.Contains("admin-result-count", Doc("Views", "Admin", "Products.cshtml"));

        // B2: du 6 chip + chip-active o Orders.
        var orders = Doc("Views", "Admin", "Orders.cshtml");
        Assert.Contains("DaXacNhan", orders);
        Assert.Contains("DangDongGoi", orders);
        Assert.Contains("chip-active", orders);

        // B6: BatTatSlot chi doi IsActive + nut o 2 view + nhap kho nhanh.
        Assert.Contains("BatTatSlot", ctrl);
        Assert.Contains("slot.IsActive = !slot.IsActive", ctrl);
        Assert.Contains("BatTatSlot", Doc("Views", "Admin", "FlashSaleSlots.cshtml"));
        Assert.Contains("BatTatSlot", tq);
        Assert.Contains("Nhập kho", dash);
        Assert.Contains("tuTrang\" value=\"Dashboard\"", dash);

        // A6: thumbnail 48px + object-fit: cover o CSS + 6 view.
        var css = Doc("wwwroot", "css", "admin.css");
        Assert.Contains(".product-thumb", css);
        Assert.Contains("object-fit: cover", css);
        Assert.Contains("width: 48px", css);
        foreach (var f in new[] { "Products", "Stock", "Combos", "FlashSaleSlots", "Dashboard" })
            Assert.Contains("product-thumb", Doc("Views", "Admin", f + ".cshtml"));
        Assert.Contains("product-thumb", tq);

        // A4: truc Y + gia tri tren cot + cot max accent o ca 2 chart.
        Assert.Contains(".chart-yaxis", css);
        Assert.Contains(".chart-value", css);
        Assert.Contains(".chart-col--max", css);
        Assert.Contains("chart-yaxis", dash);
        Assert.Contains("chart-value", dash);
        Assert.Contains("chart-col--max", dash);
        Assert.Contains("chart-yaxis", tq);
        Assert.Contains("chart-value", tq);
        Assert.Contains("tabindex=\"0\"", dash);

        // B7: dem + empty state o admin.js + o loc 3 tab moi o TongQuan.
        var js = Doc("wwwroot", "js", "admin.js");
        Assert.Contains("tongquan-count", js);
        Assert.Contains("tongquan-empty", js);
        Assert.Contains("aria-live", js);
        Assert.Contains("Xóa bộ lọc", js);
        Assert.Contains("data-tongquan-filter=\"tq-flashsale\"", tq);
        Assert.Contains("data-tongquan-filter=\"tq-notifications\"", tq);
        Assert.Contains("data-tongquan-filter=\"tq-returns\"", tq);

        // Chart JS tru ca nhan gia tri khi tinh chieu cao cot.
        Assert.Contains("chart-value", Doc("wwwroot", "js", "dashboard-chart.js"));
    }

    // ================= Helpers (theo pattern AdminWave1Tests) =================

    private sealed class FakeTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object?> LoadTempData(HttpContext context) => new Dictionary<string, object?>();
        public void SaveTempData(HttpContext context, IDictionary<string, object?> values) { }
    }

    private static string ProjectRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "TechZone", "TechZone.csproj")))
                return Path.Combine(dir.FullName, "TechZone");
            if (File.Exists(Path.Combine(dir.FullName, "TechZone.csproj")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Khong tim thay TechZone.csproj tu " + AppContext.BaseDirectory);
    }

    private static ServiceProvider BuildServices(string dbPath)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddDbContext<AppDbContext>(o => o.UseSqlite($"Data Source={dbPath}"));
        services.AddIdentity<ApplicationUser, IdentityRole>(options =>
        {
            options.Password.RequireDigit = true;
            options.Password.RequireLowercase = true;
            options.Password.RequireUppercase = false;
            options.Password.RequireNonAlphanumeric = false;
            options.Password.RequiredLength = 6;
            options.User.RequireUniqueEmail = true;
        }).AddEntityFrameworkStores<AppDbContext>().AddDefaultTokenProviders();
        services.AddScoped<LoyaltyService>();
        services.AddScoped<NotificationService>();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["DemoAccounts:Enabled"] = "true",
                ["DemoAccounts:AdminEmail"] = "admin@techzone.vn",
                ["DemoAccounts:AdminPassword"] = "AdminTest123",
                ["DemoAccounts:CustomerEmail"] = "khach@techzone.vn",
                ["DemoAccounts:CustomerPassword"] = "KhachTest123",
            }).Build());
        services.AddScoped<VoucherService>();
        services.AddScoped<PriceCalculationService>();
        services.AddScoped<VatService>();
        services.AddScoped<CheckoutService>();
        services.AddScoped<OrderService>();
        services.AddScoped<RefundService>();
        services.AddScoped<ReturnService>();
        return services.BuildServiceProvider();
    }

    private static string TempDbPath() =>
        Path.Combine(Path.GetTempPath(), "tztest-" + Guid.NewGuid().ToString("N") + ".db");

    private static void DeleteDb(string dbPath)
    {
        SqliteConnection.ClearAllPools();
        foreach (var f in new[] { dbPath, dbPath + "-wal", dbPath + "-shm", dbPath + ".bak" })
            try { if (File.Exists(f)) File.Delete(f); } catch { /* best effort */ }
    }
}
