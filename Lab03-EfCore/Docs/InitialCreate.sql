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
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120025640_InitialCreate'
)
BEGIN
    CREATE TABLE [Directors] (
        [Id] int NOT NULL IDENTITY,
        [FullName] nvarchar(200) NOT NULL,
        [Country] nvarchar(100) NULL,
        [BirthYear] int NULL,
        [CreatedAt] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
        [UpdatedAt] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [RowVersion] rowversion NULL,
        CONSTRAINT [PK_Directors] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Directors_BirthYear] CHECK ([BirthYear] IS NULL OR ([BirthYear] >= 1000 AND [BirthYear] <= 2100))
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120025640_InitialCreate'
)
BEGIN
    CREATE TABLE [Genres] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(100) NOT NULL,
        [Slug] nvarchar(100) NOT NULL,
        [Description] nvarchar(500) NULL,
        [CreatedAt] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        CONSTRAINT [PK_Genres] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120025640_InitialCreate'
)
BEGIN
    CREATE TABLE [Movies] (
        [Id] bigint NOT NULL IDENTITY,
        [Title] nvarchar(300) NOT NULL,
        [Barcode] nvarchar(20) NOT NULL,
        [DailyRate] decimal(18,2) NOT NULL,
        [AvailableDiscs] int NOT NULL DEFAULT 0,
        [ReleaseYear] int NULL,
        [DirectorId] int NOT NULL,
        [CreatedAt] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
        [UpdatedAt] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [RowVersion] rowversion NULL,
        CONSTRAINT [PK_Movies] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Movies_AvailableDiscs] CHECK ([AvailableDiscs] >= 0),
        CONSTRAINT [CK_Movies_DailyRate] CHECK ([DailyRate] > 0),
        CONSTRAINT [CK_Movies_ReleaseYear] CHECK ([ReleaseYear] IS NULL OR ([ReleaseYear] >= 1400 AND [ReleaseYear] <= 2100)),
        CONSTRAINT [FK_Movies_Directors_DirectorId] FOREIGN KEY ([DirectorId]) REFERENCES [Directors] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120025640_InitialCreate'
)
BEGIN
    CREATE TABLE [DiscDiscs] (
        [Id] bigint NOT NULL IDENTITY,
        [MovieId] bigint NOT NULL,
        [DiscNumber] nvarchar(40) NOT NULL,
        [Shelf] nvarchar(40) NULL,
        [Condition] nvarchar(20) NOT NULL DEFAULT N'Good',
        [AcquiredAt] date NULL,
        CONSTRAINT [PK_DiscDiscs] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_DiscDiscs_Condition] CHECK ([Condition] IN ('New', 'Good', 'Worn', 'Damaged', 'Lost')),
        CONSTRAINT [FK_DiscDiscs_Movies_MovieId] FOREIGN KEY ([MovieId]) REFERENCES [Movies] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120025640_InitialCreate'
)
BEGIN
    CREATE TABLE [MovieDetails] (
        [MovieId] bigint NOT NULL,
        [Description] nvarchar(2000) NOT NULL,
        [Studio] nvarchar(200) NULL,
        [DurationMinutes] int NULL,
        [Language] nvarchar(50) NOT NULL DEFAULT N'uk',
        [CoverUrl] nvarchar(500) NULL,
        CONSTRAINT [PK_MovieDetails] PRIMARY KEY ([MovieId]),
        CONSTRAINT [CK_MovieDetails_DurationMinutes] CHECK ([DurationMinutes] IS NULL OR [DurationMinutes] > 0),
        CONSTRAINT [FK_MovieDetails_Movies_MovieId] FOREIGN KEY ([MovieId]) REFERENCES [Movies] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120025640_InitialCreate'
)
BEGIN
    CREATE TABLE [MovieGenres] (
        [MovieId] bigint NOT NULL,
        [GenreId] int NOT NULL,
        [AddedAt] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
        [AddedBy] nvarchar(100) NULL,
        [IsPrimary] bit NOT NULL DEFAULT CAST(0 AS bit),
        CONSTRAINT [PK_MovieGenres] PRIMARY KEY ([MovieId], [GenreId]),
        CONSTRAINT [FK_MovieGenres_Genres_GenreId] FOREIGN KEY ([GenreId]) REFERENCES [Genres] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_MovieGenres_Movies_MovieId] FOREIGN KEY ([MovieId]) REFERENCES [Movies] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120025640_InitialCreate'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'BirthYear', N'Country', N'CreatedAt', N'FullName', N'UpdatedAt') AND [object_id] = OBJECT_ID(N'[Directors]'))
        SET IDENTITY_INSERT [Directors] ON;
    EXEC(N'INSERT INTO [Directors] ([Id], [BirthYear], [Country], [CreatedAt], [FullName], [UpdatedAt])
    VALUES (1, 1924, N''Вірменія'', ''2026-01-01T00:00:00.0000000Z'', N''Сергій Параджанов'', NULL),
    (2, 1894, N''Україна'', ''2026-01-01T00:00:00.0000000Z'', N''Олександр Довженко'', NULL),
    (3, 1941, N''Україна'', ''2026-01-01T00:00:00.0000000Z'', N''Борис Івченко'', NULL),
    (4, 1972, N''Україна'', ''2026-01-01T00:00:00.0000000Z'', N''Олесь Санін'', NULL),
    (5, 1972, N''Україна'', ''2026-01-01T00:00:00.0000000Z'', N''Ахтем Сеітаблаєв'', NULL),
    (6, 1993, N''Україна'', ''2026-01-01T00:00:00.0000000Z'', N''Антоніо Лукіч'', NULL)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'BirthYear', N'Country', N'CreatedAt', N'FullName', N'UpdatedAt') AND [object_id] = OBJECT_ID(N'[Directors]'))
        SET IDENTITY_INSERT [Directors] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120025640_InitialCreate'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'CreatedAt', N'Description', N'Name', N'Slug') AND [object_id] = OBJECT_ID(N'[Genres]'))
        SET IDENTITY_INSERT [Genres] ON;
    EXEC(N'INSERT INTO [Genres] ([Id], [CreatedAt], [Description], [Name], [Slug])
    VALUES (1, ''2026-01-01T00:00:00.0000000Z'', N''Комедійні фільми'', N''Комедія'', N''comedy''),
    (2, ''2026-01-01T00:00:00.0000000Z'', N''Фільми на історичному матеріалі'', N''Історичний'', N''historical''),
    (3, ''2026-01-01T00:00:00.0000000Z'', N''Драматичні фільми'', N''Драма'', N''drama''),
    (4, ''2026-01-01T00:00:00.0000000Z'', N''Класика українського кіно'', N''Класика'', N''classic''),
    (5, ''2026-01-01T00:00:00.0000000Z'', N''Документальне кіно'', N''Документальний'', N''documentary'')');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'CreatedAt', N'Description', N'Name', N'Slug') AND [object_id] = OBJECT_ID(N'[Genres]'))
        SET IDENTITY_INSERT [Genres] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120025640_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Directors_FullName] ON [Directors] ([FullName]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120025640_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_DiscDiscs_MovieId] ON [DiscDiscs] ([MovieId]) INCLUDE ([Condition], [Shelf]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120025640_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [UQ_DiscDiscs_DiscNumber] ON [DiscDiscs] ([DiscNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120025640_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [UQ_Genres_Name] ON [Genres] ([Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120025640_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [UQ_Genres_Slug] ON [Genres] ([Slug]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120025640_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_MovieGenres_GenreId_AddedAt] ON [MovieGenres] ([GenreId], [AddedAt] DESC) INCLUDE ([MovieId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120025640_InitialCreate'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_Movies_Active] ON [Movies] ([Title]) INCLUDE ([DailyRate], [AvailableDiscs], [DirectorId]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120025640_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Movies_DirectorId] ON [Movies] ([DirectorId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120025640_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [UQ_Movies_Barcode] ON [Movies] ([Barcode]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120025640_InitialCreate'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261120025640_InitialCreate', N'9.0.11');
END;

COMMIT;
GO

