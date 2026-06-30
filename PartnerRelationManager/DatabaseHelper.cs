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

            // 1. Partners Table
            connection.Execute(@"
                CREATE TABLE IF NOT EXISTS Partners (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL UNIQUE,
                    InternalOwner TEXT,
                    Category TEXT,
                    StrategicImportance TEXT,
                    Status TEXT,
                    BusinessAreas TEXT,
                    CountryCode TEXT,
                    PartnerProgram TEXT,
                    CurrentTier TEXT,
                    PartnerIdentification TEXT,
                    QbrFrequency TEXT,
                    Comments TEXT
                );
            ");

            // 2. Contacts Table
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

            // 3. Activities Table
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

            // 4. Documents Table
            connection.Execute(@"
                CREATE TABLE IF NOT EXISTS Documents (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    PartnerId INTEGER NOT NULL,
                    Period INTEGER,
                    FileName TEXT NOT NULL,
                    FilePath TEXT NOT NULL,
                    UploadDate TEXT NOT NULL,
                    AssetType TEXT
                );
            ");

            // 5. KPI Commercial Table
            connection.Execute(@"
                CREATE TABLE IF NOT EXISTS KPI_Commercial (
                    PartnerId INTEGER NOT NULL,
                    Period INTEGER NOT NULL,
                    AnnualRecurringRevenue REAL,
                    UpfrontRevenue REAL,
                    OemServiceAttachRate REAL,
                    OnitioServiceAttachRate REAL,
                    LifecycleMargin REAL,
                    TargetMet TEXT,
                    Comments TEXT,
                    PRIMARY KEY (PartnerId, Period)
                );
            ");

            // 6. KPI Program Control Table
            connection.Execute(@"
                CREATE TABLE IF NOT EXISTS KPI_ProgramControl (
                    PartnerId INTEGER NOT NULL,
                    Period INTEGER NOT NULL,
                    ReportedRevenueOnitio REAL,
                    ReportedRevenueOem REAL,
                    Variance REAL,
                    RebateEligibility TEXT,
                    TierProgress REAL,
                    RiskOfDowngrade TEXT,
                    Comments TEXT,
                    PRIMARY KEY (PartnerId, Period)
                );
            ");
        }

        public static bool IsDatabaseEmpty()
        {
            try
            {
                using var connection = GetConnection();
                var partnerCount = connection.ExecuteScalar<int>("SELECT COUNT(*) FROM Partners;");
                return partnerCount == 0;
            }
            catch
            {
                return true;
            }
        }

        public static void ResetDatabase()
        {
            using var connection = GetConnection();
            connection.Open();
            using var transaction = connection.BeginTransaction();
            try
            {
                connection.Execute("DROP TABLE IF EXISTS Partners;", transaction: transaction);
                connection.Execute("DROP TABLE IF EXISTS Contacts;", transaction: transaction);
                connection.Execute("DROP TABLE IF EXISTS Activities;", transaction: transaction);
                connection.Execute("DROP TABLE IF EXISTS Documents;", transaction: transaction);
                connection.Execute("DROP TABLE IF EXISTS KPI_Commercial;", transaction: transaction);
                connection.Execute("DROP TABLE IF EXISTS KPI_ProgramControl;", transaction: transaction);
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
