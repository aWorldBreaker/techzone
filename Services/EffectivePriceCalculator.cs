using TechZone.Models;

namespace TechZone.Services;

/// <summary>
/// Giá hiệu dụng 1 đơn vị (biến thể mặc định, số lượng 1) — dùng cho
/// listing: sort + lọc khoảng giá + giá hiển thị phải dùng CÙNG 1 giá.
///
/// Nghiệp vụ GIỮ NGUYÊN thứ tự ưu tiên của
/// <see cref="PriceCalculationService.TinhGiaAsync"/> (không đổi hàm đó):
///   1. FlashSale trong khung giờ + còn quota + rẻ hơn GiaBan
///      (đợt rẻ nhất còn quota; hết quota thì KHÔNG rơi xuống slot —
///      quota là ràng buộc cứng);
///   2. Slot theo giờ — CHỈ khi không có đợt FlashSale nào trong giờ
///      (kể cả đợt đã hết quota, kể cả đợt cấu hình sai GiaSale &gt;=
///      GiaBan);
///   3. GiaBan.
///
/// Khác <see cref="PriceCalculationService.TinhGiaAsync"/> ở cách lấy dữ
/// liệu: hàm này tính PURE trên dữ liệu đã nạp sẵn (không query DB), để
/// controller nạp FlashSales + SlotItems bằng ĐÚNG 2 query gộp cho toàn bộ
/// SP đã lọc rồi tính batch trong memory — KHÔNG gọi TinhGiaAsync từng SP
/// (hàm đó làm 2 query/SP → N+1).
///
/// GIỚI HẠN SCALE: batch in-memory phù hợp catalog hiện tại (13 SP) tới
/// vài trăm SP. Vượt ~500 SP thì đo lại latency listing (tải toàn bộ SP đã
/// lọc + 2 bảng sale về RAM mỗi request) rồi mới cân nhắc cột cache/refresh.
/// </summary>
public static class EffectivePriceCalculator
{
    /// <summary>
    /// Tính giá hiệu dụng cho toàn bộ SP đã cho. <paramref name="flashSales"/>
    /// và <paramref name="slotItems"/> là TOÀN BỘ bản ghi của các SP đó
    /// (chưa cần lọc theo giờ — hàm tự lọc theo <paramref name="now"/>).
    /// Trả về dict Id SP → giá hiệu dụng (luôn có đủ mọi SP đầu vào).
    /// </summary>
    public static Dictionary<int, decimal> TinhBatch(
        IEnumerable<Product> products,
        IEnumerable<FlashSale> flashSales,
        IEnumerable<FlashSaleSlotItem> slotItems,
        DateTime now)
    {
        // Lọc theo giờ trước, nhóm theo SP, sort rẻ-dần trong từng nhóm
        // (khớp TinhGiaAsync: OrderBy GiaSale rồi duyệt tìm suất còn lại).
        var flashTheoSp = flashSales
            .Where(f => f.BatDau <= now && f.KetThuc >= now)
            .GroupBy(f => f.ProductId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(f => f.GiaSale).ToList());
        var slotTheoSp = slotItems
            .Where(i => i.Slot is not null && i.Slot.IsActive
                && now.Hour >= i.Slot.GioBatDau && now.Hour < i.Slot.GioKetThuc)
            .GroupBy(i => i.ProductId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(i => i.GiaSale).ToList());

        var ketQua = new Dictionary<int, decimal>();
        foreach (var p in products)
        {
            flashTheoSp.TryGetValue(p.Id, out var flash);
            slotTheoSp.TryGetValue(p.Id, out var slot);
            ketQua[p.Id] = TinhChoMotSp(p, flash, slot);
        }
        return ketQua;
    }

    /// <summary>
    /// Giá hiệu dụng 1 SP từ FlashSale/slot ĐÃ lọc theo giờ (mỗi list đã
    /// sort theo GiaSale tăng dần). Số lượng mặc định 1 như listing
    /// (all-or-nothing giống TinhGiaAsync: đủ suất cho 1 cái mới sale).
    /// </summary>
    internal static decimal TinhChoMotSp(
        Product product,
        List<FlashSale>? flashTrongGio,
        List<FlashSaleSlotItem>? slotTrongGio)
    {
        // 1. Có đợt FlashSale trong giờ (dù rẻ hay không, dù còn quota
        // hay không) → slot không được xét.
        if (flashTrongGio is { Count: > 0 })
        {
            foreach (var f in flashTrongGio)
            {
                if (f.GiaSale > 0 && f.GiaSale < product.GiaBan
                    && f.DaBan + 1 <= f.GioiHanSL)
                    return f.GiaSale;
            }
            // Mọi đợt rẻ đều hết quota, hoặc toàn đợt cấu hình sai
            // (GiaSale >= GiaBan): về giá gốc, KHÔNG rơi xuống slot.
            return product.GiaBan;
        }

        // 2. Slot theo giờ (rẻ nhất trong nhóm đã lọc).
        if (slotTrongGio is { Count: > 0 })
        {
            var slot = slotTrongGio[0];
            if (slot.GiaSale > 0 && slot.GiaSale < product.GiaBan)
                return slot.GiaSale;
        }

        // 3. Giá gốc.
        return product.GiaBan;
    }
}
