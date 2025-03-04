using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Spectre.Console;
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.IO.Ports;


namespace ZeDMDUpdater;

public class Esp32Device
{
    private const string LibraryName = "libzedmd.dylib";

    public string DeviceAddress { get; set; } = "";
    public bool isUnknown { get; set; } = true;
    public bool isS3 { get; set; } = false;
    public bool isLilygo { get; set; } = false;
    public bool isZeDMD { get; set; } = false;
    public bool isWifi { get; set; } = false;
    public string WifiIp { get; set; } = "";
    public int ZeID { get; set; } = -1;
    public string SSID { get; set; } = "";
    public int SSIDPort { get; set; } = -1;
    public int RgbOrder { get; set; } = 0;
    public int Brightness { get; set; } = 6;
    public int Width { get; set; } = 128;
    public int Height { get; set; } = 32;
    public int MajVersion { get; set; } = 0;
    public int MinVersion { get; set; } = 0;
    public int PatVersion { get; set; } = 0;
    public int PanelDriver { get; set; } = 0;
    public int PanelClockPhase { get; set; } = 0;
    public int PanelI2SSpeed { get; set; } = 8;
    public int PanelLatchBlanking { get; set; } = 0;
    public int PanelMinRefreshRate { get; set; } = 0;
    public int UsbPacketSize { get; set; } = 64;
    public int UdpDelay { get; set; } = 0;
    public int YOffset { get; set; } = 0;

    public Esp32Device(string deviceaddress, bool iss3, bool islilygo, bool isunknown)
    {
        DeviceAddress = deviceaddress;
        isS3 = iss3;
        isLilygo = islilygo;
        isUnknown = isunknown;
    }
    public static void GetZeDMDValues(Esp32Device device, IntPtr _pZeDMD)
    {
        device.isS3 = ZeDMD_IsS3(_pZeDMD);
        string? version = Marshal.PtrToStringAnsi(ZeDMD_GetFirmwareVersion(_pZeDMD)) ?? "0.0.0";
        string[] parts = version.Split('.');
        int.TryParse(parts[0], out int major);
        int.TryParse(parts[1], out int minor);
        int.TryParse(parts[2], out int patch);
        device.MajVersion = major;
        device.MinVersion = minor;
        device.PatVersion = patch;
        device.RgbOrder = ZeDMD_GetRGBOrder(_pZeDMD);
        device.Brightness = ZeDMD_GetBrightness(_pZeDMD);
        device.Width = ZeDMD_GetPanelWidth(_pZeDMD);
        device.Height = ZeDMD_GetPanelHeight(_pZeDMD);
        device.SSID = Marshal.PtrToStringAnsi(ZeDMD_GetWiFiSSID(_pZeDMD)) ?? string.Empty;
        device.SSIDPort = ZeDMD_GetWiFiPort(_pZeDMD);
        device.PanelDriver = ZeDMD_GetPanelDriver(_pZeDMD);
        device.PanelClockPhase = ZeDMD_GetPanelClockPhase(_pZeDMD);
        device.PanelI2SSpeed = ZeDMD_GetPanelI2sSpeed(_pZeDMD);
        device.PanelLatchBlanking = ZeDMD_GetPanelLatchBlanking(_pZeDMD);
        device.PanelMinRefreshRate = ZeDMD_GetPanelMinRefreshRate(_pZeDMD);
        device.UsbPacketSize = ZeDMD_GetUsbPackageSize(_pZeDMD);
        device.UdpDelay = ZeDMD_GetUdpDelay(_pZeDMD);
        device.YOffset = ZeDMD_GetYOffset(_pZeDMD);
        device.ZeID = ZeDMD_GetId(_pZeDMD);
    }
    private static string logs = string.Empty;
    private static void LogHandler(string format, IntPtr args, IntPtr pUserData)
    {
        logs += Marshal.PtrToStringAnsi(ZeDMD_FormatLogMessage(format, args, pUserData)) + "\r\n";
    }


    public static async Task<(string logs, List<Esp32Device> esp32Devices, Esp32Device wifiDevice)> CheckZeDMDs(List<Esp32Device> esp32Devices, Esp32Device wifiDevice)
    {
        AnsiConsole.MarkupLine("[yellow]=== WiFi Test ===[/]");

        // create an instance
        GCHandle handle;
        IntPtr _pZeDMD = IntPtr.Zero;
        _pZeDMD = ZeDMD_GetInstance();

        ZeDMD_LogCallback callbackDelegate = new ZeDMD_LogCallback((format, args, userData) =>
        {
            var message = Marshal.PtrToStringAnsi(ZeDMD_FormatLogMessage(format, args, userData));
            AnsiConsole.MarkupLine($"[grey]{message}[/]");
            logs += message + "\r\n";
        });

        // Keep a reference to the delegate to prevent GC from collecting it
        handle = GCHandle.Alloc(callbackDelegate);
        ZeDMD_SetLogCallback(_pZeDMD, callbackDelegate, IntPtr.Zero);

        // check if a ZeDMD wifi is available
        byte wifitransport = 2;
        if (ZeDMD_OpenDefaultWiFi(_pZeDMD))
        {
            AnsiConsole.MarkupLine("[green]WiFi ZeDMD found[/]");
            // if so, get all the parameters
            wifiDevice.isWifi = true;
            wifiDevice.isZeDMD = true;
            wifiDevice.isUnknown = false;
            GetZeDMDValues(wifiDevice, _pZeDMD);
            wifiDevice.WifiIp = Marshal.PtrToStringAnsi(ZeDMD_GetIp(_pZeDMD)) ?? string.Empty;
            if (wifiDevice.WifiIp != "")
            {
                AnsiConsole.MarkupLine($"[green]WiFi IP: {wifiDevice.WifiIp}[/]");
                // keep the transport mode for later
                wifitransport = ZeDMD_GetTransport(_pZeDMD);
                if (wifitransport != 1 && wifitransport != 2)
                {
                    AnsiConsole.MarkupLine("[red]The WiFi ZeDMD connected has an old firmware, you need to check manually which COM # is corresponding and flash it, your WiFi ZeDMD will be ignored.[/]");
                    wifiDevice.isUnknown = true;
                    wifiDevice.ZeID = -1;
                }
                else
                {
                    // switch this device to USB
                    AnsiConsole.MarkupLine("[grey]Switching WiFi ZeDMD to USB so that we can control it...[/]");
                    ZeDMD_SetTransport(_pZeDMD, 0);
                    ZeDMD_SaveSettings(_pZeDMD);
                    ZeDMD_Reset(_pZeDMD);
                    Thread.Sleep(5000);
                }
            }
            else
            {
                wifiDevice.isUnknown = true;
                AnsiConsole.MarkupLine("[red]No WiFi device found[/]");
            }
            ZeDMD_Close(_pZeDMD);
            // Pause for 1s
            await Task.Delay(1000);
        }
        else
        {
            wifiDevice.isUnknown = true;
            AnsiConsole.MarkupLine("[red]No WiFi device found[/]");
        }

        AnsiConsole.MarkupLine("\n[yellow]=== USB Test ===[/]");
        int wifif = -1;
        AnsiConsole.MarkupLine($"[grey]Found {esp32Devices.Count} devices...[/]");
        for (int i = 0; i < esp32Devices.Count; i++)
        {
            // open the device
            Esp32Device device = esp32Devices[i];
            string deviceaddress = device.DeviceAddress;
            AnsiConsole.MarkupLine($"[grey]Testing {deviceaddress}...[/]");

            ZeDMD_SetDevice(_pZeDMD, deviceaddress);
            if (ZeDMD_Open(_pZeDMD))
            {
                AnsiConsole.MarkupLine($"[green]Found ZeDMD on {deviceaddress}[/]");
                // get its parameters
                device.isWifi = false;
                device.isUnknown = false;
                device.isZeDMD = true;
                GetZeDMDValues(device, _pZeDMD);

                ZeDMD_Close(_pZeDMD);
                await Task.Delay(1000);

                if (device.ZeID == wifiDevice.ZeID)
                {
                    AnsiConsole.MarkupLine($"[blue]Device on {deviceaddress} matches WiFi device[/]");
                    await Task.Delay(1000);
                    wifiDevice.DeviceAddress = device.DeviceAddress;
                    wifif = i;
                }
            }
            else
            {
                AnsiConsole.MarkupLine($"[red]No ZeDMD found on {deviceaddress}[/]");
            }
        }

        // if we have found the wifi device in the USB devices, we can remove it from the USB list
        if (wifif >= 0)
        {
            esp32Devices.RemoveAt(wifif);
            AnsiConsole.MarkupLine("[green]Removed WiFi device from USB list[/]");
        }

        return (logs, esp32Devices, wifiDevice);
    }
    public static string LedTest()
    {
        logs = "=== Led Test ===\r\n";
        // create an instance
        GCHandle handle;
        IntPtr _pZeDMD = IntPtr.Zero;
        _pZeDMD = ZeDMD_GetInstance();
        ZeDMD_LogCallback callbackDelegate = new ZeDMD_LogCallback(LogHandler);
        // Keep a reference to the delegate to prevent GC from collecting it
        handle = GCHandle.Alloc(callbackDelegate);
        ZeDMD_SetLogCallback(_pZeDMD, callbackDelegate, IntPtr.Zero);
        bool openOK = false;
        if (Program.selectedDevice.isWifi) openOK = ZeDMD_OpenDefaultWiFi(_pZeDMD);
        else
        {
            string comport = Program.selectedDevice.DeviceAddress;
            ZeDMD_SetDevice(_pZeDMD, comport);
            openOK = ZeDMD_Open(_pZeDMD);
        }
        if (openOK)
        {
            ZeDMD_LedTest(_pZeDMD);
            ZeDMD_Close(_pZeDMD);
        }
        else logs += "Unable to connect to the device\r\n";
        return logs;
    }

    public static byte[] ReadRawRGBFile(string filePath, int width = 128, int height = 32)
    {
        try
        {
            int expectedFileSize = width * height * 3;
            byte[] fileBytes = File.ReadAllBytes(filePath);
            if (fileBytes.Length != expectedFileSize)
            {
                AnsiConsole.MarkupLine($"[red]File size {fileBytes.Length} bytes does not match expected size for {width}x{height} RGB image ({expectedFileSize} bytes)[/]");
            }
            return fileBytes;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error reading RAW RGB file: {ex.Message}[/]");
            return new byte[0];
        }
    }

    public static byte[] CreateImageRGB24()
    {
        // Allocate buffer for 128x32 RGB image (3 bytes per pixel)
        byte[] imageBuffer = new byte[128 * 32 * 3];

        for (int y = 0; y < 32; ++y)
        {
            for (int x = 0; x < 128; ++x)
            {
                int index = (y * 128 + x) * 3;

                // Determine which quadrant we're in
                bool isLeftHalf = x < 64;
                bool isTopHalf = y < 16;

                // Set base colors for each quadrant
                if (isLeftHalf && isTopHalf) // Top left - Red
                {
                    imageBuffer[index] = 255;     // R
                    imageBuffer[index + 1] = 0;   // G
                    imageBuffer[index + 2] = 0;   // B
                }
                else if (!isLeftHalf && isTopHalf) // Top right - Green
                {
                    imageBuffer[index] = 0;       // R
                    imageBuffer[index + 1] = 255; // G
                    imageBuffer[index + 2] = 0;   // B
                }
                else if (isLeftHalf && !isTopHalf) // Bottom left - Blue
                {
                    imageBuffer[index] = 0;       // R
                    imageBuffer[index + 1] = 0;   // G
                    imageBuffer[index + 2] = 255; // B
                }
                else // Bottom right - White
                {
                    imageBuffer[index] = 255;     // R
                    imageBuffer[index + 1] = 255; // G
                    imageBuffer[index + 2] = 255; // B
                }
            }
        }

        return imageBuffer;
    }

    [DllImport(LibraryName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    // C format: extern ZEDMDAPI ZeDMD* ZeDMD_GetInstance();
    public static extern IntPtr ZeDMD_GetInstance();
    [DllImport(LibraryName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    // C format: extern ZEDMDAPI const char* ZeDMD_GetVersion();
    public static extern IntPtr ZeDMD_GetVersion();
    [DllImport(LibraryName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    // C format: extern ZEDMDAPI void ZeDMD_SetDevice(ZeDMD* pZeDMD, const char* const device);
    public static extern bool ZeDMD_SetDevice(IntPtr pZeDMD, string device);
    [DllImport(LibraryName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    // C format: extern ZEDMDAPI bool ZeDMD_OpenDefaultWiFi(ZeDMD* pZeDMD);
    public static extern bool ZeDMD_OpenDefaultWiFi(IntPtr pZeDMD);
    [DllImport(LibraryName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    // C format: extern ZEDMDAPI bool ZeDMD_Open(ZeDMD* pZeDMD);
    public static extern bool ZeDMD_Open(IntPtr pZeDMD);
    [DllImport(LibraryName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    // C format: extern ZEDMDAPI bool ZeDMD_IsS3(ZeDMD* pZeDMD);
    protected static extern bool ZeDMD_IsS3(IntPtr pZeDMD);
    [DllImport(LibraryName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    // C format: extern ZEDMDAPI void ZeDMD_Close(ZeDMD* pZeDMD);
    public static extern void ZeDMD_Close(IntPtr pZeDMD);
    [DllImport(LibraryName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    // C format: extern ZEDMDAPI const char* ZeDMD_GetFirmwareVersion(ZeDMD* pZeDMD);
    protected static extern IntPtr ZeDMD_GetFirmwareVersion(IntPtr pZeDMD);
    [DllImport(LibraryName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    // C format: extern ZEDMDAPI uint8_t ZeDMD_GetRGBOrder(ZeDMD* pZeDMD);
    private static extern byte ZeDMD_GetRGBOrder(IntPtr pZeDMD);
    [DllImport(LibraryName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    // C format: extern ZEDMDAPI uint8_t ZeDMD_GetBrightness(ZeDMD* pZeDMD);
    protected static extern byte ZeDMD_GetBrightness(IntPtr pZeDMD);
    [DllImport(LibraryName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    // C format: extern ZEDMDAPI uint8_t ZeDMD_GetBrightness(ZeDMD* pZeDMD);
    protected static extern byte ZeDMD_GetYOffset(IntPtr pZeDMD);
    [DllImport(LibraryName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    // C format: extern ZEDMDAPI uint16_t ZeDMD_GetId(ZeDMD* pZeDMD);
    protected static extern ushort ZeDMD_GetId(IntPtr pZeDMD);
    [DllImport(LibraryName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    // C format: extern ZEDMDAPI uint16_t ZeDMD_GetWidth(ZeDMD* pZeDMD);
    protected static extern ushort ZeDMD_GetPanelWidth(IntPtr pZeDMD);
    [DllImport(LibraryName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    // C format: extern ZEDMDAPI uint16_t ZeDMD_GetHeight(ZeDMD* pZeDMD);
    protected static extern ushort ZeDMD_GetPanelHeight(IntPtr pZeDMD);
    [DllImport(LibraryName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    // C format: extern ZEDMDAPI const char* ZeDMD_GetWiFiSSID(ZeDMD* pZeDMD);
    private static extern IntPtr ZeDMD_GetWiFiSSID(IntPtr pZeDMD);
    [DllImport(LibraryName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    // C format: extern ZEDMDAPI const char* ZeDMD_GetIp(ZeDMD* pZeDMD);
    private static extern IntPtr ZeDMD_GetIp(IntPtr pZeDMD);
    [DllImport(LibraryName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    // C format: extern ZEDMDAPI const char* ZeDMD_GetDevice(ZeDMD* pZeDMD);
    private static extern IntPtr ZeDMD_GetDevice(IntPtr pZeDMD);
    [DllImport(LibraryName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    // C format: extern ZEDMDAPI int ZeDMD_GetWiFiPort(ZeDMD* pZeDMD);
    private static extern int ZeDMD_GetWiFiPort(IntPtr pZeDMD);
    [DllImport(LibraryName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    // C format: extern ZEDMDAPI uint16_t ZeDMD_GetUsbPackageSize(ZeDMD* pZeDMD);
    protected static extern ushort ZeDMD_GetUsbPackageSize(IntPtr pZeDMD);
    [DllImport(LibraryName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    // C format: extern ZEDMDAPI uint8_t ZeDMD_GetUdpDelay(ZeDMD* pZeDMD);
    protected static extern byte ZeDMD_GetUdpDelay(IntPtr pZeDMD);
    [DllImport(LibraryName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    // C format: extern ZEDMDAPI uint8_t ZeDMD_GetPanelDriver(ZeDMD* pZeDMD);
    protected static extern byte ZeDMD_GetPanelDriver(IntPtr pZeDMD);
    [DllImport(LibraryName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    // C format: extern ZEDMDAPI uint8_t ZeDMD_GetPanelClockPhase(ZeDMD* pZeDMD);
    protected static extern byte ZeDMD_GetPanelClockPhase(IntPtr pZeDMD);
    [DllImport(LibraryName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    // C format: extern ZEDMDAPI uint8_t ZeDMD_GetPanelI2sSpeed(ZeDMD* pZeDMD);
    protected static extern byte ZeDMD_GetPanelI2sSpeed(IntPtr pZeDMD);
    [DllImport(LibraryName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    // C format: extern ZEDMDAPI uint8_t ZeDMD_GetPanelLatchBlanking(ZeDMD* pZeDMD);
    protected static extern byte ZeDMD_GetPanelLatchBlanking(IntPtr pZeDMD);
    [DllImport(LibraryName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    // C format: extern ZEDMDAPI uint8_t ZeDMD_GetPanelMinRefreshRate(ZeDMD* pZeDMD);
    protected static extern byte ZeDMD_GetPanelMinRefreshRate(IntPtr pZeDMD);
    [DllImport(LibraryName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    // C format: extern ZEDMDAPI void ZeDMD_SetRGBOrder(ZeDMD* pZeDMD, uint8_t rgbOrder);
    public static extern void ZeDMD_SetRGBOrder(IntPtr pZeDMD, byte rgbOrder);
    [DllImport(LibraryName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    // C format: extern ZEDMDAPI void ZeDMD_SetBrightness(ZeDMD* pZeDMD, uint8_t brightness);
    public static extern void ZeDMD_SetBrightness(IntPtr pZeDMD, byte brightness);
    [DllImport(LibraryName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    // C format: extern ZEDMDAPI void ZeDMD_SetWiFiSSID(ZeDMD* pZeDMD, const char* const ssid);
    public static extern void ZeDMD_SetWiFiSSID(IntPtr pZeDMD, string ssid);
    [DllImport(LibraryName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    // C format: extern ZEDMDAPI void ZeDMD_SetWiFiPassword(ZeDMD* pZeDMD, const char* const password);
    public static extern void ZeDMD_SetWiFiPassword(IntPtr pZeDMD, string password);
    [DllImport(LibraryName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    // C format: extern ZEDMDAPI void ZeDMD_SetWiFiPort(ZeDMD* pZeDMD, int port);
    public static extern void ZeDMD_SetWiFiPort(IntPtr pZeDMD, int port);
    [DllImport(LibraryName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    // C format: extern ZEDMDAPI void ZeDMD_SetPanelDriver(ZeDMD* pZeDMD, uint8_t driver);
    public static extern void ZeDMD_SetPanelDriver(IntPtr pZeDMD, byte uint8_t);
    [DllImport(LibraryName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    // C format: extern ZEDMDAPI void ZeDMD_SetPanelClockPhase(ZeDMD* pZeDMD, uint8_t clockPhase);
    public static extern void ZeDMD_SetPanelClockPhase(IntPtr pZeDMD, byte clockPhase);
    [DllImport(LibraryName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    // C format: extern ZEDMDAPI void ZeDMD_SetPanelI2sSpeed(ZeDMD* pZeDMD, uint8_t i2sSpeed);
    public static extern void ZeDMD_SetPanelI2sSpeed(IntPtr pZeDMD, byte i2sSpeed);
    [DllImport(LibraryName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    // C format: extern ZEDMDAPI void ZeDMD_SetPanelLatchBlanking(ZeDMD* pZeDMD, uint8_t latchBlanking);
    public static extern void ZeDMD_SetPanelLatchBlanking(IntPtr pZeDMD, byte latchBlanking);
    [DllImport(LibraryName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    // C format: extern ZEDMDAPI void ZeDMD_SetPanelMinRefreshRate(ZeDMD* pZeDMD, uint8_t minRefreshRate);
    public static extern void ZeDMD_SetPanelMinRefreshRate(IntPtr pZeDMD, byte minRefreshRate);
    [DllImport(LibraryName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    // C format: extern ZEDMDAPI void ZeDMD_SetUdpDelay(ZeDMD* pZeDMD, uint8_t udpDelay);
    public static extern void ZeDMD_SetUdpDelay(IntPtr pZeDMD, byte udpDelay);
    [DllImport(LibraryName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    // C format: extern ZEDMDAPI void ZeDMD_SetUsbPackageSize(ZeDMD* pZeDMD, uint16_t usbPackageSize);
    public static extern void ZeDMD_SetUsbPackageSize(IntPtr pZeDMD, ushort usbPackageSize);
    [DllImport(LibraryName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    // C format: extern ZEDMDAPI void ZeDMD_SetYOffset(ZeDMD* pZeDMD, uint8_t yOffset);
    public static extern void ZeDMD_SetYOffset(IntPtr pZeDMD, byte yOffset);
    [DllImport(LibraryName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    // C format: extern ZEDMDAPI void ZeDMD_SetTransport(ZeDMD* pZeDMD, uint8_t transport);
    public static extern void ZeDMD_SetTransport(IntPtr pZeDMD, byte transport);
    [DllImport(LibraryName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    // C format: extern ZEDMDAPI uint8_t ZeDMD_GetTransport(ZeDMD* pZeDMD);
    public static extern byte ZeDMD_GetTransport(IntPtr pZeDMD);
    [DllImport(LibraryName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    // C format: extern ZEDMDAPI void ZeDMD_SaveSettings(ZeDMD* pZeDMD);
    public static extern void ZeDMD_SaveSettings(IntPtr pZeDMD);
    [DllImport(LibraryName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    // C format: extern ZEDMDAPI void ZeDMD_Reset(ZeDMD* pZeDMD);
    public static extern void ZeDMD_Reset(IntPtr pZeDMD);
    [DllImport(LibraryName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    // C format: extern ZEDMDAPI void ZeDMD_LedTest(ZeDMD* pZeDMD);
    private static extern void ZeDMD_LedTest(IntPtr pZeDMD);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    // C format: typedef void(ZEDMDCALLBACK* ZeDMD_LogCallback)(const char* format, va_list args, const void* userData);
    public delegate void ZeDMD_LogCallback(string format, IntPtr args, IntPtr pUserData);
    [DllImport(LibraryName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    // C format: extern ZEDMDAPI void ZeDMD_SetLogCallback(ZeDMD* pZeDMD, ZeDMD_LogCallback callback, const void* pUserData);
    public static extern void ZeDMD_SetLogCallback(IntPtr pZeDMD, ZeDMD_LogCallback callback, IntPtr pUserData);
    [DllImport(LibraryName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    // C format: extern ZEDMDAPI const char* ZeDMD_FormatLogMessage(const char* format, va_list args, const void* pUserData);
    public static extern IntPtr ZeDMD_FormatLogMessage(string format, IntPtr args, IntPtr pUserData);
    // C format: extern ZEDMDAPI void ZeDMD_RenderRgb888(ZeDMD* pZeDMD, uint8_t* frame);
    [DllImport(LibraryName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    public static extern void ZeDMD_RenderRgb888(IntPtr pZeDMD, IntPtr pImage);
    // C format: extern ZEDMDAPI void ZeDMD_SetFrameSize(ZeDMD* pZeDMD, uint16_t width, uint16_t height)
    [DllImport(LibraryName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    public static extern void ZeDMD_SetFrameSize(IntPtr pZeDMD, ushort width, ushort height);
    // C format: extern ZEDMDAPI void ZeDMD_ClearScreen(ZeDMD* pZeDMD)
    [DllImport(LibraryName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    public static extern void ZeDMD_ClearScreen(IntPtr pZeDMD);
}
