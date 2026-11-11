using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamaEdtech.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SchoolMembers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SchoolMembers",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    SchoolId = table.Column<long>(type: "bigint", nullable: false),
                    Role = table.Column<byte>(type: "tinyint", nullable: false),
                    ShowName = table.Column<bool>(type: "bit", nullable: false),
                    CreationDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SchoolMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SchoolMembers_ApplicationUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SchoolMembers_Schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "Schools",
                        principalColumn: "Id");
                });

            migrationBuilder.InsertData(
                table: "ApplicationRoles",
                columns: new[] { "Id", "ConcurrencyStamp", "Name", "NormalizedName" },
                values: new object[] { 6L, "85465B3B-E646-49BC-AAC6-D07C450B3AE8", "Parent", "PARENT" });

            migrationBuilder.CreateIndex(
                name: "IX_SchoolMembers_SchoolId_Role",
                table: "SchoolMembers",
                columns: new[] { "SchoolId", "Role" });

            migrationBuilder.CreateIndex(
                name: "IX_SchoolMembers_UserId_SchoolId_Role",
                table: "SchoolMembers",
                columns: new[] { "UserId", "SchoolId", "Role" },
                unique: true);

            // Copy the one school a user could set on their profile (ApplicationUsers.SchoolId) into a membership:
            // Teacher (1) for users in the Teacher role (Id 2), otherwise Student (3). Nobody is named (ShowName = 0)
            // until they say so, and no account role changes.
            migrationBuilder.Sql("""
                INSERT INTO SchoolMembers (UserId, SchoolId, Role, ShowName, CreationDate)
                SELECT u.Id, u.SchoolId,
                    CASE WHEN EXISTS (SELECT 1 FROM ApplicationUserRoles r WHERE r.UserId = u.Id AND r.RoleId = 2) THEN 1 ELSE 3 END,
                    0, SYSDATETIMEOFFSET()
                FROM ApplicationUsers u
                INNER JOIN Schools s ON s.Id = u.SchoolId
                WHERE u.SchoolId IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SchoolMembers");

            migrationBuilder.DeleteData(
                table: "ApplicationRoles",
                keyColumn: "Id",
                keyValue: 6L);
        }
    }
}
