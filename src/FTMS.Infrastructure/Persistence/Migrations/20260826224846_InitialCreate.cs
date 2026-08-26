using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace FTMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TransactionStatuses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StatusName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransactionStatuses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Transactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransactionDate = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    TransactionType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    TransactionStatusId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CurrencyCode = table.Column<string>(type: "char(3)", nullable: false, defaultValue: "ZAR"),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Transactions", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                    table.CheckConstraint("CK_Transactions_Amount", "[Amount] >= 0");
                    table.ForeignKey(
                        name: "FK_Transactions_TransactionStatuses",
                        column: x => x.TransactionStatusId,
                        principalTable: "TransactionStatuses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TransactionAudits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransactionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChangeType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    OldValues = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NewValues = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ChangedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ChangedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransactionAudits", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                    table.ForeignKey(
                        name: "FK_TransactionAudits_Transactions",
                        column: x => x.TransactionId,
                        principalTable: "Transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "TransactionStatuses",
                columns: new[] { "Id", "StatusName" },
                values: new object[,]
                {
                    { new Guid("a1b2c3d4-0001-4000-8000-000000000001"), "Active" },
                    { new Guid("a1b2c3d4-0002-4000-8000-000000000002"), "Inactive" },
                    { new Guid("a1b2c3d4-0003-4000-8000-000000000003"), "Pending" },
                    { new Guid("a1b2c3d4-0004-4000-8000-000000000004"), "Completed" },
                    { new Guid("a1b2c3d4-0005-4000-8000-000000000005"), "Cancelled" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_TransactionAudits_TransactionId",
                table: "TransactionAudits",
                columns: new[] { "TransactionId", "ChangedAtUtc" })
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "UQ_TransactionStatuses_StatusName",
                table: "TransactionStatuses",
                column: "StatusName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_CreatedAtUtc",
                table: "Transactions",
                columns: new[] { "CreatedAtUtc", "Id" })
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_TransactionDate",
                table: "Transactions",
                column: "TransactionDate",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_TransactionStatusId",
                table: "Transactions",
                column: "TransactionStatusId");

            // design: doc 07 section 3 - one covering filtered index on Active transactions,
            // because the overwhelming majority of list calls ask for Active only. The INCLUDE
            // list makes the default page query covering, so listing active transactions never
            // touches the base table at all.
            //
            // Written as raw SQL rather than through the fluent API for two reasons: the
            // INCLUDE list contains Amount and CurrencyCode, which belong to the owned Money
            // type, and the filter needs the literal seeded status GUID. The fixed GUIDs from
            // doc 02 section 4 are what make a filtered index like this possible at all -
            // another payoff of deterministic seeding.
            migrationBuilder.Sql("""
                CREATE NONCLUSTERED INDEX IX_Transactions_Active_Date
                    ON dbo.Transactions (TransactionDate DESC)
                    INCLUDE (TransactionType, Amount, CurrencyCode, CreatedAtUtc, ModifiedAtUtc)
                    WHERE TransactionStatusId = 'a1b2c3d4-0001-4000-8000-000000000001';
                """);

            // TODO design: doc 06 section 5.3 and decision 6 - TransactionAudits becomes a SQL
            // Server 2022 append only ledger table for cryptographic tamper evidence. That
            // needs WITH (LEDGER = ON (APPEND_ONLY = ON)) at CREATE TABLE time, which EF Core
            // cannot emit, so it lands as its own hand written migration that recreates the
            // table. Deliberately not folded in here: it changes the table's physical
            // definition and deserves its own reviewable, separately testable change.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_Transactions_Active_Date ON dbo.Transactions;");

            migrationBuilder.DropTable(
                name: "TransactionAudits");

            migrationBuilder.DropTable(
                name: "Transactions");

            migrationBuilder.DropTable(
                name: "TransactionStatuses");
        }
    }
}
