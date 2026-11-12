<div align="center">

# 🛡️ API Contract Guardian
### Protect Your ASP.NET Core APIs from Breaking Changes Directly Inside Visual Studio

[![Visual Studio](https://img.shields.io/badge/Visual%20Studio-2022-5C2D91?style=for-the-badge&logo=visual-studio&logoColor=white)](https://visualstudio.microsoft.com/)
[![ASP.NET Core](https://img.shields.io/badge/ASP.NET%20Core-6.0%20%7C%207.0%20%7C%208.0-512BD4?style=for-the-badge&logo=.net&logoColor=white)](https://dotnet.microsoft.com/)
[![OpenAPI / Swagger](https://img.shields.io/badge/OpenAPI-Swagger-85EA2D?style=for-the-badge&logo=swagger&logoColor=black)](https://swagger.io/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg?style=for-the-badge)](https://opensource.org/licenses/MIT)

**A dedicated Visual Studio Extension that safeguards ASP.NET Core projects against breaking API changes—preventing developers from shipping updates that break dependent client applications.**

[Key Features](#-key-features) • [Installation](#-installation) • [How It Works](#-how-it-works) • [Publish Protection](#-killer-feature-pre-publish-protection) • [Contributing](#-contributing)

</div>

---

## 📖 Overview

In fast-paced development environments, a seemingly minor modification—such as renaming a parameter, changing a data type, or removing an endpoint in an **ASP.NET Core** project—can silently break mobile apps, frontend web clients, and third-party integrations.

**API Contract Guardian** acts as a real-time bodyguard inside **Visual Studio**. It continuously monitors your API contracts, compares them against previous versions or OpenAPI specifications, and alerts you immediately before changes are committed or deployed to production.

---

## ✨ Key Features

* 🔍 **Cross-Version API Diffing:** Accurately track all modifications made to your API compared to a previous stable release.
* 🚫 **Deleted Endpoint Detection:** Instant alerts when an active `Controller` route or `Action` endpoint is removed or altered.
* 🏷️ **Parameter Name Monitoring:** Detects any renaming of Route, Query, Header, or Body parameters.
* 🧬 **Data Type Inspection:** Catches changes in primitive or complex types (e.g., changing `int` to `string` or modifying DTO properties).
* 🔄 **Swagger / OpenAPI Comparison:** Full support for comparing local or remote `swagger.json` and `openapi.yaml` files against your live codebase.
* ⚠️ **Breaking Change Classification:** Intelligently distinguishes between safe, non-breaking additions (like optional fields) and dangerous breaking changes.
* 📊 **Detailed Impact Reports:** Generates a clean, structured report inside a dedicated Visual Studio tool window detailing the location, severity, and impact of each change.
* 👀 **Pre-Commit & Pre-Deploy Preview:** Review all contract modifications in an intuitive visual diff UI before finalizing your work.

---

## 🛡️ Killer Feature: Pre-Publish Protection

> **Prevents developers from publishing any update that breaks client applications relying on the API.**

Whenever you trigger a **Publish** profile or build in **Release** mode, the extension automatically runs a comprehensive contract verification check:
* If **Breaking Changes** are detected, the publish pipeline is **halted**, and a detailed warning dialog is presented.
* Developers can inspect and resolve the breaking changes immediately, or explicitly acknowledge and override the block if the change is intentional for a new major API version.

---

## 🚀 Installation

### Via Visual Studio Marketplace *(Coming Soon)*
1. Open **Visual Studio**.
2. Navigate to `Extensions` > `Manage Extensions` from the top menu.
3. Search for **`API Contract Guardian`**.
4. Click **Download** and restart Visual Studio to complete the installation.

### Manual Installation (via `.vsix`)
1. Go to the [Releases](../../releases) page of this repository.
2. Download the latest `APIContractGuardian.vsix` package.
3. Close all running instances of Visual Studio.
4. Double-click the `.vsix` file and follow the VSIX Installer prompts.

---

## ⚙️ How It Works

1. **Open Your Solution:** Load any `ASP.NET Core Web API` project in Visual Studio.
2. **Set Your Baseline:**
   * Navigate to `Tools` > `API Contract Guardian` > `Set Baseline`.
   * Choose an existing `swagger.json` file as your contract baseline, or link it directly to a Git branch (e.g., `main` or `production`).
3. **Code Normally:** As you edit Controllers, Endpoints, and DTOs, the extension analyzes your changes in the background and surfaces warnings directly in the **Error List** and inline editor squiggles.
4. **Inspect the Report:** Open `View` > `Other Windows` > `API Contract Report` to review a full breakdown of changes before committing or publishing.

---

## 📋 Detection Matrix Examples

| Change Type | Before | After | Verdict |
| :--- | :--- | :--- | :--- |
| **Endpoint Removed** | `GET /api/users/{id}` | *(Deleted)* | ❌ **Breaking Change** |
| **Parameter Renamed** | `GetUser(int userId)` | `GetUser(int id)` | ❌ **Breaking Change** |
| **Data Type Changed** | `public int Age { get; set; }` | `public string Age { get; set; }` | ❌ **Breaking Change** |
| **Required Field Added** | `CreateUserDto` (no `Phone`) | Added `[Required] string Phone` | ❌ **Breaking Change** |
| **New Endpoint Added** | — | `POST /api/users/export` | ✅ **Safe Change** |
| **Optional Field Added** | `UserDto` | Added `string? Bio` | ✅ **Safe Change** |

---

## 🤝 Contributing

Contributions, issues, and feature requests are welcome!
1. **Fork** the repository.
2. Create your feature branch (`git checkout -b feature/AmazingFeature`).
3. Commit your changes (`git commit -m 'Add some AmazingFeature'`).
4. Push to the branch (`git push origin feature/AmazingFeature`).
5. Open a **Pull Request**.

---

## 📄 License

Distributed under the **MIT License**. See [LICENSE](LICENSE) for more information.

<div align="center">
  <sub>Built to keep ASP.NET Core APIs stable and client apps safe.</sub>
</div>