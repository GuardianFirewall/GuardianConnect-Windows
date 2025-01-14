#pragma once
#include "NativeRoutines.h"

namespace NativeRoutines
{
	public ref class CreateRoutines
	{
	public:
		int CreateOrUpdateEntry(LPCTSTR phonebookPath, LPCTSTR entry_name, LPCTSTR hostname, LPCTSTR username, LPCTSTR password);

		int CreateTheCall(System::String^ phonebookPath,
			System::String^ entryName,
			System::String^ hostName,
			System::String^ userName,
			System::String^ password);

		void DeleteExistingGuardianRasEntries();

	};
}