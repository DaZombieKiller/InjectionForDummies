using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

static unsafe partial class Program
{
    // The injector takes three arguments:
    //   args[0] = The process ID to inject into.
    //   args[1] = The file path of the DLL to inject.
    //   args[2] = The RVA of the payload function to execute in hexadecimal.
    static void Main(string[] args)
    {
        // We need to open a handle to the target process, and then allocate memory for the library path.
        // A process cannot access another process' memory directly, so we can't just pass along a string
        // from the injector to the target. It must be allocated and copied into the target's memory.
        using var process = Process.GetProcessById(int.Parse(args[0], CultureInfo.InvariantCulture));
        var libraryPath   = AllocateRemoteString(process.Handle, Path.GetFullPath(args[1]));
        var payloadRva    = uint.Parse(args[2], NumberStyles.HexNumber, CultureInfo.InvariantCulture);

        // In order to call a function in a remote process, we need to know two things:
        //
        //   1. The load address in the process of the library our function is in.
        //   2. The relative virtual address (RVA) of the function we want to call.
        //
        // If you think of a DLL/EXE in memory as a byte[] array representing code, then
        // you can treat the RVA like an index into that array. That index refers to the
        // location of the first byte of the function you want to call.
        //
        // Once we know these, we can calculate the address of the function by summing
        // the two values. We can then pass this to CreateRemoteThread for execution.
        //
        // There is an important point to mention with regard to how this sample
        // calculates the address of the function. Libraries can export functions as
        // "forwarders", which causes the export to be an alias for an export in
        // another library.
        //
        // If LoadLibraryW was forwarded to a DLL other than KERNEL32.DLL, the approach
        // shown below would not work. Solving this problem requires parsing the DLL
        // manually to determine which DLL the function is actually exported in.
        //
        // This issue is not relevant for LoadLibraryW, but it is worth keeping in mind
        // if you encounter crashes attempting to call functions that are forwarded to
        // other libraries.
        //
        var kernel32    = GetRemoteModuleHandle(process.Id, "kernel32.dll");
        var loadLibrary = kernel32 + GetExportRva("kernel32.dll", "LoadLibraryW");

        // The signature of LoadLibraryA/LoadLibraryW is (mostly*) compatible with that of
        // THREAD_START_ROUTINE, meaning we can use CreateRemoteThread to call it inside
        // our target process and inject our library.
        //
        // * THREAD_START_ROUTINE is defined as having a 32-bit return value, while LoadLibrary
        //   returns the load address of the library as a pointer.
        //
        //   This means that if you are using a 32-bit injector with a 32-bit target process,
        //   you could actually use the GetExitCodeThread function with the thread handle to
        //   get the load address instead of using GetRemoteModuleHandle.
        //
        //   The downside of this is that it does not work for 64-bit, due to the load address
        //   being 64-bit while the return value is only 32-bit.
        //
        var thread = CreateRemoteThread(process.Handle, null, 0, loadLibrary, libraryPath, 0, null);

        // We now wait for the thread to finish executing, then we can
        // free the memory that we allocated for the library file path.
        WaitForSingleObject(thread, INFINITE);
        VirtualFreeEx(process.Handle, libraryPath, 0, MEM_RELEASE);

        // As mentioned above, when the injector and target are 32-bit, we can
        // actually get the load address directly via the thread's exit code:
        //
        // if (!Environment.Is64BitProcess)
        // {
        //     uint exitCode;
        //     GetExitCodeThread(thread, &exitCode);
        //     CloseHandle(thread);
        //     thread = CreateRemoteThread(process.Handle, null, 0, (byte*)exitCode + payloadRva, null, 0, null);
        //     CloseHandle(thread);
        //     return;
        // }

        // Once the thread handle is no longer in use, it should be closed.
        CloseHandle(thread);

        // Now that our payload is loaded, we can execute it by calculating the
        // address of our function and passing that along to CreateRemoteThread.
        var loadAddress = GetRemoteModuleHandle(process.Id, Path.GetFileName(args[1]));
        var payload     = loadAddress + payloadRva;
        thread          = CreateRemoteThread(process.Handle, null, 0, payload, null, 0, null);
        CloseHandle(thread);
    }

    // This function serves the same purpose as the Windows GetModuleHandle API, but can
    // be used with processes other than the calling process. This is necessary in order
    // to determine the load address of libraries loaded into other processes so that we
    // can combine them with function RVAs.
    //
    // This is a necessary step, because the load address of a library is different for
    // each process. We cannot simply load a library into our injector and use that load
    // address for our calls to CreateRemoteThread.
    //
    static byte* GetRemoteModuleHandle(int processId, string moduleName)
    {
        var entry    = new MODULEENTRY32W { dwSize = (uint)sizeof(MODULEENTRY32W) };
        var snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPMODULE, (uint)processId);

        if (Module32FirstW(snapshot, &entry))
        {
            do
            {
                var name = MemoryMarshal.CreateReadOnlySpanFromNullTerminated(entry.szModule);

                if (name.Equals(moduleName, StringComparison.OrdinalIgnoreCase))
                {
                    CloseHandle(snapshot);
                    return entry.modBaseAddr;
                }

            } while (Module32NextW(snapshot, &entry));
        }

        CloseHandle(snapshot);
        return null;
    }

    // This function calculates the RVA of an export by:
    //
    //   1. Loading the library containing the export.
    //   2. Retrieving the virtual address of the export.
    //   3. Subtracting the library's load address from the export address.
    //
    // If the export was not forwarded, the result will be a valid RVA.
    //
    // When an export is forwarded, the load address will not be that of
    // the library that actually contains the function, resulting in an
    // invalid RVA.
    //
    static uint GetExportRva(string module, string name)
    {
        var handle = NativeLibrary.Load(module);
        var export = NativeLibrary.GetExport(handle, name);
        var rva    = (uint)(export - handle);
        NativeLibrary.Free(handle);
        return rva;
    }

    // This is a simple helper function for copying a string into a remote process.
    // It can later be freed using VirtualFreeEx(target, pointer, 0, MEM_RELEASE).
    static void* AllocateRemoteString(nint target, string value)
    {
        var length = sizeof(char) * ((uint)value.Length + 1);
        var memory = VirtualAllocEx(target, null, length, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);

        fixed (char* pValue = value)
        {
            nuint written;
            WriteProcessMemory(target, memory, pValue, length, &written);
        }

        return memory;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetExitCodeThread(void* hThread, uint* lpExitCode);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(void* hObject);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial void* CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool Module32FirstW(void* hSnapshot, MODULEENTRY32W* lpme);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool Module32NextW(void* hSnapshot, MODULEENTRY32W* lpme);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WriteProcessMemory(nint hProcess, void* lpBaseAddress, void* lpBuffer, nuint nSize, nuint* lpNumberOfBytesWritten);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial void* VirtualAllocEx(nint hProcess, void* lpAddress, nuint dwSize, uint flAllocationType, uint flProtect);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool VirtualFreeEx(nint hProcess, void* lpAddress, nuint dwSize, uint dwFreeType);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial uint WaitForSingleObject(void* hHandle, uint dwMilliseconds);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial void* CreateRemoteThread(nint hProcess, void* lpThreadAttributes, nuint dwStackSize, void* lpStartAddress, void* lpParameter, uint dwCreationFlags, uint* lpThreadId);

    const uint MEM_COMMIT = 0x1000;

    const uint MEM_RESERVE = 0x2000;

    const uint PAGE_READWRITE = 0x4;

    const uint MEM_RELEASE = 0x8000;

    const uint INFINITE = 0xFFFFFFFF;

    const uint TH32CS_SNAPMODULE = 0x8;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct MODULEENTRY32W
    {
        public uint dwSize;
        public uint th32ModuleID;
        public uint th32ProcessID;
        public uint GlblcntUsage;
        public uint ProccntUsage;
        public byte* modBaseAddr;
        public uint modBaseSize;
        public void* hModule;
        public fixed char szModule[256];
        public fixed char szExePath[260];
    }
}
