Driver is built as a C library that is statically linked:
https://github.com/widavies/skidoosh-driver

```
# sudo cat /etc/systemd/system/skidoosh.service
[Unit]
Description=Skidoosh
After=network.target

[Service]
ExecStart=/home/cci/skidoosh
User=cci
WorkingDirectory=/home/cci/
Restart=on-failure

[Install]
WantedBy=multi-user.target
```

systemctl service enable
systemctl service start

Todo:

- Compile in release mode
- C# not catching systemd termination
- Resolve the strange bug:

```
 cci@ski:~ $ ./skidoosh
  Failed to load System.Private.CoreLib.dll (error code 0x80131018)
  Path: /home/cci/System.Private.CoreLib.dll
  Error message: Could not load file or assembly '/home/cci/System.Private.CoreLib.dll'. The module was expected to contain an assembly manifest. (0x80131018)
  Failed to create CoreCLR, HRESULT: 0x80131018
```