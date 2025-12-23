# Hotspot Monitor App

The Hotspot Monitor app is an efficient tool designed to manage and maintain the status of your computer's hotspot automatically. This utility ensures your hotspot is always active when you need it, seamlessly enabling it if turned off, and providing a simple interface for control via the system tray.

## Prerequisites

- Windows
- .NET SDK 10

## Build

```powershell
dotnet build HotspotMonitorService.sln
```

## Run

Start the service:

```powershell
dotnet run --project .\HotspotMonitorService\HotspotMonitorService.csproj
```

Start the Windows Forms app:

```powershell
dotnet run --project .\HotspotMonitorApp\HotspotMonitorApp.csproj
```
