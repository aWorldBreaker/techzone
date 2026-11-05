using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TechZone.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSprint2Financial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "TongVat",
                table: "Orders",
                type: "decimal(18,0)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "FlashSaleId",
                table: "OrderDetails",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "GiaGoc",
                table: "OrderDetails",
                type: "decimal(18,0)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "TenKhuyenMai",
                table: "OrderDetails",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ThueSuatVat",
                table: "OrderDetails",
                type: "decimal(5,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TienVat",
                table: "OrderDetails",
                type: "decimal(18,0)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateIndex(
                name: "IX_OrderDetails_FlashSaleId",
                table: "OrderDetails",
                column: "FlashSaleId");

            migrationBuilder.AddForeignKey(
                name: "FK_OrderDetails_FlashSales_FlashSaleId",
                table: "OrderDetails",
                column: "FlashSaleId",
                principalTable: "FlashSales",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OrderDetails_FlashSales_FlashSaleId",
                table: "OrderDetails");

            migrationBuilder.DropIndex(
                name: "IX_OrderDetails_FlashSaleId",
                table: "OrderDetails");

            migrationBuilder.DropColumn(
                name: "TongVat",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "FlashSaleId",
                table: "OrderDetails");

            migrationBuilder.DropColumn(
                name: "GiaGoc",
                table: "OrderDetails");

            migrationBuilder.DropColumn(
                name: "TenKhuyenMai",
                table: "OrderDetails");

            migrationBuilder.DropColumn(
                name: "ThueSuatVat",
                table: "OrderDetails");

            migrationBuilder.DropColumn(
                name: "TienVat",
                table: "OrderDetails");
        }
    }
}
