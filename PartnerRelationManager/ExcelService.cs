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
        private static string GetCountryName(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return string.Empty;
            switch (code.Trim().ToUpperInvariant())
            {
                case "NO": return "Norway";
                case "SE": return "Sweden";
                case "DK": return "Denmark";
                case "FI": return "Finland";
                default: return code.Trim();
            }
        }

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
                    string rawPartnerName = GetString(row, headers, "Partner Name");
                    if (string.IsNullOrWhiteSpace(rawPartnerName)) continue;

                    string countryCode = GetString(row, headers, "Countries (NO/SE/DK/FI)");
                    if (string.IsNullOrWhiteSpace(countryCode))
                    {
                        // Try to find if country code is part of Partner Name, e.g. "HP NO"
                        var parts = rawPartnerName.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length > 1)
                        {
                            var last = parts[^1].ToUpperInvariant();
                            if (last == "NO" || last == "SE" || last == "DK" || last == "FI")
                            {
                                countryCode = last;
                                rawPartnerName = string.Join(" ", parts.Take(parts.Length - 1));
                            }
                        }
                    }

                    string countryName = GetCountryName(countryCode);
                    string partnerName = string.IsNullOrEmpty(countryName) ? rawPartnerName : $"{rawPartnerName} {countryName}";

                    string partnerType = GetString(row, headers, "Partner Type (OEM/Preferred)");
                    string primarySalesModel = GetString(row, headers, "Primary Sales Model (XaaS/Hybrid/Upfront)");
                    string partnerProgram = GetString(row, headers, "Partner Program");
                    string currentTier = GetString(row, headers, "Current Tier");
                    string partnerIdStr = GetString(row, headers, "Partner Identification");
                    string strategicOwner = GetString(row, headers, "Strategic Owner (Onitio)");
                    string qbrFreq = GetString(row, headers, "QBR Frequency");
                    string overallStatus = GetString(row, headers, "Overall Status (Green/Amber/Red)");
                    string comments = GetString(row, headers, "Comments");

                    var existingPartner = connection.QueryFirstOrDefault<Partner>(
                        "SELECT * FROM Partners WHERE Name = @Name", new { Name = partnerName });

                    if (existingPartner == null)
                    {
                        connection.Execute(@"
                            INSERT INTO Partners (Name, InternalOwner, Category, StrategicImportance, Status, BusinessAreas,
                                                 CountryCode, PartnerProgram, CurrentTier, PartnerIdentification, QbrFrequency, Comments)
                            VALUES (@Name, @InternalOwner, @Category, 'Medium', @Status, @BusinessAreas,
                                    @CountryCode, @PartnerProgram, @CurrentTier, @PartnerIdentification, @QbrFrequency, @Comments);",
                            new
                            {
                                Name = partnerName,
                                InternalOwner = strategicOwner,
                                Category = partnerType,
                                Status = overallStatus,
                                BusinessAreas = primarySalesModel,
                                CountryCode = countryCode,
                                PartnerProgram = partnerProgram,
                                CurrentTier = currentTier,
                                PartnerIdentification = partnerIdStr,
                                QbrFrequency = qbrFreq,
                                Comments = comments
                            });
                    }
                    else
                    {
                        connection.Execute(@"
                            UPDATE Partners
                            SET InternalOwner = @InternalOwner,
                                Category = @Category,
                                Status = @Status,
                                BusinessAreas = @BusinessAreas,
                                CountryCode = @CountryCode,
                                PartnerProgram = @PartnerProgram,
                                CurrentTier = @CurrentTier,
                                PartnerIdentification = @PartnerIdentification,
                                QbrFrequency = @QbrFrequency,
                                Comments = @Comments
                            WHERE Id = @Id;",
                            new
                            {
                                InternalOwner = strategicOwner,
                                Category = partnerType,
                                Status = overallStatus,
                                BusinessAreas = primarySalesModel,
                                CountryCode = countryCode,
                                PartnerProgram = partnerProgram,
                                CurrentTier = currentTier,
                                PartnerIdentification = partnerIdStr,
                                QbrFrequency = qbrFreq,
                                Comments = comments,
                                Id = existingPartner.Id
                            });
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

                    string countryFilter = GetString(row, headers, "Countries");
                    string contactType = GetString(row, headers, "Contact type");
                    string contactName = GetString(row, headers, "Name");
                    string phone = GetString(row, headers, "Phone Number");
                    string email = GetString(row, headers, "Email");
                    string support = GetString(row, headers, "Support");

                    if (string.IsNullOrWhiteSpace(contactName) && string.IsNullOrWhiteSpace(email)) continue;

                    // Find matching partners
                    List<Partner> targets = new List<Partner>();
                    if (countryFilter.Equals("Nordic", StringComparison.OrdinalIgnoreCase))
                    {
                        targets = connection.Query<Partner>(
                            "SELECT * FROM Partners WHERE Name LIKE @Pattern",
                            new { Pattern = partnerName + "%" })
                            .Where(p => p.Name.Equals($"{partnerName} Norway", StringComparison.OrdinalIgnoreCase) ||
                                        p.Name.Equals($"{partnerName} Sweden", StringComparison.OrdinalIgnoreCase) ||
                                        p.Name.Equals($"{partnerName} Denmark", StringComparison.OrdinalIgnoreCase) ||
                                        p.Name.Equals($"{partnerName} Finland", StringComparison.OrdinalIgnoreCase) ||
                                        p.Name.Equals(partnerName, StringComparison.OrdinalIgnoreCase))
                            .ToList();
                    }
                    else
                    {
                        string cName = GetCountryName(countryFilter);
                        string targetName = $"{partnerName} {cName}";
                        var p = connection.QueryFirstOrDefault<Partner>(
                            "SELECT * FROM Partners WHERE Name = @Name", new { Name = targetName });
                        if (p != null) targets.Add(p);
                    }

                    foreach (var targetPartner in targets)
                    {
                        var existingContact = connection.QueryFirstOrDefault<Contact>(
                            "SELECT * FROM Contacts WHERE PartnerId = @PartnerId AND Name = @Name",
                            new { PartnerId = targetPartner.Id, Name = contactName ?? string.Empty });

                        if (existingContact == null)
                        {
                            connection.Execute(@"
                                INSERT INTO Contacts (PartnerId, Name, Role, Email, Phone, Notes)
                                VALUES (@PartnerId, @Name, @Role, @Email, @Phone, @Notes);",
                                new
                                {
                                    PartnerId = targetPartner.Id,
                                    Name = contactName ?? string.Empty,
                                    Role = contactType,
                                    Email = email,
                                    Phone = phone,
                                    Notes = $"Support: {support}"
                                });
                        }
                    }
                }
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

                    string countryCode = GetString(row, headers, "Country");
                    string countryName = GetCountryName(countryCode);
                    string targetPartnerName = $"{partnerName} {countryName}";

                    var partner = connection.QueryFirstOrDefault<Partner>(
                        "SELECT * FROM Partners WHERE Name = @Name", new { Name = targetPartnerName });
                    if (partner == null) continue;

                    int period = GetInt(row, headers, "Period") ?? 2025;
                    decimal? arr = GetDecimal(row, headers, "Annual Recurring Revenue");
                    decimal? upfront = GetDecimal(row, headers, "Upfront Revenue");
                    double? oemAttach = GetDouble(row, headers, "OEM Service Attach Rate (%)");
                    double? onitioAttach = GetDouble(row, headers, "Onitio Service Attach Rate (%)");
                    double? margin = GetDouble(row, headers, "Lifecycle Margin (%)");
                    string targetMet = GetString(row, headers, "Target Met (Yes/No)");
                    string comments = GetString(row, headers, "Comments");

                    connection.Execute(@"
                        INSERT INTO KPI_Commercial (PartnerId, Period, AnnualRecurringRevenue, UpfrontRevenue, OemServiceAttachRate, OnitioServiceAttachRate, LifecycleMargin, TargetMet, Comments)
                        VALUES (@PartnerId, @Period, @AnnualRecurringRevenue, @UpfrontRevenue, @OemServiceAttachRate, @OnitioServiceAttachRate, @LifecycleMargin, @TargetMet, @Comments)
                        ON CONFLICT(PartnerId, Period) DO UPDATE SET
                            AnnualRecurringRevenue=@AnnualRecurringRevenue, UpfrontRevenue=@UpfrontRevenue, OemServiceAttachRate=@OemServiceAttachRate,
                            OnitioServiceAttachRate=@OnitioServiceAttachRate, LifecycleMargin=@LifecycleMargin, TargetMet=@TargetMet, Comments=@Comments;",
                        new
                        {
                            PartnerId = partner.Id,
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

            // 7. Import KPI Program Control
            if (workbook.TryGetWorksheet("KPI_Program_Control", out var progControlSheet))
            {
                var headers = GetHeaders(progControlSheet);
                var rows = progControlSheet.RowsUsed().Skip(1);

                foreach (var row in rows)
                {
                    string partnerName = GetString(row, headers, "Partner Name");
                    if (string.IsNullOrWhiteSpace(partnerName)) continue;

                    string countryCode = GetString(row, headers, "Country");
                    string countryName = GetCountryName(countryCode);
                    string targetPartnerName = $"{partnerName} {countryName}";

                    var partner = connection.QueryFirstOrDefault<Partner>(
                        "SELECT * FROM Partners WHERE Name = @Name", new { Name = targetPartnerName });
                    if (partner == null) continue;

                    int period = GetInt(row, headers, "Period") ?? 2025;
                    decimal? revOnitio = GetDecimal(row, headers, "Reported Revenue (Onitio)");
                    decimal? revOem = GetDecimal(row, headers, "Reported Revenue (OEM)");
                    double? variance = GetDouble(row, headers, "Variance (%)");
                    string rebate = GetString(row, headers, "Rebate Eligibility (Yes/No)");
                    double? progress = GetDouble(row, headers, "Tier Progress (%)");
                    string risk = GetString(row, headers, "Risk of Downgrade (Yes/No)");
                    string comments = GetString(row, headers, "Comments");

                    connection.Execute(@"
                        INSERT INTO KPI_ProgramControl (PartnerId, Period, ReportedRevenueOnitio, ReportedRevenueOem, Variance, RebateEligibility, TierProgress, RiskOfDowngrade, Comments)
                        VALUES (@PartnerId, @Period, @ReportedRevenueOnitio, @ReportedRevenueOem, @Variance, @RebateEligibility, @TierProgress, @RiskOfDowngrade, @Comments)
                        ON CONFLICT(PartnerId, Period) DO UPDATE SET
                            ReportedRevenueOnitio=@ReportedRevenueOnitio, ReportedRevenueOem=@ReportedRevenueOem, Variance=@Variance,
                            RebateEligibility=@RebateEligibility, TierProgress=@TierProgress, RiskOfDowngrade=@RiskOfDowngrade, Comments=@Comments;",
                        new
                        {
                            PartnerId = partner.Id,
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

            // 9. Import QBR Log to Activities (replicating across all partner country instances)
            if (workbook.TryGetWorksheet("QBR_Log", out var qbrSheet))
            {
                var headers = GetHeaders(qbrSheet);
                var rows = qbrSheet.RowsUsed().Skip(1);

                foreach (var row in rows)
                {
                    string partnerName = GetString(row, headers, "Partner Name");
                    if (string.IsNullOrWhiteSpace(partnerName)) continue;

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

                    // Only import QBR logs for the Norway-specific version of the partner (e.g., 'HP Norway')
                    string targetName = $"{partnerName} Norway";
                    var targetPartner = connection.QueryFirstOrDefault<Partner>(
                        "SELECT * FROM Partners WHERE Name = @Name", new { Name = targetName });

                    if (targetPartner != null)
                    {
                        var existingActivity = connection.QueryFirstOrDefault<Activity>(
                            "SELECT * FROM Activities WHERE PartnerId = @PartnerId AND Title = @Title AND ActivityDate = @ActivityDate",
                            new { PartnerId = targetPartner.Id, Title = title, ActivityDate = qbrDate?.ToString("yyyy-MM-dd HH:mm:ss") });

                        if (existingActivity == null)
                        {
                            connection.Execute(@"
                                INSERT INTO Activities (PartnerId, ActivityDate, Type, Title, Description, Owner, Status, DueDate)
                                VALUES (@PartnerId, @ActivityDate, 'QBR', @Title, @Description, @Owner, @Status, @DueDate);",
                                new
                                {
                                    PartnerId = targetPartner.Id,
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
        }

        public static void ExportData(string filePath)
        {
            using var workbook = new XLWorkbook();
            using var connection = DatabaseHelper.GetConnection();
            connection.Open();

            // Export 6 tables
            WriteTable(workbook.Worksheets.Add("Partners"), connection.Query<Partner>("SELECT * FROM Partners").ToList());
            WriteTable(workbook.Worksheets.Add("Contacts"), connection.Query<Contact>("SELECT * FROM Contacts").ToList());
            WriteTable(workbook.Worksheets.Add("KPI Commercial"), connection.Query<KPI_Commercial>("SELECT * FROM KPI_Commercial").ToList());
            WriteTable(workbook.Worksheets.Add("KPI Program Control"), connection.Query<KPI_ProgramControl>("SELECT * FROM KPI_ProgramControl").ToList());
            WriteTable(workbook.Worksheets.Add("Activities"), connection.Query<Activity>("SELECT * FROM Activities").ToList());
            WriteTable(workbook.Worksheets.Add("Documents"), connection.Query<Document>("SELECT * FROM Documents").ToList());

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
