## Overview

https://github.com/user-attachments/assets/c8d2a512-fd76-4275-ba5e-10a4ffb7a53c
 
C# ski lift status & weather report display firmware for Raspberry PI 5. Pulls chair lift statuses from [liftie](https://liftie.info/), snow reports from the
[Breckenridge website](https://www.breckenridge.com/) and weather from [weather.gov](https://www.weather.gov/wrh/timeseries?site=E8345).

Displays:
- Breckenridge chairlift status
- Weather
  - Current temperature at base, mid-mountain (Horseshoe bowl), [summit](https://www.google.com/maps/place/39%C2%B029'37.0%22N+106%C2%B006'40.9%22W/@39.4935956,-106.1113684,3a,75y,195.59h,100.98t/data=!3m8!1e1!3m6!1sCIHM0ogKEICAgIDqrcrJTQ!2e10!3e11!6shttps:%2F%2Flh3.googleusercontent.com%2Fgpms-cs-s%2FAPRy3c_bzHFqoVCEQhVy_B2FvJlhKPQmZDEM9TIRsjmadBU0h4SNWzLf13dRKriDMSOCuMle850b4DrTFfU-mNuK9Jy26KrgoGl4apGCMR1CM26zx3uBpDRHA2VfLnuTTVnDrDacRlY%3Dw900-h600-k-no-pi-10.97937651197934-ya195.59363456008236-ro0-fo100!7i8704!8i4352!4m4!3m3!8m2!3d39.49362!4d-106.11137?entry=ttu&g_ep=EgoyMDI1MTIwOS4wIKXMDSoKLDEwMDc5MjA2N0gBUAM%3D) (Peak 6)
  - Snow report (snow last night, last 24 hours, last 48 hours, last 7 days)
  - Snow forecast (snow tonight, snow tomorrow)

Hardware drivers (LEDs and LCDs) are implemented in C and linked as a static library:

https://github.com/widavies/skidoosh-driver

The C# application should be registered as a systemd service with the following configuration file:
```
# sudo cat /etc/systemd/system/skidoosh.service
[Unit]
Description=Skidoosh

[Service]
ExecStartPre=rm -rf /home/cci/.net
ExecStart=/home/cci/skidoosh
User=cci
WorkingDirectory=/home/cci/
Restart=on-failure

[Install]
WantedBy=multi-user.target
```

Enable and start the service:

```
systemctl service enable
systemctl service start
```

## Miscellanous

The `ExecStartPre` line is added as a Band-Aid fix for the below issue:
```
 cci@ski:~ $ ./skidoosh
  Failed to load System.Private.CoreLib.dll (error code 0x80131018)
  Path: /home/cci/System.Private.CoreLib.dll
  Error message: Could not load file or assembly '/home/cci/System.Private.CoreLib.dll'. The module was expected to contain an assembly manifest. (0x80131018)
  Failed to create CoreCLR, HRESULT: 0x80131018
```

  PS D:\skidoosh> dotnet publish skidoosh.csproj --runtime linux-arm64 --configuration Release --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true  -o dist 
