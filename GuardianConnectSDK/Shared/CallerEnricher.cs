using System.Diagnostics;
using Serilog.Core;
using Serilog.Events;

namespace GuardianConnect.Shared;

// Adds a {Caller} property naming the first method on the stack outside the
// logging pipeline, as "Namespace.Type.Method". Uses DiagnosticMethodInfo,
// which stays available under trimming and NativeAOT (unlike
// StackFrame.GetMethod). Async methods report the original method name rather
// than the compiler-generated state machine's MoveNext.
internal sealed class CallerEnricher : ILogEventEnricher
{
    private static readonly string[] SkippedPrefixes =
    [
        "Serilog.",
        "Microsoft.Extensions.Logging.",
        "System.Runtime.CompilerServices.",
        "System.Threading."
    ];

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var frames = new StackTrace(1, false).GetFrames();
        foreach (var frame in frames)
        {
            var method = DiagnosticMethodInfo.Create(frame);
            if (method is null) continue;

            var typeName = method.DeclaringTypeName;
            if (string.IsNullOrEmpty(typeName) || typeName == typeof(CallerEnricher).FullName ||
                SkippedPrefixes.Any(p => typeName.StartsWith(p, StringComparison.Ordinal)))
                continue;

            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("Caller", Format(typeName, method.Name)));
            return;
        }
    }

    // "Ns.Type+<Method>d__5" / "MoveNext" -> "Ns.Type.Method"
    private static string Format(string typeName, string methodName)
    {
        var open = typeName.IndexOf("+<", StringComparison.Ordinal);
        if (open >= 0)
        {
            var close = typeName.LastIndexOf('>');
            if (close > open + 2)
                return $"{typeName[..open]}.{typeName[(open + 2)..close]}";
        }

        return $"{typeName.Replace('+', '.')}.{methodName}";
    }
}
