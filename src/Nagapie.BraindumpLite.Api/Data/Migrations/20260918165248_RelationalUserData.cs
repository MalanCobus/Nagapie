using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nagapie.BraindumpLite.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class RelationalUserData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsCommitted",
                table: "BrainDumps",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "OriginalAvailable",
                table: "BrainDumps",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "WasAiProcessed",
                table: "BrainDumps",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "Categories",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CustomName = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    ColorToken = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    Version = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Categories", x => new { x.UserId, x.Id });
                    table.ForeignKey(
                        name: "FK_Categories_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Drafts",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", maxLength: 5000, nullable: false),
                    InputMethod = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    WasAiProcessed = table.Column<bool>(type: "bit", nullable: false),
                    ReviewJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Version = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Drafts", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_Drafts_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RelationalAccounts",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Epoch = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ImportedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RelationalAccounts", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_RelationalAccounts_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Thoughts",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceDumpId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Text = table.Column<string>(type: "nvarchar(max)", maxLength: 5000, nullable: false),
                    PlanningHorizon = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    InputMethod = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CompletionReason = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Version = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Thoughts", x => new { x.UserId, x.Id });
                    table.ForeignKey(
                        name: "FK_Thoughts_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Thoughts_BrainDumps_UserId_SourceDumpId",
                        columns: x => new { x.UserId, x.SourceDumpId },
                        principalTable: "BrainDumps",
                        principalColumns: new[] { "UserId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Thoughts_Categories_UserId_CategoryId",
                        columns: x => new { x.UserId, x.CategoryId },
                        principalTable: "Categories",
                        principalColumns: new[] { "UserId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BrainDumps_UserId_SavedAtUtc_Id",
                table: "BrainDumps",
                columns: new[] { "UserId", "SavedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Thoughts_UserId_CategoryId",
                table: "Thoughts",
                columns: new[] { "UserId", "CategoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_Thoughts_UserId_CompletionReason_PlanningHorizon_CreatedAtUtc",
                table: "Thoughts",
                columns: new[] { "UserId", "CompletionReason", "PlanningHorizon", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Thoughts_UserId_SourceDumpId",
                table: "Thoughts",
                columns: new[] { "UserId", "SourceDumpId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Drafts");

            migrationBuilder.DropTable(
                name: "RelationalAccounts");

            migrationBuilder.DropTable(
                name: "Thoughts");

            migrationBuilder.DropTable(
                name: "Categories");

            migrationBuilder.DropIndex(
                name: "IX_BrainDumps_UserId_SavedAtUtc_Id",
                table: "BrainDumps");

            migrationBuilder.DropColumn(
                name: "IsCommitted",
                table: "BrainDumps");

            migrationBuilder.DropColumn(
                name: "OriginalAvailable",
                table: "BrainDumps");

            migrationBuilder.DropColumn(
                name: "WasAiProcessed",
                table: "BrainDumps");
        }
    }
}

