using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using TechZone.Data;
using TechZone.Models;

var builder = WebApplication.CreateBuilder(args);

// MVC + Razor Views
builder.Services.AddControllersWithViews();

// Session (gio hang khach vang lai) - het phien thi mat, khong luu DB
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(2);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// EF Core + SQLite (demo). Doi sang SQL Server: doi chuoi ket noi + goi
// UseSqlServer thay cho UseSqlite trong dong duoi.
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

// Identity: tai khoan Khach / Admin
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequiredLength = 6;
        options.User.RequireUniqueEmail = true;
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/Login";
});

builder.Services.AddHttpContextAccessor();
builder.Services.Configure<TechZone.Services.VatOptions>(builder.Configuration.GetSection("Vat"));
builder.Services.Configure<TechZone.Services.LoyaltyOptions>(builder.Configuration.GetSection("Loyalty"));
builder.Services.AddScoped<TechZone.Services.VatService>();
builder.Services.AddScoped<TechZone.Services.CartService>();
builder.Services.AddScoped<TechZone.Services.PriceCalculationService>();
builder.Services.AddScoped<TechZone.Services.LoyaltyService>();
builder.Services.AddScoped<TechZone.Services.VoucherService>();
builder.Services.AddScoped<TechZone.Services.NotificationService>();
builder.Services.AddScoped<TechZone.Services.CheckoutService>();
builder.Services.AddScoped<TechZone.Services.OrderService>();
builder.Services.AddScoped<TechZone.Services.RefundService>();
builder.Services.AddScoped<TechZone.Services.ReturnService>();

var app = builder.Build();

// Seed du lieu that (danh muc, san pham, voucher, flash sale, don mau, tai khoan demo)
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    await SeedData.InitializeAsync(services);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
// Phuc vu file .glb/.gltf cho viewer 3D: StaticFiles mac dinh khong biet
// dinh dang nay nen tra 404. Them mapping MIME chuan.
var contentTypeProvider = new FileExtensionContentTypeProvider();
contentTypeProvider.Mappings[".glb"] = "model/gltf-binary";
contentTypeProvider.Mappings[".gltf"] = "model/gltf+json";
app.UseStaticFiles(new StaticFileOptions { ContentTypeProvider = contentTypeProvider });
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseSession();

app.MapControllerRoute(
    name: "areas",
    pattern: "{area:exists}/{controller=Home}/{action=Index}/{id?}");
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
