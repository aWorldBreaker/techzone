using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TechZone.Models;

namespace TechZone.Controllers;

public partial class AdminController
{
    private const int GioiHanTongQuan = 25;

    public async Task<IActionResult> TongQuan(string? muc = null)
    {
        var vm = new TongQuanViewModel { Muc = muc ?? "products" };
        switch (vm.Muc)
        {
            default:
            case "products":
                vm.Muc = "products";
                vm.Products = await _db.Products.AsNoTracking()
                    .Include(p => p.Category).Include(p => p.Brand).Include(p => p.Images)
                    .OrderByDescending(p => p.NgayTao).ThenByDescending(p => p.Id).Take(GioiHanTongQuan).ToListAsync();
                break;
            case "orders":
                vm.DonGanDay = await _db.Orders.AsNoTracking()
                    .OrderByDescending(o => o.NgayDat).ThenByDescending(o => o.Id).Take(10).ToListAsync();
                break;
            case "customers":
                vm.KhachHang = await TaiDuLieuKhachHangAsync(KhachHangTheoNgay().Take(GioiHanTongQuan));
                break;
            case "vouchers":
                vm.Vouchers = await _db.Vouchers.AsNoTracking().OrderByDescending(v => v.NgayHetHan).ThenByDescending(v => v.Id).Take(GioiHanTongQuan).ToListAsync();
                break;
            case "combos":
                vm.Combos = await _db.Combos.AsNoTracking()
                    .Include(c => c.Items).ThenInclude(i => i.Product).ThenInclude(p => p.Images)
                    .OrderByDescending(c => c.Id).Take(GioiHanTongQuan).ToListAsync();
                break;
            case "flashsale":
                vm.FlashSaleSlots = await _db.FlashSaleSlots.AsNoTracking()
                    .Include(s => s.Items).ThenInclude(i => i.Product).ThenInclude(p => p.Images)
                    .OrderBy(s => s.GioBatDau).Take(GioiHanTongQuan).ToListAsync();
                break;
            case "banners":
                vm.Banners = await _db.Banners.AsNoTracking().OrderBy(b => b.ThuTu).Take(GioiHanTongQuan).ToListAsync();
                break;
            case "reviews":
                vm.ReviewsChoDuyet = await _db.Reviews.AsNoTracking()
                    .Include(r => r.Product).Include(r => r.User)
                    .Where(r => !r.IsApproved)
                    .OrderBy(r => r.NgayDanhGia).ThenByDescending(r => r.Id).Take(GioiHanTongQuan).ToListAsync();
                vm.ReviewsDaDuyetGanDay = await _db.Reviews.AsNoTracking()
                    .Include(r => r.Product).Include(r => r.User)
                    .Where(r => r.IsApproved)
                    .OrderByDescending(r => r.NgayDanhGia).ThenByDescending(r => r.Id).Take(10).ToListAsync();
                vm.CauHoiChuaTraLoi = await _db.ProductQuestions.AsNoTracking()
                    .Include(q => q.Product).Include(q => q.User)
                    .Where(q => q.TraLoi == null)
                    .OrderBy(q => q.NgayHoi).Take(GioiHanTongQuan).ToListAsync();
                break;
            case "stock":
                vm.SanPhamKho = await _db.Products.AsNoTracking().Include(p => p.Images).OrderBy(p => p.Ten).ThenBy(p => p.Id).Take(GioiHanTongQuan).ToListAsync();
                vm.NhapKhoGanDay = await _db.StockIns.AsNoTracking()
                    .Include(s => s.Product)
                    .OrderByDescending(s => s.NgayNhap).ThenByDescending(s => s.Id).Take(10).ToListAsync();
                break;
            case "notifications":
                vm.ThongBaoGanDay = await _db.Notifications.AsNoTracking()
                    .Include(n => n.User)
                    .OrderByDescending(n => n.NgayTao).ThenByDescending(n => n.Id).Take(20).ToListAsync();
                vm.NguoiNhanThongBao = await KhachHangQuery()
                    .OrderBy(u => u.HoTen).ThenBy(u => u.Id).Take(GioiHanTongQuan).ToListAsync();
                break;
            case "returns":
                vm.DoiTraChoDuyet = await _db.ReturnRequests.AsNoTracking()
                    .Include(r => r.Product).Include(r => r.OrderDetail).ThenInclude(d => d!.Variant).Include(r => r.Order).Include(r => r.User)
                    .Where(r => r.TrangThai == "Chờ duyệt")
                    .OrderByDescending(r => r.NgayTao).ThenByDescending(r => r.Id).Take(GioiHanTongQuan).ToListAsync();
                vm.BaoHanhChoDuyet = await _db.WarrantyClaims.AsNoTracking()
                    .Include(w => w.Product).Include(w => w.Order).Include(w => w.User)
                    .Where(w => w.TrangThai == "Chờ duyệt")
                    .OrderByDescending(w => w.NgayTao).ThenByDescending(w => w.Id).Take(GioiHanTongQuan).ToListAsync();
                break;
        }
        return View(vm);
    }
}
