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
            // Xiaomi 15 (Id 1) — theta -60deg (Sprint 5 fix follow-up, Worker D):
            // mat man hinh cua GLB KHONG huong ve theta=0. Test quyet dinh
            // (jumpCameraToGoal + cho dung yen + doc getCameraOrbit that):
            // theta=0 dung yen -0.00 nhung anh VAN nghieng (man hinh ~60%
            // chieu rong, dai canh toi phai lon); sweep thay canh-ben o +30,
            // lung o +45..+90 -> phap tuyen man hinh o -60. Anh
            // phone-neg-theta60.png (doc -60.0 dung yen): man hinh face-on,
            // chiem gan het chieu rong than may. Giu phi 75, radius 105%.
            [1] = new("/models/techzone_smartphone.glb", "-60deg 75deg 105%", "-90deg 0deg 0deg"),
            // TUF F15 (Id 3) — laptop: goc cheo 3/4 khoe ban phim + mat man hinh.
            // Man hinh mo rong (~120 do, nga ve sau) nen camera phai thap
            // (phi 80deg): phi 60-70deg chi thay lung nap; 0deg/90deg phang,
            // khong co chieu sau. Da thu 6 goc bang Chrome headless
            // (xem /tmp/verify-3d-sprint5/variants-laptop.png): 45/80 tot nhat.
            [3] = new("/models/techzone_laptop.glb", "45deg 80deg 120%", "0deg 0deg 0deg"),
            // Sony WH-CH520 (Id 4) — tai nghe: goc cheo 3/4 khoe earcup + headband.
            // Orientation 0deg. Da thu 6 goc bang Chrome headless
            // (xem /tmp/verify-3d-sprint5/variants-headphones.png): 60/80 tot
            // nhat — earcup truoc to, tron, ro mat ngoai; headband vong ra sau;
            // earcup sau van thay. 0/85 qua phang, earcup chi la dom nho 2 dau.
            [4] = new("/models/techzone_headphones.glb", "60deg 80deg 110%", "0deg 0deg 0deg"),
        };

    public static Product3dModel? ForProduct(int productId) =>
        Map.TryGetValue(productId, out var model) ? model : null;
}
