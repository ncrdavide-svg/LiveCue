using System.Runtime.Versioning;

// This library uses Win32 P/Invoke (display enumeration) and PerformanceCounter (CPU/GPU
// monitoring) throughout, both Windows-only. Declaring it here instead of per call site.
[assembly: SupportedOSPlatform("windows")]
