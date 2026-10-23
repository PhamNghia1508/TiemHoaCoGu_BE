-- Schema generated from commit 13d90e6; synthetic QA records only.
BEGIN TRANSACTION;
CREATE TABLE "Addresses" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_Addresses" PRIMARY KEY,
    "CustomerId" TEXT NOT NULL,
    "ReceiverName" TEXT NOT NULL,
    "ReceiverPhone" TEXT NOT NULL,
    "DetailAddress" TEXT NOT NULL,
    "Ward" TEXT NOT NULL,
    "District" TEXT NOT NULL,
    "Province" TEXT NOT NULL,
    "IsDefault" INTEGER NOT NULL,
    CONSTRAINT "FK_Addresses_Customers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES "Customers" ("Id") ON DELETE RESTRICT
);
CREATE TABLE "AuditLogs" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_AuditLogs" PRIMARY KEY,
    "EntityType" TEXT NOT NULL,
    "EntityId" TEXT NOT NULL,
    "Action" TEXT NOT NULL,
    "OldValuesJson" TEXT NOT NULL,
    "NewValuesJson" TEXT NOT NULL,
    "OccurredAtUtc" TEXT NOT NULL,
    "ActorId" TEXT NULL,
    "ActorRole" TEXT NULL,
    "TraceId" TEXT NULL
);
INSERT INTO "AuditLogs" VALUES('14AC84BD-8139-4BCD-B7FA-337208509006','Category','22222222-2222-2222-2222-222222222222','Added','{}','{"Code":"LEGACY","Name":"Legacy category"}','2026-10-23 03:43:28.7966114',NULL,NULL,NULL);
INSERT INTO "AuditLogs" VALUES('1DBED684-FA84-499B-A9EE-ABC76A79DF7B','Order','55555555-5555-5555-5555-555555555555','Added','{}','{"OrderCode":"LEGACY-ORD","VoucherId":"44444444-4444-4444-4444-444444444444","SubTotal":700000,"DiscountAmount":10000,"ShippingFee":30000,"TotalAmount":720000,"OrderStatus":"Confirmed","PaymentStatus":"PartiallyPaid"}','2026-10-23 03:43:28.8074913',NULL,NULL,NULL);
INSERT INTO "AuditLogs" VALUES('4552C9A5-D997-4C69-9111-1E4599B84515','Voucher','44444444-4444-4444-4444-444444444444','Added','{}','{"Code":"LEGACY-V","DiscountType":"Fixed","DiscountValue":10000,"MinOrderAmount":0,"MaxDiscountAmount":null,"StartDate":"2026-10-22T03:43:28.402882Z","EndDate":"2026-10-24T03:43:28.4029117Z","UsageLimit":10,"UsedCount":1,"IsActive":true}','2026-10-23 03:43:28.8073707',NULL,NULL,NULL);
INSERT INTO "AuditLogs" VALUES('4ED839E4-8430-479B-A006-FFF36B0F3E0E','Product','33333333-3333-3333-3333-333333333333','Added','{}','{"CategoryId":"22222222-2222-2222-2222-222222222222","Code":"LEGACY-P","Name":"Legacy bouquet","Price":350000,"OriginalPrice":null,"Badge":"","Section":"","IsCustomizable":false,"IsPreOrder":false,"IsActive":true}','2026-10-23 03:43:28.8032842',NULL,NULL,NULL);
INSERT INTO "AuditLogs" VALUES('64AD0F55-C828-479E-BF3C-8BB8530816F1','Payment','39a925f4-5490-484f-87fe-79bffbc5b751','Added','{}','{"OrderId":"55555555-5555-5555-5555-555555555555","BookingId":null,"TransactionCode":"LEGACY-PAY","PaymentType":"Order","PaymentMethod":"BankTransfer","Amount":150000,"PaymentStatus":"Paid","PaidAt":"2026-10-23T03:43:28.403909Z"}','2026-10-23 03:43:28.807541',NULL,NULL,NULL);
INSERT INTO "AuditLogs" VALUES('71CA93B7-5D61-4FEB-A607-F1310635041B','Customer','11111111-1111-1111-1111-111111111111','Added','{}','{"Role":"Customer","IsActive":true,"IsGuest":false,"CreatedAt":"2026-10-23T03:43:28.4018765Z"}','2026-10-23 03:43:28.7962089',NULL,NULL,NULL);
INSERT INTO "AuditLogs" VALUES('E4279706-CA5E-4CFE-8023-AC91EA10F087','Inventory','d70acad4-15e4-45fe-bcf1-1ab6f2ff36a9','Added','{}','{"ProductId":"33333333-3333-3333-3333-333333333333","QuantityAvailable":8,"QuantityReserved":2,"LastUpdated":"2026-10-23T03:43:28.4040021Z"}','2026-10-23 03:43:28.807561',NULL,NULL,NULL);
CREATE TABLE "Categories" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_Categories" PRIMARY KEY,
    "Code" TEXT NOT NULL,
    "Name" TEXT NOT NULL,
    "Description" TEXT NOT NULL,
    "ImageUrl" TEXT NOT NULL DEFAULT ''
);
INSERT INTO "Categories" VALUES('22222222-2222-2222-2222-222222222222','LEGACY','Legacy category','','');
CREATE TABLE "Customers" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_Customers" PRIMARY KEY,
    "FullName" TEXT NOT NULL,
    "PhoneNumber" TEXT NOT NULL,
    "Email" TEXT NOT NULL,
    "PasswordHash" TEXT NOT NULL,
    "Role" TEXT NOT NULL,
    "IsGuest" INTEGER NOT NULL,
    "IsActive" INTEGER NOT NULL DEFAULT 1,
    "CreatedAt" TEXT NOT NULL
);
INSERT INTO "Customers" VALUES('11111111-1111-1111-1111-111111111111','Legacy QA Customer','0900000011','legacy-seeded@example.test','not-a-real-credential','Customer',0,1,'2026-10-23 03:43:28.4018765');
CREATE TABLE "Deliveries" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_Deliveries" PRIMARY KEY,
    "OrderId" TEXT NOT NULL,
    "DeliveryCode" TEXT NOT NULL,
    "ShipperUnit" TEXT NOT NULL,
    "TrackingNumber" TEXT NOT NULL,
    "ShippingCost" TEXT NOT NULL,
    "DeliveryStatus" TEXT NOT NULL,
    "ShippedAt" TEXT NULL,
    "DeliveredAt" TEXT NULL,
    "Note" TEXT NOT NULL,
    CONSTRAINT "FK_Deliveries_Orders_OrderId" FOREIGN KEY ("OrderId") REFERENCES "Orders" ("Id") ON DELETE RESTRICT
);
CREATE TABLE "DeliveryItems" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_DeliveryItems" PRIMARY KEY,
    "DeliveryId" TEXT NOT NULL,
    "OrderItemId" TEXT NOT NULL,
    "ShippedQuantity" INTEGER NOT NULL,
    CONSTRAINT "FK_DeliveryItems_Deliveries_DeliveryId" FOREIGN KEY ("DeliveryId") REFERENCES "Deliveries" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_DeliveryItems_OrderItems_OrderItemId" FOREIGN KEY ("OrderItemId") REFERENCES "OrderItems" ("Id") ON DELETE RESTRICT
);
CREATE TABLE "Instructors" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_Instructors" PRIMARY KEY,
    "FullName" TEXT NOT NULL,
    "Bio" TEXT NOT NULL,
    "AvatarUrl" TEXT NOT NULL,
    "PhoneNumber" TEXT NOT NULL,
    "Email" TEXT NOT NULL
);
CREATE TABLE "Inventories" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_Inventories" PRIMARY KEY,
    "ProductId" TEXT NOT NULL,
    "QuantityAvailable" INTEGER NOT NULL,
    "QuantityReserved" INTEGER NOT NULL,
    "LastUpdated" TEXT NOT NULL,
    "ConcurrencyToken" TEXT NOT NULL,
    CONSTRAINT "CK_Inventory_Quantities" CHECK (QuantityAvailable >= 0 AND QuantityReserved >= 0),
    CONSTRAINT "FK_Inventories_Products_ProductId" FOREIGN KEY ("ProductId") REFERENCES "Products" ("Id") ON DELETE RESTRICT
);
INSERT INTO "Inventories" VALUES('D70ACAD4-15E4-45FE-BCF1-1AB6F2FF36A9','33333333-3333-3333-3333-333333333333',8,2,'2026-10-23 03:43:28.4040021','0996D69A-70B2-4267-813B-C8818D0013E8');
CREATE TABLE "OrderItems" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_OrderItems" PRIMARY KEY,
    "OrderId" TEXT NOT NULL,
    "ProductId" TEXT NOT NULL,
    "Quantity" INTEGER NOT NULL,
    "UnitPrice" TEXT NOT NULL,
    "FinalPrice" TEXT NOT NULL,
    "CustomNote" TEXT NOT NULL,
    "ReferenceImageUrl" TEXT NOT NULL,
    "AdminPriceNote" TEXT NOT NULL,
    CONSTRAINT "CK_OrderItem_Quantities" CHECK (Quantity > 0 AND UnitPrice >= 0 AND FinalPrice >= 0),
    CONSTRAINT "FK_OrderItems_Orders_OrderId" FOREIGN KEY ("OrderId") REFERENCES "Orders" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_OrderItems_Products_ProductId" FOREIGN KEY ("ProductId") REFERENCES "Products" ("Id") ON DELETE RESTRICT
);
INSERT INTO "OrderItems" VALUES('FE5769E3-42CB-4863-9F48-CE8BC9AF4DC4','55555555-5555-5555-5555-555555555555','33333333-3333-3333-3333-333333333333',2,'350000.0','350000.0','','','');
CREATE TABLE "Orders" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_Orders" PRIMARY KEY,
    "IdempotencyKey" TEXT NULL,
    "RequestFingerprint" TEXT NULL,
    "CustomerId" TEXT NULL,
    "VoucherId" TEXT NULL,
    "OrderCode" TEXT NOT NULL,
    "SubTotal" TEXT NOT NULL,
    "DiscountAmount" TEXT NOT NULL,
    "ShippingFee" TEXT NOT NULL,
    "TotalAmount" TEXT NOT NULL,
    "OrderStatus" TEXT NOT NULL,
    "PaymentStatus" TEXT NOT NULL,
    "Note" TEXT NOT NULL,
    "ShippingAddress" TEXT NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    "ConcurrencyToken" TEXT NOT NULL,
    CONSTRAINT "CK_Order_Amounts" CHECK (SubTotal >= 0 AND DiscountAmount >= 0 AND ShippingFee >= 0 AND TotalAmount >= 0),
    CONSTRAINT "FK_Orders_Customers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES "Customers" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_Orders_Vouchers_VoucherId" FOREIGN KEY ("VoucherId") REFERENCES "Vouchers" ("Id") ON DELETE RESTRICT
);
INSERT INTO "Orders" VALUES('55555555-5555-5555-5555-555555555555',NULL,NULL,'11111111-1111-1111-1111-111111111111','44444444-4444-4444-4444-444444444444','LEGACY-ORD','700000.0','10000.0','30000.0','720000.0','Confirmed','PartiallyPaid','','Legacy QA address','2026-10-23 03:43:28.4030478','A823D8C9-9503-47CB-BDB4-F1833D779ADA');
CREATE TABLE "Payments" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_Payments" PRIMARY KEY,
    "RecordedBy" TEXT NULL,
    "OrderId" TEXT NULL,
    "BookingId" TEXT NULL,
    "TransactionCode" TEXT NOT NULL,
    "PaymentType" TEXT NOT NULL,
    "PaymentMethod" TEXT NOT NULL,
    "Amount" TEXT NOT NULL,
    "PaymentStatus" TEXT NOT NULL,
    "PaidAt" TEXT NULL,
    "PaymentGatewayResponse" TEXT NOT NULL,
    CONSTRAINT "CK_Payment_Amount" CHECK (Amount >= 0 AND ((OrderId IS NOT NULL AND BookingId IS NULL) OR (OrderId IS NULL AND BookingId IS NOT NULL))),
    CONSTRAINT "FK_Payments_Orders_OrderId" FOREIGN KEY ("OrderId") REFERENCES "Orders" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_Payments_WorkshopBookings_BookingId" FOREIGN KEY ("BookingId") REFERENCES "WorkshopBookings" ("Id") ON DELETE RESTRICT
);
INSERT INTO "Payments" VALUES('39A925F4-5490-484F-87FE-79BFFBC5B751',NULL,'55555555-5555-5555-5555-555555555555',NULL,'LEGACY-PAY','Order','BankTransfer','150000.0','Paid','2026-10-23 03:43:28.403909','');
CREATE TABLE "ProductImages" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_ProductImages" PRIMARY KEY,
    "ProductId" TEXT NOT NULL,
    "ImageUrl" TEXT NOT NULL,
    "DisplayOrder" INTEGER NOT NULL,
    CONSTRAINT "FK_ProductImages_Products_ProductId" FOREIGN KEY ("ProductId") REFERENCES "Products" ("Id") ON DELETE CASCADE
);
CREATE TABLE "Products" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_Products" PRIMARY KEY,
    "CategoryId" TEXT NOT NULL,
    "Code" TEXT NOT NULL,
    "Name" TEXT NOT NULL,
    "Price" TEXT NOT NULL,
    "OriginalPrice" TEXT NULL,
    "ThumbnailUrl" TEXT NOT NULL,
    "Description" TEXT NOT NULL,
    "Badge" TEXT NOT NULL,
    "Section" TEXT NOT NULL,
    "IsCustomizable" INTEGER NOT NULL,
    "IsPreOrder" INTEGER NOT NULL,
    "IsActive" INTEGER NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    CONSTRAINT "CK_Product_Price" CHECK (Price >= 0 AND (OriginalPrice IS NULL OR OriginalPrice >= 0)),
    CONSTRAINT "FK_Products_Categories_CategoryId" FOREIGN KEY ("CategoryId") REFERENCES "Categories" ("Id") ON DELETE RESTRICT
);
INSERT INTO "Products" VALUES('33333333-3333-3333-3333-333333333333','22222222-2222-2222-2222-222222222222','LEGACY-P','Legacy bouquet','350000.0',NULL,'','','','',0,0,1,'2026-10-23 03:43:28.4024925');
CREATE TABLE "Refunds" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_Refunds" PRIMARY KEY,
    "ConcurrencyToken" TEXT NOT NULL,
    "TransactionCode" TEXT NULL,
    "RequestedAt" TEXT NOT NULL,
    "BookingId" TEXT NULL,
    "OrderId" TEXT NULL,
    "RefundAmount" TEXT NOT NULL,
    "RefundReason" TEXT NOT NULL,
    "RefundStatus" TEXT NOT NULL,
    "ProcessedAt" TEXT NULL,
    "ApprovedBy" TEXT NOT NULL,
    CONSTRAINT "CK_Refund_Target" CHECK (RefundAmount >= 0 AND ((OrderId IS NOT NULL AND BookingId IS NULL) OR (OrderId IS NULL AND BookingId IS NOT NULL))),
    CONSTRAINT "FK_Refunds_Orders_OrderId" FOREIGN KEY ("OrderId") REFERENCES "Orders" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_Refunds_WorkshopBookings_BookingId" FOREIGN KEY ("BookingId") REFERENCES "WorkshopBookings" ("Id") ON DELETE RESTRICT
);
CREATE TABLE "ShoppingCartItems" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_ShoppingCartItems" PRIMARY KEY,
    "CustomerId" TEXT NOT NULL,
    "ProductId" TEXT NOT NULL,
    "Quantity" INTEGER NOT NULL,
    "CustomNote" TEXT NOT NULL,
    "ReferenceImageUrl" TEXT NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    CONSTRAINT "FK_ShoppingCartItems_Customers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES "Customers" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_ShoppingCartItems_Products_ProductId" FOREIGN KEY ("ProductId") REFERENCES "Products" ("Id") ON DELETE RESTRICT
);
CREATE TABLE "Vouchers" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_Vouchers" PRIMARY KEY,
    "Code" TEXT NOT NULL,
    "Description" TEXT NOT NULL,
    "DiscountType" TEXT NOT NULL,
    "DiscountValue" TEXT NOT NULL,
    "MinOrderAmount" TEXT NOT NULL,
    "MaxDiscountAmount" TEXT NULL,
    "StartDate" TEXT NOT NULL,
    "EndDate" TEXT NOT NULL,
    "UsageLimit" INTEGER NOT NULL,
    "UsedCount" INTEGER NOT NULL,
    "IsActive" INTEGER NOT NULL
);
INSERT INTO "Vouchers" VALUES('44444444-4444-4444-4444-444444444444','LEGACY-V','','Fixed','10000.0','0.0',NULL,'2026-10-22 03:43:28.402882','2026-10-24 03:43:28.4029117',10,1,1);
CREATE TABLE "WorkshopBookings" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_WorkshopBookings" PRIMARY KEY,
    "IdempotencyKey" TEXT NULL,
    "RequestFingerprint" TEXT NULL,
    "SessionId" TEXT NOT NULL,
    "CustomerId" TEXT NULL,
    "BookingCode" TEXT NOT NULL,
    "CustomerName" TEXT NOT NULL,
    "CustomerPhone" TEXT NOT NULL,
    "CustomerEmail" TEXT NOT NULL,
    "TicketCount" INTEGER NOT NULL,
    "TotalAmount" TEXT NOT NULL,
    "DepositAmount" TEXT NOT NULL,
    "RemainingAmount" TEXT NOT NULL,
    "BookingStatus" TEXT NOT NULL,
    "HoldExpiresAt" TEXT NOT NULL,
    "CheckedInAt" TEXT NULL,
    "CancelReason" TEXT NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    "ConcurrencyToken" TEXT NOT NULL,
    CONSTRAINT "CK_Booking_Tickets" CHECK (TicketCount > 0 AND TotalAmount >= 0 AND DepositAmount >= 0 AND RemainingAmount >= 0),
    CONSTRAINT "FK_WorkshopBookings_Customers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES "Customers" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_WorkshopBookings_WorkshopSessions_SessionId" FOREIGN KEY ("SessionId") REFERENCES "WorkshopSessions" ("Id") ON DELETE RESTRICT
);
CREATE TABLE "WorkshopImages" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_WorkshopImages" PRIMARY KEY,
    "WorkshopId" TEXT NOT NULL,
    "ImageUrl" TEXT NOT NULL,
    "Caption" TEXT NOT NULL,
    "DisplayOrder" INTEGER NOT NULL,
    CONSTRAINT "FK_WorkshopImages_Workshops_WorkshopId" FOREIGN KEY ("WorkshopId") REFERENCES "Workshops" ("Id") ON DELETE CASCADE
);
CREATE TABLE "WorkshopSessions" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_WorkshopSessions" PRIMARY KEY,
    "WorkshopId" TEXT NOT NULL,
    "SessionDate" TEXT NOT NULL,
    "StartTime" TEXT NOT NULL,
    "EndTime" TEXT NOT NULL,
    "Location" TEXT NOT NULL,
    "Capacity" INTEGER NOT NULL,
    "BookedSlots" INTEGER NOT NULL,
    "SessionStatus" TEXT NOT NULL,
    "ConcurrencyToken" TEXT NOT NULL,
    CONSTRAINT "CK_Session_Capacity" CHECK (Capacity > 0 AND BookedSlots >= 0 AND BookedSlots <= Capacity),
    CONSTRAINT "FK_WorkshopSessions_Workshops_WorkshopId" FOREIGN KEY ("WorkshopId") REFERENCES "Workshops" ("Id") ON DELETE RESTRICT
);
CREATE TABLE "Workshops" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_Workshops" PRIMARY KEY,
    "InstructorId" TEXT NULL,
    "Code" TEXT NOT NULL,
    "Name" TEXT NOT NULL,
    "ShortName" TEXT NOT NULL,
    "Category" TEXT NOT NULL,
    "BasePrice" TEXT NOT NULL,
    "Duration" INTEGER NOT NULL,
    "MaxCapacity" INTEGER NOT NULL,
    "Description" TEXT NOT NULL,
    "LeadContent" TEXT NOT NULL,
    "GoalsJson" TEXT NOT NULL,
    "IncludesJson" TEXT NOT NULL,
    "ForWho" TEXT NOT NULL,
    "IsFeatured" INTEGER NOT NULL,
    "IsActive" INTEGER NOT NULL,
    CONSTRAINT "FK_Workshops_Instructors_InstructorId" FOREIGN KEY ("InstructorId") REFERENCES "Instructors" ("Id") ON DELETE RESTRICT
);
CREATE INDEX "IX_Addresses_CustomerId" ON "Addresses" ("CustomerId");
CREATE INDEX "IX_AuditLogs_EntityType_EntityId" ON "AuditLogs" ("EntityType", "EntityId");
CREATE INDEX "IX_AuditLogs_OccurredAtUtc" ON "AuditLogs" ("OccurredAtUtc");
CREATE UNIQUE INDEX "IX_Categories_Code" ON "Categories" ("Code");
CREATE UNIQUE INDEX "IX_Customers_Email" ON "Customers" ("Email");
CREATE INDEX "IX_Customers_PhoneNumber" ON "Customers" ("PhoneNumber");
CREATE UNIQUE INDEX "IX_Deliveries_DeliveryCode" ON "Deliveries" ("DeliveryCode");
CREATE INDEX "IX_Deliveries_OrderId" ON "Deliveries" ("OrderId");
CREATE INDEX "IX_DeliveryItems_DeliveryId" ON "DeliveryItems" ("DeliveryId");
CREATE INDEX "IX_DeliveryItems_OrderItemId" ON "DeliveryItems" ("OrderItemId");
CREATE UNIQUE INDEX "IX_Inventories_ProductId" ON "Inventories" ("ProductId");
CREATE INDEX "IX_OrderItems_OrderId" ON "OrderItems" ("OrderId");
CREATE INDEX "IX_OrderItems_ProductId" ON "OrderItems" ("ProductId");
CREATE INDEX "IX_Orders_CustomerId_CreatedAt" ON "Orders" ("CustomerId", "CreatedAt");
CREATE UNIQUE INDEX "IX_Orders_CustomerId_IdempotencyKey" ON "Orders" ("CustomerId", "IdempotencyKey") WHERE [CustomerId] IS NOT NULL AND [IdempotencyKey] IS NOT NULL;
CREATE UNIQUE INDEX "IX_Orders_IdempotencyKey" ON "Orders" ("IdempotencyKey") WHERE [CustomerId] IS NULL AND [IdempotencyKey] IS NOT NULL;
CREATE UNIQUE INDEX "IX_Orders_OrderCode" ON "Orders" ("OrderCode");
CREATE INDEX "IX_Orders_VoucherId" ON "Orders" ("VoucherId");
CREATE INDEX "IX_Payments_BookingId" ON "Payments" ("BookingId");
CREATE INDEX "IX_Payments_OrderId" ON "Payments" ("OrderId");
CREATE UNIQUE INDEX "IX_Payments_TransactionCode" ON "Payments" ("TransactionCode");
CREATE INDEX "IX_ProductImages_ProductId_DisplayOrder" ON "ProductImages" ("ProductId", "DisplayOrder");
CREATE INDEX "IX_Products_CategoryId" ON "Products" ("CategoryId");
CREATE UNIQUE INDEX "IX_Products_Code" ON "Products" ("Code");
CREATE INDEX "IX_Refunds_BookingId" ON "Refunds" ("BookingId");
CREATE INDEX "IX_Refunds_OrderId" ON "Refunds" ("OrderId");
CREATE UNIQUE INDEX "IX_Refunds_TransactionCode" ON "Refunds" ("TransactionCode") WHERE [TransactionCode] IS NOT NULL;
CREATE UNIQUE INDEX "IX_ShoppingCartItems_CustomerId_ProductId" ON "ShoppingCartItems" ("CustomerId", "ProductId");
CREATE INDEX "IX_ShoppingCartItems_ProductId" ON "ShoppingCartItems" ("ProductId");
CREATE UNIQUE INDEX "IX_Vouchers_Code" ON "Vouchers" ("Code");
CREATE UNIQUE INDEX "IX_WorkshopBookings_BookingCode" ON "WorkshopBookings" ("BookingCode");
CREATE INDEX "IX_WorkshopBookings_BookingStatus_HoldExpiresAt" ON "WorkshopBookings" ("BookingStatus", "HoldExpiresAt");
CREATE UNIQUE INDEX "IX_WorkshopBookings_CustomerId_IdempotencyKey" ON "WorkshopBookings" ("CustomerId", "IdempotencyKey") WHERE [CustomerId] IS NOT NULL AND [IdempotencyKey] IS NOT NULL;
CREATE UNIQUE INDEX "IX_WorkshopBookings_IdempotencyKey" ON "WorkshopBookings" ("IdempotencyKey") WHERE [CustomerId] IS NULL AND [IdempotencyKey] IS NOT NULL;
CREATE INDEX "IX_WorkshopBookings_SessionId_BookingStatus" ON "WorkshopBookings" ("SessionId", "BookingStatus");
CREATE INDEX "IX_WorkshopImages_WorkshopId_DisplayOrder" ON "WorkshopImages" ("WorkshopId", "DisplayOrder");
CREATE UNIQUE INDEX "IX_Workshops_Code" ON "Workshops" ("Code");
CREATE INDEX "IX_Workshops_InstructorId" ON "Workshops" ("InstructorId");
CREATE UNIQUE INDEX "IX_WorkshopSessions_WorkshopId_SessionDate_StartTime" ON "WorkshopSessions" ("WorkshopId", "SessionDate", "StartTime");
COMMIT;
