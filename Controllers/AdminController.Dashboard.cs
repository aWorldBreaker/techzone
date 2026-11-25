using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TechZone.Models;

namespace TechZone.Controllers;

public partial class AdminController
{
    public async Task<IActionResult> Dashboard() =>
        View(await TaiDuLieuDashboardAsync());

    private async Task<DashboardViewModel> TaiDuLieuDashboardAsync()
    {
        var homNay = DateTime.Today;
        var thangNay = homNay.AddDays(1 - homNay.Day);
        var thangTruoc = thangNay.AddMonths(-1);
        var batDauBieuDo = homNay.AddDays(-29);
        var ngayMai = homNay.AddDays(1);
        var batDauBaoCao = batDauBieuDo < thangTruoc ? batDauBieuDo : thangTruoc;

        // SQLite EF Core 8 không hỗ trợ SUM(decimal). Chỉ lấy hai cột của
        // khoảng thời gian cần báo cáo để tính chính xác bằng decimal.
        var donBaoCao = await _db.Orders
            .Where(o => o.NgayDat >= batDauBaoCao && o.NgayDat < ngayMai && o.TrangThai != OrderStatus.DaHuy)
            .Select(o => new { o.NgayDat, o.TongTien })
            .ToListAsync();
        var tongTheoNgay = donBaoCao.GroupBy(o => o.NgayDat.Date)
            .ToDictionary(g => g.Key, g => g.Sum(o => o.TongTien));
        var doanhThu30Ngay = Enumerable.Range(0, 30).Select(i => batDauBieuDo.AddDays(i))
            .Select(d => (Ngay: d.ToString("dd/MM"), DoanhThu: tongTheoNgay.GetValueOrDefault(d)))
            .ToList();
        var donThang = donBaoCao.Where(o => o.NgayDat >= thangNay).ToList();
        var doanhThuThang = donThang.Sum(o => o.TongTien);
        var doanhThuThangTruoc = donBaoCao.Where(o => o.NgayDat >= thangTruoc && o.NgayDat < thangNay).Sum(o => o.TongTien);

        // 7 ngay gan nhat la 7 muc cuoi cua bang 30 ngay.
        var doanhThu7Ngay = doanhThu30Ngay.TakeLast(7).ToList();

        var donTheoTrangThai = await _db.Orders
            .GroupBy(o => o.TrangThai)
            .Select(g => new { TrangThai = g.Key, SoLuong = g.Count() })
            .ToDictionaryAsync(g => g.TrangThai, g => g.SoLuong);

        var khachMoiThang = await KhachHangQuery().CountAsync(u => u.NgayTao >= thangNay && u.NgayTao < ngayMai);

        // Dot 1 Admin-enhance (A3): chi so moi chi doc, tinh tu 2 tap da load
        // o tren (DoanhThu30Ngay + DonTheoTrangThai) — khong them query.
        // Tuan truoc = 7 muc ngay truoc 7 ngay gan nhat (ngay -14..-8).
        var doanhThuTuan = doanhThu7Ngay.Sum(d => d.DoanhThu);
        var doanhThuTuanTruoc = doanhThu30Ngay.TakeLast(14).Take(7).Sum(d => d.DoanhThu);
        int DemTrangThai(string tt) => donTheoTrangThai.TryGetValue(tt, out var n) ? n : 0;

        // Dot 2 (B5): chi so moi chi doc — het hang + doi tra cho duyet la
        // Count moi chi doc; gia tri ton kho tinh theo GIA BAN tu projection
        // chi doc (GiaKhuyenMai ?? Gia) * TonKho, khong phai gia von.
        var tongDon = donTheoTrangThai.Values.Sum();
        var doiTraChoDuyet = await _db.ReturnRequests.CountAsync(r => r.TrangThai == "Chờ duyệt");
        var sanPhamHetHang = await _db.Products.CountAsync(p => p.TonKho == 0);
        var giaTriTonKho = (await _db.Products
            .Select(p => new { p.Gia, p.GiaKhuyenMai, p.TonKho })
            .ToListAsync()).Sum(p => (p.GiaKhuyenMai ?? p.Gia) * p.TonKho);

        return new DashboardViewModel
        {
            DoanhThuThang = doanhThuThang,
            DonMoi = DemTrangThai(OrderStatus.ChoXacNhan),
            TongDon = tongDon,
            TongSanPham = await _db.Products.CountAsync(),
            TopBanChay = await _db.Products.Include(p => p.Images).OrderByDescending(p => p.LuotBan).Take(5).ToListAsync(),
            SapHetHang = await _db.Products.Include(p => p.Images).Where(p => p.TonKho <= 10).OrderBy(p => p.TonKho).Take(5).ToListAsync(),
            DonGanDay = await _db.Orders
                .Include(o => o.ChiTiet)
                .OrderByDescending(o => o.NgayDat).Take(8).ToListAsync(),
            DoanhThu7Ngay = doanhThu7Ngay,
            DoanhThu30Ngay = doanhThu30Ngay,
            DoanhThuTuan = doanhThuTuan,
            DonTheoTrangThai = donTheoTrangThai,
            KhachMoiThang = khachMoiThang,
            DoanhThuTuanTruoc = doanhThuTuanTruoc,
            TiLeDoiTuan = doanhThuTuanTruoc == 0 ? null : (doanhThuTuan - doanhThuTuanTruoc) / doanhThuTuanTruoc,
            DonChoXuLy = DemTrangThai(OrderStatus.ChoXacNhan) + DemTrangThai(OrderStatus.DaXacNhan) + DemTrangThai(OrderStatus.DangDongGoi),
            DonDaHuy = DemTrangThai(OrderStatus.DaHuy),
            // Dot 2 (B5): doanh thu hom nay = muc cuoi cua bang 30 ngay (da
            // co san, khong them query); gia tri don TB tu donThang da load.
            DoanhThuHomNay = doanhThu30Ngay.Count > 0 ? doanhThu30Ngay[^1].DoanhThu : 0,
            GiaTriDonTrungBinh = donThang.Count > 0 ? doanhThuThang / donThang.Count : 0,
            TiLeHuy = tongDon > 0 ? (decimal)DemTrangThai(OrderStatus.DaHuy) / tongDon : 0,
            DoiTraChoDuyet = doiTraChoDuyet,
            SanPhamHetHang = sanPhamHetHang,
            GiaTriTonKho = giaTriTonKho,
            DoanhThuThangTruoc = doanhThuThangTruoc,
            TiLeDoiThang = doanhThuThangTruoc == 0 ? null : (doanhThuThang - doanhThuThangTruoc) / doanhThuThangTruoc
        };
    }
}
