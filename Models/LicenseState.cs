using Newtonsoft.Json;

namespace vs2026_plugin.Models
{
    /// <summary>
    /// Minimal model of the backend <c>GET /license/state</c> response. Only the
    /// fields needed to detect the license plan type are mapped; the rest is ignored.
    /// Mirrors vscode-extension/src/xygeni/service/license-state.ts.
    /// </summary>
    public class LicenseState
    {
        [JsonProperty("dataLicensePlan")]
        public DataLicensePlan DataLicensePlan { get; set; }
    }

    public class DataLicensePlan
    {
        /// <summary>License plan type, e.g. "free", "trial", "enterprise".</summary>
        [JsonProperty("licenseType")]
        public string LicenseType { get; set; }
    }
}
