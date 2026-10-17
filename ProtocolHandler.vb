Imports System.IO
Imports System.Runtime.InteropServices

''' <summary>
''' Registers this launcher as the habbo:// handler, so a hotel website's "Open AIR client" link starts the client.
''' Windows uses HKCU\SOFTWARE\Classes\habbo (see MainWindow.RegisterHabboProtocol).
''' </summary>
Public Module ProtocolHandler
    Public Const UriScheme As String = "habbo"
    Public Const LinuxDesktopFileName As String = "HabboCustomLauncher.desktop"
    Public Const MacBundleId As String = "org.LilithRainbows.HabboCustomLauncher"
    Public Const MacBundlePath As String = "/Applications/HabboCustomLauncher.app"

    ''' <summary>Desktop entry for the app menu that also handles habbo: links. LF line endings, as the spec requires.</summary>
    Public Function BuildLinuxDesktopEntry(AppPath As String, AppName As String) As String
        Return String.Join(vbLf, {
            "[Desktop Entry]",
            "Type=Application",
            "Name=" & AppName,
            "Exec=""" & AppPath.Replace("\", "\\").Replace("""", "\""") & """ %U",
            "Terminal=false",
            "Icon=HabboCustomLauncherIcon",
            "Categories=Game;",
            "MimeType=x-scheme-handler/" & UriScheme & ";"
        }) & vbLf
    End Function

    ''' <summary>Writes ~/.local/share/applications/HabboCustomLauncher.desktop and makes it the default habbo: handler.</summary>
    Public Sub RegisterLinux(AppPath As String)
        Dim DataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME")
        If String.IsNullOrWhiteSpace(DataHome) Then
            DataHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share")
        End If
        Dim ApplicationsPath = Path.Combine(DataHome, "applications")
        Directory.CreateDirectory(ApplicationsPath)
        Dim DesktopFile = Path.Combine(ApplicationsPath, LinuxDesktopFileName)
        Dim Entry = BuildLinuxDesktopEntry(AppPath, "HabboCustomLauncher")
        'linux/install-desktop may have replaced the entry with a hidden handler for the same binary; keep that one.
        If Not IsLinuxHandlerFor(DesktopFile, AppPath) Then
            File.WriteAllText(DesktopFile, Entry)
        End If
        RunQuietly("update-desktop-database", {ApplicationsPath})
        RunQuietly("xdg-mime", {"default", LinuxDesktopFileName, "x-scheme-handler/" & UriScheme})
    End Sub

    Private Function IsLinuxHandlerFor(DesktopFile As String, AppPath As String) As Boolean
        If Not File.Exists(DesktopFile) Then
            Return False
        End If
        Dim Text = File.ReadAllText(DesktopFile)
        Return Not Text.Contains(vbCr) AndAlso Text.Contains("MimeType=x-scheme-handler/" & UriScheme & ";") AndAlso Text.Contains(AppPath)
    End Function

    ''' <summary>
    ''' macOS only routes URLs to app bundles. The shortcut bundle (LauncherShortcutOSXPatch.zip) declares the habbo scheme
    ''' and forwards the URL to this binary; it is rebuilt when missing or pointing at another binary.
    ''' </summary>
    Public Sub RegisterMac(AppPath As String, CreateBundle As Action)
        Dim ScriptPath = Path.Combine(MacBundlePath, "Contents", "Resources", "script")
        Dim SettingsPath = Path.Combine(MacBundlePath, "Contents", "Resources", "AppSettings.plist")
        Dim IsCurrent = File.Exists(ScriptPath) AndAlso File.ReadAllText(ScriptPath).Contains(AppPath) AndAlso
            File.Exists(SettingsPath) AndAlso File.ReadAllText(SettingsPath).Contains("<string>" & UriScheme & "</string>")
        If Not IsCurrent Then
            CreateBundle()
        End If
        RunQuietly("/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister", {"-f", MacBundlePath})
        Dim Scheme = CFStringCreateWithCString(IntPtr.Zero, UriScheme, CFStringEncodingUTF8)
        Dim BundleId = CFStringCreateWithCString(IntPtr.Zero, MacBundleId, CFStringEncodingUTF8)
        Try
            LSSetDefaultHandlerForURLScheme(Scheme, BundleId)
        Finally
            CFRelease(Scheme)
            CFRelease(BundleId)
        End Try
    End Sub

    Private Sub RunQuietly(FileName As String, Arguments As String())
        Try
            Dim Info As New ProcessStartInfo(FileName) With {.UseShellExecute = False, .CreateNoWindow = True}
            For Each Argument In Arguments
                Info.ArgumentList.Add(Argument)
            Next
            Process.Start(Info)?.WaitForExit(5000)
        Catch
            'The tool is not installed on this desktop
        End Try
    End Sub

    Private Const CoreFoundation As String = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation"
    Private Const CoreServices As String = "/System/Library/Frameworks/CoreServices.framework/CoreServices"
    Private Const CFStringEncodingUTF8 As UInteger = &H8000100UI

    <DllImport(CoreFoundation)>
    Private Function CFStringCreateWithCString(Allocator As IntPtr, Value As String, Encoding As UInteger) As IntPtr
    End Function

    <DllImport(CoreFoundation)>
    Private Sub CFRelease(Value As IntPtr)
    End Sub

    <DllImport(CoreServices)>
    Private Function LSSetDefaultHandlerForURLScheme(Scheme As IntPtr, BundleId As IntPtr) As Integer
    End Function
End Module
