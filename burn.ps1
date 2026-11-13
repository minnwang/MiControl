param([int]$Threads = 8, [int]$Seconds = 300)

$ErrorActionPreference = "Stop"

if (-not ("Burner" -as [type])) {
Add-Type -Language CSharp @"
using System;
using System.Collections.Generic;
using System.Threading;

public static class Burner
{
    public static void Run(int threads, int seconds)
    {
        var end = DateTime.UtcNow.AddSeconds(seconds);
        var list = new List<Thread>();
        for (int i = 0; i < threads; i++)
        {
            var t = new Thread(() =>
            {
                double x = 0.0;
                while (DateTime.UtcNow < end)
                {
                    x += Math.Sqrt(x + 1.0);
                }
                GC.KeepAlive(x);
            });
            t.IsBackground = true;
            t.Start();
            list.Add(t);
        }
        foreach (var t in list) t.Join();
    }
}
"@
}

[Trace]::WriteLine("burner start threads=$Threads seconds=$Seconds")
[Burner]::Run($Threads, $Seconds)
[Trace]::WriteLine("burner done")
