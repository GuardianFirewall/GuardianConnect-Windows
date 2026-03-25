using GuardianConnect.Helpers;
using GuardianConnect.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using GuardianConnect.API;
using GuardianConnect.Credentials;
using Win32Calls;

// See https://aka.ms/new-console-template for more information
Serilog.Log.Logger = new Serilog.LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.Console()
    .CreateLogger();
var serviceCollection = new ServiceCollection();
serviceCollection.AddLogging(builder =>
{
    builder.AddSerilog(); // Add your desired logging providers
    builder.SetMinimumLevel(LogLevel.Information);
});

var serviceProvider = serviceCollection.BuildServiceProvider();
var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
StaticLoggerFactory.Initialize(loggerFactory);

Log.Information("Hello, World!");

var connAPiServer = "wifi-api-staging.dev.guardianapp.com";
var identifier = "eero1.user.cSDRlZj56acSQ8waulkwbtm9isHAJpNYWE6Z6Q5jkUXKgv9GQYg";
var secret = "FILLTHISINFORTESTING";
var email = "tim@example.com";
#if false
// - let's get the subscriber credentials and then call the GRDConnectSubscriber Create and Register methods

//Console.Write("Enter Connect API Server:");
//var connAPiServer = Console.ReadLine();
//Console.Write("Enter Identifier:");
//var identifier = Console.ReadLine();
//Console.Write("Enter Secret:");
//var secret = Console.ReadLine();
#endif
GRDVPNHelper.CreateSingleton();

ErrorResponse er;
GRDConnectSubscriber? currentConnectSubscriber = new GRDConnectSubscriber();
GRDConnectDevice? currentConnectDevice;

bool testing = true;
do
{
    DisplayMenu();
    Log.Information("Enter choice:");
    var choice = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(choice))
    {
        Log.Information("Invalid choice. Please try again.");
        continue;
    }

    switch (choice)
    {
        case "a":
            (currentConnectSubscriber, er) = GRDConnectSubscriber.GetCurrentSubscriber();
            DisplayError(er, $"Connect Subscriber retrieved successfully");
            break;
        case "b":
        {
            var cs = new GRDConnectSubscriber();
            var acceptedTOS = true;
            (currentConnectSubscriber, er) = cs.RegisterNewConnectSubscriberAsync(acceptedTOS,
                    "TimDevBox", identifier, secret, email).GetAwaiter()
                .GetResult();
            DisplayError(er, $"New Connect Subscriber registered successfully");
        }
            break;
        case "c":
            (currentConnectDevice, er) =
                currentConnectSubscriber.ConnectDeviceReferenceAsync().GetAwaiter().GetResult();
            DisplayError(er, $"Connect Device reference retrieved successfully");
            break;
        case "d":
            (List<GRDConnectDevice>? cdList, er) = currentConnectSubscriber.AllDevicesAsync().GetAwaiter().GetResult();
            DisplayError(er, $"All devices retrieved successfully");
            if (cdList != null)
            {
                foreach (var cd in cdList)
                {
                    Log.Information($"CD's PET: {cd.PEToken}, UUID: {cd.UUID}, Name: {cd.Nickname}");
                }
            }

            break;
        case "e":
            Console.Write("Enter new email address:");
            var newEmail = Console.ReadLine();
            (currentConnectSubscriber, er) = currentConnectSubscriber
                .UpdateConnectSubscriberWithEmailAddressAsync(newEmail).GetAwaiter().GetResult();
            DisplayError(er, $"Connect Subscriber updated successfully");
            break;
        case "f":
            (er) = currentConnectSubscriber.CheckGuardianAccountStateAsync().GetAwaiter().GetResult();
            DisplayError(er, $"Check on Guardian account state returned: '{er.Message}'");
            break;
        case "g":
            Log.Information($"Before call to ValidateConnectSubscriber - PET='{GRDPEToken.GetCurrentPEToken().Token}");
            (currentConnectSubscriber, er) =
                currentConnectSubscriber.ValidateConnectSubscriberAsync().GetAwaiter().GetResult();
            DisplayError(er, $"Validate subscriber returned: '{er.Message}'");
            Log.Information($"After call to ValidateConnectSubscriber - PET='{GRDPEToken.GetCurrentPEToken().Token}");
            break;
        case "h":
            er = GRDConnectSubscriber.DestroySubscriber();
            DisplayError(er, $"Connect Subscriber destroyed successfully");
            break;
        case "i":
            er = currentConnectSubscriber.LogoutConnectSubscriberAsync().GetAwaiter().GetResult();
            DisplayError(er, $"Connect Subscriber logged out successfully");
            break;
        case "j":
            (currentConnectDevice, er) = GRDConnectDevice.GetCurrentDevice();
            DisplayError(er, $"Current Device retrieved successfully");
            break;
        case "k":
        {
            var currentPet = GRDPEToken.GetCurrentPEToken();
            (currentConnectDevice, er) = GRDConnectDevice.GetCurrentDevice();
            er = currentConnectDevice.DeleteDeviceAsync(currentPet.Token, identifier, secret).GetAwaiter().GetResult();
            DisplayError(er, $"Device deleted successfully");
        }
            break;
        case "l":
        {
            var currentPet = GRDPEToken.GetCurrentPEToken();
            Console.Write("Enter new device nickname:");
            var newDeviceNickname = Console.ReadLine();
            (GRDConnectDevice addedDevice, er) = GRDConnectDevice
                .AddConnectDeviceAsync(currentPet.Token, newDeviceNickname, true).GetAwaiter().GetResult();
            DisplayError(er, $"Device added successfully");
        }
            break;
        case "m":
        {
            Console.Write("Enter new device nickname:");
            var currentPet = GRDPEToken.GetCurrentPEToken();
            var newDeviceNickname = Console.ReadLine();
            (currentConnectDevice, er) = GRDConnectDevice.GetCurrentDevice();
            var (a, b) = currentConnectDevice.UpdateConnectDeviceNicknameAsync(currentPet.Token, "TimDevBox").GetAwaiter().GetResult();
        }
            break;
        case "n":
        {
            var currentPet = GRDPEToken.GetCurrentPEToken();
            (List<GRDConnectDevice>? cdListByPet, er) = GRDConnectDevice.ListConnectDevicesForPETokenAsync(currentPet.Token).GetAwaiter().GetResult();
            DisplayError(er, $"All devices retrieved successfully");
            if (cdListByPet != null)
            {
                foreach (var cd in cdListByPet)
                {
                    Log.Information($"CD's PET: {cd.PEToken}, UUID: {cd.UUID}, Name: {cd.Nickname}");
                }
            }
        }
            break;
        case "o":
            Log.Information("Not implemented yet. Use 'k' selection");
            break;
        case "p":
        {
            var latestPet = GRDPEToken.GetCurrentPEToken();
            (GRDConnectDevice latestDevice, er) = GRDConnectDevice.GetCurrentDevice();
            (GRDConnectDevice? validateDevice, er) = latestDevice.ValidateConnectDeviceAsync(latestPet.Token).GetAwaiter().GetResult();
            DisplayError(er, $"Device validated successfully");
        }
            break;
        case "x":
            testing = false;
            break;
        default:
            Log.Information("Invalid choice. Please try again.");
            continue;

    }
} while (testing);
Environment.Exit(0);
//////
//////// Test #168
//////
//////// Setup
//////(currentConnectSubscriber, er) = GRDConnectSubscriber.GetCurrentSubscriber();
//////DisplayError(er, $"Connect Subscriber retrieved successfully");
//////
//////// TESTS
//////(cd, er) = cs.ConnectDeviceReferenceAsync().GetAwaiter().GetResult();
//////DisplayError(er, $"Connect Device retrieved successfully");
//////
//////// #170/#190
////////er = cs.CheckGuardianAccountStateAsync().GetAwaiter().GetResult();
////////DisplayError(er, $"Check on Guardian account state returned: '{er.Message}'");
//////
//////// #171/#187 - NOT WORKING
//////// er = cs.UpdateConnectSubscriberWithEmailAddressAsync("terdies@foo.com").GetAwaiter().GetResult().errorResponse;
//////// DisplayError(er, $"Check on Guardian account state returned: '{er.Message}'");
//////
//////// #172/#188
////////er = cs.ValidateConnectSubscriberAsync().GetAwaiter().GetResult().errorResponse;
////////DisplayError(er, $"Validate subscriber returned: '{er.Message}'");
//////
//////// TEST169
//////#if false
//////Console.WriteLine("Setting values and calling RegisterNewConnectSubscriberAsync()...");
//////connAPiServer = "wifi-api-staging.dev.guardianapp.com";
//////GRDVPNHelper.Singleton.ConnectAPIHostname = connAPiServer;
//////identifier = GRDKeychain.ReadRegistryData("TESTVALUE_CS_Identifier");
//////secret = GRDKeychain.ReadRegistryData("TESTVALUE_CS_Secret");
//////
//////GRDConnectSubscriber connectSubscriber = new GRDConnectSubscriber();
//////connectSubscriber.Identifier = identifier;
//////connectSubscriber.Secret = secret;
//////var ( csnew, errorResponse) = connectSubscriber.RegisterNewConnectSubscriberAsync(true, "TimDevBox").Result;
//////DisplayError(errorResponse, $"Connect Subscriber registered successfully with ID: {csnew.CreatedAt}");
////////#endif
//////
////////#else
//////// Set some variables used for calls
//////var hostName = "newyork-ipsec-14.guardianapp.com";
//////var hostDisplay = "New York, NY";
//////var eapUser = "e9d538e6b3e3eb43";
//////var eapPassword = "iPcozIEKUFRL";
//////var entryName = "Guardian Firewall - New York, NY";
//////
//////
//////// Test calls
//////var response = ConnectionRoutines.CreateOrUpdateEntry(entryName, hostName, eapUser, eapPassword, null);
//////
//////var dialResponse = ConnectionRoutines.ConnectEntry(); 
//////
//////
//////#endif
//////
//////#if false
//////RegionUtils.LongRunningRefreshTask(new CancellationToken());
//////TestRegionStuff();
//////var subcreds = GRDSubscriberCredential.GetCurrentStoredSubscriberCredential();
//////subcreds.Store();
//////
//////bool f = ClientPipe.Connect();
//////Win32Calls.ConnectionRoutines.GetRasConnections(out uint cConnections);
//////
//////Log.Information($"Connected to service? {f}");
//////var currentStatus = ClientPipe.GetCurrentVpnConnectionStatus();
//////if (currentStatus.ConnectionState == ConnectionStateEnum.Connected)
//////{
//////    ClientPipe.DisconnectVPNConnection(currentStatus.EntryName);
//////}
//////
//////
//////var serviceLogs = ClientPipe.GetServiceLogLinesAsync(500);
//////
//////string connectHost = "connect-api.guardianapp.com";
//////string petValue = "7uImrN1BoKOT0UMC7Zmc4r7vFlgnGl9U";
//////PeTokenRequest petRequest = new PeTokenRequest(petValue);
//////TestSubCred();
//////
//////void TestSubCred()
//////{
//////    // TJE - remove comment - taken from AuthenticateUser.cs in UI
//////    //Uri uri = new Uri($"https://{connectHost}/api/v1.2/subscriber-credential/create");
//////    Uri uri = new Uri($"https://{connectHost}/api/v1.2/subscriber-credential/create");
//////    try
//////    {
//////        var pet = new PeTokenRequest("pe-token", petRequest.PeToken);
//////
//////        string serializedPetReq = JsonSerializer.Serialize(pet, PeTokenRequestJsonContext.Default.PeTokenRequest);
//////        _logger.LogInformation($"CreateSubscriberCredentialForBundleId: serializedPetReq = '{serializedPetReq}'");
//////        HttpContent content = new StringContent(serializedPetReq);
//////        content.Headers.Remove("Content-Type");
//////        content.Headers.Add("Content-Type", "application/json; charset=utf-8");
//////        HttpResponseMessage response = HttpUtils.Client.PostAsync(uri, content).Result;
//////        if (response.IsSuccessStatusCode)
//////        {
//////            string result = response.Content.ReadAsStringAsync().Result;
//////            var jwt = JsonSerializer.Deserialize<GrdSubscriberCredentialJwt>(result,
//////                GRDSubScriberCredentialJwtJsonContext.Default.GrdSubscriberCredentialJwt);
//////            var LiveGrdCredential = new GRDSubscriberCredential(jwt!.SubscriberCredential!);
//////            LiveGrdCredential.Store();
//////            _logger.LogInformation("CreateSubscriberCredentialForBundleId(): JWT obtained.");
//////        }
//////    }
//////    catch (Exception e)
//////    {
//////        Log.Error(e, "Exception thrown");
//////    }
//////}
//////
//////void subcredtest2()
//////{
//////    var base64Payload = "NOTSET";
//////
//////    string payLoad = Common.DecodeFrom64(base64Payload);
//////    _logger.LogInformation($"ParseSubscriberCredentials: jwt payload = '{payLoad}'");
//////    var subCred = JsonSerializer.Deserialize<GRDSubscriberCredential>(payLoad,
//////        GRDSubscriberCredentialJsonContext.Default.GRDSubscriberCredential);
//////
//////    var SubscriptionType = subCred.SubscriptionType ?? string.Empty;
//////    var SubscriptionTypePretty = subCred.SubscriptionTypePretty ?? string.Empty;
//////
////////long expDateTimeSecondsSinceUnixEpoch = (long)gscDict["subscription-expiration-date"];
////////SubscriptionExpirationDate = Common.DateOnlyFromAppleDTI1970(expDateTimeSecondsSinceUnixEpoch);
////////SubscriptionExpirationDate = DateTimeOffset.FromUnixTimeSeconds((long)gscDict!["subscription-expiration-date"]).DateTime;
//////    var SubscriptionExpirationDate =
//////        DateTimeOffset.FromUnixTimeSeconds(subCred.SubscriptionExpirationDate.ToFileTimeUtc()).DateTime;
//////    var TokenExpirationDate =
//////        DateTimeOffset.FromUnixTimeSeconds((subCred.TokenExpirationDate).ToFileTimeUtc()).DateTime;
////////SubscriptionTypePretty = (string)gscDict["subscription-type"];
//////
//////}
//////void TestCredsStuff()
//////{
//////    GRDLoginCredentials lc = new GRDLoginCredentials();
//////    lc.Email = "terdies@dnsfilter.com";
//////    lc.Password = "P@ssw0rd!";
//////    Log.Information($"lc.Email = '{lc.Email}', lc.Password = '{lc.Password}'");
//////    string lcSerial = System.Text.Json.JsonSerializer.Serialize(lc, GRDLoginCredentialsJsonContext.Default.GRDLoginCredentials);
//////    Log.Information($"lcSerial = '{lcSerial}'");
//////    Console.ReadLine();
//////    GRDCredentialManager.LoadCredentialsList();
//////    var _credentialsList = GRDCredentialManager.CredentialsList;
//////    var singleCred = _credentialsList[0];
//////    var serializedSingle = System.Text.Json.JsonSerializer.Serialize(singleCred, GRDCredentialJsonContext.Default.GRDCredential);
//////    var serializedData = System.Text.Json.JsonSerializer.Serialize(_credentialsList, GRDCredentialJsonContext.Default.ListGRDCredential);
//////}
//////void TestRegionStuff()
//////{
//////    RegionInputParameter rip = new RegionInputParameter()
//////    {
//////        Region = "us-east",
//////    };
//////    Log.Information($"RIP.Region = '{rip.Region}'");
//////    string nsjRIPSerialized = Newtonsoft.Json.JsonConvert.SerializeObject(rip);
//////    string stjRIPSerialized = System.Text.Json.JsonSerializer.Serialize(rip);
//////    string stjRIPSerializedWithDfltOpts = System.Text.Json.JsonSerializer.Serialize(rip, JsonSerializerOptions.Default);
//////    string stjRIPSerializedWithContext =
//////        System.Text.Json.JsonSerializer.Serialize(rip, RegionInputParameterJsonContext.Default.RegionInputParameter);
//////
//////    Log.Information(
//////        $"NSJ: '{nsjRIPSerialized}', STJ: '{stjRIPSerialized}', STJOPT: '{stjRIPSerializedWithDfltOpts}', STJCTX: '{stjRIPSerializedWithContext}'");
//////    Task t = RegionUtils.RefreshDataAsync();
//////    t.Wait();
//////    Task u = RegionUtils.GetHostsForRegion("us-east");
//////    u.Wait();
//////
//////    var rhrec = RegionUtils.GetMyRegionHostRecord("us-east");
//////    Log.Information($"For our region '{rip.Region} we have selected host '{rhrec.Hostname}' in host location '{rhrec.HostLocation()}'");
//////
//////}
//////
//////#if false
//////
//////// Test of GetAdaptersInfo...
//////string nameToFind = "Guardian Firewall - Atlanta, GA";
//////char[] buffer = nameToFind.ToCharArray();
//////VpnUtils.adapterNameToMatch = buffer;
//////var adapterIndex = VpnUtils.GetAdapterIndexByName();
//////Log.Information($"Adapter Index={adapterIndex}");
//////
//////Log.Information("Calling CreateOrUpdateEntry()...");
//////
//////var hostName = "newyork-ipsec-20.guardianapp.com";
//////var hostDisplay = "New York, NY";
//////var eapUser = "e9d538e6b3e3eb43";
//////var eapPassword = "iPcozIEKUFRL";
//////var entryName = "Guardian Firewall - New York, NY";
//////var response = ConnectionRoutines.CreateOrUpdateEntry(entryName, hostName, eapUser, eapPassword, null);
//////
//////var dialResponse = ConnectionRoutines.ConnectEntry(); 
//////
//////
//////Log.Information("Calling GetRasConnections...");
//////var hrcArray = Win32Calls.ConnectionRoutines.GetRasConnections(out uint connectionCount);
////////var checkConResult = NativeRoutines.ConnectionRoutines.CheckConnection("NonExistentEntry");
////////var hRasConn = Win32Calls.ConnectionRoutines.FindAnyActiveConnection();
//////if (hrcArray.Length == 0)
//////{
//////    Log.Information("No active RAS connections found.");
//////    Console.Write("Press ENTER...");
//////    Console.ReadLine();
//////    return;
//////}
//////Console.WriteLine($"Connection Name = '{hrcArray[0].szEntryName}', Device={hrcArray[0].szDeviceName}, Type='{hrcArray[0].szDeviceType}'");
//////#endif
//////#endif
//////Console.Write("Press ENTER...");
//////Console.ReadLine();

void DisplayError(ErrorResponse errorResponse, string message)
{
    if (errorResponse.IsError)
    {
        Log.Error(errorResponse.GRDApiError != null
            ? $"Message: {errorResponse.Message}, GrdApiError: {errorResponse.GRDApiError.ToString()}"
            : $"Message: {errorResponse.Message}");
    }
    else
    {
        Log.Information(message);
    }
}

void DisplayMenu()
{
    Log.Information("ConnectSubscriber Issues: (GRDConnectSubscriber class)");
    Log.Information("a.  GetCurrentSubscriber #163 (#161 Create class -> #162 InitFromDictionary)");
    Log.Information("b.  RegisterNewConnectSubscriber #169 (#161 Create) ->  #185 HK: /api/v1.3/partners/subscribers/new -> (#164 (Store) -> (device: #174|#175|177)");
    Log.Information("c.  ConnectDeviceReference #168 -> #186 HK: /api/v1.2/partners/subscriber/device-reference");
    Log.Information("d.  AllDevices #167 -> #193 (using CS's Identifier/Secret HK: /api/v1.2/partners/subscriber/devices/list");
    Log.Information("e.  UpdateConnectSubscriberWithEmail #171 -> #187 HK: /api/v1.2/partners/subscriber/update -> #162 InitFromDictionary -> #164 Store");
    Log.Information("f.  CheckGuardianAccountState #170 -> #190 HK: /api/v1.2/partners/subscriber/account-creation-state");
    Log.Information("g.  ValidateConnectSubscriber #172 -> #188 HK: /api/v1.2/partners/subscriber/validate -> #162 InitFromDictionary -> #164 Store");
    Log.Information("h.  DestroySubscriber #165 -> (no-issue: calls GRDKeychain to remove local CS/CD/PET) ");
    Log.Information("i.  Logout #173 -> #189 HK: /api/v1.2/partners/subscriber/logout (deletes CS/CD(s)/PET on backend host)");
    Log.Information(" ");
    Log.Information("ConnectDevice Issues: (GRDConnectDevice class)");
    Log.Information("j.  [GRDConnectDevice] CurrentDevice #176 -> #175 InitFromDictionary (#174 Create class)");
    Log.Information("k.  [GRDConnectDevice] Delete #178");
    Log.Information("l.  [GRDConnectDevice] AddConnectDevice #179 -> #191 HK: /api/v1.2/partners/subscriber/devices/add -> #162 InitFromDictionary -> #164 Store");
    Log.Information("m.  [GRDConnectDevice] UpdateConnectDevice #180 -> #192 HK: /api/v1.2/partners/subscriber/device/update -> #162 InitFromDictionary -> #164 Store");
    Log.Information("n.  [GRDConnectDevice] ListConnectDevicesForPEToken #181 -> #193 (using PEToken) HK: /api/v1.2/partners/subscriber/devices/list");
    Log.Information("o. ***DISABLED*** [GRDConnectDevice] DeleteDeviceWithPEToken #182 -> #194 (using PEToken) HK: /api/v1.2/partners/subscriber/device/delete");
    Log.Information("p.  [GRDConnectDevice] ValidateConnectDeviceWithDevicePEToken #183 -> #195 (using PEToken) HK: /api/v1.2/partners/subscriber/device/validate");
    Log.Information("x.   Exit.");
}