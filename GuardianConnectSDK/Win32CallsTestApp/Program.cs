using Newtonsoft.Json.Linq;
using Serilog;
using System;
using Win32Calls;
using Windows.Win32.Foundation;
using Win32Calls.WFP;

// See https://aka.ms/new-console-template for more information
Serilog.Log.Logger = new Serilog.LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.Console()
    .CreateLogger();

Log.Information("Hello, World!");

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
Console.Write("Press ENTER...");
Console.ReadLine();
