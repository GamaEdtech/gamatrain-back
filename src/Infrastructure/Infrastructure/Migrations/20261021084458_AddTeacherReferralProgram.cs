using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamaEdtech.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTeacherReferralProgram : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<byte>(
                name: "Source",
                table: "ContentOwnerCommissions",
                type: "tinyint",
                nullable: true,
                oldClrType: typeof(byte),
                oldType: "tinyint");

            migrationBuilder.AlterColumn<string>(
                name: "ExternalFileType",
                table: "ContentOwnerCommissions",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(20)",
                oldMaxLength: 20);

            migrationBuilder.AlterColumn<long>(
                name: "ExternalContentId",
                table: "ContentOwnerCommissions",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<byte>(
                name: "ContentType",
                table: "ContentOwnerCommissions",
                type: "tinyint",
                nullable: true,
                oldClrType: typeof(byte),
                oldType: "tinyint");

            migrationBuilder.AddColumn<long>(
                name: "PaymentId",
                table: "ContentOwnerCommissions",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ReferredByUserId",
                table: "ApplicationUsers",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReferredDate",
                table: "ApplicationUsers",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PendingReferrals",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Identity = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ReferralCode = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    CreationDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpirationDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PendingReferrals", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReferralMemberships",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    OfferPercent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    OfferDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    OfferExpirationDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    OfferEmailSentDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    JoinedDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Percent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReferralMemberships", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReferralMemberships_ApplicationUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.UpdateData(
                table: "ApplicationUsers",
                keyColumn: "Id",
                keyValue: 1L,
                columns: new[] { "ReferredByUserId", "ReferredDate" },
                values: new object[] { null, null });

            migrationBuilder.CreateIndex(
                name: "IX_ContentOwnerCommissions_PaymentId",
                table: "ContentOwnerCommissions",
                column: "PaymentId",
                unique: true,
                filter: "[PaymentId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationUsers_ReferredByUserId",
                table: "ApplicationUsers",
                column: "ReferredByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PendingReferrals_Identity",
                table: "PendingReferrals",
                column: "Identity",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReferralMemberships_OfferEmailSentDate",
                table: "ReferralMemberships",
                column: "OfferEmailSentDate");

            migrationBuilder.CreateIndex(
                name: "IX_ReferralMemberships_UserId",
                table: "ReferralMemberships",
                column: "UserId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ApplicationUsers_ApplicationUsers_ReferredByUserId",
                table: "ApplicationUsers",
                column: "ReferredByUserId",
                principalTable: "ApplicationUsers",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ContentOwnerCommissions_Payments_PaymentId",
                table: "ContentOwnerCommissions",
                column: "PaymentId",
                principalTable: "Payments",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ApplicationUsers_ApplicationUsers_ReferredByUserId",
                table: "ApplicationUsers");

            migrationBuilder.DropForeignKey(
                name: "FK_ContentOwnerCommissions_Payments_PaymentId",
                table: "ContentOwnerCommissions");

            migrationBuilder.DropTable(
                name: "PendingReferrals");

            migrationBuilder.DropTable(
                name: "ReferralMemberships");

            migrationBuilder.DropIndex(
                name: "IX_ContentOwnerCommissions_PaymentId",
                table: "ContentOwnerCommissions");

            migrationBuilder.DropIndex(
                name: "IX_ApplicationUsers_ReferredByUserId",
                table: "ApplicationUsers");

            migrationBuilder.DropColumn(
                name: "PaymentId",
                table: "ContentOwnerCommissions");

            migrationBuilder.DropColumn(
                name: "ReferredByUserId",
                table: "ApplicationUsers");

            migrationBuilder.DropColumn(
                name: "ReferredDate",
                table: "ApplicationUsers");

            migrationBuilder.AlterColumn<byte>(
                name: "Source",
                table: "ContentOwnerCommissions",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)0,
                oldClrType: typeof(byte),
                oldType: "tinyint",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ExternalFileType",
                table: "ContentOwnerCommissions",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(20)",
                oldMaxLength: 20,
                oldNullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "ExternalContentId",
                table: "ContentOwnerCommissions",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AlterColumn<byte>(
                name: "ContentType",
                table: "ContentOwnerCommissions",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)0,
                oldClrType: typeof(byte),
                oldType: "tinyint",
                oldNullable: true);
        }
    }
}
