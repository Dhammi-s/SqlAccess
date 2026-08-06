-- Secret Vault schema (idempotent). Does not touch existing tables.

IF OBJECT_ID('dbo.Applications','U') IS NULL
CREATE TABLE dbo.Applications (
    ApplicationId     INT IDENTITY(1,1) PRIMARY KEY,
    Name              NVARCHAR(200) NOT NULL,
    ClientId          NVARCHAR(100) NOT NULL,
    ClientSecretHash  NVARCHAR(200) NOT NULL,       -- BCrypt
    IsActive          BIT NOT NULL CONSTRAINT DF_Applications_Active DEFAULT 1,
    CreatedOn         DATETIME2 NOT NULL CONSTRAINT DF_Applications_Created DEFAULT SYSUTCDATETIME()
);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='UX_Applications_ClientId')
    CREATE UNIQUE INDEX UX_Applications_ClientId ON dbo.Applications(ClientId);

IF OBJECT_ID('dbo.Secrets','U') IS NULL
CREATE TABLE dbo.Secrets (
    SecretId       INT IDENTITY(1,1) PRIMARY KEY,
    Name           NVARCHAR(200) NOT NULL,
    SecretType     NVARCHAR(50)  NOT NULL CONSTRAINT DF_Secrets_Type DEFAULT 'Custom',
    IsActive       BIT NOT NULL CONSTRAINT DF_Secrets_Active DEFAULT 1,
    CurrentVersion INT NOT NULL CONSTRAINT DF_Secrets_Ver DEFAULT 0,
    CreatedOn      DATETIME2 NOT NULL CONSTRAINT DF_Secrets_Created DEFAULT SYSUTCDATETIME(),
    UpdatedOn      DATETIME2 NULL
);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='UX_Secrets_Name')
    CREATE UNIQUE INDEX UX_Secrets_Name ON dbo.Secrets(Name);

IF OBJECT_ID('dbo.SecretVersions','U') IS NULL
CREATE TABLE dbo.SecretVersions (
    SecretVersionId INT IDENTITY(1,1) PRIMARY KEY,
    SecretId        INT NOT NULL,
    Version         INT NOT NULL,
    EncryptedValue  NVARCHAR(MAX) NOT NULL,          -- AES-256-GCM
    IsCurrent       BIT NOT NULL CONSTRAINT DF_SecretVersions_Cur DEFAULT 0,
    CreatedOn       DATETIME2 NOT NULL CONSTRAINT DF_SecretVersions_Created DEFAULT SYSUTCDATETIME(),
    CreatedBy       NVARCHAR(200) NULL,
    CONSTRAINT FK_SecretVersions_Secrets FOREIGN KEY (SecretId) REFERENCES dbo.Secrets(SecretId)
);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_SecretVersions_SecretId')
    CREATE INDEX IX_SecretVersions_SecretId ON dbo.SecretVersions(SecretId, Version);

IF OBJECT_ID('dbo.ApplicationSecrets','U') IS NULL
CREATE TABLE dbo.ApplicationSecrets (
    ApplicationSecretId INT IDENTITY(1,1) PRIMARY KEY,
    ApplicationId       INT NOT NULL,
    SecretId            INT NOT NULL,
    CreatedOn           DATETIME2 NOT NULL CONSTRAINT DF_AppSecrets_Created DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_AppSecrets_App    FOREIGN KEY (ApplicationId) REFERENCES dbo.Applications(ApplicationId),
    CONSTRAINT FK_AppSecrets_Secret FOREIGN KEY (SecretId)      REFERENCES dbo.Secrets(SecretId)
);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='UX_AppSecrets')
    CREATE UNIQUE INDEX UX_AppSecrets ON dbo.ApplicationSecrets(ApplicationId, SecretId);

IF OBJECT_ID('dbo.AuditLogs','U') IS NULL
CREATE TABLE dbo.AuditLogs (
    AuditLogId      BIGINT IDENTITY(1,1) PRIMARY KEY,
    ApplicationId   INT NULL,
    ApplicationName NVARCHAR(200) NULL,
    SecretId        INT NULL,
    SecretName      NVARCHAR(200) NULL,
    Action          NVARCHAR(50) NOT NULL,
    Success         BIT NOT NULL,
    IpAddress       NVARCHAR(64) NULL,
    Detail          NVARCHAR(500) NULL,
    Timestamp       DATETIME2 NOT NULL CONSTRAINT DF_AuditLogs_Ts DEFAULT SYSUTCDATETIME()
);
