using System.Diagnostics;
using System.ServiceProcess;

namespace StarResonanceDpsAnalysis.WinForm.Plugin
{
    public static class NpcapInstaller
    {
        /// <summary>
        /// Only valid for Npcap 0.96 and earlier builds that still accept `/S`.
        /// Community editions ≥ 0.97 removed silent installation support.
        /// </summary>
        public static async Task<int> InstallNpcapSilentAsync(
            string installerPath,
            string extraArgs = "/winpcap_mode=yes /loopback_support=yes /admin_only=no")
        {
            if (!File.Exists(installerPath))
                throw new FileNotFoundException("Npcap installer not found.", installerPath);

            // Optional guard: detect unsupported versions so `/S` does not hang.
            var ver = TryGetVersion(installerPath);
            if (ver != null && ver > new Version(0, 96))
                throw new InvalidOperationException($"Installer version {ver} does not support silent mode for the community edition.");

            var psi = new ProcessStartInfo
            {
                FileName = installerPath,
                Arguments = $"/S {extraArgs}",
                UseShellExecute = true,
                Verb = "runas" // requires elevation
            };

            using var p = Process.Start(psi)!;
            await p.WaitForExitAsync();
            return p.ExitCode; // 0 = success
        }

        public static bool IsNpcapInstalled()
            => ServiceController.GetServices()
               .Any(s => s.ServiceName.Equals("npcap", StringComparison.OrdinalIgnoreCase));

        public static async Task<int> UninstallNpcapSilentAsync()
        {
            string uninst = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Npcap", "uninstall.exe");

            if (!File.Exists(uninst)) return 0;

            var psi = new ProcessStartInfo
            {
                FileName = uninst,
                Arguments = "/S",
                UseShellExecute = true,
                Verb = "runas"
            };
            using var p = Process.Start(psi)!;
            await p.WaitForExitAsync();
            return p.ExitCode;
        }

        private static Version? TryGetVersion(string path)
        {
            var fvi = FileVersionInfo.GetVersionInfo(path);
            string v = fvi.FileVersion ?? fvi.ProductVersion ?? "";
            var cleaned = new string((v + ".0.0.0").TakeWhile(ch => char.IsDigit(ch) || ch == '.').ToArray());
            return Version.TryParse(cleaned, out var ver) ? ver : null;
        }
    }
}
