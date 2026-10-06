using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamaEdtech.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TicketLastActivityIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Tickets_IsReadByAdmin_LastActivityDate",
                table: "Tickets");

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_LastActivityDate",
                table: "Tickets",
                column: "LastActivityDate",
                descending: new bool[0]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Tickets_LastActivityDate",
                table: "Tickets");

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_IsReadByAdmin_LastActivityDate",
                table: "Tickets",
                columns: new[] { "IsReadByAdmin", "LastActivityDate" },
                descending: new[] { false, true });
        }
    }
}
