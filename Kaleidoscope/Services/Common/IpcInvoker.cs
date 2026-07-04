namespace Kaleidoscope.Services.Common;

/// <summary>
/// Shared primitive for calling other plugins' Dalamud IPC (<c>ICallGateSubscriber</c>) with a
/// uniform "availability guard -> try -> catch -> fallback" shape.
/// </summary>
/// <remarks>
/// The caller computes <c>canInvoke</c> (typically <c>IsAvailable &amp;&amp; subscriber != null</c>)
/// and supplies the actual invocation as a lambda so any call-gate arity is supported. On failure
/// the optional <c>onError</c> handler runs (e.g. to mark the IPC unavailable or log).
/// </remarks>
internal static class IpcInvoker
{
    /// <summary>
    /// Invokes a value-returning call-gate. Returns <paramref name="fallback"/> when the guard is
    /// false or the call throws; on exception <paramref name="onError"/> is invoked first.
    /// </summary>
    public static T Invoke<T>(bool canInvoke, Func<T> invoke, T fallback, Action<Exception>? onError = null)
    {
        if (!canInvoke) return fallback;

        try
        {
            return invoke();
        }
        catch (Exception ex)
        {
            onError?.Invoke(ex);
            return fallback;
        }
    }

    /// <summary>
    /// Invokes a void call-gate (<c>InvokeAction</c>). Returns true on success, or false when the
    /// guard is false or the call throws; on exception <paramref name="onError"/> is invoked first.
    /// </summary>
    public static bool TryInvoke(bool canInvoke, Action invoke, Action<Exception>? onError = null)
    {
        if (!canInvoke) return false;

        try
        {
            invoke();
            return true;
        }
        catch (Exception ex)
        {
            onError?.Invoke(ex);
            return false;
        }
    }
}
