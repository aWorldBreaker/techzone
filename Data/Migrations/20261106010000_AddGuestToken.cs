using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TechZone.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGuestToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // SEC-02: ma tra cuu don khach vang lai (Orders.GuestToken TEXT nullable).
            migrationBuilder.AddColumn<string>(
                name: "GuestToken",
                table: "Orders",
                type: "TEXT",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GuestToken",
                table: "Orders");
        }
    }
}
