#pragma once
#include <windows.h>
#include <string>
#include "..\ConnectionRoutines.h"
#include "..\Utility.h"

namespace NativeRoutines
{
    class VpnDnsHandler
    {
    public:

        void StartVPNConnectionChangeMonitoring();
    //protected:
        // base::win::ObjectWatcher::Delegate overrides:
        void OnObjectSignaled(HANDLE object);

        Utility::CheckConnectionResult GetVpnEntryStatus();
        bool CloseEngineSession();

        bool SetFilters(String^ connection_name);
        bool RemoveFilters(String^ connection_name);
        bool IsActive() const;
        bool IsExitTimerRunningForTesting();
        void SetConnectionResultForTesting(Utility::CheckConnectionResult result);
        void SetCloseEngineResultForTesting(bool value);
        void SetPlatformFiltersResultForTesting(bool value);
        void SetWaitingIntervalBeforeExitForTesting(int value);
        void UpdateFiltersState();
        void ScheduleExit();

    private:
        bool SetupPlatformFilters(String^ name);
        bool RemovePlatformFilters(String^ name);
        int GetWaitingIntervalBeforeExit();
        void CloseWatchers();
        void DisconnectVPN();
        void Exit();
        virtual void SubscribeForRasNotifications(HANDLE event_handle);

        static HANDLE engine_;
        HANDLE event_handle_for_vpn_ = nullptr;
        const int kWaitingIntervalBeforeExitSec = 10;
        //raw_ptr<BraveVpnDnsDelegate> delegate_;
        //base::win::ObjectWatcher connected_disconnected_event_watcher_;
        //base::RepeatingTimer periodic_timer_;
        //base::OneShotTimer exit_timer_;
        //base::WeakPtrFactory<VpnDnsHandler> weak_factory_{this};
    };
}