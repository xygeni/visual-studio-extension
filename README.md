# Xygeni Security Extension for Visual Studio

**Secure your codebase with Secrets, SAST, SCA, IaC & Supply Chain scanning directly within your VS Code environment.**

Xygeni Security Scanner is a powerful extension that brings comprehensive security scanning to your fingertips. It integrates seamlessly with your development workflow, allowing you to identify and remediate security vulnerabilities early in the process.



## Key Features

- **Comprehensive Scanning:** Detect a wide range of security issues:
  - **Secrets:** Find hardcoded credentials, API keys, and other sensitive data.
  - **SAST (Static Application Security Testing):** Analyze your source code for common vulnerabilities.
  - **SCA (Software Composition Analysis):** Identify vulnerabilities in your open-source dependencies.
  - **IaC (Infrastructure as Code):** Scan your IaC files (e.g., Terraform, CloudFormation) for misconfigurations.
  - **Misconfigurations:** Detect security misconfigurations in your application and services.
- **Remediation actions for SCA and SAST Issues:** Automatically detect and provide remediation guidance for vulnerabilities found in your source code and dependencies, enabling quick fixes directly within VS Code.
- **Seamless Integration:** The extension adds a dedicated Xygeni view to your activity bar for easy access.
- **Guided Setup:** A simple configuration process to connect to the Xygeni service.
- **In-Editor Issue Highlighting:** View security findings directly in your code, making it easy to pinpoint and fix issues.
- **Detailed Vulnerability Information:** Get rich details for each identified issue, including severity, description, and remediation guidance.
- **Proxy Support:** Configure the extension to work with your corporate proxy.

## Installation

1.  Open the **Extensions** view in Visual Studio (`Ctrl+Shift+X`).
2.  Search for `Xygeni Security`.
3.  Click **Install**.

## Getting Started

1.  **Open the Xygeni View:** After installation, click on the Xygeni icon in the main bar.
2.  **Configure the Extension:**
    -   You will be prompted to configure the connection to the Xygeni service.
    -   Obtain an API token from your [Xygeni Dashboard](https://in.xygeni.io/dashboard/configuration-panel/profile). If you don't have an account, you can sign up for a trial.
    -   Enter the Xygeni API URL and your token in the configuration view.
3.  **Run a Scan:**
    -   Once configured, the "Scan" button will be enabled.
    -   Click the "Run Scanner" button to initiate a scan of your workspace.
4.  **View Results:**
    -   Scan results will be displayed in the Xygeni Explorer view, categorized by type (SAST, SCA, Secrets, etc.).
    -   Click on an issue to see detailed information and navigate to the affected file and line.

## Extension Settings

*   **Xygeni API URL**: The URL of the Xygeni API server.
*   **Xygeni API Token**: The API token for authentication.

## Support

For support, questions, or feedback, please contact us at [support@xygeni.io](mailto:support@xygeni.io).

## License

This extension is licensed under the [MIT License](https://github.com/xygeni/vscode-extension/blob/main/LICENSE).
