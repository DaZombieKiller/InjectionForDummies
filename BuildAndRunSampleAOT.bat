@echo off
set PAYLOAD_PATH=Payload\bin\Release\net7.0\win-x64\publish\Payload.dll
set PAYLOAD_FUNC=ExecutePayload

rem Build the payload library for x64.
echo Building payload library...
dotnet publish Payload -p:PublishAOT=true -r win-x64 -c Release >nul

rem Build and run the RvaDumper to get the RVA of our export.
echo Retrieving relative virtual address of '%PAYLOAD_FUNC%'...
dotnet run --project RvaDumper -c Release -- "%PAYLOAD_PATH%" %PAYLOAD_FUNC% >nul

rem RvaDumper returns the RVA through its exit code, so we can access it from this script.
rem We use %=EXITCODE% instead of %ERRORLEVEL% to retrieve the value in hexadecimal form.
set EXPORT_RVA=%=EXITCODE%

rem Start Notepad and get the process ID. We only use 'ProcessRunner' to simplify this script.
echo Starting Notepad...
dotnet run --project ProcessRunner -c Release -- C:\Windows\System32\notepad.exe >nul

rem Retrieve the Notepad process ID from the exit code.
set NOTEPAD_PID=%ERRORLEVEL%

rem Build and run the injector to run our payload in Notepad.
echo Injecting...
dotnet run --project Injector -c Release -- %NOTEPAD_PID% "%PAYLOAD_PATH%" %EXPORT_RVA%
