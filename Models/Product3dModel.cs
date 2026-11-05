namespace TechZone.Models;

/// <summary>
/// Mo hinh 3D minh hoa (generic) gan voi 1 san pham.
/// CameraOrbit / Orientation la gia tri mac dinh cua &lt;model-viewer&gt; tren
/// trang chi tiet; product-3d.js doc lai camera-orbit tu attribute de reset.
/// </summary>
public sealed record Product3dModel(string ModelUrl, string CameraOrbit, string Orientation);

/// <summary>
/// Sprint 5 — Map Id san pham → model 3D (tong quat hoa trial chi co Id 1).
/// Chi san pham co trong map moi hien viewer; san pham khac giu nguyen anh.
/// Gamepad CO TÌNH KHÔNG co trong map: seed khong co san pham tay cam nao
/// (danh muc Gaming chi co chuot Logitech G502) — gan GLB tay cam vao chuot
/// la sai model. File techzone_gamepad.glb da copy vao wwwroot/models/ de
/// dung sau (gallery demo / khi seed co tay cam that), chua tich hop.
/// Them san pham co model: them 1 dong o day, khong sua View/JS.
/// </summary>
public static class Product3dModels
{
    public static readonly IReadOnlyDictionary<int, Product3dModel> Map =
        new Dictionary<int, Product3dModel>
        {
            // Xiaomi 15 (Id 1) — giu nguyen trial da VERIFIED 9/9: dung thang,
            // mat man hinh huong ra camera (orientation -90deg vi GLB nam phang).
            [1] = new("/models/techzone_smartphone.glb", "0deg 75deg 105%", "-90deg 0deg 0deg"),
            // TUF F15 (Id 3) — laptop: goc cheo khoe ban phim + man hinh mo.
            // Orientation 0deg (khong -90deg nhu dien thoai): GLB laptop dung
            // san, ban phim huong ve camera. Da render thu bang Chrome headless
            // (xem /tmp/glb-check/): 0deg nhan ra ngay la laptop mo; -90deg
            // xoay ngang rat xau; nhin tu tren (45deg) chi thay lung man hinh.
            [3] = new("/models/techzone_laptop.glb", "25deg 70deg 115%", "0deg 0deg 0deg"),
            // Sony WH-CH520 (Id 4) — tai nghe: goc cheo khoe earcup + headband.
            // Orientation 0deg: da render thu — nhan ra ngay la tai nghe;
            // -90deg xoay doc, rat xau.
            [4] = new("/models/techzone_headphones.glb", "20deg 70deg 110%", "0deg 0deg 0deg"),
        };

    public static Product3dModel? ForProduct(int productId) =>
        Map.TryGetValue(productId, out var model) ? model : null;
}
