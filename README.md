# Injection for Dummies
An educational sample for teaching DLL injection with .NET

# Structure
This repository contains four C# projects with an example batch script that ties it all together.

* **Injector**: The main project, a heavily commented sample of a basic DLL injector.
* **Payload**: An sample library to inject that can display a "Hello world" message to the user.

The final two projects are primarily intended to simplify `BuildAndRunSample.bat`:
* **RvaDumper**: A utility program for calculating the relative virtual address of a function export.
* **ProcessRunner**: A utility program for launching another program and returning its process ID.

`BuildAndRunSample.bat` exists to illustrate the process of injecting `Payload` into Notepad and calling a function in it using `Injector`.

# License
This is free and unencumbered software released into the public domain.
