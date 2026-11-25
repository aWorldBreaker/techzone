using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TechZone.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReturnOrderDetail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "OrderDetailId",
                table: "ReturnRequests",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReturnRequests_OrderDetailId",
                table: "ReturnRequests",
                column: "OrderDetailId");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_NgayDat",
                table: "Orders",
                column: "NgayDat");

            migrationBuilder.Sql("""
                UPDATE ReturnRequests
                SET OrderDetailId = (
                    SELECT MIN(d.Id) FROM OrderDetails d
                    WHERE d.OrderId = ReturnRequests.OrderId AND d.ProductId = ReturnRequests.ProductId
                    HAVING COUNT(*) = 1
                );
                """);

            migrationBuilder.AddForeignKey(
                name: "FK_ReturnRequests_OrderDetails_OrderDetailId",
                table: "ReturnRequests",
                column: "OrderDetailId",
                principalTable: "OrderDetails",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Orders_NgayDat",
                table: "Orders");

            migrationBuilder.DropForeignKey(
                name: "FK_ReturnRequests_OrderDetails_OrderDetailId",
                table: "ReturnRequests");

            migrationBuilder.DropIndex(
                name: "IX_ReturnRequests_OrderDetailId",
                table: "ReturnRequests");

            migrationBuilder.DropColumn(
                name: "OrderDetailId",
                table: "ReturnRequests");
        }
    }
}
