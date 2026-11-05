using System.ComponentModel.DataAnnotations.Schema;

namespace TechZone.Models;

/// <summary>
/// Ban ghi hoan tien khi huy don / doi tra duoc duyet (Sprint 2 — Mang 4, B-9).
/// Entity EF that tu migration AddSprint2Rollback: bang "Refunds" (Id PK,
/// OrderId, ReturnRequestId NULL = hoan do huy don, SoTien decimal(18,0),
/// PhuongThucTT, TrangThai "Đã hoàn", NgayTao; index IX_Refunds_OrderId).
/// Khong dat FK constraint trong model (chi luu Id) de bang hoan tien giu
/// duoc lich su du don / yeu cau doi tra co bi xoa sau nay.
///
/// Quy tac hoan tien (xem Services/RefundService):
///   - CHI ghi hoan khi tien DA THU THAT (Order.DaThanhToan == true).
///     Don COD chua thu ma huy thi KHONG co ban ghi hoan nao.
///   - Idempotent: 1 don huy chi co toi da 1 dong hoan (ReturnRequestId
///     NULL); 1 yeu cau doi tra chi co toi da 1 dong hoan.
/// </summary>
public class RefundRecord
{
    public int Id { get; set; }
    public int OrderId { get; set; }

    /// <summary>NULL = hoan do huy don; co gia tri = hoan do doi tra duoc duyet.</summary>
    public int? ReturnRequestId { get; set; }

    [Column(TypeName = "decimal(18,0)")]
    public decimal SoTien { get; set; }
    public string PhuongThucTT { get; set; } = string.Empty;

    /// <summary>"Đã hoàn" (thanh toan gia lap — ghi nhan la hoan xong).</summary>
    public string TrangThai { get; set; } = "Đã hoàn";
    public DateTime NgayTao { get; set; } = DateTime.UtcNow;
}
