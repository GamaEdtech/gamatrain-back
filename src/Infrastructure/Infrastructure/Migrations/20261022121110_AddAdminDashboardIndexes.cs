using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamaEdtech.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAdminDashboardIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReferralIpSightings",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    IpHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    LastSeenDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReferralIpSightings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReferralIpSightings_ApplicationUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_Unread_LastActivityDate",
                table: "Tickets",
                column: "LastActivityDate",
                filter: "[IsReadByAdmin] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_PostComments_Status",
                table: "PostComments",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Contributions_CategoryType_CreationDate",
                table: "Contributions",
                columns: new[] { "CategoryType", "CreationDate" },
                filter: "[Status] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_ReferralIpSightings_UserId_IpHash",
                table: "ReferralIpSightings",
                columns: new[] { "UserId", "IpHash" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReferralIpSightings");

            migrationBuilder.DropIndex(
                name: "IX_Tickets_Unread_LastActivityDate",
                table: "Tickets");

            migrationBuilder.DropIndex(
                name: "IX_PostComments_Status",
                table: "PostComments");

            migrationBuilder.DropIndex(
                name: "IX_Contributions_CategoryType_CreationDate",
                table: "Contributions");
        }
    }
}
