using System.Management.Automation;
using System.ServiceProcess;
using System.Security.Principal;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace HotspotMonitorService
{
    [System.Runtime.Versioning.SupportedOSPlatform("Windows")]
    public class Worker(ILogger<Worker> logger) : BackgroundService
    {
        private CancellationTokenSource? _workCancellationTokenSource;
        private bool? _inProcessWinRtAvailable;
        private bool _fallbackLoggedOnce;
        private readonly object _winrtLock = new();
        public event Action<int>? ClientCountChanged;
        private int _lastClientCount = -2; // -2 means unknown/not-initialized

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Create a linked token source that can be cancelled by either the service stop token or the work stop token
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, _workCancellationTokenSource?.Token ?? CancellationToken.None);
            var linkedToken = linkedCts.Token;

            while (!linkedToken.IsCancellationRequested)
            {
                logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);

                bool isHotspotActive = CheckHotspotStatus();
                logger.LogInformation("Hotspot status: {status}", isHotspotActive ? "Active" : "Inactive");

                if (!isHotspotActive)
                {
                    logger.LogWarning("Hotspot is inactive. Attempting to reactivate...");
                    ReactivateHotspot();
                }

                try
                {
                    // Check the current connected client count and notify listeners on change
                    try
                    {
                        int clientCount = GetConnectedClientCount();
                        if (clientCount != _lastClientCount)
                        {
                            _lastClientCount = clientCount;
                            ClientCountChanged?.Invoke(clientCount);
                        }
                    }
                    catch (Exception ex) { logger.LogDebug(ex, "Failed to update client count in loop"); }

                    // Wait for 5 seconds before checking again, or until the task is cancelled
                    await Task.Delay(5000, linkedToken);
                }
                catch (TaskCanceledException)
                {
                    // Task was cancelled, break out of the loop
                    break;
                }
            }
        }

        private (string Output, string Error) RunScriptViaWindowsPowerShell(string script)
        {
            // Write script to a temporary file to avoid escaping issues
            var tempFile = Path.Combine(Path.GetTempPath(), $"Hotspot_Script_{Guid.NewGuid():N}.ps1");
            File.WriteAllText(tempFile, script, Encoding.UTF8);

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{tempFile}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            var output = new StringBuilder();
            var error = new StringBuilder();

            using (var proc = new Process { StartInfo = psi })
            {
                try
                {
                    proc.Start();
                    output.Append(proc.StandardOutput.ReadToEnd());
                    error.Append(proc.StandardError.ReadToEnd());
                    proc.WaitForExit(30_000); // 30s limit
                }
                catch (Exception ex)
                {
                    error.AppendLine(ex.ToString());
                }
                finally
                {
                    try { File.Delete(tempFile); } catch { }
                }
            }

            return (Output: output.ToString(), Error: error.ToString());
        }

        private bool IsRunningAsAdmin()
        {
            try
            {
                var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        public void StartWork()
        {
            if (_workCancellationTokenSource == null || _workCancellationTokenSource.IsCancellationRequested)
            {
                _workCancellationTokenSource = new CancellationTokenSource();
                // Trigger the background work to start
                _ = ExecuteAsync(_workCancellationTokenSource.Token);
            }
        }

        public void StopWork()
        {
            if (_workCancellationTokenSource != null && !_workCancellationTokenSource.IsCancellationRequested)
            {
                // Cancel the background work
                _workCancellationTokenSource.Cancel();
                // Deactivate the hotspot
                DeactivateHotspot();
            }
        }

        /// <summary>
        /// Returns number of devices currently connected to the tethering network.
        /// Returns -1 when the call fails or is not available.
        /// </summary>
        public int GetConnectedClientCount()
        {
            try
            {
                // If unknown, check if in-process PowerShell supports WinRT types (thread-safe)
                if (!_inProcessWinRtAvailable.HasValue)
                {
                    lock (_winrtLock)
                    {
                        if (!_inProcessWinRtAvailable.HasValue)
                        {
                            _inProcessWinRtAvailable = CheckInProcessWinRtAvailability();
                        }
                    }
                }

                var script = @"
                    $connectionProfile = [Windows.Networking.Connectivity.NetworkInformation,Windows.Networking.Connectivity,ContentType=WindowsRuntime]::GetInternetConnectionProfile()
                    if ($null -eq $connectionProfile) { -1; return }
                    $tetheringManager = [Windows.Networking.NetworkOperators.NetworkOperatorTetheringManager,Windows.Networking.NetworkOperators,ContentType=WindowsRuntime]::CreateFromConnectionProfile($connectionProfile)
                    if ($null -eq $tetheringManager) { -1; return }
                    $tetheringManager.ClientCount
                ";

                // If in-process WinRT is not available, use the powershell.exe fallback directly.
                if (_inProcessWinRtAvailable == false)
                {
                    lock (_winrtLock)
                    {
                        if (!_fallbackLoggedOnce)
                        {
                            logger.LogInformation("GetConnectedClientCount: Using PowerShell.exe fallback for WinRT query.");
                            _fallbackLoggedOnce = true;
                        }
                    }
                    var fallbackResult = RunScriptViaWindowsPowerShell(script);
                    if (!string.IsNullOrWhiteSpace(fallbackResult.Error))
                    {
                        logger.LogWarning("GetConnectedClientCount fallback had errors: {0}", fallbackResult.Error);
                    }
                    var outTextFallback = fallbackResult.Output ?? string.Empty;
                    var mFallback = Regex.Match(outTextFallback, "\\b(-?\\d+)\\b");
                    if (mFallback.Success && int.TryParse(mFallback.Groups[1].Value, out var valFallback)) return valFallback;
                    var mFallback2 = Regex.Match(outTextFallback, "\\d+");
                    if (mFallback2.Success && int.TryParse(mFallback2.Value, out var valFallback2)) return valFallback2;
                    return -1;
                }

                using PowerShell ps = PowerShell.Create();
                ps.AddScript("Set-ExecutionPolicy -Scope Process -ExecutionPolicy RemoteSigned -Force");
                ps.AddScript(script);

                var result = ps.Invoke();
                if (ps.HadErrors)
                {
                    var errors = string.Join("\n", ps.Streams.Error.Select(e => e.ToString()));
                    logger.LogWarning("GetConnectedClientCount: PowerShell had errors: {errors}", errors);
                    // Mark WinRT as unavailable for in-process usage and fall back next time; log the fallback once.
                    lock (_winrtLock)
                    {
                        _inProcessWinRtAvailable = false;
                        if (!_fallbackLoggedOnce)
                        {
                            logger.LogInformation("GetConnectedClientCount: Falling back to PowerShell.exe fallback for WinRT query");
                            _fallbackLoggedOnce = true;
                        }
                    }
                    var fallbackResult = RunScriptViaWindowsPowerShell(script);
                    if (!string.IsNullOrWhiteSpace(fallbackResult.Error))
                    {
                        logger.LogWarning("GetConnectedClientCount fallback had errors: {0}", fallbackResult.Error);
                    }
                    var outText = fallbackResult.Output ?? string.Empty;
                    var m = Regex.Match(outText, "\\b(-?\\d+)\\b");
                    if (m.Success && int.TryParse(m.Groups[1].Value, out var val)) return val;
                    var m2 = Regex.Match(outText, "\\d+");
                    if (m2.Success && int.TryParse(m2.Value, out var val2)) return val2;
                    return -1;
                }

                if (result != null && result.Count > 0 && int.TryParse(result[0].ToString(), out var count))
                {
                    // If in-process succeeded, clear fallback-logged marker and mark WinRT available
                    lock (_winrtLock) { _fallbackLoggedOnce = false; _inProcessWinRtAvailable = true; }
                    return count;
                }
                return -1;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to get connected client count.");
                return -1;
            }
        }

        /// <summary>
        /// Returns a list of connected clients with Name/IPs/MAC.
        /// </summary>
        public List<ConnectedClient> GetConnectedClients()
        {
            var results = new List<ConnectedClient>();
            try
            {
                // Ensure WinRT availability is checked
                if (!_inProcessWinRtAvailable.HasValue)
                {
                    lock (_winrtLock)
                    {
                        if (!_inProcessWinRtAvailable.HasValue)
                        {
                            _inProcessWinRtAvailable = CheckInProcessWinRtAvailability();
                        }
                    }
                }

                var script = @"
                    $connectionProfile = [Windows.Networking.Connectivity.NetworkInformation,Windows.Networking.Connectivity,ContentType=WindowsRuntime]::GetInternetConnectionProfile()
                    if ($null -eq $connectionProfile) { return }
                    $tetheringManager = [Windows.Networking.NetworkOperators.NetworkOperatorTetheringManager,Windows.Networking.NetworkOperators,ContentType=WindowsRuntime]::CreateFromConnectionProfile($connectionProfile)
                    if ($null -eq $tetheringManager) { return }
                    $clients = $tetheringManager.GetTetheringClients()
                    foreach ($c in $clients) {
                        $mac = [string]$c.MacAddress
                        $name = $null
                        $ipAddrs = @()
                        foreach ($hn in $c.HostNames) {
                            $cn = $hn.CanonicalName
                            if ($cn -match '^\d{1,3}(?:\.\d{1,3}){3}$' -or $cn -match '^[0-9a-f:]+$') {
                                $ipAddrs += $cn
                            }
                            if (-not $name) {
                                if ($hn.DisplayName -and $hn.DisplayName.Trim()) { $name = $hn.DisplayName }
                                elseif ($hn.RawName -and $hn.RawName.Trim()) { $name = $hn.RawName }
                                elseif ($cn) { $name = $cn }
                            }
                        }
                        $hosts = $name -join ',' # name is single, but keep consistent output format
                        $ips = ($ipAddrs -join ',')
                        Write-Output ($mac + '|' + $hosts + '|' + $ips)
                    }
                ";

                if (_inProcessWinRtAvailable == false)
                {
                    if (!_fallbackLoggedOnce)
                    {
                        lock (_winrtLock) { if (!_fallbackLoggedOnce) { logger.LogInformation("GetConnectedClients: Using PowerShell.exe fallback for WinRT query."); _fallbackLoggedOnce = true; } }
                    }
                    var fallbackResult = RunScriptViaWindowsPowerShell(script);
                    if (!string.IsNullOrWhiteSpace(fallbackResult.Error)) logger.LogWarning("GetConnectedClients fallback had errors: {0}", fallbackResult.Error);
                    var outTextFallback = fallbackResult.Output ?? string.Empty;
                    var lines = outTextFallback.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        var parts = line.Split('|');
                        if (parts.Length >= 1)
                        {
                            var mac = parts[0].Trim().ToUpperInvariant();
                            var name = parts.Length > 1 ? parts[1].Trim() : string.Empty;
                            var ips = parts.Length > 2 ? parts[2].Trim() : string.Empty;
                            results.Add(new ConnectedClient { MacAddress = mac, DeviceName = name, IpAddresses = ips });
                        }
                    }
                    return results;
                }

                using PowerShell ps = PowerShell.Create();
                ps.AddScript("Set-ExecutionPolicy -Scope Process -ExecutionPolicy RemoteSigned -Force");
                ps.AddScript(script);
                var result = ps.Invoke();
                if (ps.HadErrors)
                {
                    var errors = string.Join("\n", ps.Streams.Error.Select(e => e.ToString()));
                    logger.LogWarning("GetConnectedClients: PowerShell had errors: {errors}", errors);
                    lock (_winrtLock) { _inProcessWinRtAvailable = false; if (!_fallbackLoggedOnce) { logger.LogInformation("GetConnectedClients: Falling back to PowerShell.exe fallback for WinRT query"); _fallbackLoggedOnce = true; } }
                    var fallbackResult = RunScriptViaWindowsPowerShell(script);
                    if (!string.IsNullOrWhiteSpace(fallbackResult.Error)) logger.LogWarning("GetConnectedClients fallback had errors: {0}", fallbackResult.Error);
                    var outText = fallbackResult.Output ?? string.Empty;
                    var lines = outText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        var parts = line.Split('|');
                        if (parts.Length >= 1)
                        {
                            var mac = parts[0].Trim().ToUpperInvariant();
                            var name = parts.Length > 1 ? parts[1].Trim() : string.Empty;
                            var ips = parts.Length > 2 ? parts[2].Trim() : string.Empty;
                            results.Add(new ConnectedClient { MacAddress = mac, DeviceName = name, IpAddresses = ips });
                        }
                    }
                    return results;
                }

                if (result != null && result.Count > 0)
                {
                    lock (_winrtLock) { _fallbackLoggedOnce = false; _inProcessWinRtAvailable = true; }
                    foreach (var r in result)
                    {
                        var s = r?.ToString() ?? string.Empty;
                        var parts = s.Split('|');
                        if (parts.Length >= 1)
                        {
                            var mac = parts[0].Trim().ToUpperInvariant();
                            var name = parts.Length > 1 ? parts[1].Trim() : string.Empty;
                            var ips = parts.Length > 2 ? parts[2].Trim() : string.Empty;
                            results.Add(new ConnectedClient { MacAddress = mac, DeviceName = name, IpAddresses = ips });
                        }
                    }
                }
                return results;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to get connected clients.");
                return results;
            }
        }

        private bool CheckInProcessWinRtAvailability()
        {
            try
            {
                using PowerShell ps = PowerShell.Create();
                ps.AddScript("Add-Type -AssemblyName System.Runtime.WindowsRuntime -ErrorAction SilentlyContinue");
                ps.AddScript("[System.Type]::GetType('Windows.Networking.Connectivity.NetworkInformation,Windows.Networking.Connectivity,ContentType=WindowsRuntime') -ne $null");
                var res = ps.Invoke();
                if (ps.HadErrors) return false;
                if (res != null && res.Count > 0 && bool.TryParse(res[0].ToString(), out var b)) return b;
                return false;
            }
            catch
            {
                return false;
            }
        }


        private bool CheckHotspotStatus()
        {
            using PowerShell ps = PowerShell.Create();
            ps.AddScript("Set-ExecutionPolicy -Scope Process -ExecutionPolicy RemoteSigned -Force");
            ps.AddScript("Get-NetAdapter | Where-Object {$_.InterfaceDescription -like '*Wi-Fi Direct Virtual Adapter*'} | Select-Object -ExpandProperty Status");

            var result = ps.Invoke();
            if (result != null && result.Count > 0) return result[0].ToString().Equals("Up", StringComparison.OrdinalIgnoreCase);
            else
            {
                logger.LogError("Failed to check hotspot status.");
                return false;
            }
        }

        private void ReactivateHotspot()
        {
            using PowerShell ps = PowerShell.Create();

            // Set the execution policy to RemoteSigned
            ps.AddScript("Set-ExecutionPolicy -Scope Process -ExecutionPolicy RemoteSigned -Force");
            ps.Invoke();

            // Check and start the WinRM service (requires Administrator privileges)
            try
            {
                ps.AddScript("Get-Service WinRM -ErrorAction Stop");
                var serviceResult = ps.Invoke();
                if (serviceResult.Count > 0 && serviceResult[0].BaseObject is ServiceController winrmService && winrmService.Status != ServiceControllerStatus.Running)
                {
                    if (!IsRunningAsAdmin())
                    {
                        logger.LogWarning("Insufficient permissions to start WinRM service. Please run the HotspotMonitor service as Administrator or grant it appropriate privileges.");
                    }
                    else
                    {
                        logger.LogInformation("Starting WinRM service...");
                        ps.Commands.Clear();
                        ps.AddScript("Start-Service WinRM");
                        ps.Invoke();
                        if (ps.HadErrors)
                        {
                            logger.LogError("Failed to start WinRM service. Error: {error}", string.Join("\n", ps.Streams.Error.Select(e => e.ToString())));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Cannot access WinRM service: {message}", ex.Message);
            }

            // Try to detect the WindowsCompatibility module; we'll fallback to launching windows powershell if missing

            // Define the script to reactivate the hotspot
            string hotspotScript = @"
                    [Windows.System.UserProfile.LockScreen,Windows.System.UserProfile,ContentType=WindowsRuntime] | Out-Null
                    Add-Type -AssemblyName System.Runtime.WindowsRuntime
                    $asTaskGeneric = ([System.WindowsRuntimeSystemExtensions].GetMethods() | ? { $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' })[0]
                    Function Await($WinRtTask, $ResultType) {
                        $asTask = $asTaskGeneric.MakeGenericMethod($ResultType)
                        $netTask = $asTask.Invoke($null, @($WinRtTask))
                        $netTask.Wait(-1) | Out-Null
                        $netTask.Result
                    }
                    $connectionProfile = [Windows.Networking.Connectivity.NetworkInformation,Windows.Networking.Connectivity,ContentType=WindowsRuntime]::GetInternetConnectionProfile()
                    $tetheringManager = [Windows.Networking.NetworkOperators.NetworkOperatorTetheringManager,Windows.Networking.NetworkOperators,ContentType=WindowsRuntime]::CreateFromConnectionProfile($connectionProfile)
                    Await ($tetheringManager.StartTetheringAsync())([Windows.Networking.NetworkOperators.NetworkOperatorTetheringOperationResult])
                ";

            // Detect whether Invoke-WinCommand (WindowsCompatibility) is available; fall back to running powershell.exe
            bool hasWindowsCompatibility = false;
            try
            {
                ps.Commands.Clear();
                ps.AddScript("Get-Command Invoke-WinCommand -ErrorAction SilentlyContinue");
                var cmd = ps.Invoke();
                if (cmd != null && cmd.Count > 0)
                {
                    hasWindowsCompatibility = true;
                }
                else
                {
                    ps.Commands.Clear();
                    ps.AddScript("Get-Module -ListAvailable WindowsCompatibility");
                    var mod = ps.Invoke();
                    hasWindowsCompatibility = mod != null && mod.Count > 0;
                }
            }
            catch
            {
                hasWindowsCompatibility = false;
            }

            // Execute the script. Prefer Invoke-WinCommand if available (WindowsCompatibility), otherwise run via powershell.exe as a fallback
            var result = new System.Collections.ObjectModel.Collection<PSObject>();
            if (hasWindowsCompatibility)
            {
                ps.Commands.Clear();
                ps.AddScript($"Invoke-WinCommand -ScriptBlock {{{hotspotScript}}}");
                result = ps.Invoke();
            }
            else
            {
                logger.LogWarning("WindowsCompatibility module not found - falling back to launching powershell.exe for tether control.");
                var fallbackResult = RunScriptViaWindowsPowerShell(hotspotScript);
                if (!string.IsNullOrEmpty(fallbackResult.Output))
                {
                    logger.LogInformation("Hotspot reactivation attempt (fallback powershell): {output}", fallbackResult.Output);
                }
                if (!string.IsNullOrEmpty(fallbackResult.Error))
                {
                    logger.LogError("Hotspot reactivation failed (fallback powershell): {error}", fallbackResult.Error);
                }
            }

            // Collect results (if using PowerShell invocation)
            if (result != null && result.Count > 0)
            {
                logger.LogInformation("Hotspot reactivation attempt: {output}", string.Join("\n", result.Where(r => r != null).Select(r => r.ToString())));
            }
            else if (ps.HadErrors)
            {
                logger.LogError("Failed to reactivate hotspot. Error: {error}", string.Join("\n", ps.Streams.Error.Select(e => e.ToString())));
            }
            else
            {
                logger.LogInformation("Hotspot reactivated successfully.");
            }
        }
        private void DeactivateHotspot()
        {
            using PowerShell ps = PowerShell.Create();

            // Set the execution policy to RemoteSigned
            ps.AddScript("Set-ExecutionPolicy -Scope Process -ExecutionPolicy RemoteSigned -Force");
            ps.Invoke();

            // Try to detect the WindowsCompatibility module; we'll fallback to launching windows powershell if missing
            bool hasWindowsCompatibility = false;
            try
            {
                ps.Commands.Clear();
                ps.AddScript("Get-Command Invoke-WinCommand -ErrorAction SilentlyContinue");
                var cmd = ps.Invoke();
                if (cmd != null && cmd.Count > 0)
                {
                    hasWindowsCompatibility = true;
                }
            }
            catch
            {
                hasWindowsCompatibility = false;
            }

            // Define the script to deactivate the hotspot
            string hotspotScript = @"
                    [Windows.System.UserProfile.LockScreen,Windows.System.UserProfile,ContentType=WindowsRuntime] | Out-Null
                    Add-Type -AssemblyName System.Runtime.WindowsRuntime
                    $asTaskGeneric = ([System.WindowsRuntimeSystemExtensions].GetMethods() | ? { $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' })[0]
                    Function Await($WinRtTask, $ResultType) {
                        $asTask = $asTaskGeneric.MakeGenericMethod($ResultType)
                        $netTask = $asTask.Invoke($null, @($WinRtTask))
                        $netTask.Wait(-1) | Out-Null
                        $netTask.Result
                    }
                    $connectionProfile = [Windows.Networking.Connectivity.NetworkInformation,Windows.Networking.Connectivity,ContentType=WindowsRuntime]::GetInternetConnectionProfile()
                    $tetheringManager = [Windows.Networking.NetworkOperators.NetworkOperatorTetheringManager,Windows.Networking.NetworkOperators,ContentType=WindowsRuntime]::CreateFromConnectionProfile($connectionProfile)
                    Await ($tetheringManager.StopTetheringAsync())([Windows.Networking.NetworkOperators.NetworkOperatorTetheringOperationResult])
                ";

            if (hasWindowsCompatibility)
            {
                ps.AddScript($"Invoke-WinCommand -ScriptBlock {{{hotspotScript}}}");
                var result = ps.Invoke();
                if (result.Count > 0) logger.LogInformation("Hotspot deactivation attempt: {output}", string.Join("\n", result.Where(r => r != null).Select(r => r.ToString())));
                else if (ps.HadErrors) logger.LogError("Failed to deactivate hotspot. Error: {error}", string.Join("\n", ps.Streams.Error.Select(e => e.ToString())));
                else logger.LogInformation("Hotspot deactivated successfully.");
            }
            else
            {
                logger.LogWarning("WindowsCompatibility module not found - falling back to launching powershell.exe for tether control.");
                var fallbackResult = RunScriptViaWindowsPowerShell(hotspotScript);
                if (!string.IsNullOrEmpty(fallbackResult.Output)) logger.LogInformation("Hotspot deactivation attempt (fallback powershell): {output}", fallbackResult.Output);
                if (!string.IsNullOrEmpty(fallbackResult.Error)) logger.LogError("Hotspot deactivation failed (fallback powershell): {error}", fallbackResult.Error);
            }
        }

    }
}
