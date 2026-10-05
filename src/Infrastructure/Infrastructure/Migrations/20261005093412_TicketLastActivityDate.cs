using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamaEdtech.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TicketLastActivityDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastActivityDate",
                table: "Tickets",
                type: "datetimeoffset",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            // Backfill existing tickets: newest reply's date, or the ticket's own CreationDate when it has none.
            migrationBuilder.Sql(@"
UPDATE t
SET t.LastActivityDate = COALESCE(r.LastReplyDate, t.CreationDate)
FROM Tickets t
OUTER APPLY (SELECT MAX(tr.CreationDate) AS LastReplyDate FROM TicketReplies tr WHERE tr.TicketId = t.Id) r;");

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_IsReadByAdmin_LastActivityDate",
                table: "Tickets",
                columns: new[] { "IsReadByAdmin", "LastActivityDate" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Tickets_IsReadByAdmin_LastActivityDate",
                table: "Tickets");

            migrationBuilder.DropColumn(
                name: "LastActivityDate",
                table: "Tickets");
        }
    }
}
