Imports System.IO
Imports System.IO.Compression
Imports System.Net.Http
Imports System.Net.Http.Headers
Imports System.Text
Imports System.Text.Json
Imports System.Text.RegularExpressions

''' <summary>
''' Custom hotel support: load the official AIR client against a hotel that is not Habbo.
''' The hotel publishes https://&lt;domain&gt;/api/client/air.json (see README, "Custom hotel domain").
''' </summary>
Public Module CustomHotel
    Public Const ConfigPath As String = "/api/client/air.json"
    Public Const DefaultEnvironment As String = "en"
    Public Const DefaultClientUrlsUrl As String = "https://www.habbo.com/gamedata/clienturls"

    'Environment ids the AIR client accepts (embedded live.environment.list).
    Private ReadOnly KnownEnvironments As String() = {"en", "pt", "de", "es", "fi", "fr", "it", "nl", "tr", "s2"}
    Private ReadOnly HabboDomainRegex As New Regex("(^|\.)habbo\.(com|com\.br|com\.tr|de|es|fi|fr|it|nl|se|dk|no|ch)$", RegexOptions.IgnoreCase)

    ''' <summary>Turns "https://www.example.com/path" into "www.example.com". Keeps an explicit port.</summary>
    Public Function NormalizeDomain(Domain As String) As String
        If String.IsNullOrWhiteSpace(Domain) Then
            Return ""
        End If
        Dim Value = Domain.Trim()
        Dim SchemeIndex = Value.IndexOf("://", StringComparison.Ordinal)
        If SchemeIndex >= 0 Then
            Value = Value.Substring(SchemeIndex + 3)
        End If
        Dim PathIndex = Value.IndexOfAny({"/"c, "?"c, "#"c})
        If PathIndex >= 0 Then
            Value = Value.Substring(0, PathIndex)
        End If
        Return Value.Trim().TrimEnd("."c).ToLowerInvariant()
    End Function

    ''' <summary>True for an empty domain and for Habbo's own hotels (habbo.com, www.habbo.de, sandbox.habbo.com, ...).</summary>
    Public Function IsHabboDomain(Domain As String) As Boolean
        Dim Host = NormalizeDomain(Domain)
        If Host = "" Then
            Return True
        End If
        Dim PortIndex = Host.LastIndexOf(":"c)
        If PortIndex >= 0 Then
            Host = Host.Substring(0, PortIndex)
        End If
        Return HabboDomainRegex.IsMatch(Host)
    End Function

    Public Function IsCustomHotelDomain(Domain As String) As Boolean
        Return Not IsHabboDomain(Domain)
    End Function

    ''' <summary>Folder-safe name for a domain, used to keep each hotel's patched client apart.</summary>
    Public Function GetDomainFolderName(Domain As String) As String
        Return Regex.Replace(NormalizeDomain(Domain), "[^a-z0-9.\-]", "_")
    End Function

    Public Function GetConfigUrl(Domain As String) As String
        Return "https://" & NormalizeDomain(Domain) & ConfigPath
    End Function

    ''' <summary>Arguments for the AIR client. HabboAir.parseArguments reads "-server" and "-ticket" pairs.</summary>
    Public Function BuildLaunchArguments(Environment As String, Ticket As String) As List(Of String)
        Dim Arguments As New List(Of String) From {"-server", Environment}
        If Not String.IsNullOrWhiteSpace(Ticket) Then
            Arguments.Add("-ticket")
            Arguments.Add(Ticket)
        End If
        Return Arguments
    End Function

    Public Async Function FetchConfigAsync(Domain As String, UserAgent As String) As Task(Of HotelClientConfig)
        Using Client As New HttpClient()
            Client.Timeout = TimeSpan.FromSeconds(15)
            Client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent)
            Client.DefaultRequestHeaders.Accept.ParseAdd("application/json")
            Dim Url = GetConfigUrl(Domain)
            Dim Response = Await Client.GetAsync(Url)
            If Not Response.IsSuccessStatusCode Then
                Throw New Exception("Hotel config " & Url & " returned HTTP " & CInt(Response.StatusCode))
            End If
            Return HotelClientConfig.Parse(Await Response.Content.ReadAsStringAsync(), Domain)
        End Using
    End Function

    ''' <summary>POSTs the hotel's ticket endpoint and returns the SSO ticket. HotelToken is sent as a Bearer token when set.</summary>
    Public Async Function FetchTicketAsync(Config As HotelClientConfig, HotelToken As String, UserAgent As String) As Task(Of String)
        If String.IsNullOrWhiteSpace(Config.TicketUrl) Then
            Return ""
        End If
        Using Client As New HttpClient()
            Client.Timeout = TimeSpan.FromSeconds(15)
            Client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent)
            Client.DefaultRequestHeaders.Accept.ParseAdd("application/json")
            If Not String.IsNullOrWhiteSpace(HotelToken) Then
                Client.DefaultRequestHeaders.Authorization = New AuthenticationHeaderValue("Bearer", HotelToken.Trim())
            End If
            Dim Response = Await Client.PostAsync(Config.TicketUrl, New StringContent("{}", Encoding.UTF8, "application/json"))
            If Not Response.IsSuccessStatusCode Then
                Throw New Exception("Hotel ticket endpoint returned HTTP " & CInt(Response.StatusCode))
            End If
            Return ParseTicketResponse(Await Response.Content.ReadAsStringAsync())
        End Using
    End Function

    Public Function ParseTicketResponse(Json As String) As String
        Dim Root = JsonDocument.Parse(Json).RootElement
        Dim Ticket = GetString(Root, "ticket")
        If String.IsNullOrWhiteSpace(Ticket) Then
            Throw New Exception("Hotel ticket endpoint returned no ticket")
        End If
        Return Ticket
    End Function

    ''' <summary>Reads a habbo://hab?server=&lt;domain&gt;&amp;token=&lt;ticket&gt; link for the configured hotel. Returns "" when it does not match.</summary>
    Public Function GetTicketFromProtocolLink(Link As String, Domain As String) As String
        If String.IsNullOrWhiteSpace(Link) OrElse Not Link.StartsWith("habbo://", StringComparison.OrdinalIgnoreCase) Then
            Return ""
        End If
        Dim QueryIndex = Link.IndexOf("?"c)
        If QueryIndex < 0 Then
            Return ""
        End If
        Dim Server = ""
        Dim Token = ""
        For Each Pair In Link.Substring(QueryIndex + 1).Split("&"c)
            Dim Separator = Pair.IndexOf("="c)
            If Separator <= 0 Then
                Continue For
            End If
            Dim Key = Pair.Substring(0, Separator)
            Dim Value = Uri.UnescapeDataString(Pair.Substring(Separator + 1))
            If Key = "server" Then Server = Value
            If Key = "token" Then Token = Value
        Next
        If NormalizeDomain(Server) = NormalizeDomain(Domain) AndAlso NormalizeDomain(Domain) <> "" Then
            Return Token
        End If
        Return ""
    End Function

    Friend Function GetString(Element As JsonElement, Name As String) As String
        Dim Value As JsonElement
        If Element.ValueKind = JsonValueKind.Object AndAlso Element.TryGetProperty(Name, Value) Then
            Select Case Value.ValueKind
                Case JsonValueKind.String
                    Return Value.GetString()
                Case JsonValueKind.Number
                    Return Value.GetRawText()
            End Select
        End If
        Return ""
    End Function

    Friend Function IsValidEnvironment(Environment As String) As Boolean
        Return KnownEnvironments.Contains(Environment)
    End Function
End Module

''' <summary>Parsed https://&lt;domain&gt;/api/client/air.json.</summary>
Public Class HotelClientConfig
    Public Domain As String = ""
    Public Name As String = ""
    Public Environment As String = CustomHotel.DefaultEnvironment
    Public ClientVersion As String = ""
    Public ClientUrl As String = ""
    Public GameHost As String = ""
    Public GamePorts As String = ""
    Public UrlPrefix As String = ""
    Public WebApi As String = ""
    Public PocketApi As String = ""
    Public RsaModulus As String = ""
    Public TicketUrl As String = ""
    Public ExtraConfiguration As New Dictionary(Of String, String)

    Public Shared Function Parse(Json As String, Domain As String) As HotelClientConfig
        Dim Root = JsonDocument.Parse(Json).RootElement
        Dim Config As New HotelClientConfig With {.Domain = CustomHotel.NormalizeDomain(Domain)}
        Config.Name = CustomHotel.GetString(Root, "name")
        Dim Environment = CustomHotel.GetString(Root, "environment")
        If Environment <> "" Then
            Config.Environment = Environment.ToLowerInvariant()
        End If
        If Not CustomHotel.IsValidEnvironment(Config.Environment) Then
            Throw New Exception("Hotel config environment '" & Config.Environment & "' is not one the AIR client accepts")
        End If

        Dim Element As JsonElement
        If Root.TryGetProperty("client", Element) Then
            Config.ClientVersion = CustomHotel.GetString(Element, "version")
            Config.ClientUrl = CustomHotel.GetString(Element, "url")
        End If
        If Root.TryGetProperty("game", Element) Then
            Config.GameHost = CustomHotel.GetString(Element, "host")
            Config.GamePorts = CustomHotel.GetString(Element, "ports")
        End If
        If Root.TryGetProperty("urls", Element) Then
            Config.UrlPrefix = CustomHotel.GetString(Element, "prefix")
            Config.WebApi = CustomHotel.GetString(Element, "webApi")
            Config.PocketApi = CustomHotel.GetString(Element, "pocketApi")
        End If
        If Root.TryGetProperty("rsa", Element) Then
            Config.RsaModulus = CustomHotel.GetString(Element, "modulus")
        End If
        If Root.TryGetProperty("ticket", Element) Then
            Config.TicketUrl = CustomHotel.GetString(Element, "url")
        End If
        If Root.TryGetProperty("configuration", Element) AndAlso Element.ValueKind = JsonValueKind.Object Then
            For Each Item In Element.EnumerateObject()
                Config.ExtraConfiguration(Item.Name) = If(Item.Value.ValueKind = JsonValueKind.String, Item.Value.GetString(), Item.Value.GetRawText())
            Next
        End If

        If Config.GameHost = "" OrElse Config.GamePorts = "" Then
            Throw New Exception("Hotel config needs game.host and game.ports")
        End If
        If Config.UrlPrefix = "" Then Config.UrlPrefix = "https://" & Config.Domain
        Config.UrlPrefix = Config.UrlPrefix.TrimEnd("/"c)
        If Config.WebApi = "" Then Config.WebApi = Config.UrlPrefix
        If Config.PocketApi = "" Then Config.PocketApi = Config.UrlPrefix
        If Config.RsaModulus <> "" AndAlso Not Regex.IsMatch(Config.RsaModulus, "^[0-9a-fA-F]{128,}$") Then
            Throw New Exception("Hotel config rsa.modulus must be a hex string")
        End If
        If Config.TicketUrl.StartsWith("/") Then
            Config.TicketUrl = "https://" & Config.Domain & Config.TicketUrl
        End If
        If Config.ClientUrl <> "" AndAlso Config.ClientVersion = "" Then
            Throw New Exception("Hotel config client.url needs client.version")
        End If
        Return Config
    End Function

    ''' <summary>Embedded configuration lines to set. Keys use the environment suffix the client resolves in updateEnvironmentVariables.</summary>
    Public Function GetConfigurationOverrides() As Dictionary(Of String, String)
        Dim ConfigValues As New Dictionary(Of String, String) From {
            {"connection.info.host." & Environment, GameHost},
            {"connection.info.port." & Environment, GamePorts},
            {"url.prefix." & Environment, UrlPrefix},
            {"web.api." & Environment, WebApi},
            {"pocket.api." & Environment, PocketApi}
        }
        If Name <> "" Then
            ConfigValues("connection.info.name." & Environment) = Name
        End If
        For Each Item In ExtraConfiguration
            ConfigValues(Item.Key) = Item.Value
        Next
        Return ConfigValues
    End Function

    ''' <summary>Stable fingerprint of everything that ends up in the patched SWF.</summary>
    Public Function GetPatchFingerprint() As String
        Dim Builder As New StringBuilder()
        For Each Item In GetConfigurationOverrides().OrderBy(Function(x) x.Key, StringComparer.Ordinal)
            Builder.Append(Item.Key).Append("="c).Append(Item.Value).Append(ChrW(10))
        Next
        Builder.Append("rsa=").Append(RsaModulus.ToUpperInvariant())
        Return Builder.ToString()
    End Function
End Class

''' <summary>
''' Rewrites the hotel endpoints embedded in HabboAir.swf.
''' The AIR client only connects to hosts listed in its embedded common_configuration (connection.info.host.&lt;env&gt;, url.prefix.&lt;env&gt;, ...),
''' and verifies the Diffie-Hellman handshake with an RSA modulus kept in an RC4-obfuscated string table. Both live in DefineBinaryData tags.
''' </summary>
Public Module HotelSwfPatcher
    Private Const DefineBinaryDataTag As Integer = 87

    Private Class SwfTag
        Public Code As Integer
        Public IsLong As Boolean
        Public Data As Byte()
    End Class

    Public Sub PatchFile(SourcePath As String, DestinationPath As String, Config As HotelClientConfig)
        Dim Patched = Patch(File.ReadAllBytes(SourcePath), Config)
        File.WriteAllBytes(DestinationPath, Patched)
    End Sub

    Public Function Patch(Swf As Byte(), Config As HotelClientConfig) As Byte()
        Dim Signature = Encoding.ASCII.GetString(Swf, 0, 3)
        Dim Version = Swf(3)
        Dim Body As Byte()
        Select Case Signature
            Case "FWS"
                Body = New Byte(Swf.Length - 9) {}
                Array.Copy(Swf, 8, Body, 0, Body.Length)
            Case "CWS"
                Using Input As New MemoryStream(Swf, 8, Swf.Length - 8), Inflater As New ZLibStream(Input, CompressionMode.Decompress), Output As New MemoryStream()
                    Inflater.CopyTo(Output)
                    Body = Output.ToArray()
                End Using
            Case Else
                Throw New Exception("Unsupported SWF type " & Signature & " (decrypt it first)")
        End Select

        Dim HeaderLength = GetHeaderLength(Body)
        Dim Tags = ReadTags(Body, HeaderLength)

        Dim ConfigurationPatched = False
        For Each Tag In Tags.Where(Function(x) x.Code = DefineBinaryDataTag)
            Dim Text = TryGetText(Tag.Data, 6)
            If Text IsNot Nothing AndAlso Text.Contains("connection.info.host.en=") AndAlso Text.Contains("live.environment.list=") Then
                Tag.Data = Tag.Data.Take(6).Concat(Encoding.UTF8.GetBytes(PatchConfigurationText(Text, Config.GetConfigurationOverrides()))).ToArray()
                ConfigurationPatched = True
            End If
        Next
        If Not ConfigurationPatched Then
            Throw New Exception("Embedded client configuration not found in HabboAir.swf")
        End If

        If Config.RsaModulus <> "" Then
            PatchRsaModulus(Tags, Config.RsaModulus)
        End If

        Using Output As New MemoryStream()
            Output.Write(Body, 0, HeaderLength)
            For Each Tag In Tags
                WriteTag(Output, Tag)
            Next
            Dim NewBody = Output.ToArray()
            Using Result As New MemoryStream()
                Result.Write(Encoding.ASCII.GetBytes(Signature), 0, 3)
                Result.WriteByte(Version)
                Result.Write(BitConverter.GetBytes(CUInt(NewBody.Length + 8)), 0, 4)
                If Signature = "CWS" Then
                    Using Deflater As New ZLibStream(Result, CompressionLevel.Optimal, True)
                        Deflater.Write(NewBody, 0, NewBody.Length)
                    End Using
                Else
                    Result.Write(NewBody, 0, NewBody.Length)
                End If
                Return Result.ToArray()
            End Using
        End Using
    End Function

    ''' <summary>Replaces "key=value" lines in the embedded configuration, or inserts them before "configuration.readonly" so they stay writable.</summary>
    Public Function PatchConfigurationText(Text As String, ConfigValues As Dictionary(Of String, String)) As String
        Dim Lines = Text.Replace(vbCrLf, vbLf).Split(ChrW(10)).ToList()
        Dim Pending As New Dictionary(Of String, String)(ConfigValues)
        For Index = 0 To Lines.Count - 1
            Dim Line = Lines(Index)
            Dim Separator = Line.IndexOf("="c)
            If Separator <= 0 OrElse Line.StartsWith("#") Then
                Continue For
            End If
            Dim Key = Line.Substring(0, Separator).Trim()
            If Pending.ContainsKey(Key) Then
                Lines(Index) = Key & "=" & Pending(Key)
                Pending.Remove(Key)
            End If
        Next
        If Pending.Count > 0 Then
            Dim InsertAt = Lines.FindIndex(Function(x) x.Trim().StartsWith("configuration.readonly"))
            If InsertAt < 0 Then InsertAt = Lines.Count
            Lines.InsertRange(InsertAt, Pending.Select(Function(x) x.Key & "=" & x.Value))
        End If
        Return String.Join(vbLf, Lines)
    End Function

    ''' <summary>
    ''' The client's string table is three binaries: RC4 keys (count byte + 16-byte keys), the encrypted strings
    ''' (int count, then int length + bytes, each RC4'd with key[index mod count]) and an XOR index int.
    ''' The RSA modulus is the hex string in that table; RSAKey.parsePublicKey uses it with exponent 10001.
    ''' </summary>
    Private Sub PatchRsaModulus(Tags As List(Of SwfTag), Modulus As String)
        Dim Binaries = Tags.Where(Function(x) x.Code = DefineBinaryDataTag AndAlso x.Data.Length > 6).ToList()
        For Each KeyTag In Binaries
            Dim KeyData = KeyTag.Data.Skip(6).ToArray()
            If KeyData.Length < 17 OrElse KeyData.Length - 1 <> 16 * KeyData(0) Then
                Continue For
            End If
            Dim Keys = Enumerable.Range(0, KeyData(0)).Select(Function(i) KeyData.Skip(1 + 16 * i).Take(16).ToArray()).ToList()
            For Each StringTag In Binaries
                Dim Strings = TryReadStringTable(StringTag.Data.Skip(6).ToArray(), Keys)
                If Strings Is Nothing Then
                    Continue For
                End If
                Dim HexIndex = Strings.FindIndex(Function(x) Regex.IsMatch(Encoding.UTF8.GetString(x), "^[0-9a-fA-F]{256,}$"))
                If HexIndex < 0 Then
                    Continue For
                End If
                Strings(HexIndex) = Encoding.UTF8.GetBytes(Modulus.ToUpperInvariant())
                StringTag.Data = StringTag.Data.Take(6).Concat(WriteStringTable(Strings, Keys)).ToArray()
                Return
            Next
        Next
        Throw New Exception("RSA key table not found in HabboAir.swf")
    End Sub

    Private Function TryReadStringTable(Data As Byte(), Keys As List(Of Byte())) As List(Of Byte())
        If Data.Length < 8 Then Return Nothing
        Dim Count = ReadInt32BigEndian(Data, 0)
        If Count <= 0 OrElse Count > 100000 Then Return Nothing
        Dim Position = 4
        Dim Result As New List(Of Byte())
        For Index = 0 To Count - 1
            If Position + 4 > Data.Length Then Return Nothing
            Dim Length = ReadInt32BigEndian(Data, Position)
            Position += 4
            If Length < 0 OrElse Position + Length > Data.Length Then Return Nothing
            Result.Add(Rc4(Keys(Index Mod Keys.Count), Data.Skip(Position).Take(Length).ToArray()))
            Position += Length
        Next
        If Position <> Data.Length Then Return Nothing
        Return Result
    End Function

    Private Function WriteStringTable(Strings As List(Of Byte()), Keys As List(Of Byte())) As Byte()
        Using Output As New MemoryStream()
            WriteInt32BigEndian(Output, Strings.Count)
            For Index = 0 To Strings.Count - 1
                Dim Encrypted = Rc4(Keys(Index Mod Keys.Count), Strings(Index))
                WriteInt32BigEndian(Output, Encrypted.Length)
                Output.Write(Encrypted, 0, Encrypted.Length)
            Next
            Return Output.ToArray()
        End Using
    End Function

    Private Function Rc4(Key As Byte(), Data As Byte()) As Byte()
        Dim S = Enumerable.Range(0, 256).ToArray()
        Dim J = 0
        For I = 0 To 255
            J = (J + S(I) + Key(I Mod Key.Length)) And 255
            Dim Swap = S(I) : S(I) = S(J) : S(J) = Swap
        Next
        Dim Output(Data.Length - 1) As Byte
        Dim X = 0
        J = 0
        For Index = 0 To Data.Length - 1
            X = (X + 1) And 255
            J = (J + S(X)) And 255
            Dim Swap = S(X) : S(X) = S(J) : S(J) = Swap
            Output(Index) = CByte(Data(Index) Xor S((S(X) + S(J)) And 255))
        Next
        Return Output
    End Function

    Private Function TryGetText(Data As Byte(), Offset As Integer) As String
        If Data.Length <= Offset Then Return Nothing
        Try
            Return New UTF8Encoding(False, True).GetString(Data, Offset, Data.Length - Offset)
        Catch
            Return Nothing
        End Try
    End Function

    Private Function GetHeaderLength(Body As Byte()) As Integer
        Dim Bits = Body(0) >> 3
        Dim RectBytes = (5 + 4 * Bits + 7) \ 8
        Return RectBytes + 4 'frame rate + frame count
    End Function

    Private Function ReadTags(Body As Byte(), Start As Integer) As List(Of SwfTag)
        Dim Tags As New List(Of SwfTag)
        Dim Position = Start
        While Position + 2 <= Body.Length
            Dim Header = BitConverter.ToUInt16(Body, Position)
            Position += 2
            Dim Code = Header >> 6
            Dim Length As Integer = Header And &H3F
            Dim IsLong = Length = &H3F
            If IsLong Then
                Length = BitConverter.ToInt32(Body, Position)
                Position += 4
            End If
            Dim Data(Length - 1) As Byte
            Array.Copy(Body, Position, Data, 0, Length)
            Tags.Add(New SwfTag With {.Code = Code, .IsLong = IsLong, .Data = Data})
            Position += Length
            If Code = 0 Then Exit While
        End While
        Return Tags
    End Function

    Private Sub WriteTag(Output As Stream, Tag As SwfTag)
        If Tag.Data.Length < &H3F AndAlso Not Tag.IsLong Then
            Output.Write(BitConverter.GetBytes(CUShort((Tag.Code << 6) Or Tag.Data.Length)), 0, 2)
        Else
            Output.Write(BitConverter.GetBytes(CUShort((Tag.Code << 6) Or &H3F)), 0, 2)
            Output.Write(BitConverter.GetBytes(Tag.Data.Length), 0, 4)
        End If
        Output.Write(Tag.Data, 0, Tag.Data.Length)
    End Sub

    Private Function ReadInt32BigEndian(Data As Byte(), Offset As Integer) As Integer
        Return (CInt(Data(Offset)) << 24) Or (CInt(Data(Offset + 1)) << 16) Or (CInt(Data(Offset + 2)) << 8) Or Data(Offset + 3)
    End Function

    Private Sub WriteInt32BigEndian(Output As Stream, Value As Integer)
        Output.WriteByte(CByte((Value >> 24) And 255))
        Output.WriteByte(CByte((Value >> 16) And 255))
        Output.WriteByte(CByte((Value >> 8) And 255))
        Output.WriteByte(CByte(Value And 255))
    End Sub
End Module
