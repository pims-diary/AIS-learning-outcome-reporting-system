# Learning Outcome Achievement Reporting System

A .NET 8 web-based system for rubric-based marking and automated Learning Outcome (LO) achievement reporting (AIS/NZQA-aligned).

## Project Overview
This system supports:
- Course-level Learning Outcomes (LOs) managed by Admin
- Rubric management (create/edit/view) and performance levels (e.g., 0–4)
- LO mapping & weighting (criteria ↔ LOs + weights)
- Student marking using rubric criteria
- Automatic calculation of weighted scores and LO achievement %
- Report generation and PDF export

## Tech Stack
- Backend: .NET 8 (C#)
- Frontend: ASP.NET Core MVC (Razor Views)
- Database: SQL Server
- ORM: Entity Framework Core (Code-First), LINQ
- Auth: Custom cookie-based authentication with BCrypt password hashing (RBAC: Admin / Lecturer / Moderator)
- Document Processing: Open XML SDK (Word), EPPlus (Excel)
- PDF: QuestPDF, iText 7 (itext7 / itext7.pdfhtml)

## Local Setup (Developer)
### Prerequisites
- .NET SDK 8
- Visual Studio 2022 (or VS Code)
- SQL Server / SQL Server Express / LocalDB

### Run the Web App
From the repo root:
```bash
dotnet restore
dotnet run --project AIS_LO_System
