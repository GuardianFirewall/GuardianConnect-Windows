using GuardianConnect.API.Model;
using GuardianConnect.Credentials;
using GuardianConnect.Helpers;
using GuardianConnect.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

//using Newtonsoft.Json;
//using Newtonsoft.Json.Linq;
using Serilog;
using System;
using System.Text.Json;
using Win32Calls;
using Win32Calls.WFP;

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

#if true
Log.Information("Calling CreateOrUpdateEntry()...");

var hostName = "newyork-ipsec-14.guardianapp.com";
var hostDisplay = "New York, NY";
var eapUser = "e9d538e6b3e3eb43";
var eapPassword = "iPcozIEKUFRL";
var entryName = "Guardian Firewall - New York, NY";
var response = ConnectionRoutines.CreateOrUpdateEntry(entryName, hostName, eapUser, eapPassword, null);

var dialResponse = ConnectionRoutines.ConnectEntry(); 


#endif

#if false
RegionUtils.LongRunningRefreshTask(new CancellationToken());
TestRegionStuff();
var subcreds = GRDSubscriberCredential.GetCurrentStoredSubscriberCredential();
subcreds.Store();

bool f = ClientPipe.Connect();
Win32Calls.ConnectionRoutines.GetRasConnections(out uint cConnections);

Log.Information($"Connected to service? {f}");
var currentStatus = ClientPipe.GetCurrentVpnConnectionStatus();
if (currentStatus.ConnectionState == ConnectionStateEnum.Connected)
{
    ClientPipe.DisconnectVPNConnection(currentStatus.EntryName);
}


var serviceLogs = ClientPipe.GetServiceLogLinesAsync(500);

string connectHost = "connect-api.guardianapp.com";
string petValue = "7uImrN1BoKOT0UMC7Zmc4r7vFlgnGl9U";
PeTokenRequest petRequest = new PeTokenRequest(petValue);
TestSubCred();

void TestSubCred()
{
    // TJE - remove comment - taken from AuthenticateUser.cs in UI
    //Uri uri = new Uri($"https://{connectHost}/api/v1.2/subscriber-credential/create");
    Uri uri = new Uri($"https://{connectHost}/api/v1.2/subscriber-credential/create");
    try
    {
        var pet = new PeTokenRequest("pe-token", petRequest.PeToken);

        string serializedPetReq = JsonSerializer.Serialize(pet, PeTokenRequestJsonContext.Default.PeTokenRequest);
        _logger.LogInformation($"CreateSubscriberCredentialForBundleId: serializedPetReq = '{serializedPetReq}'");
        HttpContent content = new StringContent(serializedPetReq);
        content.Headers.Remove("Content-Type");
        content.Headers.Add("Content-Type", "application/json; charset=utf-8");
        HttpResponseMessage response = HttpUtils.Client.PostAsync(uri, content).Result;
        if (response.IsSuccessStatusCode)
        {
            string result = response.Content.ReadAsStringAsync().Result;
            var jwt = JsonSerializer.Deserialize<GrdSubscriberCredentialJwt>(result,
                GRDSubScriberCredentialJwtJsonContext.Default.GrdSubscriberCredentialJwt);
            var LiveGrdCredential = new GRDSubscriberCredential(jwt!.SubscriberCredential!);
            LiveGrdCredential.Store();
            _logger.LogInformation("CreateSubscriberCredentialForBundleId(): JWT obtained.");
        }
    }
    catch (Exception e)
    {
        Log.Error(e, "Exception thrown");
    }
}

void subcredtest2()
{
    var base64Payload = "NOTSET";

    string payLoad = Common.DecodeFrom64(base64Payload);
    _logger.LogInformation($"ParseSubscriberCredentials: jwt payload = '{payLoad}'");
    var subCred = JsonSerializer.Deserialize<GRDSubscriberCredential>(payLoad,
        GRDSubscriberCredentialJsonContext.Default.GRDSubscriberCredential);

    var SubscriptionType = subCred.SubscriptionType ?? string.Empty;
    var SubscriptionTypePretty = subCred.SubscriptionTypePretty ?? string.Empty;

//long expDateTimeSecondsSinceUnixEpoch = (long)gscDict["subscription-expiration-date"];
//SubscriptionExpirationDate = Common.DateOnlyFromAppleDTI1970(expDateTimeSecondsSinceUnixEpoch);
//SubscriptionExpirationDate = DateTimeOffset.FromUnixTimeSeconds((long)gscDict!["subscription-expiration-date"]).DateTime;
    var SubscriptionExpirationDate =
        DateTimeOffset.FromUnixTimeSeconds(subCred.SubscriptionExpirationDate.ToFileTimeUtc()).DateTime;
    var TokenExpirationDate =
        DateTimeOffset.FromUnixTimeSeconds((subCred.TokenExpirationDate).ToFileTimeUtc()).DateTime;
//SubscriptionTypePretty = (string)gscDict["subscription-type"];

}
void TestCredsStuff()
{
    GRDLoginCredentials lc = new GRDLoginCredentials();
    lc.Email = "terdies@dnsfilter.com";
    lc.Password = "P@ssw0rd!";
    Log.Information($"lc.Email = '{lc.Email}', lc.Password = '{lc.Password}'");
    string lcSerial = System.Text.Json.JsonSerializer.Serialize(lc, GRDLoginCredentialsJsonContext.Default.GRDLoginCredentials);
    Log.Information($"lcSerial = '{lcSerial}'");
    Console.ReadLine();
    GRDCredentialManager.LoadCredentialsList();
    var _credentialsList = GRDCredentialManager.CredentialsList;
    var singleCred = _credentialsList[0];
    var serializedSingle = System.Text.Json.JsonSerializer.Serialize(singleCred, GRDCredentialJsonContext.Default.GRDCredential);
    var serializedData = System.Text.Json.JsonSerializer.Serialize(_credentialsList, GRDCredentialJsonContext.Default.ListGRDCredential);
}
void TestRegionStuff()
{
    RegionInputParameter rip = new RegionInputParameter()
    {
        Region = "us-east",
    };
    Log.Information($"RIP.Region = '{rip.Region}'");
    string nsjRIPSerialized = Newtonsoft.Json.JsonConvert.SerializeObject(rip);
    string stjRIPSerialized = System.Text.Json.JsonSerializer.Serialize(rip);
    string stjRIPSerializedWithDfltOpts = System.Text.Json.JsonSerializer.Serialize(rip, JsonSerializerOptions.Default);
    string stjRIPSerializedWithContext =
        System.Text.Json.JsonSerializer.Serialize(rip, RegionInputParameterJsonContext.Default.RegionInputParameter);

    Log.Information(
        $"NSJ: '{nsjRIPSerialized}', STJ: '{stjRIPSerialized}', STJOPT: '{stjRIPSerializedWithDfltOpts}', STJCTX: '{stjRIPSerializedWithContext}'");
    Task t = RegionUtils.RefreshDataAsync();
    t.Wait();
    Task u = RegionUtils.GetHostsForRegion("us-east");
    u.Wait();

    var rhrec = RegionUtils.GetMyRegionHostRecord("us-east");
    Log.Information($"For our region '{rip.Region} we have selected host '{rhrec.Hostname}' in host location '{rhrec.HostLocation()}'");

}

#if false

// Test of GetAdaptersInfo...
string nameToFind = "Guardian Firewall - Atlanta, GA";
char[] buffer = nameToFind.ToCharArray();
VpnUtils.adapterNameToMatch = buffer;
var adapterIndex = VpnUtils.GetAdapterIndexByName();
Log.Information($"Adapter Index={adapterIndex}");

Log.Information("Calling CreateOrUpdateEntry()...");

var hostName = "newyork-ipsec-20.guardianapp.com";
var hostDisplay = "New York, NY";
var eapUser = "e9d538e6b3e3eb43";
var eapPassword = "iPcozIEKUFRL";
var entryName = "Guardian Firewall - New York, NY";
var response = ConnectionRoutines.CreateOrUpdateEntry(entryName, hostName, eapUser, eapPassword, null);

var dialResponse = ConnectionRoutines.ConnectEntry(); 


Log.Information("Calling GetRasConnections...");
var hrcArray = Win32Calls.ConnectionRoutines.GetRasConnections(out uint connectionCount);
//var checkConResult = NativeRoutines.ConnectionRoutines.CheckConnection("NonExistentEntry");
//var hRasConn = Win32Calls.ConnectionRoutines.FindAnyActiveConnection();
if (hrcArray.Length == 0)
{
    Log.Information("No active RAS connections found.");
    Console.Write("Press ENTER...");
    Console.ReadLine();
    return;
}
Console.WriteLine($"Connection Name = '{hrcArray[0].szEntryName}', Device={hrcArray[0].szDeviceName}, Type='{hrcArray[0].szDeviceType}'");
#endif
#endif
Console.Write("Press ENTER...");
Console.ReadLine();
