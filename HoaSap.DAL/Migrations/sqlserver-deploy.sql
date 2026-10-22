IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022090137_InitialProduction'
)
BEGIN
    CREATE TABLE [Categories] (
        [Id] uniqueidentifier NOT NULL,
        [Code] nvarchar(450) NOT NULL,
        [Name] nvarchar(max) NOT NULL,
        [Description] nvarchar(max) NOT NULL,
        CONSTRAINT [PK_Categories] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022090137_InitialProduction'
)
BEGIN
    CREATE TABLE [Customers] (
        [Id] uniqueidentifier NOT NULL,
        [FullName] nvarchar(max) NOT NULL,
        [PhoneNumber] nvarchar(450) NOT NULL,
        [Email] nvarchar(450) NOT NULL,
        [PasswordHash] nvarchar(max) NOT NULL,
        [Role] nvarchar(max) NOT NULL,
        [IsGuest] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_Customers] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022090137_InitialProduction'
)
BEGIN
    CREATE TABLE [Instructors] (
        [Id] uniqueidentifier NOT NULL,
        [FullName] nvarchar(max) NOT NULL,
        [Bio] nvarchar(max) NOT NULL,
        [AvatarUrl] nvarchar(max) NOT NULL,
        [PhoneNumber] nvarchar(max) NOT NULL,
        [Email] nvarchar(max) NOT NULL,
        CONSTRAINT [PK_Instructors] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022090137_InitialProduction'
)
BEGIN
    CREATE TABLE [Vouchers] (
        [Id] uniqueidentifier NOT NULL,
        [Code] nvarchar(450) NOT NULL,
        [Description] nvarchar(max) NOT NULL,
        [DiscountType] nvarchar(max) NOT NULL,
        [DiscountValue] decimal(18,2) NOT NULL,
        [MinOrderAmount] decimal(18,2) NOT NULL,
        [MaxDiscountAmount] decimal(18,2) NULL,
        [StartDate] datetime2 NOT NULL,
        [EndDate] datetime2 NOT NULL,
        [UsageLimit] int NOT NULL,
        [UsedCount] int NOT NULL,
        [IsActive] bit NOT NULL,
        CONSTRAINT [PK_Vouchers] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022090137_InitialProduction'
)
BEGIN
    CREATE TABLE [Products] (
        [Id] uniqueidentifier NOT NULL,
        [CategoryId] uniqueidentifier NOT NULL,
        [Code] nvarchar(450) NOT NULL,
        [Name] nvarchar(max) NOT NULL,
        [Price] decimal(18,2) NOT NULL,
        [OriginalPrice] decimal(18,2) NULL,
        [ThumbnailUrl] nvarchar(max) NOT NULL,
        [Description] nvarchar(max) NOT NULL,
        [Badge] nvarchar(max) NOT NULL,
        [Section] nvarchar(max) NOT NULL,
        [IsCustomizable] bit NOT NULL,
        [IsPreOrder] bit NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_Products] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Product_Price] CHECK (Price >= 0 AND (OriginalPrice IS NULL OR OriginalPrice >= 0)),
        CONSTRAINT [FK_Products_Categories_CategoryId] FOREIGN KEY ([CategoryId]) REFERENCES [Categories] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022090137_InitialProduction'
)
BEGIN
    CREATE TABLE [Addresses] (
        [Id] uniqueidentifier NOT NULL,
        [CustomerId] uniqueidentifier NOT NULL,
        [ReceiverName] nvarchar(max) NOT NULL,
        [ReceiverPhone] nvarchar(max) NOT NULL,
        [DetailAddress] nvarchar(max) NOT NULL,
        [Ward] nvarchar(max) NOT NULL,
        [District] nvarchar(max) NOT NULL,
        [Province] nvarchar(max) NOT NULL,
        [IsDefault] bit NOT NULL,
        CONSTRAINT [PK_Addresses] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Addresses_Customers_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [Customers] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022090137_InitialProduction'
)
BEGIN
    CREATE TABLE [Workshops] (
        [Id] uniqueidentifier NOT NULL,
        [InstructorId] uniqueidentifier NULL,
        [Code] nvarchar(450) NOT NULL,
        [Name] nvarchar(max) NOT NULL,
        [ShortName] nvarchar(max) NOT NULL,
        [Category] nvarchar(max) NOT NULL,
        [BasePrice] decimal(18,2) NOT NULL,
        [Duration] int NOT NULL,
        [MaxCapacity] int NOT NULL,
        [Description] nvarchar(max) NOT NULL,
        [LeadContent] nvarchar(max) NOT NULL,
        [GoalsJson] nvarchar(max) NOT NULL,
        [IncludesJson] nvarchar(max) NOT NULL,
        [ForWho] nvarchar(max) NOT NULL,
        [IsFeatured] bit NOT NULL,
        [IsActive] bit NOT NULL,
        CONSTRAINT [PK_Workshops] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Workshops_Instructors_InstructorId] FOREIGN KEY ([InstructorId]) REFERENCES [Instructors] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022090137_InitialProduction'
)
BEGIN
    CREATE TABLE [Orders] (
        [Id] uniqueidentifier NOT NULL,
        [IdempotencyKey] nvarchar(100) NULL,
        [RequestFingerprint] nvarchar(64) NULL,
        [CustomerId] uniqueidentifier NULL,
        [VoucherId] uniqueidentifier NULL,
        [OrderCode] nvarchar(450) NOT NULL,
        [SubTotal] decimal(18,2) NOT NULL,
        [DiscountAmount] decimal(18,2) NOT NULL,
        [ShippingFee] decimal(18,2) NOT NULL,
        [TotalAmount] decimal(18,2) NOT NULL,
        [OrderStatus] nvarchar(max) NOT NULL,
        [PaymentStatus] nvarchar(max) NOT NULL,
        [Note] nvarchar(max) NOT NULL,
        [ShippingAddress] nvarchar(max) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [ConcurrencyToken] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_Orders] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Order_Amounts] CHECK (SubTotal >= 0 AND DiscountAmount >= 0 AND ShippingFee >= 0 AND TotalAmount >= 0),
        CONSTRAINT [FK_Orders_Customers_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [Customers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Orders_Vouchers_VoucherId] FOREIGN KEY ([VoucherId]) REFERENCES [Vouchers] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022090137_InitialProduction'
)
BEGIN
    CREATE TABLE [Inventories] (
        [Id] uniqueidentifier NOT NULL,
        [ProductId] uniqueidentifier NOT NULL,
        [QuantityAvailable] int NOT NULL,
        [QuantityReserved] int NOT NULL,
        [LastUpdated] datetime2 NOT NULL,
        [ConcurrencyToken] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_Inventories] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Inventory_Quantities] CHECK (QuantityAvailable >= 0 AND QuantityReserved >= 0),
        CONSTRAINT [FK_Inventories_Products_ProductId] FOREIGN KEY ([ProductId]) REFERENCES [Products] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022090137_InitialProduction'
)
BEGIN
    CREATE TABLE [ProductImages] (
        [Id] uniqueidentifier NOT NULL,
        [ProductId] uniqueidentifier NOT NULL,
        [ImageUrl] nvarchar(max) NOT NULL,
        [DisplayOrder] int NOT NULL,
        CONSTRAINT [PK_ProductImages] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ProductImages_Products_ProductId] FOREIGN KEY ([ProductId]) REFERENCES [Products] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022090137_InitialProduction'
)
BEGIN
    CREATE TABLE [ShoppingCartItems] (
        [Id] uniqueidentifier NOT NULL,
        [CustomerId] uniqueidentifier NOT NULL,
        [ProductId] uniqueidentifier NOT NULL,
        [Quantity] int NOT NULL,
        [CustomNote] nvarchar(max) NOT NULL,
        [ReferenceImageUrl] nvarchar(max) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_ShoppingCartItems] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ShoppingCartItems_Customers_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [Customers] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_ShoppingCartItems_Products_ProductId] FOREIGN KEY ([ProductId]) REFERENCES [Products] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022090137_InitialProduction'
)
BEGIN
    CREATE TABLE [WorkshopImages] (
        [Id] uniqueidentifier NOT NULL,
        [WorkshopId] uniqueidentifier NOT NULL,
        [ImageUrl] nvarchar(max) NOT NULL,
        [Caption] nvarchar(max) NOT NULL,
        [DisplayOrder] int NOT NULL,
        CONSTRAINT [PK_WorkshopImages] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_WorkshopImages_Workshops_WorkshopId] FOREIGN KEY ([WorkshopId]) REFERENCES [Workshops] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022090137_InitialProduction'
)
BEGIN
    CREATE TABLE [WorkshopSessions] (
        [Id] uniqueidentifier NOT NULL,
        [WorkshopId] uniqueidentifier NOT NULL,
        [SessionDate] datetime2 NOT NULL,
        [StartTime] time NOT NULL,
        [EndTime] time NOT NULL,
        [Location] nvarchar(max) NOT NULL,
        [Capacity] int NOT NULL,
        [BookedSlots] int NOT NULL,
        [SessionStatus] nvarchar(max) NOT NULL,
        [ConcurrencyToken] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_WorkshopSessions] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Session_Capacity] CHECK (Capacity > 0 AND BookedSlots >= 0 AND BookedSlots <= Capacity),
        CONSTRAINT [FK_WorkshopSessions_Workshops_WorkshopId] FOREIGN KEY ([WorkshopId]) REFERENCES [Workshops] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022090137_InitialProduction'
)
BEGIN
    CREATE TABLE [Deliveries] (
        [Id] uniqueidentifier NOT NULL,
        [OrderId] uniqueidentifier NOT NULL,
        [DeliveryCode] nvarchar(450) NOT NULL,
        [ShipperUnit] nvarchar(max) NOT NULL,
        [TrackingNumber] nvarchar(max) NOT NULL,
        [ShippingCost] decimal(18,2) NOT NULL,
        [DeliveryStatus] nvarchar(max) NOT NULL,
        [ShippedAt] datetime2 NULL,
        [DeliveredAt] datetime2 NULL,
        [Note] nvarchar(max) NOT NULL,
        CONSTRAINT [PK_Deliveries] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Deliveries_Orders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [Orders] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022090137_InitialProduction'
)
BEGIN
    CREATE TABLE [OrderItems] (
        [Id] uniqueidentifier NOT NULL,
        [OrderId] uniqueidentifier NOT NULL,
        [ProductId] uniqueidentifier NOT NULL,
        [Quantity] int NOT NULL,
        [UnitPrice] decimal(18,2) NOT NULL,
        [FinalPrice] decimal(18,2) NOT NULL,
        [CustomNote] nvarchar(max) NOT NULL,
        [ReferenceImageUrl] nvarchar(max) NOT NULL,
        [AdminPriceNote] nvarchar(max) NOT NULL,
        CONSTRAINT [PK_OrderItems] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_OrderItem_Quantities] CHECK (Quantity > 0 AND UnitPrice >= 0 AND FinalPrice >= 0),
        CONSTRAINT [FK_OrderItems_Orders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [Orders] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_OrderItems_Products_ProductId] FOREIGN KEY ([ProductId]) REFERENCES [Products] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022090137_InitialProduction'
)
BEGIN
    CREATE TABLE [WorkshopBookings] (
        [Id] uniqueidentifier NOT NULL,
        [IdempotencyKey] nvarchar(100) NULL,
        [RequestFingerprint] nvarchar(64) NULL,
        [SessionId] uniqueidentifier NOT NULL,
        [CustomerId] uniqueidentifier NULL,
        [BookingCode] nvarchar(450) NOT NULL,
        [CustomerName] nvarchar(max) NOT NULL,
        [CustomerPhone] nvarchar(max) NOT NULL,
        [CustomerEmail] nvarchar(max) NOT NULL,
        [TicketCount] int NOT NULL,
        [TotalAmount] decimal(18,2) NOT NULL,
        [DepositAmount] decimal(18,2) NOT NULL,
        [RemainingAmount] decimal(18,2) NOT NULL,
        [BookingStatus] nvarchar(450) NOT NULL,
        [HoldExpiresAt] datetime2 NOT NULL,
        [CheckedInAt] datetime2 NULL,
        [CancelReason] nvarchar(max) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [ConcurrencyToken] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_WorkshopBookings] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Booking_Tickets] CHECK (TicketCount > 0 AND TotalAmount >= 0 AND DepositAmount >= 0 AND RemainingAmount >= 0),
        CONSTRAINT [FK_WorkshopBookings_Customers_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [Customers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_WorkshopBookings_WorkshopSessions_SessionId] FOREIGN KEY ([SessionId]) REFERENCES [WorkshopSessions] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022090137_InitialProduction'
)
BEGIN
    CREATE TABLE [DeliveryItems] (
        [Id] uniqueidentifier NOT NULL,
        [DeliveryId] uniqueidentifier NOT NULL,
        [OrderItemId] uniqueidentifier NOT NULL,
        [ShippedQuantity] int NOT NULL,
        CONSTRAINT [PK_DeliveryItems] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_DeliveryItems_Deliveries_DeliveryId] FOREIGN KEY ([DeliveryId]) REFERENCES [Deliveries] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_DeliveryItems_OrderItems_OrderItemId] FOREIGN KEY ([OrderItemId]) REFERENCES [OrderItems] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022090137_InitialProduction'
)
BEGIN
    CREATE TABLE [Payments] (
        [Id] uniqueidentifier NOT NULL,
        [RecordedBy] uniqueidentifier NULL,
        [OrderId] uniqueidentifier NULL,
        [BookingId] uniqueidentifier NULL,
        [TransactionCode] nvarchar(100) NOT NULL,
        [PaymentType] nvarchar(max) NOT NULL,
        [PaymentMethod] nvarchar(max) NOT NULL,
        [Amount] decimal(18,2) NOT NULL,
        [PaymentStatus] nvarchar(max) NOT NULL,
        [PaidAt] datetime2 NULL,
        [PaymentGatewayResponse] nvarchar(max) NOT NULL,
        CONSTRAINT [PK_Payments] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Payment_Amount] CHECK (Amount >= 0 AND ((OrderId IS NOT NULL AND BookingId IS NULL) OR (OrderId IS NULL AND BookingId IS NOT NULL))),
        CONSTRAINT [FK_Payments_Orders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [Orders] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Payments_WorkshopBookings_BookingId] FOREIGN KEY ([BookingId]) REFERENCES [WorkshopBookings] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022090137_InitialProduction'
)
BEGIN
    CREATE TABLE [Refunds] (
        [Id] uniqueidentifier NOT NULL,
        [ConcurrencyToken] uniqueidentifier NOT NULL,
        [TransactionCode] nvarchar(100) NULL,
        [RequestedAt] datetime2 NOT NULL,
        [BookingId] uniqueidentifier NULL,
        [OrderId] uniqueidentifier NULL,
        [RefundAmount] decimal(18,2) NOT NULL,
        [RefundReason] nvarchar(max) NOT NULL,
        [RefundStatus] nvarchar(max) NOT NULL,
        [ProcessedAt] datetime2 NULL,
        [ApprovedBy] nvarchar(max) NOT NULL,
        CONSTRAINT [PK_Refunds] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Refund_Target] CHECK (RefundAmount >= 0 AND ((OrderId IS NOT NULL AND BookingId IS NULL) OR (OrderId IS NULL AND BookingId IS NOT NULL))),
        CONSTRAINT [FK_Refunds_Orders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [Orders] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Refunds_WorkshopBookings_BookingId] FOREIGN KEY ([BookingId]) REFERENCES [WorkshopBookings] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022090137_InitialProduction'
)
BEGIN
    CREATE INDEX [IX_Addresses_CustomerId] ON [Addresses] ([CustomerId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022090137_InitialProduction'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Categories_Code] ON [Categories] ([Code]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022090137_InitialProduction'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Customers_Email] ON [Customers] ([Email]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022090137_InitialProduction'
)
BEGIN
    CREATE INDEX [IX_Customers_PhoneNumber] ON [Customers] ([PhoneNumber]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022090137_InitialProduction'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Deliveries_DeliveryCode] ON [Deliveries] ([DeliveryCode]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022090137_InitialProduction'
)
BEGIN
    CREATE INDEX [IX_Deliveries_OrderId] ON [Deliveries] ([OrderId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022090137_InitialProduction'
)
BEGIN
    CREATE INDEX [IX_DeliveryItems_DeliveryId] ON [DeliveryItems] ([DeliveryId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022090137_InitialProduction'
)
BEGIN
    CREATE INDEX [IX_DeliveryItems_OrderItemId] ON [DeliveryItems] ([OrderItemId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022090137_InitialProduction'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Inventories_ProductId] ON [Inventories] ([ProductId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022090137_InitialProduction'
)
BEGIN
    CREATE INDEX [IX_OrderItems_OrderId] ON [OrderItems] ([OrderId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022090137_InitialProduction'
)
BEGIN
    CREATE INDEX [IX_OrderItems_ProductId] ON [OrderItems] ([ProductId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022090137_InitialProduction'
)
BEGIN
    CREATE INDEX [IX_Orders_CustomerId_CreatedAt] ON [Orders] ([CustomerId], [CreatedAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022090137_InitialProduction'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Orders_CustomerId_IdempotencyKey] ON [Orders] ([CustomerId], [IdempotencyKey]) WHERE [CustomerId] IS NOT NULL AND [IdempotencyKey] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022090137_InitialProduction'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Orders_IdempotencyKey] ON [Orders] ([IdempotencyKey]) WHERE [CustomerId] IS NULL AND [IdempotencyKey] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022090137_InitialProduction'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Orders_OrderCode] ON [Orders] ([OrderCode]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022090137_InitialProduction'
)
BEGIN
    CREATE INDEX [IX_Orders_VoucherId] ON [Orders] ([VoucherId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022090137_InitialProduction'
)
BEGIN
    CREATE INDEX [IX_Payments_BookingId] ON [Payments] ([BookingId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022090137_InitialProduction'
)
BEGIN
    CREATE INDEX [IX_Payments_OrderId] ON [Payments] ([OrderId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022090137_InitialProduction'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Payments_TransactionCode] ON [Payments] ([TransactionCode]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022090137_InitialProduction'
)
BEGIN
    CREATE INDEX [IX_ProductImages_ProductId_DisplayOrder] ON [ProductImages] ([ProductId], [DisplayOrder]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022090137_InitialProduction'
)
BEGIN
    CREATE INDEX [IX_Products_CategoryId] ON [Products] ([CategoryId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022090137_InitialProduction'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Products_Code] ON [Products] ([Code]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022090137_InitialProduction'
)
BEGIN
    CREATE INDEX [IX_Refunds_BookingId] ON [Refunds] ([BookingId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022090137_InitialProduction'
)
BEGIN
    CREATE INDEX [IX_Refunds_OrderId] ON [Refunds] ([OrderId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022090137_InitialProduction'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Refunds_TransactionCode] ON [Refunds] ([TransactionCode]) WHERE [TransactionCode] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022090137_InitialProduction'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ShoppingCartItems_CustomerId_ProductId] ON [ShoppingCartItems] ([CustomerId], [ProductId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022090137_InitialProduction'
)
BEGIN
    CREATE INDEX [IX_ShoppingCartItems_ProductId] ON [ShoppingCartItems] ([ProductId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022090137_InitialProduction'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Vouchers_Code] ON [Vouchers] ([Code]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022090137_InitialProduction'
)
BEGIN
    CREATE UNIQUE INDEX [IX_WorkshopBookings_BookingCode] ON [WorkshopBookings] ([BookingCode]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022090137_InitialProduction'
)
BEGIN
    CREATE INDEX [IX_WorkshopBookings_BookingStatus_HoldExpiresAt] ON [WorkshopBookings] ([BookingStatus], [HoldExpiresAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022090137_InitialProduction'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_WorkshopBookings_CustomerId_IdempotencyKey] ON [WorkshopBookings] ([CustomerId], [IdempotencyKey]) WHERE [CustomerId] IS NOT NULL AND [IdempotencyKey] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022090137_InitialProduction'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_WorkshopBookings_IdempotencyKey] ON [WorkshopBookings] ([IdempotencyKey]) WHERE [CustomerId] IS NULL AND [IdempotencyKey] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022090137_InitialProduction'
)
BEGIN
    CREATE INDEX [IX_WorkshopBookings_SessionId_BookingStatus] ON [WorkshopBookings] ([SessionId], [BookingStatus]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022090137_InitialProduction'
)
BEGIN
    CREATE INDEX [IX_WorkshopImages_WorkshopId_DisplayOrder] ON [WorkshopImages] ([WorkshopId], [DisplayOrder]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022090137_InitialProduction'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Workshops_Code] ON [Workshops] ([Code]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022090137_InitialProduction'
)
BEGIN
    CREATE INDEX [IX_Workshops_InstructorId] ON [Workshops] ([InstructorId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022090137_InitialProduction'
)
BEGIN
    CREATE UNIQUE INDEX [IX_WorkshopSessions_WorkshopId_SessionDate_StartTime] ON [WorkshopSessions] ([WorkshopId], [SessionDate], [StartTime]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022090137_InitialProduction'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261022090137_InitialProduction', N'8.0.22');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022092453_AddAuditLogs'
)
BEGIN
    CREATE TABLE [AuditLogs] (
        [Id] uniqueidentifier NOT NULL,
        [EntityType] nvarchar(80) NOT NULL,
        [EntityId] nvarchar(100) NOT NULL,
        [Action] nvarchar(20) NOT NULL,
        [OldValuesJson] nvarchar(max) NOT NULL,
        [NewValuesJson] nvarchar(max) NOT NULL,
        [OccurredAtUtc] datetime2 NOT NULL,
        [ActorId] uniqueidentifier NULL,
        [ActorRole] nvarchar(40) NULL,
        [TraceId] nvarchar(200) NULL,
        CONSTRAINT [PK_AuditLogs] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022092453_AddAuditLogs'
)
BEGIN
    CREATE INDEX [IX_AuditLogs_EntityType_EntityId] ON [AuditLogs] ([EntityType], [EntityId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022092453_AddAuditLogs'
)
BEGIN
    CREATE INDEX [IX_AuditLogs_OccurredAtUtc] ON [AuditLogs] ([OccurredAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022092453_AddAuditLogs'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261022092453_AddAuditLogs', N'8.0.22');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022193130_AddIdentityCmsDefaults'
)
BEGIN
    ALTER TABLE [Customers] ADD [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022193130_AddIdentityCmsDefaults'
)
BEGIN
    ALTER TABLE [Categories] ADD [ImageUrl] nvarchar(max) NOT NULL DEFAULT N'';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022193130_AddIdentityCmsDefaults'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261022193130_AddIdentityCmsDefaults', N'8.0.22');
END;
GO

COMMIT;
GO

