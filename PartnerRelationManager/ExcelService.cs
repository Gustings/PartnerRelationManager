using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using ClosedXML.Excel;
using Dapper;
using PartnerRelationManager.Models;

namespace PartnerRelationManager.Services
{
    public static class ExcelService
    {
        public static void ImportTrackerData(string filePath)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("The specified tracker file was not found.", filePath);
            }

            using var workbook = new XLWorkbook(filePath);
            using var connection = DatabaseHelper.GetConnection();
            connection.Open();

            // 1. Import Partners from Partner_Overview
            if (workbook.TryGetWorksheet("Partner_Overview", out var overviewSheet))
            {
                var headers = GetHeaders(overviewSheet);
                var rows = overviewSheet.RowsUsed().Skip(1);

                foreach (var row in rows)
                {
                    string partnerName = GetString(row, headers, "Partner Name");
                    if (string.IsNullOrWhiteSpace(partnerName)) continue;

                    string partnerType = GetString(row, headers, "Partner Type (OEM/Preferred)");
                    string countriesStr = GetString(row, headers, "Countries (NO/SE/DK/FI)");
                    string primarySalesModel = GetString(row, headers, "Primary Sales Model (XaaS/Hybrid/Upfront)");
                    string partnerProgram = GetString(row, headers, "Partner Program");
                    string currentTier = GetString(row, headers, "Current Tier");
                    string partnerIdStr = GetString(row, headers, "Partner Identification");
                    string strategicOwner = GetString(row, headers, "Strategic Owner (Onitio)");
                    string qbrFreq = GetString(row, headers, "QBR Frequency");
                    string overallStatus = GetString(row, headers, "Overall Status (Green/Amber/Red)");
                    string comments = GetString(row, headers, "Comments");

                    // Insert/Update Partner
                    var existingPartner = connection.QueryFirstOrDefault<Partner>(
                        "SELECT * FROM Partners WHERE Name = @Name", new { Name = partnerName });

                    int partnerId;
                    if (existingPartner == null)
                    {
                        partnerId = connection.QuerySingle<int>(@"
                            INSERT INTO Partners (Name, InternalOwner, Category, StrategicImportance, Status, BusinessAreas)
                            VALUES (@Name, @InternalOwner, @Category, @StrategicImportance, @Status, @BusinessAreas);
                            SELECT last_insert_rowid();",
                            new
                            {
                                Name = partnerName,
                                InternalOwner = strategicOwner,
                                Category = partnerType,
                                StrategicImportance = "Medium", // default
                                Status = overallStatus,
                                BusinessAreas = primarySalesModel
                            });
                    }
                    else
                    {
                        partnerId = existingPartner.Id;
                        connection.Execute(@"
                            UPDATE Partners
                            SET InternalOwner = @InternalOwner,
                                Category = @Category,
                                Status = @Status,
                                BusinessAreas = @BusinessAreas
                            WHERE Id = @Id;",
                            new
                            {
                                InternalOwner = strategicOwner,
                                Category = partnerType,
                                Status = overallStatus,
                                BusinessAreas = primarySalesModel,
                                Id = partnerId
                            });
                    }

                    // Process Country & Tier for PartnerTier
                    if (!string.IsNullOrWhiteSpace(countriesStr))
                    {
                        var countryList = countriesStr.Split(new[] { ',', '/', ';' }, StringSplitOptions.RemoveEmptyEntries)
                                                      .Select(c => c.Trim());

                        foreach (var cCode in countryList)
                        {
                            var country = connection.QueryFirstOrDefault<Country>(
                                "SELECT * FROM Countries WHERE Code = @Code", new { Code = cCode });

                            int countryId;
                            if (country == null)
                            {
                                countryId = connection.QuerySingle<int>(
                                    "INSERT INTO Countries (Name, Code) VALUES (@Name, @Code); SELECT last_insert_rowid();",
                                    new { Name = cCode, Code = cCode });
                            }
                            else
                            {
                                countryId = country.Id;
                            }

                            int tierId = 1; // Default 'None'
                            if (!string.IsNullOrWhiteSpace(currentTier))
                            {
                                var tier = connection.QueryFirstOrDefault<Tier>(
                                    "SELECT * FROM Tiers WHERE Name = @Name", new { Name = currentTier });

                                if (tier == null)
                                {
                                    tierId = connection.QuerySingle<int>(
                                        "INSERT INTO Tiers (Name) VALUES (@Name); SELECT last_insert_rowid();",
                                        new { Name = currentTier });
                                }
                                else
                                {
                                    tierId = tier.Id;
                                }
                            }

                            // Upsert PartnerTier for Period 2025
                            connection.Execute(@"
                                INSERT INTO PartnerTiers (PartnerId, CountryId, Period, TierId)
                                VALUES (@PartnerId, @CountryId, 2025, @TierId)
                                ON CONFLICT(PartnerId, CountryId, Period) DO UPDATE SET TierId = @TierId;",
                                new { PartnerId = partnerId, CountryId = countryId, TierId = tierId });
                        }
                    }
                }
            }

            // 2. Import Contacts from Partner_Contacts
            if (workbook.TryGetWorksheet("Partner_Contacts", out var contactsSheet))
            {
                var headers = GetHeaders(contactsSheet);
                var rows = contactsSheet.RowsUsed().Skip(1);

                foreach (var row in rows)
                {
                    string partnerName = GetString(row, headers, "Partner Name");
                    if (string.IsNullOrWhiteSpace(partnerName)) continue;

                    var partner = connection.QueryFirstOrDefault<Partner>(
                        "SELECT * FROM Partners WHERE Name = @Name", new { Name = partnerName });
                    if (partner == null) continue;

                    string country = GetString(row, headers, "Countries");
                    string contactType = GetString(row, headers, "Contact type");
                    string contactName = GetString(row, headers, "Name");
                    string phone = GetString(row, headers, "Phone Number");
                    string email = GetString(row, headers, "Email");
                    string support = GetString(row, headers, "Support");

                    if (string.IsNullOrWhiteSpace(contactName) && string.IsNullOrWhiteSpace(email)) continue;

                    // Check if contact already exists
                    var existingContact = connection.QueryFirstOrDefault<Contact>(
                        "SELECT * FROM Contacts WHERE PartnerId = @PartnerId AND Name = @Name",
                        new { PartnerId = partner.Id, Name = contactName ?? string.Empty });

                    if (existingContact == null)
                    {
                        connection.Execute(@"
                            INSERT INTO Contacts (PartnerId, Name, Role, Email, Phone, Notes)
                            VALUES (@PartnerId, @Name, @Role, @Email, @Phone, @Notes);",
                            new
                            {
                                PartnerId = partner.Id,
                                Name = contactName ?? string.Empty,
                                Role = contactType,
                                Email = email,
                                Phone = phone,
                                Notes = $"Country: {country}. Support: {support}"
                            });
                    }
                }
            }

            // Helper to find country by code or name
            int GetOrCreateCountryId(string codeOrName)
            {
                if (string.IsNullOrWhiteSpace(codeOrName)) return 1; // default to first
                var country = connection.QueryFirstOrDefault<Country>(
                    "SELECT * FROM Countries WHERE Code = @Code OR Name = @Name",
                    new { Code = codeOrName, Name = codeOrName });
                if (country != null) return country.Id;

                return connection.QuerySingle<int>(
                    "INSERT INTO Countries (Name, Code) VALUES (@Name, @Code); SELECT last_insert_rowid();",
                    new { Name = codeOrName, Code = codeOrName });
            }

            // 3. Import KPI Commercial
            if (workbook.TryGetWorksheet("KPI_Commercial", out var commercialSheet))
            {
                var headers = GetHeaders(commercialSheet);
                var rows = commercialSheet.RowsUsed().Skip(1);

                foreach (var row in rows)
                {
                    string partnerName = GetString(row, headers, "Partner Name");
                    if (string.IsNullOrWhiteSpace(partnerName)) continue;

                    var partner = connection.QueryFirstOrDefault<Partner>(
                        "SELECT * FROM Partners WHERE Name = @Name", new { Name = partnerName });
                    if (partner == null) continue;

                    int period = GetInt(row, headers, "Period") ?? 2025;
                    string countryCode = GetString(row, headers, "Country");
                    int countryId = GetOrCreateCountryId(countryCode);

                    decimal? arr = GetDecimal(row, headers, "Annual Recurring Revenue");
                    decimal? upfront = GetDecimal(row, headers, "Upfront Revenue");
                    double? oemAttach = GetDouble(row, headers, "OEM Service Attach Rate (%)");
                    double? onitioAttach = GetDouble(row, headers, "Onitio Service Attach Rate (%)");
                    double? margin = GetDouble(row, headers, "Lifecycle Margin (%)");
                    string targetMet = GetString(row, headers, "Target Met (Yes/No)");
                    string comments = GetString(row, headers, "Comments");

                    connection.Execute(@"
                        INSERT INTO KPI_Commercial (PartnerId, CountryId, Period, AnnualRecurringRevenue, UpfrontRevenue, OemServiceAttachRate, OnitioServiceAttachRate, LifecycleMargin, TargetMet, Comments)
                        VALUES (@PartnerId, @CountryId, @Period, @AnnualRecurringRevenue, @UpfrontRevenue, @OemServiceAttachRate, @OnitioServiceAttachRate, @LifecycleMargin, @TargetMet, @Comments)
                        ON CONFLICT(PartnerId, CountryId, Period) DO UPDATE SET
                            AnnualRecurringRevenue=@AnnualRecurringRevenue, UpfrontRevenue=@UpfrontRevenue, OemServiceAttachRate=@OemServiceAttachRate,
                            OnitioServiceAttachRate=@OnitioServiceAttachRate, LifecycleMargin=@LifecycleMargin, TargetMet=@TargetMet, Comments=@Comments;",
                        new
                        {
                            PartnerId = partner.Id,
                            CountryId = countryId,
                            Period = period,
                            AnnualRecurringRevenue = arr,
                            UpfrontRevenue = upfront,
                            OemServiceAttachRate = oemAttach,
                            OnitioServiceAttachRate = onitioAttach,
                            LifecycleMargin = margin,
                            TargetMet = targetMet,
                            Comments = comments
                        });
                }
            }

            // 4. Import KPI Operational
            if (workbook.TryGetWorksheet("KPI_Operational", out var operationalSheet))
            {
                var headers = GetHeaders(operationalSheet);
                var rows = operationalSheet.RowsUsed().Skip(1);

                foreach (var row in rows)
                {
                    string partnerName = GetString(row, headers, "Partner Name");
                    if (string.IsNullOrWhiteSpace(partnerName)) continue;

                    var partner = connection.QueryFirstOrDefault<Partner>(
                        "SELECT * FROM Partners WHERE Name = @Name", new { Name = partnerName });
                    if (partner == null) continue;

                    int period = GetInt(row, headers, "Period") ?? 2025;
                    double? doa = GetDouble(row, headers, "DOA Rate (%)");
                    double? rma = GetDouble(row, headers, "Avg RMA Lead Time (Days)");
                    double? dataQuality = GetDouble(row, headers, "Asset Data Quality (%)");
                    int? issues = GetInt(row, headers, "Operational Issues (Count)");
                    string targetMet = GetString(row, headers, "Target Met (Yes/No)");
                    string comments = GetString(row, headers, "Comments");

                    connection.Execute(@"
                        INSERT INTO KPI_Operational (PartnerId, Period, DoaRate, AvgRmaLeadTime, AssetDataQuality, OperationalIssuesCount, TargetMet, Comments)
                        VALUES (@PartnerId, @Period, @DoaRate, @AvgRmaLeadTime, @AssetDataQuality, @OperationalIssuesCount, @TargetMet, @Comments)
                        ON CONFLICT(PartnerId, Period) DO UPDATE SET
                            DoaRate=@DoaRate, AvgRmaLeadTime=@AvgRmaLeadTime, AssetDataQuality=@AssetDataQuality,
                            OperationalIssuesCount=@OperationalIssuesCount, TargetMet=@TargetMet, Comments=@Comments;",
                        new
                        {
                            PartnerId = partner.Id,
                            Period = period,
                            DoaRate = doa,
                            AvgRmaLeadTime = rma,
                            AssetDataQuality = dataQuality,
                            OperationalIssuesCount = issues,
                            TargetMet = targetMet,
                            Comments = comments
                        });
                }
            }

            // 5. Import KPI Strategic
            if (workbook.TryGetWorksheet("KPI_Strategic", out var strategicSheet))
            {
                var headers = GetHeaders(strategicSheet);
                var rows = strategicSheet.RowsUsed().Skip(1);

                foreach (var row in rows)
                {
                    string partnerName = GetString(row, headers, "Partner Name");
                    if (string.IsNullOrWhiteSpace(partnerName)) continue;

                    var partner = connection.QueryFirstOrDefault<Partner>(
                        "SELECT * FROM Partners WHERE Name = @Name", new { Name = partnerName });
                    if (partner == null) continue;

                    int period = GetInt(row, headers, "Period") ?? 2025;
                    string std = GetString(row, headers, "Degree of Standardization (High/Medium/Low)");
                    double? deployTime = GetDouble(row, headers, "Avg Time-to-Deploy (Days)");
                    double? impact = GetDouble(row, headers, "Customer Impact Score (1-5)");
                    string fit = GetString(row, headers, "Strategic Fit Assessment");
                    string comments = GetString(row, headers, "Comments");

                    connection.Execute(@"
                        INSERT INTO KPI_Strategic (PartnerId, Period, DegreeOfStandardization, AvgTimeToDeploy, CustomerImpactScore, StrategicFitAssessment, Comments)
                        VALUES (@PartnerId, @Period, @DegreeOfStandardization, @AvgTimeToDeploy, @CustomerImpactScore, @StrategicFitAssessment, @Comments)
                        ON CONFLICT(PartnerId, Period) DO UPDATE SET
                            DegreeOfStandardization=@DegreeOfStandardization, AvgTimeToDeploy=@AvgTimeToDeploy,
                            CustomerImpactScore=@CustomerImpactScore, StrategicFitAssessment=@StrategicFitAssessment, Comments=@Comments;",
                        new
                        {
                            PartnerId = partner.Id,
                            Period = period,
                            DegreeOfStandardization = std,
                            AvgTimeToDeploy = deployTime,
                            CustomerImpactScore = impact,
                            StrategicFitAssessment = fit,
                            Comments = comments
                        });
                }
            }

            // 6. Import KPI Compliance
            if (workbook.TryGetWorksheet("KPI_Compliance", out var complianceSheet))
            {
                var headers = GetHeaders(complianceSheet);
                var rows = complianceSheet.RowsUsed().Skip(1);

                foreach (var row in rows)
                {
                    string partnerName = GetString(row, headers, "Partner Name");
                    if (string.IsNullOrWhiteSpace(partnerName)) continue;

                    var partner = connection.QueryFirstOrDefault<Partner>(
                        "SELECT * FROM Partners WHERE Name = @Name", new { Name = partnerName });
                    if (partner == null) continue;

                    string countryCode = GetString(row, headers, "Country");
                    int countryId = GetOrCreateCountryId(countryCode);
                    int period = 2025; // default

                    string certsNeeded = GetString(row, headers, "Certifications Needed (Yes/No)");
                    int? reqCerts = GetInt(row, headers, "Required Certifications");
                    double? covered = GetDouble(row, headers, "Certifications Covered (%)");
                    int? exp3 = GetInt(row, headers, "Certs Expiring < 3 Months");
                    int? exp6 = GetInt(row, headers, "Certs Expiring < 6 Months");
                    int? exp12 = GetInt(row, headers, "Certs Expiring < 12 Months");
                    string status = GetString(row, headers, "Program Compliance Status (OK/Deviation)");
                    string risk = GetString(row, headers, "Tier Risk (Yes/No)");

                    connection.Execute(@"
                        INSERT INTO KPI_Compliance (PartnerId, CountryId, Period, CertificationsNeeded, RequiredCertifications, CertificationsCovered, CertsExpiring3Months, CertsExpiring6Months, CertsExpiring12Months, ProgramComplianceStatus, TierRisk, Comments)
                        VALUES (@PartnerId, @CountryId, @Period, @CertificationsNeeded, @RequiredCertifications, @CertificationsCovered, @CertsExpiring3Months, @CertsExpiring6Months, @CertsExpiring12Months, @ProgramComplianceStatus, @TierRisk, '')
                        ON CONFLICT(PartnerId, CountryId, Period) DO UPDATE SET
                            CertificationsNeeded=@CertificationsNeeded, RequiredCertifications=@RequiredCertifications, CertificationsCovered=@CertificationsCovered,
                            CertsExpiring3Months=@CertsExpiring3Months, CertsExpiring6Months=@CertsExpiring6Months, CertsExpiring12Months=@CertsExpiring12Months,
                            ProgramComplianceStatus=@ProgramComplianceStatus, TierRisk=@TierRisk;",
                        new
                        {
                            PartnerId = partner.Id,
                            CountryId = countryId,
                            Period = period,
                            CertificationsNeeded = certsNeeded,
                            RequiredCertifications = reqCerts,
                            CertificationsCovered = covered,
                            CertsExpiring3Months = exp3,
                            CertsExpiring6Months = exp6,
                            CertsExpiring12Months = exp12,
                            ProgramComplianceStatus = status,
                            TierRisk = risk
                        });
                }
            }

            // 7. Import KPI Program Control
            if (workbook.TryGetWorksheet("KPI_Program_Control", out var progControlSheet))
            {
                var headers = GetHeaders(progControlSheet);
                var rows = progControlSheet.RowsUsed().Skip(1);

                foreach (var row in rows)
                {
                    string partnerName = GetString(row, headers, "Partner Name");
                    if (string.IsNullOrWhiteSpace(partnerName)) continue;

                    var partner = connection.QueryFirstOrDefault<Partner>(
                        "SELECT * FROM Partners WHERE Name = @Name", new { Name = partnerName });
                    if (partner == null) continue;

                    int period = GetInt(row, headers, "Period") ?? 2025;
                    string countryCode = GetString(row, headers, "Country");
                    int countryId = GetOrCreateCountryId(countryCode);

                    decimal? revOnitio = GetDecimal(row, headers, "Reported Revenue (Onitio)");
                    decimal? revOem = GetDecimal(row, headers, "Reported Revenue (OEM)");
                    double? variance = GetDouble(row, headers, "Variance (%)");
                    string rebate = GetString(row, headers, "Rebate Eligibility (Yes/No)");
                    double? progress = GetDouble(row, headers, "Tier Progress (%)");
                    string risk = GetString(row, headers, "Risk of Downgrade (Yes/No)");
                    string comments = GetString(row, headers, "Comments");

                    connection.Execute(@"
                        INSERT INTO KPI_ProgramControl (PartnerId, CountryId, Period, ReportedRevenueOnitio, ReportedRevenueOem, Variance, RebateEligibility, TierProgress, RiskOfDowngrade, Comments)
                        VALUES (@PartnerId, @CountryId, @Period, @ReportedRevenueOnitio, @ReportedRevenueOem, @Variance, @RebateEligibility, @TierProgress, @RiskOfDowngrade, @Comments)
                        ON CONFLICT(PartnerId, CountryId, Period) DO UPDATE SET
                            ReportedRevenueOnitio=@ReportedRevenueOnitio, ReportedRevenueOem=@ReportedRevenueOem, Variance=@Variance,
                            RebateEligibility=@RebateEligibility, TierProgress=@TierProgress, RiskOfDowngrade=@RiskOfDowngrade, Comments=@Comments;",
                        new
                        {
                            PartnerId = partner.Id,
                            CountryId = countryId,
                            Period = period,
                            ReportedRevenueOnitio = revOnitio,
                            ReportedRevenueOem = revOem,
                            Variance = variance,
                            RebateEligibility = rebate,
                            TierProgress = progress,
                            RiskOfDowngrade = risk,
                            Comments = comments
                        });
                }
            }

            // 8. Import KPI Sustainability / ESG
            if (workbook.TryGetWorksheet("KPI_Sustainability_ESG", out var esgSheet))
            {
                var headers = GetHeaders(esgSheet);
                var rows = esgSheet.RowsUsed().Skip(1);

                foreach (var row in rows)
                {
                    string partnerName = GetString(row, headers, "Partner Name");
                    if (string.IsNullOrWhiteSpace(partnerName)) continue;

                    var partner = connection.QueryFirstOrDefault<Partner>(
                        "SELECT * FROM Partners WHERE Name = @Name", new { Name = partnerName });
                    if (partner == null) continue;

                    string countryCode = GetString(row, headers, "Country");
                    int countryId = GetOrCreateCountryId(countryCode);
                    int period = 2025; // default

                    string takeback = GetString(row, headers, "Take-back / Recycling Program (Yes/No)");
                    string refurb = GetString(row, headers, "Refurb / Second Life Support (Yes/No)");
                    string envData = GetString(row, headers, "Environmental Data Available (Yes/No)");
                    string compliance = GetString(row, headers, "ESG Compliance Status (OK/Deviation)");
                    string logistics = GetString(row, headers, "Logistics Emission Reduction Initiatives");
                    string qualification = GetString(row, headers, "Sustainability Qualification Met (Yes/No)");
                    string comments = GetString(row, headers, "Comments");

                    connection.Execute(@"
                        INSERT INTO KPI_SustainabilityESG (PartnerId, CountryId, Period, TakeBackRecyclingProgram, RefurbSecondLifeSupport, EnvironmentalDataAvailable, EsgComplianceStatus, LogisticsEmissionReduction, SustainabilityQualificationMet, Comments)
                        VALUES (@PartnerId, @CountryId, @Period, @TakeBackRecyclingProgram, @RefurbSecondLifeSupport, @EnvironmentalDataAvailable, @EsgComplianceStatus, @LogisticsEmissionReduction, @SustainabilityQualificationMet, @Comments)
                        ON CONFLICT(PartnerId, CountryId, Period) DO UPDATE SET
                            TakeBackRecyclingProgram=@TakeBackRecyclingProgram, RefurbSecondLifeSupport=@RefurbSecondLifeSupport, EnvironmentalDataAvailable=@EnvironmentalDataAvailable,
                            EsgComplianceStatus=@EsgComplianceStatus, LogisticsEmissionReduction=@LogisticsEmissionReduction, SustainabilityQualificationMet=@SustainabilityQualificationMet, Comments=@Comments;",
                        new
                        {
                            PartnerId = partner.Id,
                            CountryId = countryId,
                            Period = period,
                            TakeBackRecyclingProgram = takeback,
                            RefurbSecondLifeSupport = refurb,
                            EnvironmentalDataAvailable = envData,
                            EsgComplianceStatus = compliance,
                            LogisticsEmissionReduction = logistics,
                            SustainabilityQualificationMet = qualification,
                            Comments = comments
                        });
                }
            }

            // 9. Import QBR Log to Activities
            if (workbook.TryGetWorksheet("QBR_Log", out var qbrSheet))
            {
                var headers = GetHeaders(qbrSheet);
                var rows = qbrSheet.RowsUsed().Skip(1);

                foreach (var row in rows)
                {
                    string partnerName = GetString(row, headers, "Partner Name");
                    if (string.IsNullOrWhiteSpace(partnerName)) continue;

                    var partner = connection.QueryFirstOrDefault<Partner>(
                        "SELECT * FROM Partners WHERE Name = @Name", new { Name = partnerName });
                    if (partner == null) continue;

                    DateTime? qbrDate = GetDateTime(row, headers, "QBR Date");
                    string qbrType = GetString(row, headers, "QBR Type (Regular/Extraordinary)");
                    string keyTopics = GetString(row, headers, "Key Topics");
                    string openRisks = GetString(row, headers, "Open Risks");
                    string escalation = GetString(row, headers, "Escalation Level (0/1/2/3)");
                    string actions = GetString(row, headers, "Actions Agreed");
                    string owner = GetString(row, headers, "Owner");
                    DateTime? dueDate = GetDateTime(row, headers, "Due Date");
                    string status = GetString(row, headers, "Status");

                    string title = $"QBR - {qbrType}";
                    string description = $"Key Topics:\n{keyTopics}\n\nOpen Risks:\n{openRisks}\n\nActions Agreed:\n{actions}\n\nEscalation Level: {escalation}";

                    // Check if already exists
                    var existingActivity = connection.QueryFirstOrDefault<Activity>(
                        "SELECT * FROM Activities WHERE PartnerId = @PartnerId AND Title = @Title AND ActivityDate = @ActivityDate",
                        new { PartnerId = partner.Id, Title = title, ActivityDate = qbrDate?.ToString("yyyy-MM-dd HH:mm:ss") });

                    if (existingActivity == null)
                    {
                        connection.Execute(@"
                            INSERT INTO Activities (PartnerId, ActivityDate, Type, Title, Description, Owner, Status, DueDate)
                            VALUES (@PartnerId, @ActivityDate, 'QBR', @Title, @Description, @Owner, @Status, @DueDate);",
                            new
                            {
                                PartnerId = partner.Id,
                                ActivityDate = qbrDate?.ToString("yyyy-MM-dd HH:mm:ss"),
                                Title = title,
                                Description = description,
                                Owner = owner,
                                Status = status,
                                DueDate = dueDate?.ToString("yyyy-MM-dd HH:mm:ss")
                            });
                    }
                }
            }
        }

        public static void ExportData(string filePath)
        {
            using var workbook = new XLWorkbook();
            using var connection = DatabaseHelper.GetConnection();
            connection.Open();

            // Export Partners
            var partners = connection.Query<Partner>("SELECT * FROM Partners").ToList();
            var wsPartners = workbook.Worksheets.Add("Partners");
            WriteTable(wsPartners, partners);

            // Export Countries
            var countries = connection.Query<Country>("SELECT * FROM Countries").ToList();
            var wsCountries = workbook.Worksheets.Add("Countries");
            WriteTable(wsCountries, countries);

            // Export Tiers
            var tiers = connection.Query<Tier>("SELECT * FROM Tiers").ToList();
            var wsTiers = workbook.Worksheets.Add("Tiers");
            WriteTable(wsTiers, tiers);

            // Export PartnerTiers
            var partnerTiers = connection.Query<PartnerTier>(@"
                SELECT pt.*, p.Name as PartnerName, c.Name as CountryName, t.Name as TierName
                FROM PartnerTiers pt
                JOIN Partners p ON pt.PartnerId = p.Id
                JOIN Countries c ON pt.CountryId = c.Id
                JOIN Tiers t ON pt.TierId = t.Id").ToList();
            var wsPt = workbook.Worksheets.Add("Partner Tiers");
            WriteTable(wsPt, partnerTiers);

            // Export Contacts
            var contacts = connection.Query<Contact>("SELECT * FROM Contacts").ToList();
            var wsContacts = workbook.Worksheets.Add("Contacts");
            WriteTable(wsContacts, contacts);

            // Export Products
            var products = connection.Query<ProductService>("SELECT * FROM ProductsServices").ToList();
            var wsProducts = workbook.Worksheets.Add("Products & Services");
            WriteTable(wsProducts, products);

            // Export Campaigns
            var campaigns = connection.Query<MarketingCampaign>("SELECT * FROM MarketingCampaigns").ToList();
            var wsCampaigns = workbook.Worksheets.Add("Marketing Campaigns");
            WriteTable(wsCampaigns, campaigns);

            // Export Cases
            var cases = connection.Query<CustomerCase>("SELECT * FROM CustomerCases").ToList();
            var wsCases = workbook.Worksheets.Add("Customer Cases");
            WriteTable(wsCases, cases);

            // Export Activities
            var activities = connection.Query<Activity>("SELECT * FROM Activities").ToList();
            var wsActivities = workbook.Worksheets.Add("Activities");
            WriteTable(wsActivities, activities);

            // Export Documents
            var docs = connection.Query<Document>("SELECT * FROM Documents").ToList();
            var wsDocs = workbook.Worksheets.Add("Documents");
            WriteTable(wsDocs, docs);

            // Export KPI Tables
            WriteTable(workbook.Worksheets.Add("KPI Commercial"), connection.Query<KPI_Commercial>("SELECT * FROM KPI_Commercial").ToList());
            WriteTable(workbook.Worksheets.Add("KPI Compliance"), connection.Query<KPI_Compliance>("SELECT * FROM KPI_Compliance").ToList());
            WriteTable(workbook.Worksheets.Add("KPI Program Control"), connection.Query<KPI_ProgramControl>("SELECT * FROM KPI_ProgramControl").ToList());
            WriteTable(workbook.Worksheets.Add("KPI Sustainability ESG"), connection.Query<KPI_SustainabilityESG>("SELECT * FROM KPI_SustainabilityESG").ToList());
            WriteTable(workbook.Worksheets.Add("KPI Operational"), connection.Query<KPI_Operational>("SELECT * FROM KPI_Operational").ToList());
            WriteTable(workbook.Worksheets.Add("KPI Strategic"), connection.Query<KPI_Strategic>("SELECT * FROM KPI_Strategic").ToList());

            workbook.SaveAs(filePath);
        }

        private static void WriteTable<T>(IXLWorksheet ws, List<T> items)
        {
            if (items == null || items.Count == 0)
            {
                ws.Cell(1, 1).Value = "No data available";
                return;
            }

            var properties = typeof(T).GetProperties();
            
            // Write headers
            for (int col = 0; col < properties.Length; col++)
            {
                var cell = ws.Cell(1, col + 1);
                cell.Value = properties[col].Name;
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#0F52BA"); // Sapphire Blue
                cell.Style.Font.FontColor = XLColor.White;
            }

            // Write rows
            for (int row = 0; row < items.Count; row++)
            {
                for (int col = 0; col < properties.Length; col++)
                {
                    var val = properties[col].GetValue(items[row]);
                    var cell = ws.Cell(row + 2, col + 1);
                    if (val != null)
                    {
                        if (val is DateTime dt)
                            cell.Value = dt.ToString("yyyy-MM-dd");
                        else if (val is decimal dec)
                            cell.Value = dec;
                        else if (val is double d)
                            cell.Value = d;
                        else if (val is int i)
                            cell.Value = i;
                        else if (val is bool b)
                            cell.Value = b ? "Yes" : "No";
                        else
                            cell.Value = val.ToString();
                    }
                    else
                    {
                        cell.Value = string.Empty;
                    }
                }
            }

            ws.Columns().AdjustToContents();
            ws.RangeUsed()?.Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin)
                           .Border.SetInsideBorder(XLBorderStyleValues.Thin);
        }

        // Helper methods for robust cell reading
        private static Dictionary<string, int> GetHeaders(IXLWorksheet ws)
        {
            var dict = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var firstRow = ws.Row(1);
            int colCount = firstRow.LastCellUsed()?.Address.ColumnNumber ?? 0;

            for (int col = 1; col <= colCount; col++)
            {
                string raw = firstRow.Cell(col).GetString();
                string cleaned = CleanHeader(raw);
                if (!string.IsNullOrEmpty(cleaned) && !dict.ContainsKey(cleaned))
                {
                    dict[cleaned] = col;
                }
            }
            return dict;
        }

        private static string CleanHeader(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return string.Empty;
            return raw.Replace("\r", "").Replace("\n", " ").Replace("  ", " ").Trim();
        }

        private static IXLCell GetCell(IXLRow row, Dictionary<string, int> headers, string key)
        {
            if (headers.TryGetValue(key, out int colIdx))
            {
                return row.Cell(colIdx);
            }
            return null;
        }

        private static string GetString(IXLRow row, Dictionary<string, int> headers, string key)
        {
            var cell = GetCell(row, headers, key);
            return cell?.GetString()?.Trim() ?? string.Empty;
        }

        private static double? GetDouble(IXLRow row, Dictionary<string, int> headers, string key)
        {
            var cell = GetCell(row, headers, key);
            if (cell == null || cell.IsEmpty()) return null;

            try
            {
                if (cell.DataType == XLDataType.Number) return cell.GetDouble();
                string s = cell.GetString().Replace("%", "").Trim();
                if (double.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double d))
                {
                    // Check if it was in percentage format as text (e.g. "85") vs fraction (e.g. "0.85")
                    if (key.Contains("%") && d > 1.0 && cell.GetString().Contains("%"))
                    {
                        return d / 100.0;
                    }
                    return d;
                }
            }
            catch { }
            return null;
        }

        private static decimal? GetDecimal(IXLRow row, Dictionary<string, int> headers, string key)
        {
            var cell = GetCell(row, headers, key);
            if (cell == null || cell.IsEmpty()) return null;

            try
            {
                if (cell.DataType == XLDataType.Number) return (decimal)cell.GetDouble();
                string s = cell.GetString().Replace("$", "").Replace("€", "").Replace("kr", "").Trim();
                if (decimal.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal d)) return d;
            }
            catch { }
            return null;
        }

        private static int? GetInt(IXLRow row, Dictionary<string, int> headers, string key)
        {
            var cell = GetCell(row, headers, key);
            if (cell == null || cell.IsEmpty()) return null;

            try
            {
                if (cell.DataType == XLDataType.Number) return (int)cell.GetDouble();
                string s = cell.GetString().Trim();
                if (int.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out int i)) return i;
            }
            catch { }
            return null;
        }

        private static DateTime? GetDateTime(IXLRow row, Dictionary<string, int> headers, string key)
        {
            var cell = GetCell(row, headers, key);
            if (cell == null || cell.IsEmpty()) return null;

            try
            {
                if (cell.DataType == XLDataType.DateTime) return cell.GetDateTime();
                if (cell.DataType == XLDataType.Number) return DateTime.FromOADate(cell.GetDouble());
                string s = cell.GetString().Trim();
                if (DateTime.TryParse(s, out DateTime dt)) return dt;
            }
            catch { }
            return null;
        }
    }
}
