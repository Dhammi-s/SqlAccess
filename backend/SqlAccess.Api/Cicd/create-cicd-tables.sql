-- CI/CD Deployment Portal — schema (idempotent). Run against the master DB.

IF OBJECT_ID('dbo.Websites','U') IS NULL
CREATE TABLE dbo.Websites (
    WebsiteId       INT IDENTITY(1,1) PRIMARY KEY,
    WebsiteName     NVARCHAR(200)  NOT NULL,
    RepositoryUrl   NVARCHAR(500)  NULL,
    GitProvider     NVARCHAR(50)   NOT NULL CONSTRAINT DF_Websites_Provider DEFAULT 'GitHub',
    DefaultBranch   NVARCHAR(200)  NULL,
    ProjectType     NVARCHAR(50)   NOT NULL CONSTRAINT DF_Websites_Type DEFAULT 'AspNetCore',
    GitPat          NVARCHAR(MAX)  NULL,   -- encrypted at rest
    BuildCommand    NVARCHAR(500)  NULL,
    PublishCommand  NVARCHAR(500)  NULL,
    PublishFolder   NVARCHAR(200)  NULL,
    DeployProvider  NVARCHAR(20)   NOT NULL CONSTRAINT DF_Websites_DeployProvider DEFAULT 'FTP',
    FtpHost         NVARCHAR(200)  NULL,
    FtpPort         INT            NOT NULL CONSTRAINT DF_Websites_FtpPort DEFAULT 21,
    FtpUsername     NVARCHAR(200)  NULL,
    FtpPassword     NVARCHAR(MAX)  NULL,   -- encrypted at rest
    FtpRootFolder   NVARCHAR(300)  NULL,
    IsActive        BIT            NOT NULL CONSTRAINT DF_Websites_Active DEFAULT 1,
    CreatedOn       DATETIME2      NOT NULL CONSTRAINT DF_Websites_Created DEFAULT SYSUTCDATETIME(),
    UpdatedOn       DATETIME2      NULL
);

IF OBJECT_ID('dbo.Deployments','U') IS NULL
CREATE TABLE dbo.Deployments (
    DeploymentId    INT IDENTITY(1,1) PRIMARY KEY,
    WebsiteId       INT            NOT NULL,
    Branch          NVARCHAR(200)  NULL,
    CommitId        NVARCHAR(100)  NULL,
    CommitMessage   NVARCHAR(1000) NULL,
    TriggeredBy     NVARCHAR(200)  NULL,
    Status          NVARCHAR(50)   NOT NULL CONSTRAINT DF_Deployments_Status DEFAULT 'Queued',
    StartedOn       DATETIME2      NULL,
    FinishedOn      DATETIME2      NULL,
    CreatedOn       DATETIME2      NOT NULL CONSTRAINT DF_Deployments_Created DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_Deployments_Websites FOREIGN KEY (WebsiteId) REFERENCES dbo.Websites(WebsiteId)
);

IF OBJECT_ID('dbo.DeploymentLogs','U') IS NULL
CREATE TABLE dbo.DeploymentLogs (
    LogId           BIGINT IDENTITY(1,1) PRIMARY KEY,
    DeploymentId    INT            NOT NULL,
    Timestamp       DATETIME2      NOT NULL CONSTRAINT DF_Logs_Ts DEFAULT SYSUTCDATETIME(),
    LogType         NVARCHAR(20)   NOT NULL CONSTRAINT DF_Logs_Type DEFAULT 'Info',
    Message         NVARCHAR(MAX)  NULL,
    CONSTRAINT FK_DeploymentLogs_Deployments FOREIGN KEY (DeploymentId) REFERENCES dbo.Deployments(DeploymentId)
);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_Deployments_WebsiteId')
    CREATE INDEX IX_Deployments_WebsiteId ON dbo.Deployments(WebsiteId);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_DeploymentLogs_DeploymentId')
    CREATE INDEX IX_DeploymentLogs_DeploymentId ON dbo.DeploymentLogs(DeploymentId, LogId);
