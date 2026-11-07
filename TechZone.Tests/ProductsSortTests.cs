using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TechZone.Controllers;
using TechZone.Data;
using TechZone.Models;
using TechZone.Services;
using Xunit.Abstractions;

namespace TechZone.Tests;

/// <summary>
/// Test hoi quy Sprint 6 Module 3 — P1: /Products?SapXep=gia-tang|gia-giam
/// tra HTTP 500 tren SQLite ("SQLite does not support expressions of type
/// 'decimal' in ORDER BY", ProductsController.cs:91).
/// Goi TRUC TIEP ProductsController.Index tren DB SQLite tam (khong qua
/// HTTP) — Index dung chung query sort + phan trang voi request that.
/// Gia hieu dung = GiaKhuyenMai ?? Gia (Product.GiaBan).
/// </summary>
[Collection("DbTests")]
public class ProductsSortTests
{
    private readonly ITestOutputHelper _out;

    public ProductsSortTests(ITestOutputHelper output) => _out = output;

    // gia-tang: thu tu tang dan theo gia hieu dung, gom ca SP co
    // GiaKhuyenMai null (lay Gia) va SP co gia khuyen mai (lay GiaKhuyenMai).
    [Fact]
    public async Task SapXep_GiaTang_ThuTuTangDanTheoGiaHieuDung()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            await SeedAsync(services,
                ("A-gia-500k-khong-KM", 500_000, null),
                ("B-gia-1000k-KM-300k", 1_000_000, 300_000),
                ("C-gia-200k-khong-KM", 200_000, null),
                ("D-gia-900k-KM-800k", 900_000, 800_000));

            var ketQua = await GoiIndexAsync(services, "gia-tang", trang: 1);

            // Gia hieu dung: C 200k, B 300k, A 500k, D 800k.
            Assert.Equal(new[] { "C-gia-200k-khong-KM", "B-gia-1000k-KM-300k",
                "A-gia-500k-khong-KM", "D-gia-900k-KM-800k" },
                ketQua.Select(p => p.Ten).ToArray());
            Assert.Equal(new decimal[] { 200_000, 300_000, 500_000, 800_000 },
                ketQua.Select(p => p.GiaBan).ToArray());
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // gia-giam: thu tu giam dan theo gia hieu dung.
    [Fact]
    public async Task SapXep_GiaGiam_ThuTuGiamDanTheoGiaHieuDung()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            await SeedAsync(services,
                ("A-gia-500k-khong-KM", 500_000, null),
                ("B-gia-1000k-KM-300k", 1_000_000, 300_000),
                ("C-gia-200k-khong-KM", 200_000, null),
                ("D-gia-900k-KM-800k", 900_000, 800_000));

            var ketQua = await GoiIndexAsync(services, "gia-giam", trang: 1);

            Assert.Equal(new[] { "D-gia-900k-KM-800k", "A-gia-500k-khong-KM",
                "B-gia-1000k-KM-300k", "C-gia-200k-khong-KM" },
                ketQua.Select(p => p.Ten).ToArray());
            Assert.Equal(new decimal[] { 800_000, 500_000, 300_000, 200_000 },
                ketQua.Select(p => p.GiaBan).ToArray());
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // Sort phai xay ra TRUOC Skip/Take: 13 SP (SoMoiTrang = 12), trang 1
    // gia-tang phai la 12 gia thap nhat theo thu tu, trang 2 chi con gia
    // cao nhat. Neu sort sau khi materialize + phan trang thi trang 2 sai.
    [Fact]
    public async Task SapXep_GiaTang_PhanTrang_SortTruocSkipTake()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var seed = Enumerable.Range(1, 13)
                .Select(i => ($"SP-{i:00}-{(1300 - (i - 1) * 100)}k", (1300 - (i - 1) * 100) * 1000m, (decimal?)null))
                .ToArray();
            // Seed nguoc gia (1300k -> 100k) de thu tu insert khac thu tu sort.
            await SeedAsync(services, seed);

            var trang1 = await GoiIndexAsync(services, "gia-tang", trang: 1);
            var trang2 = await GoiIndexAsync(services, "gia-tang", trang: 2);

            Assert.Equal(12, trang1.Count);
            Assert.Equal(Enumerable.Range(1, 12).Select(i => i * 100_000m).ToArray(),
                trang1.Select(p => p.GiaBan).ToArray());
            Assert.Single(trang2);
            Assert.Equal(1_300_000m, trang2[0].GiaBan);
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ============ MOI — Sprint 6 Module 3: gia hieu dung + tie-breaker
    // + loc gia tren gia hieu dung + clamp trang ============

    // PARITY: gia hieu dung batch (controller) KHOP TinhGiaAsync tung SP
    // tren 13 SP seed that (SeedData): flash dang chay, flash == GiaBan,
    // flash het han, flash het quota (+ slot khong duoc fallback), slot
    // theo gio, slot == GiaBan, slot khong kich hoat, slot ngoai gio.
    [Fact]
    public async Task GiaHieuDung_Batch_KhopTinhGiaAsync_TrenSeed13SP()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var now = DateTime.Now;
            await Seed13SeedDataAsync(services);
            await SeedFlashSlotParityAsync(services, now);
            var ids = Enumerable.Range(1, 13).ToArray();

            // Trang 1 + trang 2 gop lai moi du 13 SP.
            var moHinh1 = await GoiIndexModelAsync(services,
                new ProductFilterViewModel { SapXep = "gia-tang", Trang = 1 });
            var moHinh2 = await GoiIndexModelAsync(services,
                new ProductFilterViewModel { SapXep = "gia-tang", Trang = 2 });
            Assert.Equal(13, moHinh1.TongSo);
            // GiaHieuDung tren model da chua TOAN BO SP sau loc (khong chi
            // trang hien tai) nen lay thang tu trang 1, khong gop 2 trang.
            var giaBatch = moHinh1.GiaHieuDung;
            Assert.Equal(13, giaBatch.Count);

            foreach (var id in ids)
            {
                var kyVong = await TinhGiaServiceAsync(services, id, now);
                Assert.True(giaBatch.TryGetValue(id, out var thucTe),
                    $"Thieu gia hieu dung cho SP #{id}");
                Assert.Equal(kyVong, thucTe);
                _out.WriteLine($"SP #{id}: TinhGiaAsync={kyVong:N0} batch={thucTe:N0}");
            }

            // Gia hien thi tren listing (GiaBan cua SP trong trang) cung
            // la gia hieu dung — khop ca 3 cho (sort/loc/hien thi).
            foreach (var p in moHinh1.KetQua.Concat(moHinh2.KetQua))
                Assert.Equal(giaBatch[p.Id], p.GiaBan);
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // Sort gia-tang tren seed 13 SP (chi flash, khong slot): WH 850k dau,
    // Buds 1.090k DONG GIA voi G502 1.090k (tie-breaker Id: Buds #11 truoc
    // G502 #12), Reno11 = iPad = 9.990k (Id #6 truoc #8).
    [Fact]
    public async Task Sort_GiaTang_GiaHieuDung_ThuTuDayDu13SP()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var now = DateTime.Now;
            await Seed13SeedDataAsync(services);
            await SeedFlashAsync(services,
                new FlashSale { ProductId = 1, GiaSale = 12_990_000, BatDau = now.AddHours(-1), KetThuc = now.AddHours(1), GioiHanSL = 20, DaBan = 7 },
                new FlashSale { ProductId = 11, GiaSale = 1_090_000, BatDau = now.AddHours(-1), KetThuc = now.AddHours(1), GioiHanSL = 30, DaBan = 12 },
                new FlashSale { ProductId = 4, GiaSale = 850_000, BatDau = now.AddHours(-1), KetThuc = now.AddHours(1), GioiHanSL = 25, DaBan = 9 });

            var trang1 = await GoiIndexModelAsync(services,
                new ProductFilterViewModel { SapXep = "gia-tang", Trang = 1 });
            var trang2 = await GoiIndexModelAsync(services,
                new ProductFilterViewModel { SapXep = "gia-tang", Trang = 2 });

            // Id theo thu tu gia hieu dung tang dan.
            Assert.Equal(new[] { 4, 11, 12, 9, 13, 10, 5, 6, 8, 1, 3, 7 },
                trang1.KetQua.Select(p => p.Id).ToArray());
            Assert.Equal(new[] { 850_000m, 1_090_000m, 1_090_000m, 2_390_000m, 2_490_000m,
                4_290_000m, 7_490_000m, 9_990_000m, 9_990_000m, 12_990_000m, 18_490_000m, 22_990_000m },
                trang1.KetQua.Select(p => p.GiaBan).ToArray());
            Assert.Equal(new[] { 2 }, trang2.KetQua.Select(p => p.Id).ToArray());
            Assert.Equal(28_990_000m, trang2.KetQua[0].GiaBan);

            // gia-giam: tie-breaker ThenBy(Id) van TANG DAN o ca 2 chieu
            // nen cap dong gia KHONG dao nguoc: 9.990k van [6, 8],
            // 1.090k van [11, 12] (chi thu tu gia dao chieu).
            var giam1 = await GoiIndexModelAsync(services,
                new ProductFilterViewModel { SapXep = "gia-giam", Trang = 1 });
            var giam2 = await GoiIndexModelAsync(services,
                new ProductFilterViewModel { SapXep = "gia-giam", Trang = 2 });
            Assert.Equal(new[] { 2, 7, 3, 1, 6, 8, 5, 10, 13, 9, 11, 12 },
                giam1.KetQua.Select(p => p.Id).ToArray());
            Assert.Equal(new[] { 4 }, giam2.KetQua.Select(p => p.Id).ToArray());
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // Tie-breaker: 3 SP dong gia 5.000.000d, seed theo thu tu Id 30, 10,
    // 20 (co tinh dao thu tu insert so voi Id) — sort phai tra Id tang dan
    // (10, 20, 30), khong phai thu tu insert. Code cu (khong ThenBy) tra
    // theo thu tu quet DB (30, 10, 20) nen test nay fail khi revert.
    [Fact]
    public async Task Sort_DongGia_TieBreaker_IdTangDan()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var (cat, _, brand, _) = await SeedCatBrandAsync(services);
            await SeedProductsAsync(services, new[]
            {
                new Product { Id = 30, Ten = "Dong-30", Gia = 5_000_000, CategoryId = cat, BrandId = brand, TonKho = 10 },
                new Product { Id = 10, Ten = "Dong-10", Gia = 5_000_000, CategoryId = cat, BrandId = brand, TonKho = 10 },
                new Product { Id = 20, Ten = "Dong-20", Gia = 5_000_000, CategoryId = cat, BrandId = brand, TonKho = 10 },
                new Product { Id = 40, Ten = "Re-40", Gia = 1_000_000, CategoryId = cat, BrandId = brand, TonKho = 10 },
                new Product { Id = 50, Ten = "Dat-50", Gia = 9_000_000, CategoryId = cat, BrandId = brand, TonKho = 10 },
            });

            var tang = await GoiIndexModelAsync(services,
                new ProductFilterViewModel { SapXep = "gia-tang", Trang = 1 });
            Assert.Equal(new[] { 40, 10, 20, 30, 50 },
                tang.KetQua.Select(p => p.Id).ToArray());
            // Goi lai phai cho cung thu tu (on dinh qua cac request).
            var tang2 = await GoiIndexModelAsync(services,
                new ProductFilterViewModel { SapXep = "gia-tang", Trang = 1 });
            Assert.Equal(tang.KetQua.Select(p => p.Id).ToArray(),
                tang2.KetQua.Select(p => p.Id).ToArray());

            var giam = await GoiIndexModelAsync(services,
                new ProductFilterViewModel { SapXep = "gia-giam", Trang = 1 });
            Assert.Equal(new[] { 50, 10, 20, 30, 40 },
                giam.KetQua.Select(p => p.Id).ToArray());
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // Tie-breaker o ca 3 nhanh sort con lai: ban-chay (cung LuotBan),
    // moi-nhat (cung NgayTao), mac-dinh (cung LaNoiBat + LuotBan).
    [Fact]
    public async Task Sort_BanChay_MoiNhat_MacDinh_TieBreaker_Id()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var (cat, _, brand, _) = await SeedCatBrandAsync(services);
            var ngay = new DateTime(2026, 11, 1, 10, 0, 0);
            await SeedProductsAsync(services, new[]
            {
                new Product { Id = 30, Ten = "SP-30", Gia = 1_000_000, CategoryId = cat, BrandId = brand, TonKho = 10, LuotBan = 50, LaNoiBat = true, NgayTao = ngay },
                new Product { Id = 10, Ten = "SP-10", Gia = 2_000_000, CategoryId = cat, BrandId = brand, TonKho = 10, LuotBan = 50, LaNoiBat = true, NgayTao = ngay },
                new Product { Id = 20, Ten = "SP-20", Gia = 3_000_000, CategoryId = cat, BrandId = brand, TonKho = 10, LuotBan = 50, LaNoiBat = true, NgayTao = ngay },
            });

            var banChay = await GoiIndexModelAsync(services,
                new ProductFilterViewModel { SapXep = "ban-chay", Trang = 1 });
            Assert.Equal(new[] { 10, 20, 30 }, banChay.KetQua.Select(p => p.Id).ToArray());
            var moiNhat = await GoiIndexModelAsync(services,
                new ProductFilterViewModel { SapXep = "moi-nhat", Trang = 1 });
            Assert.Equal(new[] { 10, 20, 30 }, moiNhat.KetQua.Select(p => p.Id).ToArray());
            var macDinh = await GoiIndexModelAsync(services,
                new ProductFilterViewModel { SapXep = "mac-dinh", Trang = 1 });
            Assert.Equal(new[] { 10, 20, 30 }, macDinh.KetQua.Select(p => p.Id).ToArray());
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // 24 SP: 2 trang day (12 + 12), khong trung/khong mat SP nao.
    [Fact]
    public async Task PhanTrang_24SP_HaiTrangDayDu_KhongTrungKhongMat()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            await SeedNhieuSPAsync(services, 24);

            var t1 = await GoiIndexModelAsync(services,
                new ProductFilterViewModel { SapXep = "gia-tang", Trang = 1 });
            var t2 = await GoiIndexModelAsync(services,
                new ProductFilterViewModel { SapXep = "gia-tang", Trang = 2 });
            Assert.Equal(24, t1.TongSo);
            Assert.Equal(2, t1.TongTrang);
            Assert.Equal(12, t1.KetQua.Count);
            Assert.Equal(12, t2.KetQua.Count);
            var tatCa = t1.KetQua.Concat(t2.KetQua).ToList();
            Assert.Equal(24, tatCa.Select(p => p.Id).Distinct().Count());
            Assert.Equal(Enumerable.Range(1, 24).Select(i => i * 100_000m).ToArray(),
                tatCa.Select(p => p.GiaBan).ToArray());
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // 25+ SP: 3 trang (12 + 12 + 1), khong trung/khong mat; SP dong gia
    // o bien trang 1/2 van on dinh nho tie-breaker.
    [Fact]
    public async Task PhanTrang_25SP_BaTrang_KhongTrungKhongMat()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            await SeedNhieuSPAsync(services, 25);

            var t1 = await GoiIndexModelAsync(services,
                new ProductFilterViewModel { SapXep = "gia-tang", Trang = 1 });
            var t2 = await GoiIndexModelAsync(services,
                new ProductFilterViewModel { SapXep = "gia-tang", Trang = 2 });
            var t3 = await GoiIndexModelAsync(services,
                new ProductFilterViewModel { SapXep = "gia-tang", Trang = 3 });
            Assert.Equal(25, t1.TongSo);
            Assert.Equal(3, t1.TongTrang);
            Assert.Equal(12, t1.KetQua.Count);
            Assert.Equal(12, t2.KetQua.Count);
            Assert.Single(t3.KetQua);
            var tatCa = t1.KetQua.Concat(t2.KetQua).Concat(t3.KetQua).ToList();
            Assert.Equal(25, tatCa.Select(p => p.Id).Distinct().Count());
            Assert.Equal(2_500_000m, t3.KetQua[0].GiaBan);
            // Goi lai trang 2 phai giong het (khong dao SP dong gia).
            var t2b = await GoiIndexModelAsync(services,
                new ProductFilterViewModel { SapXep = "gia-tang", Trang = 2 });
            Assert.Equal(t2.KetQua.Select(p => p.Id).ToArray(),
                t2b.KetQua.Select(p => p.Id).ToArray());
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // Trang vuot gioi han -> clamp ve trang cuoi + co TrangVuotGioiHan.
    [Fact]
    public async Task PhanTrang_TrangVuotGioiHan_ClampVeTrangCuoi()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            await SeedNhieuSPAsync(services, 13);

            var vuot = await GoiIndexModelAsync(services,
                new ProductFilterViewModel { SapXep = "gia-tang", Trang = 99 });
            Assert.Equal(2, vuot.TongTrang);
            Assert.Equal(2, vuot.Trang);
            Assert.True(vuot.TrangVuotGioiHan);
            Assert.Single(vuot.KetQua);
            Assert.Equal(1_300_000m, vuot.KetQua[0].GiaBan);

            var vuot3 = await GoiIndexModelAsync(services,
                new ProductFilterViewModel { SapXep = "gia-tang", Trang = 3 });
            Assert.Equal(2, vuot3.Trang);
            Assert.True(vuot3.TrangVuotGioiHan);
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // Trang=0 / am -> 1, khong danh dau vuot gioi han.
    [Fact]
    public async Task PhanTrang_Trang0VaAm_Ve1()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            await SeedNhieuSPAsync(services, 13);

            var khong = await GoiIndexModelAsync(services,
                new ProductFilterViewModel { SapXep = "gia-tang", Trang = 0 });
            Assert.Equal(1, khong.Trang);
            Assert.False(khong.TrangVuotGioiHan);
            Assert.Equal(12, khong.KetQua.Count);

            var am = await GoiIndexModelAsync(services,
                new ProductFilterViewModel { SapXep = "gia-tang", Trang = -5 });
            Assert.Equal(1, am.Trang);
            Assert.False(am.TrangVuotGioiHan);
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // Khong co ket qua: TongTrang = 0, Trang giu 1, khong danh dau vuot
    // (khong phai loi trang — la loc khong khop).
    [Fact]
    public async Task PhanTrang_KhongCoKetQua_Trang1_KhongVuot()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            await SeedNhieuSPAsync(services, 13);

            var rong = await GoiIndexModelAsync(services,
                new ProductFilterViewModel { SapXep = "gia-tang", Trang = 99, TuKhoa = "khong-co-sp-nay" });
            Assert.Equal(0, rong.TongSo);
            Assert.Equal(0, rong.TongTrang);
            Assert.Equal(1, rong.Trang);
            Assert.False(rong.TrangVuotGioiHan);
            Assert.Empty(rong.KetQua);
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // Loc GiaDen tren GIA HIEU DUNG: Buds 5 (GiaBan 1.190.000d, thuc tra
    // 1.090.000d) phai LOT QUA GiaDen = 1.100.000d — code cu loc tren
    // GiaBan se loai sai no. (Ghi chu: vi du trong task noi "tu
    // 1.100.000d" — voi ngu nghia GiaTu (gia toi thieu) thi Buds phai BI
    // LOAI vi 1.090.000 < 1.100.000; test duoi kiem ca 2 chieu cho dung
    // ngu nghia min/max.)
    [Fact]
    public async Task Loc_GiaDen_TrenGiaHieuDung_BudsKhongBiLoai()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var now = DateTime.Now;
            await Seed13SeedDataAsync(services);
            await SeedFlashAsync(services,
                new FlashSale { ProductId = 11, GiaSale = 1_090_000, BatDau = now.AddHours(-1), KetThuc = now.AddHours(1), GioiHanSL = 30, DaBan = 12 },
                new FlashSale { ProductId = 4, GiaSale = 850_000, BatDau = now.AddHours(-1), KetThuc = now.AddHours(1), GioiHanSL = 25, DaBan = 9 });

            var den = await GoiIndexModelAsync(services,
                new ProductFilterViewModel { SapXep = "gia-tang", Trang = 1, GiaDen = 1_100_000 });
            // Gia hieu dung <= 1.100.000: WH 850k, Buds 1.090k, G502 1.090k.
            Assert.Equal(new[] { 4, 11, 12 }, den.KetQua.Select(p => p.Id).ToArray());
            Assert.Equal(3, den.TongSo);
            Assert.Contains(den.KetQua, p => p.Id == 11);
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // Chieu nguoc: GiaTu = 1.100.000d phai LOAI Buds 5 (thuc tra 1.090.000d
    // < 1.100.000d) du GiaBan cua no la 1.190.000d.
    [Fact]
    public async Task Loc_GiaTu_TrenGiaHieuDung_BudsBiLoai()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var now = DateTime.Now;
            await Seed13SeedDataAsync(services);
            await SeedFlashAsync(services,
                new FlashSale { ProductId = 11, GiaSale = 1_090_000, BatDau = now.AddHours(-1), KetThuc = now.AddHours(1), GioiHanSL = 30, DaBan = 12 });

            var tu = await GoiIndexModelAsync(services,
                new ProductFilterViewModel { SapXep = "gia-tang", Trang = 1, GiaTu = 1_100_000 });
            Assert.DoesNotContain(tu.KetQua, p => p.Id == 11);
            Assert.All(tu.KetQua, p => Assert.True(tu.GiaHieuDung[p.Id] >= 1_100_000));
            // G502 (GiaBan = gia hieu dung = 1.090.000d) cung bi loai.
            Assert.DoesNotContain(tu.KetQua, p => p.Id == 12);
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // Sort + filter ket hop: category, brand, tu khoa, khoang gia.
    [Fact]
    public async Task Sort_KetHopFilter_Category_Brand_TuKhoa_KhoangGia()
    {
        var db = TempDbPath();
        try
        {
            var services = BuildServices(db);
            var now = DateTime.Now;
            var (catTaiNghe, _, brandXiaomi, _) = await Seed13SeedDataAsync(services);
            await SeedFlashAsync(services,
                new FlashSale { ProductId = 11, GiaSale = 1_090_000, BatDau = now.AddHours(-1), KetThuc = now.AddHours(1), GioiHanSL = 30, DaBan = 12 },
                new FlashSale { ProductId = 4, GiaSale = 850_000, BatDau = now.AddHours(-1), KetThuc = now.AddHours(1), GioiHanSL = 25, DaBan = 9 });

            // Category Tai nghe + gia-tang: WH 850k truoc Buds 1.090k.
            var theoCat = await GoiIndexModelAsync(services,
                new ProductFilterViewModel { SapXep = "gia-tang", Trang = 1, CategoryId = catTaiNghe });
            Assert.Equal(new[] { 4, 11 }, theoCat.KetQua.Select(p => p.Id).ToArray());

            // Brand Xiaomi + gia-giam: Xiaomi 12.990k, S3 2.390k, Buds 1.090k.
            var theoBrand = await GoiIndexModelAsync(services,
                new ProductFilterViewModel { SapXep = "gia-giam", Trang = 1, BrandId = brandXiaomi });
            Assert.Equal(new[] { 1, 9, 11 }, theoBrand.KetQua.Select(p => p.Id).ToArray());

            // Tu khoa "Xiaomi" + gia-tang: Buds, S3, Xiaomi 15.
            var theoTuKhoa = await GoiIndexModelAsync(services,
                new ProductFilterViewModel { SapXep = "gia-tang", Trang = 1, TuKhoa = "Xiaomi" });
            Assert.Equal(new[] { 11, 9, 1 }, theoTuKhoa.KetQua.Select(p => p.Id).ToArray());

            // Khoang gia hieu dung 1.000.000–3.000.000 + gia-tang.
            var theoKhoang = await GoiIndexModelAsync(services,
                new ProductFilterViewModel { SapXep = "gia-tang", Trang = 1, GiaTu = 1_000_000, GiaDen = 3_000_000 });
            Assert.Equal(new[] { 11, 12, 9, 13 }, theoKhoang.KetQua.Select(p => p.Id).ToArray());
            Assert.Equal(4, theoKhoang.TongSo);
        }
        finally
        {
            DeleteDb(db);
        }
    }

    // ================= Helpers =================

    private static ServiceProvider BuildServices(string dbPath)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddHttpContextAccessor();
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
        services.AddScoped<VoucherService>();
        services.AddScoped<PriceCalculationService>();
        services.AddScoped<VatService>();
        services.AddScoped<CartService>();
        services.AddScoped<CheckoutService>();
        services.AddScoped<OrderService>();
        services.AddScoped<RefundService>();
        services.AddScoped<ReturnService>();
        return services.BuildServiceProvider();
    }

    private static async Task SeedAsync(ServiceProvider services,
        params (string Ten, decimal Gia, decimal? GiaKhuyenMai)[] sanPham)
    {
        using (var scope = services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await ctx.Database.MigrateAsync();
            await ctx.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
        }
        using var scope2 = services.CreateScope();
        var ctx2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        var category = await ctx2.Categories.FirstOrDefaultAsync(c => c.Slug == "test-cat");
        if (category is null)
        {
            category = new Category { Ten = "Test Cat", Slug = "test-cat" };
            ctx2.Categories.Add(category);
            await ctx2.SaveChangesAsync();
        }
        var brand = await ctx2.Brands.FirstOrDefaultAsync(b => b.Ten == "Test Brand");
        if (brand is null)
        {
            brand = new Brand { Ten = "Test Brand" };
            ctx2.Brands.Add(brand);
            await ctx2.SaveChangesAsync();
        }
        foreach (var (ten, gia, km) in sanPham)
        {
            ctx2.Products.Add(new Product
            {
                Ten = ten,
                Gia = gia,
                GiaKhuyenMai = km,
                CategoryId = category.Id,
                BrandId = brand.Id,
                TonKho = 10,
                LuotBan = 0,
            });
        }
        await ctx2.SaveChangesAsync();
    }

    private static async Task<List<Product>> GoiIndexAsync(ServiceProvider services,
        string sapXep, int trang)
    {
        var moHinh = await GoiIndexModelAsync(services,
            new ProductFilterViewModel { SapXep = sapXep, Trang = trang });
        return moHinh.KetQua;
    }

    private static async Task<ProductFilterViewModel> GoiIndexModelAsync(
        ServiceProvider services, ProductFilterViewModel filter)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var ctrl = new ProductsController(
            sp.GetRequiredService<AppDbContext>(),
            new TestWebHostEnvironment(),
            sp.GetRequiredService<PriceCalculationService>())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
        var ketQua = await ctrl.Index(filter);
        var view = Assert.IsType<ViewResult>(ketQua);
        return Assert.IsType<ProductFilterViewModel>(view.Model);
    }

    // Seed 2 danh muc + 2 thuong hieu test (tra Id). CatA = "Tai nghe",
    // BrandA = "Xiaomi" de test sort+filter dung nghia seed that.
    private static async Task<(int CatA, int CatB, int BrandA, int BrandB)> SeedCatBrandAsync(
        ServiceProvider services)
    {
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await ctx.Database.MigrateAsync();
        await ctx.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
        async Task<int> Cat(string ten, string slug)
        {
            var c = await ctx.Categories.FirstOrDefaultAsync(x => x.Slug == slug);
            if (c is null)
            {
                c = new Category { Ten = ten, Slug = slug };
                ctx.Categories.Add(c);
                await ctx.SaveChangesAsync();
            }
            return c.Id;
        }
        async Task<int> Brand(string ten)
        {
            var b = await ctx.Brands.FirstOrDefaultAsync(x => x.Ten == ten);
            if (b is null)
            {
                b = new Brand { Ten = ten };
                ctx.Brands.Add(b);
                await ctx.SaveChangesAsync();
            }
            return b.Id;
        }
        return (await Cat("Tai nghe", "tai-nghe"), await Cat("Khác", "khac"),
            await Brand("Xiaomi"), await Brand("Khác"));
    }

    private static async Task SeedProductsAsync(ServiceProvider services,
        IEnumerable<Product> products)
    {
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        ctx.Products.AddRange(products);
        await ctx.SaveChangesAsync();
    }

    private static async Task SeedFlashAsync(ServiceProvider services,
        params FlashSale[] sales)
    {
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        ctx.FlashSales.AddRange(sales);
        await ctx.SaveChangesAsync();
    }

    // 13 SP Y HET SeedData (ten/gia/GiaKhuyenMai), Id co dinh 1..13 theo
    // thu tu seed (Xiaomi=1, iPhone=2, TUF=3, WH=4, A55=5, Reno11=6,
    // LOQ=7, iPad=8, S3=9, GT4=10, Buds=11, G502=12, SSD=13). Tra ve
    // (CatTaiNghe, CatKhac, BrandXiaomi, BrandKhac) de test filter.
    private static async Task<(int CatA, int CatB, int BrandA, int BrandB)> Seed13SeedDataAsync(
        ServiceProvider services)
    {
        var (catA, catB, brandA, brandB) = await SeedCatBrandAsync(services);
        Product P(int id, string ten, decimal gia, decimal? km, int cat, int brand) =>
            new() { Id = id, Ten = ten, Gia = gia, GiaKhuyenMai = km, CategoryId = cat, BrandId = brand, TonKho = 10 };
        await SeedProductsAsync(services, new[]
        {
            P(1, "Xiaomi 15 5G", 15_990_000, 12_990_000, catB, brandA),
            P(2, "iPhone 15 Pro", 31_990_000, 28_990_000, catB, brandB),
            P(3, "Laptop ASUS TUF Gaming F15", 24_650_000, 18_490_000, catB, brandB),
            P(4, "Tai nghe Sony WH-CH520", 1_150_000, 890_000, catA, brandB),
            P(5, "Samsung Galaxy A55 5G", 9_990_000, 7_490_000, catB, brandB),
            P(6, "OPPO Reno11 5G", 10_990_000, 9_990_000, catB, brandB),
            P(7, "Laptop Lenovo LOQ 15", 25_990_000, 22_990_000, catB, brandB),
            P(8, "iPad Gen 10 10.9\" 64GB WiFi", 11_490_000, 9_990_000, catB, brandB),
            P(9, "Đồng hồ Xiaomi Watch S3", 3_290_000, 2_390_000, catB, brandA),
            P(10, "Đồng hồ Huawei Watch GT 4", 5_500_000, 4_290_000, catB, brandB),
            P(11, "Tai nghe True Wireless Xiaomi Buds 5", 1_650_000, 1_190_000, catA, brandA),
            P(12, "Chuột Gaming Logitech G502 HERO", 1_390_000, 1_090_000, catB, brandB),
            P(13, "SSD Samsung 980 PRO 1TB NVMe", 3_190_000, 2_490_000, catB, brandB),
        });
        return (catA, catB, brandA, brandB);
    }

    // Du lieu sale cho test parity: bao het truong hop cua TinhGiaAsync.
    // Flash: Xiaomi == GiaBan; Buds + WH re + con quota; GT4 het han;
    // SSD re nhung DaBan == GioiHanSL (het quota). Slot rong [0,24):
    // A55 slot re (chi slot); SSD slot re (phai KHONG ap — flash het
    // quota chan slot); S3 slot == GiaBan (khong doi). Slot tat (LOQ) +
    // slot ngoai gio (Reno11) phai bi bo qua.
    private static async Task SeedFlashSlotParityAsync(ServiceProvider services, DateTime now)
    {
        await SeedFlashAsync(services,
            new FlashSale { ProductId = 1, GiaSale = 12_990_000, BatDau = now.AddHours(-1), KetThuc = now.AddHours(1), GioiHanSL = 20, DaBan = 7 },
            new FlashSale { ProductId = 11, GiaSale = 1_090_000, BatDau = now.AddHours(-1), KetThuc = now.AddHours(1), GioiHanSL = 30, DaBan = 12 },
            new FlashSale { ProductId = 4, GiaSale = 850_000, BatDau = now.AddHours(-1), KetThuc = now.AddHours(1), GioiHanSL = 25, DaBan = 9 },
            new FlashSale { ProductId = 10, GiaSale = 3_000_000, BatDau = now.AddHours(-3), KetThuc = now.AddHours(-2), GioiHanSL = 10, DaBan = 0 },
            new FlashSale { ProductId = 13, GiaSale = 2_000_000, BatDau = now.AddHours(-1), KetThuc = now.AddHours(1), GioiHanSL = 10, DaBan = 10 });
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var slotRong = new FlashSaleSlot { GioBatDau = 0, GioKetThuc = 24, IsActive = true };
        var slotTat = new FlashSaleSlot { GioBatDau = 0, GioKetThuc = 24, IsActive = false };
        var gioNgoai = (now.Hour + 2) % 24;
        var slotNgoaiGio = new FlashSaleSlot { GioBatDau = gioNgoai, GioKetThuc = gioNgoai + 1, IsActive = true };
        ctx.FlashSaleSlots.AddRange(slotRong, slotTat, slotNgoaiGio);
        await ctx.SaveChangesAsync();
        ctx.FlashSaleSlotItems.AddRange(
            new FlashSaleSlotItem { SlotId = slotRong.Id, ProductId = 5, GiaSale = 6_990_000 },
            new FlashSaleSlotItem { SlotId = slotRong.Id, ProductId = 13, GiaSale = 2_000_000 },
            new FlashSaleSlotItem { SlotId = slotRong.Id, ProductId = 9, GiaSale = 2_390_000 },
            new FlashSaleSlotItem { SlotId = slotTat.Id, ProductId = 7, GiaSale = 20_000_000 },
            new FlashSaleSlotItem { SlotId = slotNgoaiGio.Id, ProductId = 6, GiaSale = 8_000_000 });
        await ctx.SaveChangesAsync();
    }

    // N SP gia 100k..N*100k, seed nguoc gia de thu tu insert khac sort.
    private static async Task SeedNhieuSPAsync(ServiceProvider services, int n)
    {
        var (cat, _, brand, _) = await SeedCatBrandAsync(services);
        var seed = Enumerable.Range(1, n)
            .Select(i => new Product
            {
                Ten = $"SP-{i:00}-{(n - i + 1) * 100}k",
                Gia = (n - i + 1) * 100_000m,
                CategoryId = cat,
                BrandId = brand,
                TonKho = 10,
            });
        await SeedProductsAsync(services, seed);
    }

    // Gia goc tham chieu: TinhGiaAsync tung SP (khong doi service).
    private static async Task<decimal> TinhGiaServiceAsync(ServiceProvider services,
        int productId, DateTime now)
    {
        using var scope = services.CreateScope();
        var gia = scope.ServiceProvider.GetRequiredService<PriceCalculationService>();
        return (await gia.TinhGiaAsync(productId, 0, 1, now)).DonGia;
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
