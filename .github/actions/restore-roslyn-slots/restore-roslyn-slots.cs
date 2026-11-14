// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

#:property TargetFramework=net11.0

using System.Diagnostics;

const int restoreTimeoutMinutes = 20;

using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(restoreTimeoutMinutes));

var token = cancellation.Token;

var source = Environment.GetEnvironmentVariable("SRC_FOLDER") ?? "src";

var projects = Lines("PROJECTS");

var slots = Lines("SLOTS");

if (projects.Length == 0 || slots.Length == 0)
{
    Console.Error.WriteLine("Projects and Roslyn slots must both be supplied.");
    return 1;
}

foreach (var slot in slots)
{
    foreach (var project in projects)
    {
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = source };
        foreach (var argument in new[] { "restore", project, "-p:Configuration=Release", $"-p:RoslynVersion={slot}" })
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start dotnet restore.");
        var status = await process.WaitForExitStatusAsync(token);
        if (status is not { ExitCode: 0 })
        {
            return 1;
        }
    }
}

return 0;

static string[] Lines(string name) => (Environment.GetEnvironmentVariable(name) ?? string.Empty)
    .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
