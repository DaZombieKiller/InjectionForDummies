using System.Runtime.InteropServices;

public static unsafe partial class Payload
{
    // This is the function that we will execute in our target process.
    // Because we will use CreateRemoteThread to execute it, there are
    // some requirements that it must fulfill:
    //
    //   * It must use the stdcall calling convention
    //   * It must return a uint and take a pointer as input
    //
    // On Windows, stdcall is the default calling convention used for methods
    // marked with [UnmanagedCallersOnly], so we do not need to explicitly
    // specify it for this sample.
    //
    // We also need to export it by assigning the EntryPoint property of the
    // [UnmanagedCallersOnly] attribute. This will export the function when
    // the library is published with NativeAOT.
    //
    [UnmanagedCallersOnly(EntryPoint = "ExecutePayload")]
    public static uint Execute(void* _)
    {
        // You can do essentially anything in this function,
        // but for simplicity we'll just show a message box.
        MessageBoxW(null, "Hello, world!", "Payload", 0);
        return 0;
    }

    [LibraryImport("user32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBoxW(void* hWnd, string lpText, string lpCaption, uint uType);
}
