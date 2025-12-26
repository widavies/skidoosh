C# ski lift status & weather report display firmware for Raspberry PI 5. Pulls chair lift statuses from [liftie](https://liftie.info/), snow reports from the
[Breckenridge website](https://www.breckenridge.com/) and weather from [weather.gov](https://www.weather.gov/wrh/timeseries?site=E8345).

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
