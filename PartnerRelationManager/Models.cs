using System;

namespace PartnerRelationManager.Models
{
    public class Partner
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string InternalOwner { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string StrategicImportance { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string BusinessAreas { get; set; } = string.Empty;
        
        // Country-specific fields & program details directly on Partner
        public string CountryCode { get; set; } = string.Empty;
        public string PartnerProgram { get; set; } = string.Empty;
        public string CurrentTier { get; set; } = string.Empty;
        public string PartnerIdentification { get; set; } = string.Empty;
        public string QbrFrequency { get; set; } = string.Empty;
        public string Comments { get; set; } = string.Empty;
    }

    public class Contact
    {
        public int Id { get; set; }
        public int PartnerId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
    }

    public class Activity
    {
        public int Id { get; set; }
        public int PartnerId { get; set; }
        public DateTime? ActivityDate { get; set; }
        public string Type { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Owner { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime? DueDate { get; set; }
    }

    public class Document
    {
        public int Id { get; set; }
        public int PartnerId { get; set; }
        public int Period { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public DateTime UploadDate { get; set; }
        public string AssetType { get; set; } = string.Empty;
    }

    public class KPI_Commercial
    {
        public int PartnerId { get; set; }
        public int Period { get; set; }
        public decimal? AnnualRecurringRevenue { get; set; }
        public decimal? UpfrontRevenue { get; set; }
        public double? OemServiceAttachRate { get; set; }
        public double? OnitioServiceAttachRate { get; set; }
        public double? LifecycleMargin { get; set; }
        public string TargetMet { get; set; } = string.Empty;
        public string Comments { get; set; } = string.Empty;
    }

    public class KPI_ProgramControl
    {
        public int PartnerId { get; set; }
        public int Period { get; set; }
        public decimal? ReportedRevenueOnitio { get; set; }
        public decimal? ReportedRevenueOem { get; set; }
        public double? Variance { get; set; }
        public string RebateEligibility { get; set; } = string.Empty;
        public double? TierProgress { get; set; }
        public string RiskOfDowngrade { get; set; } = string.Empty;
        public string Comments { get; set; } = string.Empty;
    }

    public class KPI_Compliance
    {
        public int PartnerId { get; set; }
        public int Period { get; set; }
        public string CertificationsNeeded { get; set; } = string.Empty;
        public int? RequiredCertifications { get; set; }
        public double? CertificationsCovered { get; set; }
        public int? CertsExpiring3Months { get; set; }
        public int? CertsExpiring6Months { get; set; }
        public int? CertsExpiring12Months { get; set; }
        public string ProgramComplianceStatus { get; set; } = string.Empty;
        public string TierRisk { get; set; } = string.Empty;
        public string Comments { get; set; } = string.Empty;
    }
}
