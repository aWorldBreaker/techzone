using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TechZone.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSprint3Combo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ComboId",
                table: "Orders",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ComboSoBo",
                table: "Orders",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "GiamGiaCombo",
                table: "Orders",
                type: "decimal(18,0)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "Combos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Ten = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    MoTa = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    PhanTramGiam = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    BatDau = table.Column<DateTime>(type: "TEXT", nullable: false),
                    KetThuc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    GioiHanSL = table.Column<int>(type: "INTEGER", nullable: true),
                    DaBan = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Combos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ComboItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ComboId = table.Column<int>(type: "INTEGER", nullable: false),
                    ProductId = table.Column<int>(type: "INTEGER", nullable: false),
                    SoLuong = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComboItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ComboItems_Combos_ComboId",
                        column: x => x.ComboId,
                        principalTable: "Combos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ComboItems_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_ComboId",
                table: "Orders",
                column: "ComboId");

            migrationBuilder.CreateIndex(
                name: "IX_ComboItems_ComboId_ProductId",
                table: "ComboItems",
                columns: new[] { "ComboId", "ProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ComboItems_ProductId",
                table: "ComboItems",
                column: "ProductId");

            migrationBuilder.AddForeignKey(
                name: "FK_Orders_Combos_ComboId",
                table: "Orders",
                column: "ComboId",
                principalTable: "Combos",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Orders_Combos_ComboId",
                table: "Orders");

            migrationBuilder.DropTable(
                name: "ComboItems");

            migrationBuilder.DropTable(
                name: "Combos");

            migrationBuilder.DropIndex(
                name: "IX_Orders_ComboId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ComboId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ComboSoBo",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "GiamGiaCombo",
                table: "Orders");
        }
    }
}
