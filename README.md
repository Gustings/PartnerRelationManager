# Partner Relation Manager (SRM Desktop App)

A modern, premium Windows desktop application built with **.NET 9.0** and **WPF** to manage partner relationships across multiple countries, replacing fragmented manual Excel tracking.

Designed with **WPF-UI** for native Windows 11 Fluent styling (supporting dark/light theme options, smooth animations, and clean cards) and backed by a local SQLite database.

---

## 🌟 Key Features

1. **Onboarding Setup**: On first launch, select which partners (e.g., HP, Lenovo, Dell) and countries (e.g., Norway, Sweden, Denmark, Finland) you want to manage.
2. **Dashboard Overview**: Get high-level metrics, compliance alerts (expiring certs, downgrade risks, program deviations), and a timeline of recent/upcoming QBRs.
3. **Comprehensive Profile Management**:
   - **Internal Ownership & Category**: Set relationship owners and categories.
   - **Contact Directory**: Manage partner contact points (Sales, Technical, Support, etc.).
   - **Product Lifecycle Mapping**: Map products/services linked to partners and track their active/outgoing status.
4. **Campaigns & Reference Cases**:
   - Track planned/active co-marketing campaigns and budgets.
   - Catalog customer reference cases and approval status.
5. **Multi-Country KPI Logging**: Tabbed forms with inline editing and InvariantCulture parsing for:
   - **Commercial KPIs** (ARR, Upfront Revenue, Attach Rates, Margins)
   - **Compliance KPIs** (Certifications required vs covered, expirations)
   - **Program Control** (Reported revenues, rebate eligibility, tier progress)
   - **Sustainability & ESG** (Recycling programs, carbon/ESG compliance status)
   - **Operational & Strategic** (DOA rates, deployment lead times, fit assessments)
6. **Local Document Hub**: Link local files (PDFs, Word documents, certificates) to a partner. The app copies documents to local application storage and links them in the database for easy default opening.
7. **Excel Data Exchange**: Seeding/updating your workspace data from the KPI Tracker Excel workbook and exporting all database logs back to a multi-sheet spreadsheet (via ClosedXML).
8. **Auto-Updater**: The application automatically checks for newer releases from GitHub on boot, downloads installer assets in the background, and runs a silent update sequence on exit.

---

## 🛠️ Technology Stack

- **Framework**: .NET 9.0 (WPF)
- **UI library**: `Wpf.Ui` (v4.3.0)
- **ORM & Database**: `Dapper` + `Microsoft.Data.Sqlite`
- **Excel Exchange**: `ClosedXML`
- **Installer**: Inno Setup 6

## 🚀 Installation & Auto-Updates

1. **Download & Install**:
   - Go to the [Releases](https://github.com/Gustings/PartnerRelationManager/releases) page on GitHub.
   - Download the latest `PartnerRelationManagerSetup-*.exe` installer asset.
   - Run the installer. It will install the application locally under your user profile without requiring administrator privileges.
2. **Automatic Updates**:
   - Every time the application boots up, it automatically checks the GitHub repository in the background for a newer release.
   - If an update is found, it will prompt you with the release notes and offer to download/install it silently.
   - If accepted, the updater will download and apply the update automatically, then restart the application.
