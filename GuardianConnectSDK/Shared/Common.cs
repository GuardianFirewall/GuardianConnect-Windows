using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Configuration;
using Serilog.Events;
using Serilog.Enrichers.WithCaller;
using Serilog.Formatting.Json;

namespace GuardianConnect.Shared;

public class Common
{
    //
    //  Shared.h
    //  Guardian
    //
    //  Created by Kevin Bradley on 10/13/20.
    //  Copyright © 2020 Sudo Security Group Inc. All rights reserved.
    //

    //typedef void (^StandardBlock)(BOOL success, NSString * _Nullable errorMessage);
    //typedef void (^ResponseBlock)(NSDictionary * _Nullable response, NSString * _Nullable errorMessage, BOOL success);

    // Define below to 0 to make guardian specific code inactive
    //#define GUARDIAN_INTERNAL 1

    public enum PowerTransitionStates { Suspend, Resume, Running }

    public enum PowerNotificationTypes
    {
        PBT_APMPOWERSTATUSCHANGE = 10,  //(0xA) Power status has changed.
        PBT_APMRESUMEAUTOMATIC = 18,    // (0x12) Operation is resuming automatically from a low-power state.
                                        // This message is sent every time the system resumes.
        PBT_APMRESUMESUSPEND = 7,       // (0x7) Operation is resuming from a low-power state.
                                        // This message is sent after PBT_APMRESUMEAUTOMATIC if the resume
                                        // is triggered by user input, such as pressing a key.
        PBT_APMSUSPEND = 4,             // (0x4) System is suspending operation.
        PBT_POWERSETTINGCHANGE = 32787  //  (0x8013)
    }

    public const string kServicePowerResumeReconnectAttempts = "ServicePowerResumeAttempts";
    public const string DefaultPowerResumeReconnectAttempts = "10";
    public const string kPowerResumeReconnectWatcherTries = "ClientPowerResumeWatcherTries";
    
    public const string kAppNeedsSelfRepair = @"guardianNeedsSelfRepair";
    public const string kWhetherToSpawnUpdateChecker = "StartUpdateChecker";
    public const string kWhetherLoggingCurrentlyOn = "WhetherLoggingCurrentlyOn";
    public const string kVpnCallParametersForReboot = "VPNCallParametersForReboot";

    /// Public production Connect API environment
    public const string kConnectAPIHostname = @"connect-api.guardianapp.com";

    public const string kGuardianNetworkHealthStatusNotification = @"networkHealthStatusNotification";
    public const string kGuardianSuccessfulSubscription = @"successfullySubscribedToGuardian";

    public const string kGRDDefaultGatewayUUID = @"kGRDDefaultGatewayUUID";

    public const string kVPNHadNetworkHealthDisconnect = @"vpnHadNetworkHealthDisconnect";
    public const string kGRDHostnameOverride = @"APIHostname-Override";
    public const string kGRDEAPSharedHostname = @"SharedAPIHostname";
    public const string kGRDVPNHostLocation = @"kGRDVPNHostLocation";
    public const string kGRDIncludesAllNetworks = @"kGRDIncludesAllNetworks";
    public const string kGRDExcludeLocalNetworks = @"kGRDExcludeLocalNetworks";
    public const string kGRDWifiAssistEnableFallback = @"kGRDWifiAssistEnableFallback";
    public const string kGRDRefreshProxySettings = @"kGRDRefreshProxySettings";
    public const string kGRDTunnelEnabled = @"kGRDTunnelEnabled";
    public const string kGuardianTransportProtocol = @"kGuardianTransportProtocol";

    public const string kGRDWGDevicePublicKey = @"wg-device-public-key";
    public const string kGRDWGDevicePrivateKey = @"wg-device-private-key";
    public const string kGRDWGServerPublicKey = @"server-public-key";
    public const string kGRDWGIPv4Address = @"mapped-ipv4-address";
    public const string kGRDWGIPv6Address = @"mapped-ipv6-address";
    public const string kGRDClientId = @"client-id";


    public const string kGuardianRegionOverride = @"kGuardianRegionOverride";
    public const string kGuardianFauxTimeZone = @"faux-timezone";
    public const string kGuardianFauxTimeZonePretty = @"faux-timezone-pretty";
    public const string kGuardianUseFauxTimeZone = @"use-faux-timezone";
    public const string kKnownHousekeepingTimeZonesForRegions = @"kKnownHousekeepingTimeZonesForRegions";
    public const string housekeepingTimezonesTimestamp = @"housekeepingTimezonesTimestamp";
    public const string kGuardianAllRegions = @"kGRDAllRegions";
    public const string kGuardianAllRegionsTimeStamp = @"kGRDAllRegionsTimeStamp";
    public const string kKnownGuardianHosts = @"kKnownGuardianHosts";
    public const string kGuardianSubscriptionExpiresDate = @"subscriptionExpiresDate";
    public const string kGuardianSubscriptionTypeEssentials = @"grd_type_essentials";
    public const string kGuardianSubscriptionDayPass = @"grd_day_pass";
    public const string kGuardianSubscriptionDayPassAlt = @"grd_day_pass_alt";
    public const string kGuardianSubscriptionGiftedDayPass = @"grd_gifted_day_pass";
    public const string kGuardianSubscriptionCustomDayPass = @"custom_day_pass";
    public const string kGuardianSubscriptionMonthly = @"grd_monthly";
    public const string kGuardianSubscriptionThreeMonths = @"grd_three_months";
    public const string kGuardianSubscriptionAnnual = @"grd_annual";
    public const string kGuardianSubscriptionTypeProfessionalIAP = @"grd_pro";
    public const string kGuardianSubscriptionTypeCustomDayPass = @"grd_custom_day_pass";

    public const string kGuardianSubscriptionTypeIntroductory = @"grd_day_pass_introductory";

    // "grd_teams" is an umbrealla description. Should never be used in production since it does not accurately describe the subscription length etc.
    public const string kGuardianSubscriptionTypeTeams = @"grd_teams";
    public const string kGuardianSubscriptionTypeTeamsAnnual = @"grd_teams_annual";

    public const string kGuardianFreeTrial3Days = @"grd_trial_3_days";
    public const string kGuardianExtendedTrial30Days = @"grd_extended_trial_30_days";
    public const string kGuardianTrialBalanceDayPasses = @"grd_trial_balance_day_passes";
    public const string kGuardianSubscriptionFreeTrial = @"free_trial";

    public const string kGuardianSubscriptionTypeVisionary = @"grd_visionary";
    public const string kGuardianSubscriptionTypeProfessionalMonthly = @"grd_pro_monthly";
    public const string kGuardianSubscriptionTypeProfessionalYearly = @"grd_pro_yearly";
    public const string kGuardianSubscriptionTypeProfessionalBrave = @"bravevpn.yearly-pro";

    public const string kGuardianFreeTrialPeTokenSet = @"kGRDFreeTrialPETokenSet";
    public const string kGuardianDayPassExpirationDate = @"GuardianDayPassExpirationDate";
    public const string kGuardianPETokenExpirationDate = @"kGuardianPETokenExpirationDate";
    public const string kGuardianPETConnectAPIEnv = @"kGuardianPETConnectAPIEnv";

    public const string kGuardianSubscriptionProductIds = @"kGuardianSubscriptionProductIds";

    // Registry Key Names for each Guardian User on Windows
    public const string kKeychainStr_EapUsername = @"eap-username";
    public const string kKeychainStr_EapPassword = @"eap-password";
    public const string kKeychainStr_AuthToken = @"auth-token";
    public const string kKeychainStr_APIAuthToken = @"api-auth-token";
    public const string kKeychainStr_SubscriberCredential = @"subscriber-credential";
    public const string kKeychainStr_PEToken = @"pe-token";

    public const string kGuardianCredentialsList = @"GuardianCredentialsList";
    public const string kKeychainStr_PEToken_Object = @"pe-token-object";
    public const string kKeychainStr_PEToken_Itself = @"pe-token-tokenitself";

    public const string kGRDServicePipeName = @"GuardianFirewallService";
    public const string kPreferredRegion = @"preferred_region";

    public static readonly List<string> GuardianKeychainItemsKeys = new List<string>
    {
//        kKeychainStr_EapUsername,
//        kKeychainStr_EapPassword,
//        kKeychainStr_AuthToken,
//        kKeychainStr_APIAuthToken,
        kKeychainStr_SubscriberCredential,
//        kKeychainStr_PEToken,
        kGuardianCredentialsList,
        kKeychainStr_PEToken_Object,
        kKeychainStr_PEToken_Itself

    };

    public const string kGuardianConnectSubscriberSecret = @"kGuardianConnectSubscriberSecret";

    // Used to hard to code IAP receipts and create Subscriber Credentials
    public const string kGuardianEncodedAppStoreReceipt = @"kGuardianEncodedAppStoreReceipt";

    //moved to make framework friendly
    public const string kIsPremiumUser = @"userHasPaidSubscription";
    public const string kSubscriptionPlanTypeStr = @"subscriptionPlanType";

    public enum GRDPlanDetailType
    {
        GRDPlanDetailTypeFree = 0,
        GRDPlanDetailTypeEssentials,
        GRDPlanDetailTypeProfessional
    }

    public const string kGRDServerUpdatedNotification = @"GRDServerUpdatedNotification";
    public const string kGRDLocationUpdatedNotification = @"GRDLocationUpdatedNotification";
    public const string kGRDSubscriptionUpdatedNotification = @"GRDSubscriptionUpdatedNotification";

    public const string kGRDTrialExpirationInterval = @"GRDTrialExpirationInterval";
    public const string kGRDFreeTrialExpired = @"GRDFreeTrialExpired";

    public const string kGRDDeviceFilterConfigBlocklist = @"GRDDeviceFilterConfigBlocklist";

    // Note from CJ 2023-03-23
    // These are now deprecated, but we may want to use them in the future. They can be deleted at any time
    public const string kGRDDeviceFilterConfigBlockNone = @"kGRDDeviceFilterConfigBlockNone";
    public const string kGRDDeviceFilterConfigBlockAds = @"kGRDDeviceFilterConfigBlockAds";
    public const string kGRDDeviceFilterConfigBlockPhishing = @"kGRDDeviceFilterConfigBlockPhishing";
    public const string kGRDDeviceFilterConfigUsePredictiveBlocking = @"kGRDDeviceFilterConfigUsePredictiveBlocking";

    public const int FortyEightHoursInSeconds = 172800;

    // Error Text Here?? (TJE TODO)
    public const string kPETOKENNOTSET = "PE TOKEN IS NOT SET";

    // Other useful definitions and constants
    public const string VPNEVT_NAME_CLIENTSIDE = "Global\\GRDRASCONNCLIENTSIGNAL";
    public const string VPNEVT_NAME_SVRSIDE = "Global\\GRDRASCONNSERVICESIGNAL";
    public static ILogger Logger { get; set; } = null!;

    private static Dictionary<LoggingLevels, Serilog.LoggerConfiguration> LevelBasedLoggerConfigurations = new Dictionary<LoggingLevels, Serilog.LoggerConfiguration>();

    public enum LoggingLevels { Debug, Verbose, Information, Warning, Error }
    public static ILogger GetLogger() { return Logger; }
    public const LoggingLevels DefaultMinimumLogLevel = LoggingLevels.Information;
    public static LoggingLevels CurrentMinimumLogLevel { get; set; } = DefaultMinimumLogLevel;

    public static string LogFilePath { get; set; } = "INVALID:";
    public static bool LogFilterOn { get; set; }

    public static DateTime TimeFromUnixTimestamp(int unixTimestamp)
    {
        DateTime unixYear0 = new DateTime(1970, 1, 1);
        long unixTimeStampInTicks = unixTimestamp * TimeSpan.TicksPerSecond;
        DateTime dtUnix = new DateTime(unixYear0.Ticks + unixTimeStampInTicks);
        return dtUnix;
    }

    public static long UnixTimestampFromDateTime(DateTime date)
    {
        long unixTimestamp = date.Ticks - new DateTime(1970, 1, 1).Ticks;
        unixTimestamp /= TimeSpan.TicksPerSecond;
        return unixTimestamp;
    }

    public static string EncodeTo64(string toEncode)
    {
        byte[] toEncodeAsBytes = System.Text.Encoding.ASCII.GetBytes(toEncode);
        string returnValue = Convert.ToBase64String(toEncodeAsBytes);

        return returnValue;
    }

    public static string DecodeFrom64(string encodedData)
    {
        byte[] encodedDataAsBytes = Convert.FromBase64String(encodedData);
        string returnValue = System.Text.Encoding.ASCII.GetString(encodedDataAsBytes);

        return returnValue;
    }

    public static DateOnly DateOnlyFromAppleDTI1970(long dateWithTimeIntervalSince1970)
    {
        DateTime convDateTime = DateTime.UnixEpoch.AddSeconds(dateWithTimeIntervalSince1970);
        DateOnly convDateOnly = DateOnly.FromDateTime(convDateTime);
        return convDateOnly;
    }

    public static DateTime DateTimeFromAppleDTI1970(long dateWithTimeIntervalSince1970)
    {
        DateTime convDateTime = DateTime.UnixEpoch.AddSeconds(dateWithTimeIntervalSince1970);
        return convDateTime;
    }


    public static void GRDLog(string logMessage)
    {
        string msg = $"[{DateTime.Now.ToShortTimeString()}]-U {logMessage}";
        Debug.WriteLine(msg);
        Logger.Information(logMessage);
    }

    public static List<string> GetLastLogLines(int maxToReturn = 20)
    {
        List<string> logLines = new List<string>();

        try
        {
            var fs = new FileStream(LogFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using (var sr = new StreamReader(fs))
            {
                var content = sr.ReadToEnd();
                logLines = content.Split('\n').TakeLast(maxToReturn).ToList();
            }
        }
        catch (Exception e)
        {
            Debug.WriteLine($"Unable to open logfile {LogFilePath} for Tech Support Email: {e.Message}");
        }

        return logLines;
    }

    public static void SetUpLogging()
    {
        var loggingRegSetting = RegistrySettings.RetrieveGuardianUserSettings(kWhetherLoggingCurrentlyOn);
        if (string.IsNullOrEmpty(loggingRegSetting))
        {
            loggingRegSetting = "true";
            RegistrySettings.UpdateGuardianUserSettings(kWhetherLoggingCurrentlyOn, loggingRegSetting);
        }
        LogFilterOn = loggingRegSetting != "true";

        if (!LogFilterOn)
        {
            var dlc = new LoggerConfiguration().MinimumLevel.Debug().MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .Enrich.WithProcessId()
                .Enrich.WithThreadId()
                .Enrich.WithThreadName()
                .Enrich.WithCaller(false, 0)
                .WriteTo.Conditional(evt => !LogFilterOn, wt => wt.File(LogFilePath, shared: true,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss:ffffff-K} {ProcessId}.{ThreadId}[{ThreadName}]):{Caller} [{Level:u3}] {Message}{NewLine}{Exception}"));
            LevelBasedLoggerConfigurations.Add(LoggingLevels.Debug, dlc);
                
            var vlc = new LoggerConfiguration().MinimumLevel.Verbose().MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .Enrich.WithProcessId().Enrich.WithThreadId()
                .WriteTo.Conditional(evt => !LogFilterOn, wt => wt.File(LogFilePath, shared: true,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss:ffffff-K} {ProcessId}.{ThreadId}) [{Level:u3}] {Message}{NewLine}{Exception}"));
            LevelBasedLoggerConfigurations.Add(LoggingLevels.Verbose, vlc);

            var ilc = new LoggerConfiguration().MinimumLevel.Information().MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .Enrich.WithProcessId()
                .Enrich.WithThreadId()
                .Enrich.WithThreadName()
                .WriteTo.Conditional(evt => !LogFilterOn, wt => wt.File(LogFilePath, shared: true,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss:ffffff-K} {ProcessId}.{ThreadId}) [{Level:u3}] {Message}{NewLine}{Exception}",
                    buffered:false, flushToDiskInterval:TimeSpan.FromMilliseconds(500)));
            LevelBasedLoggerConfigurations.Add(LoggingLevels.Information, ilc);

            SetMinimumLogLevelToCurrentLevel();
        }

        Logger = Log.Logger;
    }

    public static void SetMinimumLogLevelToCurrentLevel()
    {
        Log.Logger = LevelBasedLoggerConfigurations[CurrentMinimumLogLevel].CreateLogger();
        Log.Logger.Information($"Serilog logger set up. Current Minimum Log Level starting at '{CurrentMinimumLogLevel}'");
    }
}