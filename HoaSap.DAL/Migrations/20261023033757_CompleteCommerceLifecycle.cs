using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HoaSap.DAL.Migrations
{
    /// <inheritdoc />
    public partial class CompleteCommerceLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ShippingWeightGrams",
                table: "Products",
                type: "int",
                nullable: false,
                defaultValue: 500);

            migrationBuilder.AddColumn<Guid>(
                name: "ShippingQuoteId",
                table: "Orders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CustomOrderRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    ReferenceImageUrl = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    RequestedCompletionDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ProductionStartedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomOrderRequests", x => x.Id);
                    table.CheckConstraint("CK_CustomRequest_Quantity", "Quantity > 0");
                });

            migrationBuilder.CreateTable(
                name: "GatewayPaymentIntents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ProviderKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    ReturnUrlOrigin = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    RedirectUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    TransactionCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ReconciliationStatus = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    EvidenceReference = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ProviderResponse = table.Column<string>(type: "nvarchar(max)", maxLength: 10000, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GatewayPaymentIntents", x => x.Id);
                    table.CheckConstraint("CK_GatewayIntent_Amount", "Amount > 0");
                    table.ForeignKey(
                        name: "FK_GatewayPaymentIntents_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GatewayRefundAttempt",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RefundId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ProviderTransactionCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GatewayRefundAttempt", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GatewayRefundAttempt_Payments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "Payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GatewayRefundAttempt_Refunds_RefundId",
                        column: x => x.RefundId,
                        principalTable: "Refunds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NotificationOutbox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Destination = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: true),
                    Channel = table.Column<int>(type: "int", nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    NextAttemptAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LeasedUntilUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LeaseOwner = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SentAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationOutbox", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PasswordResetToken",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TokenHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UsedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PasswordResetToken", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RateLimitCounters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BucketKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    WindowStartUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Count = table.Column<int>(type: "int", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RateLimitCounters", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RefreshToken",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TokenHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    FamilyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RevokedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReplacedByHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    RevocationReason = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefreshToken", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ShippingQuotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CartFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Origin = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Destination = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Weight = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TrackingNumber = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UsedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShippingQuotes", x => x.Id);
                    table.CheckConstraint("CK_ShippingQuotes_Amounts", "Weight > 0 AND Amount >= 0");
                });

            migrationBuilder.CreateTable(
                name: "CustomQuotations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomOrderRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CurrentRevision = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomQuotations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomQuotations_CustomOrderRequests_CustomOrderRequestId",
                        column: x => x.CustomOrderRequestId,
                        principalTable: "CustomOrderRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CustomOrderCheckouts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomQuotationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuotationVersion = table.Column<int>(type: "int", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomOrderCheckouts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomOrderCheckouts_CustomQuotations_CustomQuotationId",
                        column: x => x.CustomQuotationId,
                        principalTable: "CustomQuotations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomOrderCheckouts_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CustomQuotationRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomQuotationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    DepositAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CompletionDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AdminNote = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomQuotationRevisions", x => x.Id);
                    table.CheckConstraint("CK_CustomQuote_Amounts", "UnitPrice >= 0 AND TotalAmount >= 0 AND DepositAmount >= 0 AND CAST(DepositAmount AS DECIMAL(18,2)) <= CAST(TotalAmount AS DECIMAL(18,2)) AND Quantity > 0");
                    table.ForeignKey(
                        name: "FK_CustomQuotationRevisions_CustomQuotations_CustomQuotationId",
                        column: x => x.CustomQuotationId,
                        principalTable: "CustomQuotations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Product_ShippingWeight",
                table: "Products",
                sql: "ShippingWeightGrams > 0");

            migrationBuilder.CreateIndex(
                name: "IX_CustomOrderCheckouts_CustomerId_IdempotencyKey",
                table: "CustomOrderCheckouts",
                columns: new[] { "CustomerId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomOrderCheckouts_CustomQuotationId",
                table: "CustomOrderCheckouts",
                column: "CustomQuotationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomOrderCheckouts_OrderId",
                table: "CustomOrderCheckouts",
                column: "OrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomOrderRequests_CustomerId_CreatedAt",
                table: "CustomOrderRequests",
                columns: new[] { "CustomerId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomQuotationRevisions_CustomQuotationId_Version",
                table: "CustomQuotationRevisions",
                columns: new[] { "CustomQuotationId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomQuotations_CustomOrderRequestId",
                table: "CustomQuotations",
                column: "CustomOrderRequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GatewayPaymentIntents_OrderId_ClientKey",
                table: "GatewayPaymentIntents",
                columns: new[] { "OrderId", "ClientKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GatewayPaymentIntents_OrderId_OwnerId",
                table: "GatewayPaymentIntents",
                columns: new[] { "OrderId", "OwnerId" });

            migrationBuilder.CreateIndex(
                name: "IX_GatewayPaymentIntents_ProviderKey",
                table: "GatewayPaymentIntents",
                column: "ProviderKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GatewayPaymentIntents_TransactionCode",
                table: "GatewayPaymentIntents",
                column: "TransactionCode",
                unique: true,
                filter: "[TransactionCode] <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_GatewayRefundAttempt_PaymentId",
                table: "GatewayRefundAttempt",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_GatewayRefundAttempt_RefundId",
                table: "GatewayRefundAttempt",
                column: "RefundId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NotificationOutbox_LeasedUntilUtc",
                table: "NotificationOutbox",
                column: "LeasedUntilUtc");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationOutbox_Status_NextAttemptAtUtc",
                table: "NotificationOutbox",
                columns: new[] { "Status", "NextAttemptAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PasswordResetToken_CustomerId_ExpiresAtUtc",
                table: "PasswordResetToken",
                columns: new[] { "CustomerId", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PasswordResetToken_TokenHash",
                table: "PasswordResetToken",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RateLimitCounters_BucketKey_WindowStartUtc",
                table: "RateLimitCounters",
                columns: new[] { "BucketKey", "WindowStartUtc" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RateLimitCounters_UpdatedAtUtc",
                table: "RateLimitCounters",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshToken_CustomerId_FamilyId",
                table: "RefreshToken",
                columns: new[] { "CustomerId", "FamilyId" });

            migrationBuilder.CreateIndex(
                name: "IX_RefreshToken_TokenHash",
                table: "RefreshToken",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShippingQuotes_CustomerId_Id",
                table: "ShippingQuotes",
                columns: new[] { "CustomerId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ShippingQuotes_OrderId",
                table: "ShippingQuotes",
                column: "OrderId",
                unique: true,
                filter: "[OrderId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomOrderCheckouts");

            migrationBuilder.DropTable(
                name: "CustomQuotationRevisions");

            migrationBuilder.DropTable(
                name: "GatewayPaymentIntents");

            migrationBuilder.DropTable(
                name: "GatewayRefundAttempt");

            migrationBuilder.DropTable(
                name: "NotificationOutbox");

            migrationBuilder.DropTable(
                name: "PasswordResetToken");

            migrationBuilder.DropTable(
                name: "RateLimitCounters");

            migrationBuilder.DropTable(
                name: "RefreshToken");

            migrationBuilder.DropTable(
                name: "ShippingQuotes");

            migrationBuilder.DropTable(
                name: "CustomQuotations");

            migrationBuilder.DropTable(
                name: "CustomOrderRequests");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Product_ShippingWeight",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "ShippingWeightGrams",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "ShippingQuoteId",
                table: "Orders");
        }
    }
}
