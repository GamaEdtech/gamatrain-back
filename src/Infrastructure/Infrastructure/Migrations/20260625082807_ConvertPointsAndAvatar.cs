using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamaEdtech.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ConvertPointsAndAvatar : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AvatarId",
                table: "ApplicationUsers",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.Sql(@"
UPDATE T SET t.Points = FLOOR(p.Amount*6500)
FROM Transactions t
INNER JOIN Payments p on t.IdentifierId=p.Id AND t.UserId=p.UserId
WHERE p.Currency in (0) and p.Status=1 and t.Description='Increase Balance by Payment'
");

            migrationBuilder.Sql(@"
UPDATE T SET t.Points = FLOOR(p.Amount*100)
FROM Transactions t
INNER JOIN Payments p on t.IdentifierId=p.Id AND t.UserId=p.UserId
WHERE p.Currency in (1,3,4) and p.Status=1 and t.Description='Increase Balance by Payment'
");

            migrationBuilder.Sql(@"
UPDATE T SET t.Points = FLOOR(p.Amount/520000000)
FROM Transactions t
INNER JOIN Payments p on t.IdentifierId=p.Id AND t.UserId=p.UserId
WHERE p.Currency in (2) and p.Status=1 and t.Description='Increase Balance by Payment'
");

            migrationBuilder.Sql(@"
UPDATE Transactions SET Points=(
CASE
	WHEN Points >= 1000000 THEN (Points/1000000)
	WHEN Points >= 1000 THEN Points/1000
	WHEN Points >= 100 THEN Points/100
	ELSE Points
END)
WHERE Description <> 'Increase Balance by Payment'
");

            migrationBuilder.Sql(@"
WITH CalculatedBalances AS (
    SELECT Id, SUM(CASE WHEN IsDebit = 1 THEN -Points ELSE Points END) 
        OVER (PARTITION BY UserId ORDER BY PreviousTransactionId) AS NewBalance
    FROM Transactions
)
UPDATE t
SET t.CurrentBalance = cb.NewBalance
FROM Transactions t
INNER JOIN CalculatedBalances cb ON t.Id = cb.Id;
");

            migrationBuilder.Sql(@"
WITH CalculatedBalances AS (
    SELECT UserId, CurrentBalance, ROW_NUMBER() OVER (PARTITION BY UserId ORDER BY CreationDate DESC) AS RN
    FROM Transactions
)
UPDATE u
SET u.CurrentBalance = cb.CurrentBalance
FROM ApplicationUsers u
INNER JOIN (SELECT * FROM CalculatedBalances WHERE RN=1) cb ON u.Id = cb.UserId
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AvatarId",
                table: "ApplicationUsers");
        }
    }
}
