using System;
using System.IO;
using System.Data;
using Microsoft.Data.Sqlite;
using Dapper;

namespace PartnerRelationManager.Services
{
    public static class DatabaseHelper
    {
        private static readonly string DbFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PartnerRelationManager"
        );

        private static readonly string DbPath = Path.Combine(DbFolder, "srm.db");
        private static readonly string ConnectionString = $"Data Source={DbPath}";

        public static string DatabasePath => DbPath;
        public static string DocumentsFolder => Path.Combine(DbFolder, "Documents");

        public static SqliteConnection GetConnection()
        {
            return new SqliteConnection(ConnectionString);
        }

        public static void InitializeDatabase()
        {
            // Ensure directories exist
            if (!Directory.Exists(DbFolder))
            {
                Directory.CreateDirectory(DbFolder);
            }
            if (!Directory.Exists(DocumentsFolder))
            {
                Directory.CreateDirectory(DocumentsFolder);
            }

            using var connection = GetConnection();
            connection.Open();

            // Create tables
            connection.Execute(@"
                CREATE TABLE IF NOT EXISTS Partners (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL UNIQUE,
                    InternalOwner TEXT,
                    Category TEXT,
                    StrategicImportance TEXT,
                    Status TEXT,
                    BusinessAreas TEXT
                );
            ");

            connection.Execute(@"
                CREATE TABLE IF NOT EXISTS Countries (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL,
                    Code TEXT NOT NULL UNIQUE
                );
            ");

            connection.Execute(@"
                CREATE TABLE IF NOT EXISTS Tiers (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL UNIQUE
                );
            ");

            connection.Execute(@"
                CREATE TABLE IF NOT EXISTS PartnerTiers (
                    PartnerId INTEGER,
                    CountryId INTEGER,
                    Period INTEGER,
                    TierId INTEGER,
                    PRIMARY KEY (PartnerId, CountryId, Period)
                );
            ");

            connection.Execute(@"
                CREATE TABLE IF NOT EXISTS Contacts (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    PartnerId INTEGER NOT NULL,
                    Name TEXT NOT NULL,
                    Role TEXT,
                    Email TEXT,
                    Phone TEXT,
                    Notes TEXT
                );
            ");

            connection.Execute(@"
                CREATE TABLE IF NOT EXISTS ProductsServices (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    PartnerId INTEGER NOT NULL,
                    Name TEXT NOT NULL,
                    Type TEXT,
                    Status TEXT,
                    Replacement TEXT,
                    Description TEXT
                );
            ");

            connection.Execute(@"
                CREATE TABLE IF NOT EXISTS MarketingCampaigns (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    PartnerId INTEGER NOT NULL,
                    Name TEXT NOT NULL,
                    Type TEXT,
                    FundingType TEXT,
                    Budget REAL,
                    StartDate TEXT,
                    Status TEXT,
                    Comments TEXT
                );
            ");

            connection.Execute(@"
                CREATE TABLE IF NOT EXISTS CustomerCases (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    PartnerId INTEGER NOT NULL,
                    CustomerName TEXT NOT NULL,
                    ApprovalStatus TEXT,
                    IsExternal INTEGER,
                    Owner TEXT,
                    ApprovedText TEXT,
                    Comments TEXT
                );
            ");

            connection.Execute(@"
                CREATE TABLE IF NOT EXISTS Activities (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    PartnerId INTEGER NOT NULL,
                    ActivityDate TEXT,
                    Type TEXT,
                    Title TEXT,
                    Description TEXT,
                    Owner TEXT,
                    Status TEXT,
                    DueDate TEXT
                );
            ");

            connection.Execute(@"
                CREATE TABLE IF NOT EXISTS Documents (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    PartnerId INTEGER,
                    CountryId INTEGER,
                    Period INTEGER,
                    FileName TEXT NOT NULL,
                    FilePath TEXT NOT NULL,
                    UploadDate TEXT NOT NULL,
                    AssetType TEXT
                );
            ");

            // KPI tables
            connection.Execute(@"
                CREATE TABLE IF NOT EXISTS KPI_Commercial (
                    PartnerId INTEGER,
                    CountryId INTEGER,
                    Period INTEGER,
                    AnnualRecurringRevenue REAL,
                    UpfrontRevenue REAL,
                    OemServiceAttachRate REAL,
                    OnitioServiceAttachRate REAL,
                    LifecycleMargin REAL,
                    TargetMet TEXT,
                    Comments TEXT,
                    PRIMARY KEY (PartnerId, CountryId, Period)
                );
            ");

            connection.Execute(@"
                CREATE TABLE IF NOT EXISTS KPI_Compliance (
                    PartnerId INTEGER,
                    CountryId INTEGER,
                    Period INTEGER,
                    CertificationsNeeded TEXT,
                    RequiredCertifications INTEGER,
                    CertificationsCovered REAL,
                    CertsExpiring3Months INTEGER,
                    CertsExpiring6Months INTEGER,
                    CertsExpiring12Months INTEGER,
                    ProgramComplianceStatus TEXT,
                    TierRisk TEXT,
                    Comments TEXT,
                    PRIMARY KEY (PartnerId, CountryId, Period)
                );
            ");

            connection.Execute(@"
                CREATE TABLE IF NOT EXISTS KPI_ProgramControl (
                    PartnerId INTEGER,
                    CountryId INTEGER,
                    Period INTEGER,
                    ReportedRevenueOnitio REAL,
                    ReportedRevenueOem REAL,
                    Variance REAL,
                    RebateEligibility TEXT,
                    TierProgress REAL,
                    RiskOfDowngrade TEXT,
                    Comments TEXT,
                    PRIMARY KEY (PartnerId, CountryId, Period)
                );
            ");

            connection.Execute(@"
                CREATE TABLE IF NOT EXISTS KPI_SustainabilityESG (
                    PartnerId INTEGER,
                    CountryId INTEGER,
                    Period INTEGER,
                    TakeBackRecyclingProgram TEXT,
                    RefurbSecondLifeSupport TEXT,
                    EnvironmentalDataAvailable TEXT,
                    EsgComplianceStatus TEXT,
                    LogisticsEmissionReduction TEXT,
                    SustainabilityQualificationMet TEXT,
                    Comments TEXT,
                    PRIMARY KEY (PartnerId, CountryId, Period)
                );
            ");

            connection.Execute(@"
                CREATE TABLE IF NOT EXISTS KPI_Operational (
                    PartnerId INTEGER,
                    Period INTEGER,
                    DoaRate REAL,
                    AvgRmaLeadTime REAL,
                    AssetDataQuality REAL,
                    OperationalIssuesCount INTEGER,
                    TargetMet TEXT,
                    Comments TEXT,
                    PRIMARY KEY (PartnerId, Period)
                );
            ");

            connection.Execute(@"
                CREATE TABLE IF NOT EXISTS KPI_Strategic (
                    PartnerId INTEGER,
                    Period INTEGER,
                    DegreeOfStandardization TEXT,
                    AvgTimeToDeploy REAL,
                    CustomerImpactScore REAL,
                    StrategicFitAssessment TEXT,
                    Comments TEXT,
                    PRIMARY KEY (PartnerId, Period)
                );
            ");

            // Seed default Countries if empty
            var countryCount = connection.ExecuteScalar<int>("SELECT COUNT(*) FROM Countries;");
            if (countryCount == 0)
            {
                connection.Execute("INSERT INTO Countries (Name, Code) VALUES ('Norway', 'NO');");
                connection.Execute("INSERT INTO Countries (Name, Code) VALUES ('Sweden', 'SE');");
                connection.Execute("INSERT INTO Countries (Name, Code) VALUES ('Denmark', 'DK');");
                connection.Execute("INSERT INTO Countries (Name, Code) VALUES ('Finland', 'FI');");
            }

            // Seed default Tiers if empty
            var tierCount = connection.ExecuteScalar<int>("SELECT COUNT(*) FROM Tiers;");
            if (tierCount == 0)
            {
                connection.Execute("INSERT INTO Tiers (Name) VALUES ('None');");
                connection.Execute("INSERT INTO Tiers (Name) VALUES ('Preferred');");
                connection.Execute("INSERT INTO Tiers (Name) VALUES ('Synergy');");
                connection.Execute("INSERT INTO Tiers (Name) VALUES ('Power');");
                connection.Execute("INSERT INTO Tiers (Name) VALUES ('Power Elite');");
            }
        }

        public static bool IsDatabaseEmpty()
        {
            using var connection = GetConnection();
            var partnerCount = connection.ExecuteScalar<int>("SELECT COUNT(*) FROM Partners;");
            return partnerCount == 0;
        }

        public static void ResetDatabase()
        {
            using var connection = GetConnection();
            connection.Open();
            using var transaction = connection.BeginTransaction();
            try
            {
                connection.Execute("DROP TABLE IF EXISTS Partners;", transaction: transaction);
                connection.Execute("DROP TABLE IF EXISTS Countries;", transaction: transaction);
                connection.Execute("DROP TABLE IF EXISTS Tiers;", transaction: transaction);
                connection.Execute("DROP TABLE IF EXISTS PartnerTiers;", transaction: transaction);
                connection.Execute("DROP TABLE IF EXISTS Contacts;", transaction: transaction);
                connection.Execute("DROP TABLE IF EXISTS ProductsServices;", transaction: transaction);
                connection.Execute("DROP TABLE IF EXISTS MarketingCampaigns;", transaction: transaction);
                connection.Execute("DROP TABLE IF EXISTS CustomerCases;", transaction: transaction);
                connection.Execute("DROP TABLE IF EXISTS Activities;", transaction: transaction);
                connection.Execute("DROP TABLE IF EXISTS Documents;", transaction: transaction);
                connection.Execute("DROP TABLE IF EXISTS KPI_Commercial;", transaction: transaction);
                connection.Execute("DROP TABLE IF EXISTS KPI_Compliance;", transaction: transaction);
                connection.Execute("DROP TABLE IF EXISTS KPI_ProgramControl;", transaction: transaction);
                connection.Execute("DROP TABLE IF EXISTS KPI_SustainabilityESG;", transaction: transaction);
                connection.Execute("DROP TABLE IF EXISTS KPI_Operational;", transaction: transaction);
                connection.Execute("DROP TABLE IF EXISTS KPI_Strategic;", transaction: transaction);
                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }

            InitializeDatabase();
        }
    }
}
