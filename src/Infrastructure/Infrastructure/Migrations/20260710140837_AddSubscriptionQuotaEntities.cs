using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamaEdtech.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionQuotaEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "BaseCurrencyAmount",
                table: "Payments",
                type: "numeric(36,18)",
                precision: 36,
                scale: 18,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ExchangeRate",
                table: "Payments",
                type: "numeric(36,18)",
                precision: 36,
                scale: 18,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "UserSubscriptionId",
                table: "Payments",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Features",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreationDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Features", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SubscriptionPlanPrices",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SubscriptionPlanId = table.Column<long>(type: "bigint", nullable: false),
                    CountryCode = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: true),
                    Currency = table.Column<byte>(type: "tinyint", nullable: false),
                    Price = table.Column<decimal>(type: "numeric(36,18)", precision: 36, scale: 18, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionPlanPrices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubscriptionPlanPrices_SubscriptionPlans_SubscriptionPlanId",
                        column: x => x.SubscriptionPlanId,
                        principalTable: "SubscriptionPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserSubscriptions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    SubscriptionPlanId = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    CreationDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    StartDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ExpirationDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PricePaid = table.Column<decimal>(type: "numeric(36,18)", precision: 36, scale: 18, nullable: false),
                    Currency = table.Column<byte>(type: "tinyint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSubscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserSubscriptions_ApplicationUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UserSubscriptions_SubscriptionPlans_SubscriptionPlanId",
                        column: x => x.SubscriptionPlanId,
                        principalTable: "SubscriptionPlans",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "SubscriptionPlanFeatures",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SubscriptionPlanId = table.Column<long>(type: "bigint", nullable: false),
                    FeatureId = table.Column<int>(type: "int", nullable: false),
                    Limit = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionPlanFeatures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubscriptionPlanFeatures_Features_FeatureId",
                        column: x => x.FeatureId,
                        principalTable: "Features",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SubscriptionPlanFeatures_SubscriptionPlans_SubscriptionPlanId",
                        column: x => x.SubscriptionPlanId,
                        principalTable: "SubscriptionPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SubscriptionPlanGatewayMappings",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SubscriptionPlanPriceId = table.Column<long>(type: "bigint", nullable: false),
                    Gateway = table.Column<byte>(type: "tinyint", nullable: false),
                    ExternalProductId = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    ExternalPlanId = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionPlanGatewayMappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubscriptionPlanGatewayMappings_SubscriptionPlanPrices_SubscriptionPlanPriceId",
                        column: x => x.SubscriptionPlanPriceId,
                        principalTable: "SubscriptionPlanPrices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserSubscriptionQuotas",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserSubscriptionId = table.Column<long>(type: "bigint", nullable: false),
                    FeatureId = table.Column<int>(type: "int", nullable: false),
                    Limit = table.Column<int>(type: "int", nullable: false),
                    Used = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSubscriptionQuotas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserSubscriptionQuotas_Features_FeatureId",
                        column: x => x.FeatureId,
                        principalTable: "Features",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UserSubscriptionQuotas_UserSubscriptions_UserSubscriptionId",
                        column: x => x.UserSubscriptionId,
                        principalTable: "UserSubscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Payments_UserSubscriptionId",
                table: "Payments",
                column: "UserSubscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_Features_Code",
                table: "Features",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPlanFeatures_FeatureId",
                table: "SubscriptionPlanFeatures",
                column: "FeatureId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPlanFeatures_SubscriptionPlanId_FeatureId",
                table: "SubscriptionPlanFeatures",
                columns: new[] { "SubscriptionPlanId", "FeatureId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPlanGatewayMappings_SubscriptionPlanPriceId_Gateway",
                table: "SubscriptionPlanGatewayMappings",
                columns: new[] { "SubscriptionPlanPriceId", "Gateway" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPlanPrices_SubscriptionPlanId_CountryCode",
                table: "SubscriptionPlanPrices",
                columns: new[] { "SubscriptionPlanId", "CountryCode" },
                unique: true,
                filter: "[CountryCode] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_UserSubscriptionQuotas_FeatureId",
                table: "UserSubscriptionQuotas",
                column: "FeatureId");

            migrationBuilder.CreateIndex(
                name: "IX_UserSubscriptionQuotas_UserSubscriptionId_FeatureId",
                table: "UserSubscriptionQuotas",
                columns: new[] { "UserSubscriptionId", "FeatureId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserSubscriptions_Status_ExpirationDate",
                table: "UserSubscriptions",
                columns: new[] { "Status", "ExpirationDate" });

            migrationBuilder.CreateIndex(
                name: "IX_UserSubscriptions_SubscriptionPlanId",
                table: "UserSubscriptions",
                column: "SubscriptionPlanId");

            migrationBuilder.CreateIndex(
                name: "IX_UserSubscriptions_UserId_Status",
                table: "UserSubscriptions",
                columns: new[] { "UserId", "Status" });

            migrationBuilder.AddForeignKey(
                name: "FK_Payments_UserSubscriptions_UserSubscriptionId",
                table: "Payments",
                column: "UserSubscriptionId",
                principalTable: "UserSubscriptions",
                principalColumn: "Id");

            // Copy each plan's current pricing into a default (global, CountryCode = NULL) price row.
            // SubscriptionPlans.Price/Currency still exist here; they are dropped by a follow-up migration.
            migrationBuilder.Sql(@"
INSERT INTO SubscriptionPlanPrices (SubscriptionPlanId, CountryCode, Currency, Price)
SELECT Id, NULL, Currency, Price FROM SubscriptionPlans");

            // Seed the feature catalog; codes must match Domain.Enumeration.FeatureCodes.
            // TestSubmission/ExamParticipation are seeded inactive: no enforcement site charges them yet.
            migrationBuilder.Sql(@"
INSERT INTO Features (Code, Name, Description, IsActive, CreationDate) VALUES
('PastpaperDownload', N'Pastpaper Downloads', NULL, 1, SYSDATETIMEOFFSET()),
('TestDownload', N'Test Downloads', NULL, 1, SYSDATETIMEOFFSET()),
('TestSubmission', N'Test Submissions', NULL, 0, SYSDATETIMEOFFSET()),
('ExamParticipation', N'Exam Participations', NULL, 0, SYSDATETIMEOFFSET())");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Payments_UserSubscriptions_UserSubscriptionId",
                table: "Payments");

            migrationBuilder.DropTable(
                name: "SubscriptionPlanFeatures");

            migrationBuilder.DropTable(
                name: "SubscriptionPlanGatewayMappings");

            migrationBuilder.DropTable(
                name: "UserSubscriptionQuotas");

            migrationBuilder.DropTable(
                name: "SubscriptionPlanPrices");

            migrationBuilder.DropTable(
                name: "Features");

            migrationBuilder.DropTable(
                name: "UserSubscriptions");

            migrationBuilder.DropIndex(
                name: "IX_Payments_UserSubscriptionId",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "BaseCurrencyAmount",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "ExchangeRate",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "UserSubscriptionId",
                table: "Payments");
        }
    }
}
