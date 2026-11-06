using TechZone.Services;

namespace TechZone.Services;

/// <summary>
/// Ảnh giá checkout lúc GET (snapshot các thành phần giá TRƯỚC điểm).
/// Chỉ để SO SÁNH "giá có đổi không" giữa GET và POST — không bao giờ
/// là nguồn sự thật cho tổng tiền (tổng thật luôn tính lại từ DB).
/// Lưu trong session ("TechZone.CheckoutGia") ở Checkout GET và mỗi lần
/// render lại trang xác nhận giá mới.
/// </summary>
public record AnhGiaCheckout(
    decimal TamTinh,
    decimal GiamGiaVoucher,
    decimal GiamGiaCombo,
    decimal GiamGiaHang,
    bool FreeShip);

/// <summary>
/// So sánh giá GET vs giá tính lại ở POST (Bước 4a, 2026-11-06).
/// So theo TỪNG THÀNH PHẦN thay vì so tổng cuối, vì điểm là do người
/// dùng nhập ở form POST (GET tính với 0 điểm) nên tổng cuối khác nhau
/// là bình thường khi khách dùng điểm lần đầu — không phải "giá đổi".
/// "Đổi" = một trong:
/// - TamTinh đổi (giá dòng đổi: Flash Sale / slot hết hoặc mới chạy),
/// - giảm voucher / combo / hạng đổi (hết hạn, hết lượt, đổi hạng),
/// - freeship đổi (lên/xuống hạng Vàng, voucher freeship chết),
/// - số điểm xin dùng không dùng đủ SO VỚI MỨC DỰ KIẾN từ ảnh GET
///   (min(số xin, phần còn lại sau giảm lúc GET)) — vd voucher chết
///   làm co phần còn lại, hoặc số dư giảm. So với mức dự kiến này
///   (không so với số xin thô) để không báo "giá đổi" oan khi điểm
///   vốn đã bị chặn ở phần còn lại ngay từ đầu (vd xin 50 điểm mà
///   voucher chỉ chừa 5.000đ thì chỉ dùng được 5 điểm cả ở GET lẫn
///   POST — đó không phải thay đổi, và so với số xin thô sẽ kẹt
///   vòng lặp xác nhận lại vô hạn). Xin vượt số dư thì đã bị chặn
///   riêng ở nhánh LoiDiem của controller trước khi tới đây.
/// Trả về false khi chỉ khác ở chỗ khách dùng điểm (điểm dùng đủ như
/// xin) — đó không phải giá đổi, POST đặt luôn.
/// </summary>
public static class CheckoutGiaHelper
{
    public static AnhGiaCheckout ChupTu(KetQuaTongDon ketQua) => new(
        ketQua.TamTinh,
        ketQua.GiamGiaVoucher,
        ketQua.GiamGiaCombo,
        ketQua.GiamGiaHang,
        ketQua.FreeShip);

    public static bool CoThayDoi(AnhGiaCheckout cu, KetQuaTongDon moi, int diemDungYeuCau)
    {
        if (moi.TamTinh != cu.TamTinh) return true;
        if (moi.GiamGiaVoucher != cu.GiamGiaVoucher) return true;
        if (moi.GiamGiaCombo != cu.GiamGiaCombo) return true;
        if (moi.GiamGiaHang != cu.GiamGiaHang) return true;
        if (moi.FreeShip != cu.FreeShip) return true;
        // Điểm: so số điểm dùng được với mức DỰ KIẾN từ ảnh GET
        // (min(số xin, phần còn lại sau giảm lúc GET) — cùng công thức
        // DungDiemAsync, chưa tính số dư vì số dư xin vượt đã bị chặn
        // ở nhánh LoiDiem trước khi tới đây). Chỉ khi dùng được ít
        // hơn dự kiến thì dữ liệu mới thật sự đổi (voucher chết, hạng
        // đổi, số dư giảm...). So với số xin thô sẽ báo oan khi điểm
        // vốn đã bị chặn ở phần còn lại ngay từ đầu.
        if (diemDungYeuCau > 0)
        {
            var conLaiCu = Math.Max(0,
                cu.TamTinh - cu.GiamGiaVoucher - cu.GiamGiaCombo - cu.GiamGiaHang);
            var giamDuKien = Math.Min(diemDungYeuCau * 1_000m, conLaiCu);
            var diemDungDuKien = (int)(giamDuKien / 1_000m);
            if (moi.DiemDungThucTe != diemDungDuKien) return true;
        }
        // Phí ship kỳ vọng nếu chỉ có điểm là mới (áp điểm mới lên ảnh cũ):
        // nếu phí ship tính lại khác mức đó thì ngưỡng ship đã bị đổi bởi
        // một khoản giảm / tạm tính — nhưng các khoản đó đã so ở trên nên
        // tới đây chỉ còn khác do làm tròn / ngưỡng, vẫn coi là đổi.
        var sauGiamCuVoiDiemMoi = Math.Max(0,
            cu.TamTinh - cu.GiamGiaVoucher - cu.GiamGiaCombo - cu.GiamGiaHang - moi.GiamGiaDiem);
        var phiShipCuVoiDiemMoi = cu.FreeShip || sauGiamCuVoiDiemMoi >= 2_000_000 || cu.TamTinh == 0
            ? 0 : 30_000;
        if (moi.PhiShip != phiShipCuVoiDiemMoi) return true;
        return false;
    }
}
