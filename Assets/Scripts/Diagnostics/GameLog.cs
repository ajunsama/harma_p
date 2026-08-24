using System.Diagnostics;
using UnityEngine;

/// <summary>
/// High-volume runtime diagnostics. Calls and argument evaluation are omitted
/// unless HARMA_VERBOSE_LOGS is defined in Player Settings.
/// </summary>
internal static class GameLog
{
    [Conditional("HARMA_VERBOSE_LOGS")]
    public static void Verbose(object message, Object context = null)
    {
        if (context != null)
            UnityEngine.Debug.Log(message, context);
        else
            UnityEngine.Debug.Log(message);
    }
}
