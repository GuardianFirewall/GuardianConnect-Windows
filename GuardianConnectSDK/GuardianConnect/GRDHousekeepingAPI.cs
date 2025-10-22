using System.Diagnostics;
using System.Text.Json;
using GuardianConnect.API.Model;
using GuardianConnect.Credentials;
using GuardianConnect.Helpers;
using GuardianConnect.Shared;
using GuardianConnect.Shared.Extensions;
using System.Text.Json.Serialization;
using Serilog;

namespace GuardianConnect;

public class GRDHousekeepingAPI
{
#if false
    public class PETRequest
    {
        public string validationMethod = "pe-token";
        public string peToken = string.Empty;
    }

    public class Credentials
    {
        [JsonPropertyName("email")]
        public string Email = string.Empty;
        [JsonPropertyName("password")]
        public string Password = string.Empty;

        public Credentials(string userEmail, string userPassword)
        {
            Email = userEmail;
            Password = userPassword;
        }
    }
#endif

    public static GrdUserLoginResponse LoginResponse { get; set; } = new GrdUserLoginResponse();
    public static GRDSubscriberCredential? LiveGrdCredential { get; set; }

    /// endpoint: /api/v1/users/info-for-pe-token
    /// @param token password equivalent token for which to request information for
    /// @param completion completion block returning NSDictionary with information for the requested token, an error message and a bool indicating success of the request
    public async Task<ErrorResponse> RequestPETokenInformationForToken(string peToken)
    {
        ErrorResponse errorResponse = new ErrorResponse();
        
        // Validation Method PEToken...
        if (string.IsNullOrEmpty(peToken))
        {
            Debug.WriteLine("No pe token provided");
            Log.Error("RequestPETokenInformationForToken: PEToken is null or empty");

            return errorResponse
                .SetException(new ArgumentNullException("NO PE Token Provided"))
                .SetErrorMessage("No pe token provided");
        }

        Uri uri = new Uri($"https://{Common.kConnectAPIHostname}/api/v1/users/info-for-pe-token");
        try
        {
            var pet = new PeTokenRequest("pe-token", peToken);
            HttpContent content = new StringContent(JsonSerializer.Serialize(pet, GRDPETokenJsonContext.Default.GRDPEToken));
            content.Headers.Remove("Content-Type");
            content.Headers.Add("Content-Type", "application/json; charset=utf-8");
            HttpResponseMessage response = await HttpUtils.Client.PostAsync(uri, content);
            if (response.IsSuccessStatusCode)
            {
                string result = await response.Content.ReadAsStringAsync();
                PeTokenResponse peTokenResponse = JsonSerializer.Deserialize<PeTokenResponse>(result, PeTokenResponseJsonContext.Default.PeTokenResponse) ?? new PeTokenResponse();
                errorResponse.SetData(peTokenResponse).SetResponse(response);
            }
            else
            {
                // ... check response values...
                int statusCode = (int)response.StatusCode;
                if (statusCode == 500)
                {
                    Debug.WriteLine(@"Housekeeping failed to return subscriber credential");
                    //if (completion) completion(nil, NO, @"Internal server error - couldn't create subscriber credential");
                    errorResponse.SetResponse(response).SetErrorMessage("500 - Internal Server Error");
                }
                if (statusCode == 400)
                {
                    Debug.WriteLine(@"Failed to create subscriber credential. Faulty input values");
                    //if (completion) completion(nil, NO, @"Failed to create subscriber credential. Faulty input values");
                    errorResponse.SetResponse(response).SetErrorMessage("400 - Failed to create subscriber credential. Faulty input values");
                }
                if (statusCode == 401)
                {
                    Debug.WriteLine(@"No subscription present");
                    //if (completion) completion(nil, NO, @"No subscription present");
                    errorResponse.SetResponse(response).SetErrorMessage("401 - No subscription present");
                }
                if (statusCode == 410)
                {
                    System.Diagnostics.Debug.WriteLine(@"Subscription expired");
                    // Not sending an error message back so that we're not showing a useless error to the user
                    // The app should transition to free/unpaid if required
                    //if (completion) completion(nil, NO, nil);
                    errorResponse.SetResponse(response).SetErrorMessage("410 - Subscription expired");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine(@"\tERROR {0}", ex.Message);
            Log.Error(ex, $"Exception thrown - RequestPETokenInformationForToken: {ex.Message}");
            errorResponse.SetException(ex);
        }
        //

        return errorResponse;
    }

    /// endpoint: /api/v1.2/subscriber-credential/create
    /// Used to obtain a signed JWT from housekeeping for later authentication with zoe-agent
    /// @param validationMethod set to determine how to authenticate with housekeeping
    /// @param dict NSDictionary only used when the 'validationMethod' is set to 'ValidationMethodCustom'
    /// @param completion completion block returning a signed JWT, indicating request success and a user actionable error message if the request failed
    public async Task<ErrorResponse> CreateSubscriberCredentialForBundleId(string bundleId)
    {
        ErrorResponse errorResponse;
        // CONN#9
        Log.Information("CONN#9");
        // TJE: Called by GRDVPNHelper.GetValidSubscriberCredentialWithCompletion()
        
        // set host to use
        string connectHost = GRDVPNHelper.Instance.PeToken?.ConnectAPIEnv ?? Common.kConnectAPIHostname;
        // Validation Method PEToken...
        string peToken = GRDVPNHelper.Instance.PeToken?.Token;
        if (string.IsNullOrEmpty(peToken))
        {
            Log.Error(@"PEToken Object has empty token. Trying string from keychain...");
            peToken = GRDKeychain.GetPasswordStringForAccount(IGRDKeychain.kKeychainStr_PEToken_Itself);
            if (string.IsNullOrEmpty(peToken))
            {
                Log.Error(@"Failed to retrieve PEToken from keychain");
                return new ErrorResponse
                {
                    Data = null,
                    Message = "Failed to retrieve PEToken from keychain",
                    IsError = true
                };
            }
        }

        PeTokenRequest petRequest = new PeTokenRequest(peToken);

        // TJE - remove comment - taken from AuthenticateUser.cs in UI
        Uri uri = new Uri($"https://{connectHost}/api/v1.2/subscriber-credential/create");
        try
        {
            var pet = new PeTokenRequest("pe-token", petRequest.PeToken);
            
            string serializedPetReq = JsonSerializer.Serialize(pet, PeTokenRequestJsonContext.Default.PeTokenRequest);
            Log.Information($"CreateSubscriberCredentialForBundleId: serializedPetReq = '{serializedPetReq}'");
            HttpContent content = new StringContent(serializedPetReq);
            content.Headers.Remove("Content-Type");
            content.Headers.Add("Content-Type", "application/json; charset=utf-8");
            HttpResponseMessage response = await HttpUtils.Client.PostAsync(uri, content);
            if (response.IsSuccessStatusCode)
            {
                string result = await response.Content.ReadAsStringAsync();
                var jwt = JsonSerializer.Deserialize<GrdSubscriberCredentialJwt>(result, GRDSubScriberCredentialJwtJsonContext.Default.GrdSubscriberCredentialJwt);
                LiveGrdCredential = new GRDSubscriberCredential(jwt!.SubscriberCredential!);
                LiveGrdCredential.Store();
                Log.Information("CreateSubscriberCredentialForBundleId(): JWT obtained.");
            }
            else
            {
                Log.Information("CreateSubscriberCredentialForBundleId(): Error occurred.");
                // ... check response values...
                int statusCode = (int)response.StatusCode;
                if (statusCode == 500)
                {
                    var message = "Housekeeping failed to return subscriber credential";
                    Log.Information(message);
                    errorResponse = new ErrorResponse(message, null, true, response);
                    return errorResponse;

                }
                else if (statusCode == 400)
                {
                    var message = "Failed to create subscriber credential. Faulty input values";
                    Log.Information(message);
                    errorResponse = new ErrorResponse(message, null, true, response);
                    return errorResponse;
                }
                else if (statusCode == 401)
                {
                    var message = "No subscription present";
                    Log.Information(message);
                    errorResponse = new ErrorResponse(message, null, true, response);
                    return errorResponse;

                }
                else if (statusCode == 410)
                {
                    // Not sending an error message back so that we're not showing a useless error to the user
                    // The app should transition to free/unpaid if required
                    var message = "Subscription expired";
                    Log.Information(message);
                    errorResponse = new ErrorResponse(message, null, true, response);
                    return errorResponse;

                }
                else if (statusCode == 402) // Payment required
                {
                    var message = "Payment required";
                    Log.Information(message);
                    errorResponse = new ErrorResponse(message, null, true, response);
                    return errorResponse;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Information($"\tERROR {ex.Message}");
            errorResponse = new ErrorResponse(ex.Message, ex, true, null);
            return errorResponse;
        }
        //
        errorResponse = new ErrorResponse(string.Empty, null, false, string.Empty)
        {
            Data = LiveGrdCredential.Jwt
        };
        return errorResponse;
    }
}