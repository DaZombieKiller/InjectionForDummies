// A simple program to launch another program and get its process ID.
//
// This tool only exists to simplify the sample script because there
// is no simple method to retrieve a process ID when starting a program
// from a batch file.
//

using System.Diagnostics;

return Process.Start(args[0]).Id;
