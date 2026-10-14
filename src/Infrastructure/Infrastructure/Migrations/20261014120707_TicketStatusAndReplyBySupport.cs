using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamaEdtech.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TicketStatusAndReplyBySupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte>(
                name: "Status",
                table: "Tickets",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.AddColumn<bool>(
                name: "BySupport",
                table: "TicketReplies",
                type: "bit",
                nullable: false,
                defaultValue: false);

            // Existing replies never stored who wrote them. Until now only Admin-role users could answer tickets, so a
            // reply by a current admin is a support reply. This runs once; later replies store it when they are made.
            migrationBuilder.Sql(@"
UPDATE r SET BySupport = 1
FROM TicketReplies r
WHERE EXISTS (
    SELECT 1 FROM ApplicationUserRoles ur
    INNER JOIN ApplicationRoles ro ON ro.Id = ur.RoleId
    WHERE ur.UserId = r.CreationUserId AND ro.NormalizedName = 'ADMIN')");

            // Every ticket starts Open (0); one whose newest reply came from support is waiting on the customer, so it
            // becomes Answered (1). Tickets an admin started without a reply can't be told apart and stay Open.
            migrationBuilder.Sql(@"
UPDATE t SET Status = 1
FROM Tickets t
CROSS APPLY (SELECT TOP 1 r.BySupport FROM TicketReplies r WHERE r.TicketId = t.Id ORDER BY r.CreationDate DESC, r.Id DESC) lastReply
WHERE lastReply.BySupport = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Status",
                table: "Tickets");

            migrationBuilder.DropColumn(
                name: "BySupport",
                table: "TicketReplies");
        }
    }
}
