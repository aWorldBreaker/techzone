using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TechZone.Data;
using TechZone.Models;

namespace TechZone.Controllers;

[Authorize]
public class AddressesController(AppDbContext db) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return Challenge();
        return View(await db.Addresses.AsNoTracking()
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.MacDinh).ThenByDescending(a => a.Id)
            .ToListAsync());
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int? id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return Challenge();
        if (id is null) return View(new AddressInput());

        var address = await db.Addresses.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId);
        if (address is null) return NotFound();
        ViewData["AddressId"] = id;
        return View(new AddressInput
        {
            HoTenNhan = address.HoTenNhan,
            SoDienThoai = address.SoDienThoai,
            DiaChi = address.DiaChi,
            MacDinh = address.MacDinh
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int? id, AddressInput input)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return Challenge();
        if (!ModelState.IsValid)
        {
            ViewData["AddressId"] = id;
            return View(input);
        }

        await using var transaction = await db.Database.BeginTransactionAsync();
        var own = db.Addresses.Where(a => a.UserId == userId);
        var address = id is null ? new Address { UserId = userId } : await own.FirstOrDefaultAsync(a => a.Id == id);
        if (address is null) return NotFound();
        if (input.MacDinh)
        {
            await own.Where(a => a.Id != address.Id && a.MacDinh)
                .ExecuteUpdateAsync(setters => setters.SetProperty(a => a.MacDinh, false));
        }

        address.HoTenNhan = input.HoTenNhan.Trim();
        address.SoDienThoai = input.SoDienThoai.Trim();
        address.DiaChi = input.DiaChi.Trim();
        address.MacDinh = input.MacDinh;
        if (id is null) db.Addresses.Add(address);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        TempData["Success"] = "Đã lưu địa chỉ giao hàng.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return Challenge();
        var deleted = await db.Addresses.Where(a => a.Id == id && a.UserId == userId).ExecuteDeleteAsync();
        if (deleted == 0) return NotFound();
        TempData["Success"] = "Đã xóa địa chỉ giao hàng.";
        return RedirectToAction(nameof(Index));
    }
}

public class AddressInput
{
    [Required(ErrorMessage = "Nhập họ tên người nhận."), MaxLength(100, ErrorMessage = "Họ tên tối đa 100 ký tự.")]
    public string HoTenNhan { get; set; } = string.Empty;

    [Required(ErrorMessage = "Nhập số điện thoại."), MaxLength(20, ErrorMessage = "Số điện thoại tối đa 20 ký tự.")]
    public string SoDienThoai { get; set; } = string.Empty;

    [Required(ErrorMessage = "Nhập địa chỉ giao hàng."), MaxLength(500, ErrorMessage = "Địa chỉ tối đa 500 ký tự.")]
    public string DiaChi { get; set; } = string.Empty;

    public bool MacDinh { get; set; }
}
