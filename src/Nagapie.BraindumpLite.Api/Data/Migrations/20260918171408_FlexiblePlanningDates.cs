using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nagapie.BraindumpLite.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class FlexiblePlanningDates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "PlannedDate",
                table: "Thoughts",
                type: "date",
                nullable: true);

            migrationBuilder.Sql("UPDATE [Thoughts] SET [PlanningHorizon] = N'later', [Version] = NEWID() WHERE [PlanningHorizon] = N'next-week';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PlannedDate",
                table: "Thoughts");
        }
    }
}
