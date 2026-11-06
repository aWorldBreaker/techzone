using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TechZone.Data.Migrations
{
    /// <inheritdoc />
    public partial class GuestTokenHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "GuestTokenHetHan",
                table: "Orders",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "GuestTokenKhoaDen",
                table: "Orders",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GuestTokenSaiLan",
                table: "Orders",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            // SEC-04 — Duong nang cap du lieu cho token cu:
            //  - Token cu (GuestToken NOT NULL, tao truoc SEC-04) duoc an han
            //    30 ngay ke tu luc migrate — van dung duoc, khong chet ngay
            //    vi NgayDat + 30 ngay co the da qua. Token moi thi han tinh
            //    tu ngay dat (CheckoutService).
            //  - KHONG hash truc tiep gia tri GuestToken dang luu o day:
            //    SQLite khong co ham SHA-256 trong SQL. Viec hash thuc hien
            //    bang C# o Services/GuestTokenBackfill.ChayAsync (chay 1 lan
            //    sau migrate) hoac tu dong o lan verify thanh cong dau tien
            //    (OrderAccess.XacMinhToken — nang cap co hoi). Den luc do,
            //    verify van chap nhan plaintext cu (duong doc kep).
            migrationBuilder.Sql(
                "UPDATE \"Orders\" SET \"GuestTokenHetHan\" = datetime('now', '+30 days') WHERE \"GuestToken\" IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GuestTokenHetHan",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "GuestTokenKhoaDen",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "GuestTokenSaiLan",
                table: "Orders");
        }
    }
}
