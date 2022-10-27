// A simple program to retrieve the RVA of an exported function.
//
// It takes two arguments:
//   args[0] = The file path of the library.
//   args[1] = The name of the exported function.
//
// The RVA is also returned through the exit code to allow the
// sample batch script to retrieve it and pass it along.
//

using System.Runtime.InteropServices;

var handle = NativeLibrary.Load(args[0]);
var export = NativeLibrary.GetExport(handle, args[1]);
var rva    = (uint)(export - handle);
Console.WriteLine($"{args[1]}: 0x{rva:X}");
return (int)rva;
