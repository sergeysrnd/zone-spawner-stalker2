#define WIN32_LEAN_AND_MEAN
#include <windows.h>

static HMODULE instance;
BOOL WINAPI DllMain(HINSTANCE module, DWORD reason, LPVOID reserved) {
    if (reason == DLL_PROCESS_ATTACH) { instance = module; DisableThreadLibraryCalls(module); }
    return TRUE;
}

/* Direct Win32 launch: no command shell, console window, timers or game-object access. */
__declspec(dllexport) DWORD launch_for_pid(DWORD game_pid) {
    WCHAR directory[MAX_PATH], executable[MAX_PATH], command[MAX_PATH + 80], report[MAX_PATH];
    STARTUPINFOW startup = {0};
    PROCESS_INFORMATION process = {0};
    DWORD length = GetModuleFileNameW(instance, directory, MAX_PATH);
    DWORD error, menu_pid = 0, written;
    int i;
    BOOL ok;
    CHAR message[128];
    HANDLE file;
    if (!length || length >= MAX_PATH) return ERROR_FILENAME_EXCED_RANGE;
    for (i = (int)length - 1; i >= 0 && directory[i] != L'\\'; --i) {}
    if (i < 0 || i + 32 >= MAX_PATH) return ERROR_FILENAME_EXCED_RANGE;
    directory[i + 1] = 0;
    lstrcpyW(executable, directory); lstrcatW(executable, L"ZoneSpawner.exe");
    lstrcpyW(report, directory); lstrcatW(report, L"bridge\\launcher-status.txt");
    wsprintfW(command, L"\"%s\" --start-hidden --game-pid %lu", executable, game_pid);
    startup.cb = sizeof(startup);
    startup.dwFlags = STARTF_USESHOWWINDOW;
    startup.wShowWindow = SW_HIDE;
    ok = CreateProcessW(executable, command, NULL, NULL, FALSE, CREATE_NO_WINDOW, NULL, directory, &startup, &process);
    error = ok ? ERROR_SUCCESS : GetLastError();
    if (ok) { menu_pid = process.dwProcessId; CloseHandle(process.hThread); CloseHandle(process.hProcess); }
    wsprintfA(message, "%s\t%lu\t%lu\t%lu\n", ok ? "started" : "error", game_pid, menu_pid, error);
    file = CreateFileW(report, GENERIC_WRITE, FILE_SHARE_READ, NULL, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
    if (file != INVALID_HANDLE_VALUE) { WriteFile(file, message, lstrlenA(message), &written, NULL); CloseHandle(file); }
    return error;
}

/* Lua C function ABI; zero results, no dependency on UE4SS's Lua exports. */
__declspec(dllexport) int launch_menu(void* lua_state) {
    launch_for_pid(GetCurrentProcessId());
    return 0;
}
