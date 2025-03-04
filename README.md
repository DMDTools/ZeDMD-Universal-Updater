# ZeDMD Updater

A universal (Windows, Linux, MacOS) command-line utility to update firmware on ZeDMD displays and configure them.

![Screenshot](screenshot.png)

> [!NOTE]
> ZeDMDUpdater-universal is a non-Windows specific alternative to the great [ZeDMD_Updater2](https://github.com/zesinger/ZeDMD_Updater2).
> It only supports flashing through USB for now, and does not yet allow to set ZeDMD configuration.
> Leveraging a lot of code from ZeDMD_Updater2, ZeDMDUpdater-universal adopts the same Open Source license - GPL 3.0.

## Features

- Support for multiple board types:
  - ESP32
  - ESP32-S3
  - LilygoS3Amoled (with optional WiFi support)
- Firmware version selection
- Easy-to-use interactive menu
- Device settings configuration

## Prerequisites

- Windows, Linux, or macOS
- [.NET 8.0 runtime](https://dotnet.microsoft.com/en-us/download/dotnet/8.0) or later for your OS
- [esptool](https://github.com/espressif/esptool) (`esptool.exe` or `esptool`) in the same folder as ZeDMDUpdater

### For Windows WSL Users

Ensure you have [usbipd-win](https://github.com/dorssel/usbipd-win) installed to expose the ZeDMD device:

```bash
usbipd list
usbipd bind --busid=<your-device-busid>
usbipd attach --wsl --busid=<your-device-busid>
```

## Usage

1. Connect your ZeDMD display to your computer on USB
2. Run the application:

    ```shell
    ./ZeDMDUpdater
    ```

3. Follow the interactive menu to:

   - Choose firmware version
   - Download firmware
   - Flash your device
   - Configure device settings
