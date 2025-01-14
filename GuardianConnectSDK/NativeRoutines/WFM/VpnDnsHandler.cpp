#include "pch.h"
#include "VpnDnsHandler.h"
#include "VpnUtils.h"
#include "../PrintRoutines.h"

namespace  NativeRoutines
{
  ref class PrintRoutines;
  //ref class VpnUtils;
  using NativeRoutines::PrintRoutines;
  
  //class VpnDnsHandler
  //{
    constexpr int kCheckConnectionIntervalInSeconds = 3;
    bool VpnDnsHandler::SetupPlatformFilters(HANDLE engine_handle,
                                             String^ name) {
      return VpnUtils::AddWpmFilters(engine_handle, name);
    }
    bool VpnDnsHandler::CloseEngineSession() {
      return VpnUtils::CloseWpmSession(engine_);
    }


    bool VpnDnsHandler::SetFilters(String^ connection_name) {
      PrintRoutines::Output(Grd::FormatAString("{0}: {1}", gcnew array<Object^> { *__func__, connection_name }));
      if (IsActive()) {
        PrintRoutines::Output(Grd::FormatAString("Filters activated for: {0}", gcnew array<Object^> {connection_name }));
        return true;
      }

      engine_ = VpnUtils::OpenWpmSession();
      if (!engine_) {
        PrintRoutines::Output("Failed to open engine session");
        return false;
      }

      if (!SetupPlatformFilters(engine_, connection_name)) {
        if (!RemoveFilters(connection_name)) {
          PrintRoutines::Output("Failed to remove DNS filters");
        }
        return false;
      }
      return true;
    }

    bool VpnDnsHandler::IsActive() const {
      return engine_ != nullptr;
    }

    bool VpnDnsHandler::RemoveFilters(String^ connection_name) {
      PrintRoutines::Output(Grd::FormatAString("{0}: {1}", gcnew array<Object^>
        {
          *__func__,
          connection_name
        }
        ));
      if (!IsActive()) {
        PrintRoutines::Output("No active filters");
        return true;
      }
      bool success = CloseEngineSession();
      if (success) {
        engine_ = nullptr;
      }
      return success;
    }

    Utility::CheckConnectionResult VpnDnsHandler::GetVpnEntryStatus() {
      PrintRoutines::Output(Grd::FormatAString("{0}", gcnew array<Object^> {*__func__} ));
      return ConnectionRoutines::CheckConnection(ConnectionRoutines::ActiveConnectionEntryName->ToString());
    }

    void VpnDnsHandler::DisconnectVPN() {
        auto result = false;
      result = ConnectionRoutines::DisconnectEntry(ConnectionRoutines::ActiveConnectionEntryName->ToString());
      if (!result) {
        PrintRoutines::Output(Grd::FormatAString("Failed to disconnect entry:{0}", gcnew array<Object^> {GetLastError() }));
      }
    }

    void VpnDnsHandler::UpdateFiltersState() {
      PrintRoutines::Output(Grd::FormatAString("{0}", gcnew array<Object^> {*__func__ }));
      String^ entryName = gcnew String(ConnectionRoutines::ActiveConnectionEntryName);
      switch (GetVpnEntryStatus()) {
      case Utility::CheckConnectionResult::CONNECTED:
        PrintRoutines::Output("GuardianVPN connected, set filters");
        if (IsActive()) {
          PrintRoutines::Output("Filters are already installed");
          return;
        }
        if (!SetFilters(entryName))
        {
          PrintRoutines::Output("Failed to set DNS filters");
          DisconnectVPN(); // TJE ?? to CJ/Will - Disconnect if can't add filters? (Baby/Bathwater)
          ScheduleExit();
          return;
        }
        VpnUtils::SetFiltersInstalledFlag();
        break;
      case Utility::CheckConnectionResult::DISCONNECTED:
        PrintRoutines::Output("GuardianVPN Disconnected, remove filters");
        if (!RemoveFilters(entryName))
        {
          PrintRoutines::Output("Failed to remove DNS filters");
          Exit();
          break;
        }
        // Reset service launch counter if dns filters successfully removed.
        VpnUtils::ResetFiltersInstalledFlag();
        ScheduleExit();
        break;
      default:
        PrintRoutines::Output(Grd::FormatAString("GuardianVPN is connecting, try later after {0} seconds",
                 gcnew array<Object^> {kCheckConnectionIntervalInSeconds} ));
        break;
      }
    }

    void VpnDnsHandler::CloseWatchers() {
      if (event_handle_for_vpn_) {
        CloseHandle(event_handle_for_vpn_);
        event_handle_for_vpn_ = nullptr;
      }
//      periodic_timer_.Stop();
    }

    int VpnDnsHandler::GetWaitingIntervalBeforeExit() {
      return kWaitingIntervalBeforeExitSec;
    }

    void VpnDnsHandler::ScheduleExit() {
#if WHATTODO
      if (exit_timer_.IsRunning()) {
        return;
      }
      exit_timer_.Start(
          FROM_HERE, base::Seconds(GetWaitingIntervalBeforeExit()),
          base::BindOnce(&VpnDnsHandler::Exit, weak_factory_.GetWeakPtr()));
#endif
    }

  // TJE - ASK CJ/WILL - Do we exit if VPN active? 'We' are the GuardianWinService - NOT the UI.
    void VpnDnsHandler::Exit() {
      if (GetVpnEntryStatus() == Utility::CheckConnectionResult::CONNECTED) {
        PrintRoutines::Output(Grd::FormatAString("{0}: vpn is active, do not exit", gcnew array<Object^> { *__func__ }));
        return;
      }
      CloseWatchers();
      // TJE?? delegate_->SignalExit();
    }

    void VpnDnsHandler::OnObjectSignaled(HANDLE object) {
      PrintRoutines::Output(Grd::FormatAString("{0}", gcnew array<Object^> {*__func__} ));
      // We receive events from all connections in the system and filter here
      // only expected brave vpn event.
      if (object != event_handle_for_vpn_) {
        return;
      }
#if NEEDED
      if (exit_timer_.IsRunning()) {
        exit_timer_.Stop();
      }
#endif
      UpdateFiltersState();
    }

    void VpnDnsHandler::SubscribeForRasNotifications(HANDLE event_handle) {
      PrintRoutines::Output(Grd::FormatAString("{0}", gcnew array<Object^> {*__func__} ));
#if NOTYET
      if (!SubscribeRasConnectionNotification(event_handle)) {
        PrintRoutines::Output(FormatAString("{0} "Failed to subscripbe for vpn notifications";
      }
    }

    void VpnDnsHandler::StartVPNConnectionChangeMonitoring() {
      DCHECK(!event_handle_for_vpn_);
      DCHECK(!IsActive());

      event_handle_for_vpn_ = CreateEvent(NULL, false, false, NULL);
      SubscribeForRasNotifications(event_handle_for_vpn_);

      connected_disconnected_event_watcher_.StartWatchingMultipleTimes(
          event_handle_for_vpn_, this);

      periodic_timer_.Start(FROM_HERE,
                            base::Seconds(kCheckConnectionIntervalInSeconds),
                            base::BindRepeating(&VpnDnsHandler::UpdateFiltersState,
                                                weak_factory_.GetWeakPtr()));
      UpdateFiltersState();
#endif
    }

#if NEEDED
    bool VpnDnsHandler::IsExitTimerRunningForTesting() {
      return exit_timer_.IsRunning();
    }
#endif
//  };
}
    
