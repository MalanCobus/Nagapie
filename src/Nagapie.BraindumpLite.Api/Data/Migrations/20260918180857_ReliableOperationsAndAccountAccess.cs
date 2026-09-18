using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nagapie.BraindumpLite.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReliableOperationsAndAccountAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LicenseHash",
                table: "RelationalAccounts",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SuccessfulAiDumps",
                table: "RelationalAccounts",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql("""
                UPDATE a SET SuccessfulAiDumps = (SELECT COUNT(*) FROM BrainDumps d
                    WHERE d.UserId = a.UserId AND d.WasAiProcessed = 1)
                FROM RelationalAccounts a;
                """);

            migrationBuilder.CreateTable(
                name: "OperationReceipts",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ResponseJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperationReceipts", x => new { x.UserId, x.Id });
                    table.ForeignKey(
                        name: "FK_OperationReceipts_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProcessedDumps",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessedDumps", x => new { x.UserId, x.Id });
                    table.ForeignKey(
                        name: "FK_ProcessedDumps_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RelationalAccounts_LicenseHash",
                table: "RelationalAccounts",
                column: "LicenseHash",
                unique: true,
                filter: "[LicenseHash] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OperationReceipts");

            migrationBuilder.DropTable(
                name: "ProcessedDumps");

            migrationBuilder.DropIndex(
                name: "IX_RelationalAccounts_LicenseHash",
                table: "RelationalAccounts");

            migrationBuilder.DropColumn(
                name: "LicenseHash",
                table: "RelationalAccounts");

            migrationBuilder.DropColumn(
                name: "SuccessfulAiDumps",
                table: "RelationalAccounts");
        }
    }
}
