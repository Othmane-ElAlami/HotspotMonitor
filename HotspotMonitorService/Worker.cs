using Microsoft.Extensions.Logging;
using System.Management.Automation;
using System.ServiceProcess;

namespace HotspotMonitorService
{
    [System.Runtime.Versioning.SupportedOSPlatform("Windows")]
    public class Worker(ILogger<Worker> logger) : BackgroundService
    {
        private CancellationTokenSource? _workCancellationTokenSource;

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

            // Check and start the WinRM service
            ps.AddScript("Get-Service WinRM");
            var serviceResult = ps.Invoke();
            if (serviceResult.Count > 0 && serviceResult[0].BaseObject is ServiceController winrmService && winrmService.Status != ServiceControllerStatus.Running)
            {
                logger.LogInformation("Starting WinRM service...");
                ps.Commands.Clear();
                ps.AddScript("Start-Service WinRM");
                ps.Invoke();
            }

            // Import the WindowsCompatibility module
            ps.AddScript("Import-Module WindowsCompatibility");

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

            // Use Invoke-WinCommand to execute the script
            ps.AddScript($"Invoke-WinCommand -ScriptBlock {{{hotspotScript}}}");

            var result = ps.Invoke();
            if (result.Count > 0) logger.LogInformation("Hotspot reactivation attempt: {output}", string.Join("\n", result.Where(r => r != null).Select(r => r.ToString())));
            else if (ps.HadErrors) logger.LogError("Failed to reactivate hotspot. Error: {error}", string.Join("\n", ps.Streams.Error.Select(e => e.ToString())));
            else logger.LogInformation("Hotspot reactivated successfully.");
        }
        private void DeactivateHotspot()
        {
            using PowerShell ps = PowerShell.Create();

            // Set the execution policy to RemoteSigned
            ps.AddScript("Set-ExecutionPolicy -Scope Process -ExecutionPolicy RemoteSigned -Force");
            ps.Invoke();

            // Import the WindowsCompatibility module
            ps.AddScript("Import-Module WindowsCompatibility");

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

            // Use Invoke-WinCommand to execute the script
            ps.AddScript($"Invoke-WinCommand -ScriptBlock {{{hotspotScript}}}");

            var result = ps.Invoke();
            if (result.Count > 0) logger.LogInformation("Hotspot deactivation attempt: {output}", string.Join("\n", result.Where(r => r != null).Select(r => r.ToString())));
            else if (ps.HadErrors) logger.LogError("Failed to deactivate hotspot. Error: {error}", string.Join("\n", ps.Streams.Error.Select(e => e.ToString())));
            else logger.LogInformation("Hotspot deactivated successfully.");
        }

    }
}
